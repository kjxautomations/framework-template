"""JSON-RPC over a WebSocket.

One background thread reads the socket and routes what it finds: responses to whoever is waiting
for that id, stream notifications to the subscription's queue. Everything a script calls is
synchronous, because a script reads better that way.
"""

from __future__ import annotations

import itertools
import json
import queue
import threading
from typing import Any

from ._errors import ConnectionLost, InstrumentError, error_for

__all__ = ["Transport", "StreamQueue"]

_STREAM = "$/stream"


class StreamQueue:
    """The values of one subscription, as the reader thread hands them over."""

    def __init__(self) -> None:
        self.items: queue.Queue = queue.Queue()

    def put_value(self, value: Any, dropped: int = 0) -> None:
        self.items.put(("value", value, dropped))

    def put_complete(self) -> None:
        self.items.put(("complete", None, 0))

    def put_error(self, error: InstrumentError) -> None:
        self.items.put(("error", error, 0))


class Transport:
    """A connection to one instrument."""

    def __init__(self, connection, endpoint: str) -> None:
        self._connection = connection
        self._ids = itertools.count(1)
        self._lock = threading.Lock()
        self._pending: dict[int, tuple[threading.Event, list]] = {}
        self._streams: dict[str, StreamQueue] = {}
        self._closed = threading.Event()
        self._failure: Exception | None = None

        self.endpoint = endpoint
        self._reader = threading.Thread(target=self._read_loop, name="kjx-instrument", daemon=True)
        self._reader.start()

    # -- requests --------------------------------------------------------------

    def call(self, method: str, params: dict | None = None, timeout: float | None = 30.0) -> Any:
        """Sends a request and waits for its response."""
        request_id = next(self._ids)
        signal, slot = threading.Event(), []

        with self._lock:
            if self._closed.is_set():
                raise self._failure or ConnectionLost()
            self._pending[request_id] = (signal, slot)

        message = {"jsonrpc": "2.0", "id": request_id, "method": method}
        if params:
            message["params"] = params

        self._send(message)

        if not signal.wait(timeout):
            with self._lock:
                self._pending.pop(request_id, None)
            self.notify("$/cancelRequest", {"id": request_id})
            raise TimeoutError(f"'{method}' did not answer within {timeout}s.")

        outcome, payload = slot[0]
        if outcome == "error":
            raise payload

        return payload

    def notify(self, method: str, params: dict | None = None) -> None:
        """Sends a notification, which has no response."""
        message = {"jsonrpc": "2.0", "method": method}
        if params:
            message["params"] = params

        try:
            self._send(message)
        except ConnectionLost:
            pass

    def subscribe(self, subscription: str) -> StreamQueue:
        """Registers a queue for a subscription the caller has just opened."""
        stream = StreamQueue()
        with self._lock:
            self._streams[subscription] = stream
        return stream

    def unregister(self, subscription: str) -> None:
        with self._lock:
            self._streams.pop(subscription, None)

    def close(self) -> None:
        """Closes the connection and wakes anything that was waiting."""
        if self._closed.is_set():
            return

        self._closed.set()

        try:
            self._connection.close()
        except Exception:
            pass

        self._fail(ConnectionLost("The connection was closed."))

    @property
    def closed(self) -> bool:
        return self._closed.is_set()

    # -- plumbing --------------------------------------------------------------

    def _send(self, message: dict) -> None:
        try:
            self._connection.send(json.dumps(message))
        except Exception as failure:  # the socket went away mid-call
            raise ConnectionLost(str(failure)) from failure

    def _read_loop(self) -> None:
        try:
            for raw in self._connection:
                self._route(json.loads(raw))
        except Exception as failure:
            if not self._closed.is_set():
                self._fail(ConnectionLost(str(failure)))
            return

        if not self._closed.is_set():
            self._fail(ConnectionLost())

    def _route(self, message: dict) -> None:
        if message.get("method") == _STREAM:
            self._route_stream(message.get("params") or {})
            return

        request_id = message.get("id")
        if request_id is None:
            return

        with self._lock:
            waiting = self._pending.pop(request_id, None)

        if waiting is None:
            return

        signal, slot = waiting
        error = message.get("error")

        if error is not None:
            slot.append(("error", error_for(error["code"], error.get("message", ""), error.get("data"))))
        else:
            slot.append(("result", message.get("result")))

        signal.set()

    def _route_stream(self, params: dict) -> None:
        with self._lock:
            stream = self._streams.get(params.get("subscription"))

        if stream is None:
            return

        if params.get("complete"):
            stream.put_complete()
        elif "error" in params:
            failure = params["error"]
            stream.put_error(error_for(failure["code"], failure.get("message", ""), failure.get("data")))
        else:
            stream.put_value(params.get("value"), params.get("dropped", 0))

    def _fail(self, failure: Exception) -> None:
        self._failure = failure

        with self._lock:
            waiting, streams = list(self._pending.values()), list(self._streams.values())
            self._pending.clear()
            self._streams.clear()

        for signal, slot in waiting:
            slot.append(("error", failure))
            signal.set()

        for stream in streams:
            stream.put_error(failure if isinstance(failure, InstrumentError) else ConnectionLost(str(failure)))
