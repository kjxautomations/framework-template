"""Proxies for the things a script addresses.

Members come from the descriptor, so an unknown name fails locally with AttributeError instead of
becoming a round trip that the instrument has to reject.
"""

from __future__ import annotations

import inspect
from typing import Any

from ._descriptor import Member
from ._errors import HandleReleased

__all__ = ["RemoteObject", "Device", "Handle", "Subscription", "BoundMember"]


class BoundMember:
    """One member of one target, ready to call."""

    def __init__(self, target: "RemoteObject", member: Member) -> None:
        self._target = target
        self._member = member

        self.__name__ = member.name
        self.__qualname__ = f"{target._id}.{member.name}"
        self.__doc__ = self._documentation()
        self.__signature__ = self._signature()

    def __call__(self, *args: Any, **kwargs: Any) -> Any:
        bound = self.__signature__.bind(*args, **kwargs)
        # Only what the caller actually passed is sent; the instrument applies its own defaults.
        return self._target._invoke(self._member, dict(bound.arguments))

    def __repr__(self) -> str:  # pragma: no cover - formatting only
        return f"<{self.__qualname__}{self.__signature__}>"

    def _signature(self) -> inspect.Signature:
        parameters = [
            inspect.Parameter(
                declared["name"],
                inspect.Parameter.POSITIONAL_OR_KEYWORD,
                default=declared.get("default", inspect.Parameter.empty)
                if not declared.get("required", True)
                else inspect.Parameter.empty,
            )
            for declared in self._member.params
        ]

        return inspect.Signature(parameters)

    def _documentation(self) -> str:
        lines = [self._member.doc or f"Calls {self.__qualname__}."]

        described = [f"{item['name']}: {item.get('doc', '')}".rstrip(": ") for item in self._member.params]
        if described:
            lines += ["", "Arguments:"] + [f"    {item}" for item in described]

        if self._member.is_stream:
            lines += ["", "Returns a subscription: iterate it, and close it when you are done."]
        elif self._member.returns_reference:
            lines += ["", "Returns a handle. Use it with 'with' so that it is released."]

        return "\n".join(lines)


class RemoteObject:
    """A device or a handle, addressed by its wire id."""

    def __init__(self, instrument, wire_id: str, wire_type: str, members: dict[str, Member]) -> None:
        self._instrument = instrument
        self._id = wire_id
        self._type = wire_type
        self._members = members
        self._released = False
        self._endpoint = instrument.endpoint_id

    def __getattr__(self, name: str) -> BoundMember:
        # Only reached for names that are not real attributes.
        members = self.__dict__.get("_members") or {}

        if name in members:
            return BoundMember(self, members[name])

        raise AttributeError(
            f"'{self.__dict__.get('_id', '?')}' has no member '{name}'. "
            f"It offers: {', '.join(sorted(members)) or 'nothing'}."
        )

    def __dir__(self) -> list[str]:
        return sorted(set(super().__dir__()) | set(self._members))

    def __repr__(self) -> str:  # pragma: no cover - formatting only
        return f"<{type(self).__name__} {self._id}: {self._type}>"

    def _reference(self, endpoint: str) -> dict:
        """The wire form of a reference to this object."""
        if endpoint != self._endpoint:
            raise ValueError(
                f"'{self._id}' belongs to another instrument. A reference cannot be passed "
                "between connections."
            )

        if self._released:
            raise HandleReleased(-32002, f"'{self._id}' has been released.")

        return {"$ref": self._id, "$type": self._type}

    def _invoke(self, member: Member, arguments: dict) -> Any:
        if self._released:
            raise HandleReleased(-32002, f"'{self._id}' has been released.")

        return self._instrument._invoke(self, member, arguments)


class Device(RemoteObject):
    """A configured device. Devices are permanent, and are never released."""


class Handle(RemoteObject):
    """
    An object this session created. It holds something on the instrument until it is released,
    which is why it is a context manager.
    """

    def __enter__(self) -> "Handle":
        return self

    def __exit__(self, *_: Any) -> None:
        self.release()

    def release(self) -> None:
        """Releases the handle now."""
        if self._released:
            return

        self._released = True
        self._instrument._release(self._id)

    def __del__(self) -> None:
        # Never send from __del__: the id is queued and flushed on the next call out.
        if self.__dict__.get("_released", True):
            return

        self._released = True
        instrument = self.__dict__.get("_instrument")

        if instrument is not None:
            instrument._enqueue_release(self._id)


class Subscription:
    """
    The values of a stream. Iterating blocks until the next one arrives; closing it tells the
    instrument to stop sending.
    """

    def __init__(self, instrument, subscription_id: str, stream, item_type: dict | None) -> None:
        self._instrument = instrument
        self._id = subscription_id
        self._stream = stream
        self._item_type = item_type
        self._closed = False

        self.dropped = 0
        """How many values the instrument dropped because this client fell behind."""

        self.timeout: float | None = None
        """Seconds to wait for each value. None waits for as long as the stream is open."""

    def __iter__(self) -> "Subscription":
        return self

    def __next__(self) -> Any:
        if self._closed:
            raise StopIteration

        outcome, payload, dropped = self._stream.items.get(timeout=self.timeout)
        self.dropped += dropped

        if outcome == "value":
            return self._instrument.descriptor.decode(payload, self._item_type)

        self._closed = True

        if outcome == "error":
            raise payload

        raise StopIteration

    def __enter__(self) -> "Subscription":
        return self

    def __exit__(self, *_: Any) -> None:
        self.close()

    def close(self) -> None:
        """Ends the subscription."""
        if self._closed:
            return

        self._closed = True
        self._instrument._unsubscribe(self._id)

    def __del__(self) -> None:
        if not self.__dict__.get("_closed", True):
            self.close()

    def __repr__(self) -> str:  # pragma: no cover - formatting only
        return f"<Subscription {self._id}{' closed' if self._closed else ''}>"
