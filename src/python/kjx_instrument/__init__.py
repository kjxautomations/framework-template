"""Talk to a KJX instrument from Python.

    inst = connect()                                  # the unix socket on the instrument
    inst = connect("bench-01:7443", token="...")      # a remote instrument

    inst.XMotor.home()
    inst.XMotor.move_to(2.5)

    with inst.Bench.claim("washing") as claim:
        claim.use()

Everything a script can reach comes from the descriptor the instrument serves on connect, so an
unknown member fails here rather than as a round trip.
"""

from __future__ import annotations

import glob
import os
import socket
import threading
import uuid
from typing import Any

from websockets.sync.client import connect as _ws_connect

from ._descriptor import Descriptor, Member
from ._errors import (
    Cancelled,
    ConnectionLost,
    ControlRequired,
    HandleReleased,
    InstrumentError,
    InvalidCall,
    NotFound,
)
from ._proxy import Device, Handle, RemoteObject, Subscription
from ._stubs import cache_root, emit as _emit_stubs
from ._transport import Transport

__all__ = [
    "connect",
    "Instrument",
    "Device",
    "Handle",
    "RemoteObject",
    "Subscription",
    "InstrumentError",
    "InvalidCall",
    "NotFound",
    "ControlRequired",
    "HandleReleased",
    "Cancelled",
    "ConnectionLost",
    "cache_root",
]

__version__ = "0.1.0"

_DEFAULT_PATH = "/rpc"


def connect(
    endpoint: str | None = None,
    *,
    token: str | None = None,
    socket_path: str | None = None,
    timeout: float = 30.0,
    emit_stubs: bool = True,
) -> "Instrument":
    """
    Connects to an instrument.

    With no arguments, connects over the unix domain socket on this machine, where the socket's
    permissions are the access control. Give an endpoint to reach one over the network, which
    needs a token.

    Args:
        endpoint: ``host:port``, or a full ``ws://`` or ``wss://`` URI. None uses the local socket.
        token: The bearer token for a remote instrument.
        socket_path: An explicit unix socket path, instead of looking for one.
        timeout: Seconds to wait for any single call.
        emit_stubs: Writes .pyi stubs for this instrument and puts them on sys.path.
    """
    if endpoint is None and socket_path is None:
        socket_path = _find_socket()

    if socket_path is not None:
        connection = _connect_unix(socket_path, timeout)
        description = socket_path
    else:
        description = _uri(endpoint)
        headers = {"Authorization": f"Bearer {token}"} if token else None
        connection = _ws_connect(description, additional_headers=headers, open_timeout=timeout)

    return Instrument(Transport(connection, description), timeout=timeout, emit_stubs=emit_stubs)


def _uri(endpoint: str) -> str:
    if "://" in endpoint:
        return endpoint if _path_of(endpoint) else endpoint.rstrip("/") + _DEFAULT_PATH

    # A bare host:port is assumed to be remote, and remote means TLS.
    return f"wss://{endpoint}{_DEFAULT_PATH}"


def _path_of(uri: str) -> str:
    return uri.split("://", 1)[1].partition("/")[2]


def _connect_unix(path: str, timeout: float):
    if not hasattr(socket, "AF_UNIX"):
        raise ConnectionLost("This platform has no unix domain sockets; connect over TCP instead.")

    raw = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    raw.settimeout(timeout)

    try:
        raw.connect(path)
    except OSError as failure:
        raw.close()
        raise ConnectionLost(f"No instrument is listening on '{path}': {failure}") from failure

    raw.settimeout(None)
    return _ws_connect(f"ws://localhost{_DEFAULT_PATH}", sock=raw, open_timeout=timeout)


def _find_socket() -> str:
    """Looks for the instrument's socket in the places the host puts it."""
    configured = os.environ.get("KJX_INSTRUMENT_SOCKET")
    if configured:
        return configured

    if os.name == "nt":
        pattern = os.path.join(os.environ.get("TEMP", "."), "*.rpc.sock")
    else:
        pattern = "/run/instrument/*.rpc.sock"

    candidates = sorted(glob.glob(pattern))

    if not candidates:
        raise ConnectionLost(
            f"No instrument socket found at '{pattern}'. Start the instrument application, or "
            "pass socket_path= or an endpoint."
        )

    if len(candidates) > 1:
        listed = ", ".join(candidates)
        raise ConnectionLost(f"More than one instrument is running: {listed}. Pass socket_path=.")

    return candidates[0]


