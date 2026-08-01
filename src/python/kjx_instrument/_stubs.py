"""Turns the descriptor into .pyi stubs.

The stubs are what make an editor useful against an instrument it has never seen: they are
generated from the same descriptor the proxies are built from, cached under its hash, and put on
sys.path so an import of the generated module resolves.
"""

from __future__ import annotations

import os
import re
import sys
from pathlib import Path

__all__ = ["MODULE", "emit"]

MODULE = "kjx_instrument_api"

_SCALARS = {
    "bool": "bool",
    "int8": "int",
    "uint8": "int",
    "int16": "int",
    "uint16": "int",
    "int32": "int",
    "uint32": "int",
    "int64": "int",
    "uint64": "int",
    "float32": "float",
    "float64": "float",
    "decimal": "float",
    "char": "str",
    "string": "str",
    "guid": "str",
    "timestamp": "datetime.datetime",
    "duration": "datetime.timedelta",
}

_HEADER = '''"""Generated from the instrument's API descriptor. Do not edit; it is rewritten on connect."""

from __future__ import annotations

import datetime
from contextlib import AbstractContextManager
from typing import Any, Iterator, Literal, Protocol, TypedDict, TypeVar

_T = TypeVar("_T")


class Subscription(Iterator[_T], Protocol[_T]):
    """The values of a stream. Iterate it, and close it when you are done."""

    dropped: int
    timeout: float | None

    def close(self) -> None: ...
    def __enter__(self) -> Subscription[_T]: ...
    def __exit__(self, *exc: Any) -> None: ...
'''


def cache_root() -> Path:
    """Where generated stubs live, following XDG when it is set."""
    configured = os.environ.get("KJX_INSTRUMENT_CACHE")
    if configured:
        return Path(configured)

    base = os.environ.get("XDG_CACHE_HOME")
    return (Path(base) if base else Path.home() / ".cache") / "instrument"


def emit(descriptor, endpoint: str) -> Path:
    """
    Writes the stub package for a descriptor and puts it on sys.path. Cached by hash, so the
    work happens once per distinct instrument surface.
    """
    directory = cache_root() / descriptor.hash.replace(":", "-")
    stub = directory / f"{MODULE}.pyi"

    if not stub.exists():
        directory.mkdir(parents=True, exist_ok=True)
        stub.write_text(_render(descriptor, endpoint), encoding="utf-8")

        # A module the stub can attach to, so 'from kjx_instrument_api import connect' works.
        (directory / f"{MODULE}.py").write_text(
            "from kjx_instrument import connect  # noqa: F401\n", encoding="utf-8"
        )

        (cache_root() / "latest.txt").write_text(str(directory) + "\n", encoding="utf-8")

    if str(directory) not in sys.path:
        sys.path.insert(0, str(directory))

    return directory


def _render(descriptor, endpoint: str) -> str:
    lines = [_HEADER, f"# Instrument: {endpoint}", f"# Descriptor: {descriptor.hash}", ""]
    devices = _devices(descriptor)

    for enum in descriptor.api["enums"]:
        values = ", ".join(f'"{value["name"]}"' for value in enum["values"])
        lines.append(f"{_identifier(enum['name'])} = Literal[{values}]")

    if descriptor.api["enums"]:
        lines += ["", ""]

    for dto in descriptor.api["dtos"]:
        lines.append(f"class {_identifier(dto['name'])}(TypedDict):")
        lines.append(f'    """{_summary(dto.get("doc"), dto["name"])}"""')
        lines.append("")
        for prop in dto["properties"]:
            lines.append(f"    {prop['name']}: {_annotation(prop['type'])}")
        lines += ["", ""]

    for declared in descriptor.api["types"]:
        bases = [_identifier(name) for name in declared.get("extends", [])] + ["Protocol"]
        lines.append(f"class {_identifier(declared['name'])}({', '.join(bases)}):")
        lines.append(f'    """{_summary(declared.get("doc"), declared["name"])}"""')
        lines.append("")

        for member in declared["members"]:
            lines += [f"    {line}" if line else "" for line in _member(member)]

        lines += ["", ""]

    for device_id, attributes in devices.items():
        lines.append(f"class {attributes['class']}({', '.join(attributes['bases'])}):")
        lines.append(f'    """The device configured as {device_id}."""')
        lines += ["", ""]

    lines.append("class Instrument(Protocol):")
    lines.append('    """The connected instrument."""')
    lines.append("")
    lines.append("    descriptor_hash: str")

    for attributes in devices.values():
        for name in attributes["attributes"]:
            lines.append(f"    {name}: {attributes['class']}")

    lines += [
        "",
        "    def close(self) -> None: ...",
        "    def acquire_control(self) -> bool: ...",
        "    def release_control(self) -> None: ...",
        "    def heartbeat(self) -> dict[str, Any]: ...",
        "    def __enter__(self) -> Instrument: ...",
        "    def __exit__(self, *exc: Any) -> None: ...",
        "",
        "",
        "def connect(",
        "    endpoint: str | None = ...,",
        "    *,",
        "    token: str | None = ...,",
        "    socket_path: str | None = ...,",
        "    timeout: float = ...,",
        "    emit_stubs: bool = ...,",
        ") -> Instrument: ...",
        "",
    ]

    return "\n".join(lines)


def _devices(descriptor) -> dict[str, dict]:
    devices = {}

    for device_id, declared in descriptor.devices.items():
        name = device_id.split("/", 1)[1]
        alias = _snake(name)

        devices[device_id] = {
            "class": f"_{_identifier(name)}",
            "bases": [_identifier(item) for item in declared["types"]] + ["Protocol"],
            "attributes": [name] if alias == name else [name, alias],
        }

    return devices


def _member(member: dict) -> list[str]:
    arguments = ["self"]

    for param in member.get("params", []):
        annotation = _annotation(param["type"])
        arguments.append(
            f"{param['name']}: {annotation}" + ("" if param.get("required", True) else " = ...")
        )

    if member["kind"] == "stream":
        returns = f"Subscription[{_annotation(member.get('yields'))}]"
    elif member.get("returns") is None:
        returns = "None"
    elif member["returns"]["kind"] == "ref":
        # A handle has to be released, so forgetting 'with' should not type check.
        returns = f"AbstractContextManager[{_identifier(member['returns']['name'])}]"
    else:
        returns = _annotation(member["returns"])

    return [
        f"def {member['name']}({', '.join(arguments)}) -> {returns}:",
        f'    """{_summary(member.get("doc"), member["name"])}"""',
        "    ...",
        "",
    ]


def _annotation(ref: dict | None) -> str:
    if ref is None:
        return "None"

    kind = ref["kind"]

    if kind in _SCALARS:
        annotation = _SCALARS[kind]
    elif kind in ("array", "list"):
        annotation = f"list[{_annotation(ref['items'])}]"
    elif kind == "dto":
        annotation = _identifier(ref["name"])
    elif kind == "ref":
        annotation = _identifier(ref["name"])
    elif kind == "enum":
        annotation = _identifier(ref["name"])
    else:
        annotation = "Any"

    return f"{annotation} | None" if ref.get("nullable") else annotation


def _identifier(wire_name: str) -> str:
    return "".join(part[:1].upper() + part[1:] for part in wire_name.split("_") if part)


def _snake(name: str) -> str:
    name = re.sub(r"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", "_", name)
    return re.sub(r"\W+", "_", name).lower()


def _summary(doc: str | None, fallback: str) -> str:
    text = (doc or fallback).replace("\n", " ").replace('"""', "'''").strip()
    return text if len(text) <= 200 else text[:197] + "..."
