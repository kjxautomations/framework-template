"""The API descriptor, and the value conversions it drives.

The instrument describes its whole surface on connect, so nothing here is hard-coded: which
members exist, what they take and what they return all come from the descriptor. That is also
what lets values be converted properly in both directions.
"""

from __future__ import annotations

import datetime as _datetime
import re
from typing import Any

__all__ = ["Descriptor", "Member", "encode_duration", "decode_duration"]

_NUMBERS = {
    "float32": float,
    "float64": float,
    "decimal": float,
    "int8": int,
    "uint8": int,
    "int16": int,
    "uint16": int,
    "int32": int,
    "uint32": int,
    "int64": int,
    "uint64": int,
}

_DURATION = re.compile(
    r"^(?P<sign>-)?(?:(?P<days>\d+)\.)?(?P<hours>\d{1,2}):(?P<minutes>\d{2})"
    r"(?::(?P<seconds>\d{2})(?:\.(?P<fraction>\d{1,7}))?)?$"
)


def encode_duration(value: _datetime.timedelta) -> str:
    """Formats a duration the way the instrument parses it: [-][d.]hh:mm:ss[.fffffff]."""
    sign = "-" if value < _datetime.timedelta(0) else ""
    value = abs(value)

    hours, remainder = divmod(value.seconds, 3600)
    minutes, seconds = divmod(remainder, 60)
    ticks = value.microseconds * 10

    text = sign + (f"{value.days}." if value.days else "")
    text += f"{hours:02}:{minutes:02}:{seconds:02}"
    return text + (f".{ticks:07}" if ticks else "")


def decode_duration(text: str) -> _datetime.timedelta:
    """Parses the duration format above."""
    match = _DURATION.match(text)
    if not match:
        raise ValueError(f"'{text}' is not a duration.")

    parts = match.groupdict()
    fraction = parts["fraction"] or ""
    value = _datetime.timedelta(
        days=int(parts["days"] or 0),
        hours=int(parts["hours"]),
        minutes=int(parts["minutes"]),
        seconds=int(parts["seconds"] or 0),
        microseconds=int(fraction.ljust(7, "0")) // 10 if fraction else 0,
    )

    return -value if parts["sign"] else value


class Member:
    """One callable member of a device or handle."""

    __slots__ = ("name", "kind", "access", "doc", "params", "returns", "owner")

    def __init__(self, declared: dict, owner: str) -> None:
        self.name: str = declared["name"]
        self.kind: str = declared["kind"]
        self.access: str = declared["access"]
        self.doc: str | None = declared.get("doc")
        self.params: list[dict] = declared.get("params", [])
        self.returns: dict | None = declared.get("returns") or declared.get("yields")
        self.owner = owner

    @property
    def is_stream(self) -> bool:
        return self.kind == "stream"

    @property
    def returns_reference(self) -> bool:
        return bool(self.returns) and self.returns.get("kind") == "ref"


class Descriptor:
    """Everything the instrument said about itself."""

    def __init__(self, document: dict) -> None:
        self.hash: str = document["hash"]
        self.api: dict = document["api"]
        self.types = {item["name"]: item for item in self.api["types"]}
        self.dtos = {item["name"]: item for item in self.api["dtos"]}
        self.enums = {item["name"]: item for item in self.api["enums"]}
        self.devices = {item["id"]: item for item in document.get("devices", [])}

    def members_of(self, wire_types: list[str]) -> dict[str, Member]:
        """
        Every member a target answers to. A device carries the set of interfaces it was
        configured with, so this is the union over that set and everything they inherit.
        """
        members: dict[str, Member] = {}

        for wire_type in wire_types:
            for name in self._expand(wire_type):
                for declared in self.types.get(name, {}).get("members", []):
                    members.setdefault(declared["name"], Member(declared, name))

        return members

    def _expand(self, wire_type: str) -> list[str]:
        found: list[str] = []
        pending = [wire_type]

        while pending:
            name = pending.pop()
            if name in found or name not in self.types:
                continue
            found.append(name)
            pending.extend(self.types[name].get("extends", []))

        return found

    # -- values ----------------------------------------------------------------

    def encode(self, value: Any, ref: dict | None, endpoint: str) -> Any:
        """Turns a Python value into its wire form, using the declared type as the guide."""
        from ._proxy import RemoteObject  # local import: the proxy needs the descriptor

        if value is None:
            return None

        if isinstance(value, RemoteObject):
            return value._reference(endpoint)

        if isinstance(value, _datetime.timedelta):
            return encode_duration(value)

        if isinstance(value, _datetime.datetime):
            return value.isoformat()

        kind = (ref or {}).get("kind")

        if kind in ("array", "list"):
            items = ref.get("items")
            return [self.encode(item, items, endpoint) for item in value]

        if kind == "dto" and isinstance(value, dict):
            properties = {item["name"]: item["type"] for item in self.dtos[ref["name"]]["properties"]}
            return {key: self.encode(item, properties.get(key), endpoint) for key, item in value.items()}

        return value

    def decode(self, value: Any, ref: dict | None) -> Any:
        """Turns a wire value back into Python, converting timestamps and durations."""
        if value is None:
            return None

        kind = (ref or {}).get("kind")

        # JSON has one number type, so 0.0 arrives as 0. The declared type is what the generated
        # stubs promise, so it is what the value is converted to.
        if kind in _NUMBERS and isinstance(value, (int, float)) and not isinstance(value, bool):
            return _NUMBERS[kind](value)

        if kind == "timestamp":
            return _datetime.datetime.fromisoformat(value)

        if kind == "duration":
            return decode_duration(value)

        if kind in ("array", "list"):
            return [self.decode(item, ref.get("items")) for item in value]

        if kind == "dto":
            properties = {item["name"]: item["type"] for item in self.dtos[ref["name"]]["properties"]}
            return {key: self.decode(item, properties.get(key)) for key, item in value.items()}

        return value