class Instrument:
    """A connected instrument. Devices are reached as attributes."""

    def __init__(self, transport: Transport, timeout: float = 30.0, emit_stubs: bool = True) -> None:
        self._transport = transport
        self._timeout = timeout
        self._releases: list[str] = []
        self._lock = threading.Lock()

        self.endpoint_id = uuid.uuid4().hex
        """Identifies this connection, so a handle cannot be passed to a different instrument."""

        self.descriptor = Descriptor(transport.call("describe", timeout=timeout))
        self.descriptor_hash = self.descriptor.hash

        self._devices: dict[str, Device] = {}
        self._by_attribute: dict[str, Device] = {}
        self._build_devices()

        self.stub_path = _emit_stubs(self.descriptor, transport.endpoint) if emit_stubs else None
        """Where the generated .pyi stubs for this instrument were written."""

    # -- devices ---------------------------------------------------------------

    def _build_devices(self) -> None:
        from ._stubs import _snake

        for device_id, declared in self.descriptor.devices.items():
            members = self.descriptor.members_of(declared["types"])
            device = Device(self, device_id, declared["types"][0], members)

            name = device_id.split("/", 1)[1]
            self._devices[device_id] = device
            self._by_attribute[name] = device

            alias = _snake(name)
            if alias != name:
                self._by_attribute.setdefault(alias, device)

    @property
    def devices(self) -> dict[str, Device]:
        """Every device, by wire id."""
        return dict(self._devices)

    def __getattr__(self, name: str) -> Device:
        devices = self.__dict__.get("_by_attribute") or {}

        if name in devices:
            return devices[name]

        raise AttributeError(
            f"This instrument has no device '{name}'. It has: {', '.join(sorted(devices)) or 'none'}."
        )

    def __getitem__(self, name: str) -> Device:
        if name in self._devices:
            return self._devices[name]

        if name in self._by_attribute:
            return self._by_attribute[name]

        raise KeyError(name)

    def __dir__(self) -> list[str]:
        return sorted(set(super().__dir__()) | set(self._by_attribute))

    # -- control ---------------------------------------------------------------

    def acquire_control(self) -> bool:
        """Takes exclusive control. Returns False if another session holds it."""
        return bool(self._transport.call("acquire_control", timeout=self._timeout)["granted"])

    def release_control(self) -> None:
        """Gives up exclusive control."""
        self._transport.call("release_control", timeout=self._timeout)

    def heartbeat(self) -> dict[str, Any]:
        """Tells the instrument this session is still here, and reports who holds control."""
        return self._transport.call("heartbeat", timeout=self._timeout)

    # -- lifetime --------------------------------------------------------------

    def close(self) -> None:
        """Closes the connection. Anything this session still held is released by the instrument."""
        self._transport.close()

    def __enter__(self) -> "Instrument":
        return self

    def __exit__(self, *_: Any) -> None:
        self.close()

    def __repr__(self) -> str:  # pragma: no cover - formatting only
        return f"<Instrument {self._transport.endpoint} ({len(self._devices)} devices)>"

    # -- dispatch --------------------------------------------------------------

    def _invoke(self, target: RemoteObject, member: Member, arguments: dict) -> Any:
        self._flush_releases()

        declared = {item["name"]: item["type"] for item in member.params}
        params = {
            name: self.descriptor.encode(value, declared.get(name), self.endpoint_id)
            for name, value in arguments.items()
        }

        if member.is_stream:
            return self._subscribe(target, member, params)

        result = self._transport.call(f"{target._id}.{member.name}", params, timeout=self._timeout)

        if member.returns_reference:
            return self._handle(result, member.returns["name"])

        return self.descriptor.decode(result, member.returns)

    def _subscribe(self, target: RemoteObject, member: Member, params: dict) -> Subscription:
        opened = self._transport.call(
            "subscribe",
            {"target": target._id, "member": member.name, "params": params},
            timeout=self._timeout,
        )

        subscription = opened["subscription"]
        return Subscription(self, subscription, self._transport.subscribe(subscription), member.returns)

    def _handle(self, reference: dict | None, wire_type: str) -> Handle | None:
        if reference is None:
            return None

        members = self.descriptor.members_of([reference.get("$type", wire_type)])
        return Handle(self, reference["$ref"], reference.get("$type", wire_type), members)

    def _unsubscribe(self, subscription: str) -> None:
        self._transport.unregister(subscription)

        try:
            self._transport.call("unsubscribe", {"subscription": subscription}, timeout=self._timeout)
        except (NotFound, ConnectionLost):
            # Already gone, which is what we wanted.
            pass

    def _release(self, handle: str) -> None:
        try:
            self._transport.call("release", {"handle": handle}, timeout=self._timeout)
        except (HandleReleased, ConnectionLost):
            pass

    def _enqueue_release(self, handle: str) -> None:
        """
        Called from __del__, so it only records the id. Sending from a finaliser can run on any
        thread at any moment, including during interpreter shutdown.
        """
        with self._lock:
            self._releases.append(handle)

    def _flush_releases(self) -> None:
        with self._lock:
            pending, self._releases = self._releases, []

        if not pending:
            return

        try:
            self._transport.call("release", {"handles": pending}, timeout=self._timeout)
        except (InstrumentError, TimeoutError):
            # A handle the instrument has already reclaimed is not a problem worth raising.
            pass
