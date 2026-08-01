"""Errors the instrument can return.

Every failure that crosses the wire arrives as a JSON-RPC error object. The code decides which
exception type it becomes, so a script can catch the case it cares about without inspecting
numbers.
"""

from __future__ import annotations

__all__ = [
    "InstrumentError",
    "InvalidCall",
    "NotFound",
    "ControlRequired",
    "HandleReleased",
    "Cancelled",
    "ConnectionLost",
    "error_for",
]


class InstrumentError(Exception):
    """An error returned by the instrument."""

    def __init__(self, code: int, message: str, data: dict | None = None) -> None:
        super().__init__(message)
        self.code = code
        self.message = message
        self.data = data or {}

    def __str__(self) -> str:  # pragma: no cover - formatting only
        detail = ", ".join(f"{key}={value}" for key, value in self.data.items() if key != "problem")
        return f"{self.message} [{self.code}]" + (f" ({detail})" if detail else "")


class InvalidCall(InstrumentError):
    """An argument was missing, of the wrong type, or the call was malformed."""


class NotFound(InstrumentError):
    """No such device, member or subscription."""


class ControlRequired(InstrumentError):
    """Another session holds exclusive control of the instrument."""


class HandleReleased(InstrumentError):
    """The handle has been released, or belongs to another session."""


class Cancelled(InstrumentError):
    """The call was cancelled."""


class ConnectionLost(InstrumentError):
    """The connection to the instrument went away."""

    def __init__(self, message: str = "The connection to the instrument was lost.") -> None:
        super().__init__(0, message)


_BY_CODE = {
    -32001: HandleReleased,
    -32002: HandleReleased,
    -32003: HandleReleased,
    -32004: HandleReleased,
    -32006: ControlRequired,
    -32600: InvalidCall,
    -32601: NotFound,
    -32602: InvalidCall,
    -32800: Cancelled,
    -32000: NotFound,
}


def error_for(code: int, message: str, data: dict | None) -> InstrumentError:
    """Builds the exception a JSON-RPC error code stands for."""
    return _BY_CODE.get(code, InstrumentError)(code, message, data)
