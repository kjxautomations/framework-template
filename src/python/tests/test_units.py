"""Tests for the parts of the client that do not need an instrument.

These cover the pieces the end-to-end suite cannot always reach: the socket discovery and the
unix transport are skipped on a Python without AF_UNIX, and duration formatting is easier to pin
down here than over the wire.
"""

from __future__ import annotations

import datetime
import os
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import mock

from kjx_instrument import ConnectionLost, _find_socket, _uri
from kjx_instrument._descriptor import Descriptor, decode_duration, encode_duration
from kjx_instrument._stubs import _snake


class DurationTests(unittest.TestCase):
    CASES = [
        (datetime.timedelta(0), "00:00:00"),
        (datetime.timedelta(seconds=1), "00:00:01"),
        (datetime.timedelta(seconds=1.5), "00:00:01.5000000"),
        (datetime.timedelta(hours=2, minutes=3, seconds=4), "02:03:04"),
        (datetime.timedelta(days=1, hours=2), "1.02:00:00"),
        (datetime.timedelta(microseconds=1), "00:00:00.0000010"),
        (-datetime.timedelta(seconds=90), "-00:01:30"),
    ]

    def test_durations_are_formatted_the_way_the_instrument_parses_them(self):
        for value, expected in self.CASES:
            with self.subTest(value=value):
                self.assertEqual(expected, encode_duration(value))

    def test_durations_round_trip(self):
        for value, text in self.CASES:
            with self.subTest(value=value):
                self.assertEqual(value, decode_duration(text))

    def test_a_duration_without_seconds_is_accepted(self):
        self.assertEqual(datetime.timedelta(hours=1, minutes=2), decode_duration("01:02"))

    def test_nonsense_is_rejected(self):
        with self.assertRaises(ValueError):
            decode_duration("about an hour")


class EndpointTests(unittest.TestCase):
    def test_a_bare_host_and_port_is_assumed_to_be_remote_and_secure(self):
        self.assertEqual("wss://bench-01:7443/rpc", _uri("bench-01:7443"))

    def test_a_full_uri_is_used_as_given(self):
        self.assertEqual("ws://127.0.0.1:5000/rpc", _uri("ws://127.0.0.1:5000/rpc"))

    def test_a_uri_without_a_path_gets_the_default_one(self):
        self.assertEqual("ws://127.0.0.1:5000/rpc", _uri("ws://127.0.0.1:5000"))


class SocketDiscoveryTests(unittest.TestCase):
    def test_the_environment_wins(self):
        with mock.patch.dict(os.environ, {"KJX_INSTRUMENT_SOCKET": "/tmp/chosen.sock"}):
            self.assertEqual("/tmp/chosen.sock", _find_socket())

    def test_a_single_socket_is_found(self):
        with TemporaryDirectory() as directory:
            expected = Path(directory) / "kjx-control.rpc.sock"
            expected.write_text("")

            with mock.patch.dict(os.environ, {"KJX_INSTRUMENT_SOCKET": "", "TEMP": directory}), \
                 mock.patch("kjx_instrument.os.name", "nt"):
                self.assertEqual(str(expected), _find_socket())

    def test_nothing_running_is_explained(self):
        with TemporaryDirectory() as directory:
            with mock.patch.dict(os.environ, {"KJX_INSTRUMENT_SOCKET": "", "TEMP": directory}), \
                 mock.patch("kjx_instrument.os.name", "nt"):
                with self.assertRaises(ConnectionLost) as caught:
                    _find_socket()

        self.assertIn("No instrument socket found", str(caught.exception))

    def test_an_ambiguous_choice_is_explained(self):
        with TemporaryDirectory() as directory:
            for name in ("kjx-control.rpc.sock", "kjx-engineering.rpc.sock"):
                (Path(directory) / name).write_text("")

            with mock.patch.dict(os.environ, {"KJX_INSTRUMENT_SOCKET": "", "TEMP": directory}), \
                 mock.patch("kjx_instrument.os.name", "nt"):
                with self.assertRaises(ConnectionLost) as caught:
                    _find_socket()

        self.assertIn("More than one instrument", str(caught.exception))


class DescriptorTests(unittest.TestCase):
    DOCUMENT = {
        "hash": "sha256:abc",
        "api": {
            "version": 1,
            "types": [
                {
                    "name": "device",
                    "clr": "X.IDevice",
                    "members": [
                        {"name": "initialize", "kind": "call", "access": "invoke", "clr": "Initialize", "params": []}
                    ],
                },
                {
                    "name": "motor",
                    "clr": "X.IMotor",
                    "extends": ["device"],
                    "members": [
                        {
                            "name": "move_to",
                            "kind": "call",
                            "access": "invoke",
                            "clr": "MoveTo",
                            "params": [{"name": "position", "type": {"kind": "float64"}, "required": True}],
                        }
                    ],
                },
                {
                    "name": "supports_homing",
                    "clr": "X.ISupportsHoming",
                    "members": [
                        {"name": "home", "kind": "call", "access": "invoke", "clr": "Home", "params": []}
                    ],
                },
            ],
            "dtos": [
                {
                    "name": "reading",
                    "clr": "X.Reading",
                    "properties": [
                        {"name": "value", "type": {"kind": "float64"}},
                        {"name": "taken", "type": {"kind": "timestamp"}},
                    ],
                }
            ],
            "enums": [],
        },
        "devices": [{"id": "dev/XMotor", "types": ["motor", "supports_homing"]}],
    }

    def setUp(self):
        self.descriptor = Descriptor(self.DOCUMENT)

    def test_a_target_answers_to_every_capability_and_everything_they_inherit(self):
        members = self.descriptor.members_of(["motor", "supports_homing"])

        self.assertEqual({"move_to", "initialize", "home"}, set(members))

    def test_a_target_without_a_capability_does_not_have_its_members(self):
        self.assertNotIn("home", self.descriptor.members_of(["motor"]))

    def test_numbers_are_converted_to_the_declared_type(self):
        self.assertIsInstance(self.descriptor.decode(0, {"kind": "float64"}), float)
        self.assertIsInstance(self.descriptor.decode(1.0, {"kind": "int32"}), int)

    def test_dto_properties_are_converted(self):
        decoded = self.descriptor.decode(
            {"value": 1, "taken": "2026-07-31T12:00:00+00:00"}, {"kind": "dto", "name": "reading"}
        )

        self.assertIsInstance(decoded["value"], float)
        self.assertIsInstance(decoded["taken"], datetime.datetime)

    def test_lists_are_converted_element_by_element(self):
        decoded = self.descriptor.decode([1, 2], {"kind": "array", "items": {"kind": "float64"}})

        self.assertEqual([1.0, 2.0], decoded)
        self.assertTrue(all(isinstance(item, float) for item in decoded))


_GOLDEN = (
    Path(__file__).resolve().parents[2] / "KJX.Scripting.Tests" / "TestData" / "StreamSample.descriptor.json"
)


@unittest.skipUnless(_GOLDEN.exists(), f"{_GOLDEN} is not present.")
class StubTests(unittest.TestCase):
    """
    Renders stubs from the generator's own golden descriptor, so the two ends of the pipeline are
    checked against each other without a running instrument.
    """

    def setUp(self):
        import json

        from kjx_instrument._stubs import _render

        document = json.loads(_GOLDEN.read_text(encoding="utf-8"))
        document["devices"] = [{"id": "dev/Pump1", "types": ["syringe_pump"]}]

        self.stub = _render(Descriptor(document), "ws://bench/rpc")

    def test_the_stub_is_valid_python(self):
        compile(self.stub, "kjx_instrument_api.pyi", "exec")

    def test_scalars_and_dtos_are_declared(self):
        self.assertIn("class FlowReading(TypedDict):", self.stub)
        self.assertIn("    microlitres: float", self.stub)
        self.assertIn("    taken: datetime.datetime", self.stub)

    def test_enums_become_literals(self):
        self.assertIn('PumpState = Literal["Idle", "Priming", "Dispensing"]', self.stub)

    def test_optional_parameters_are_optional_in_the_stub(self):
        self.assertIn("def calibrate(self, passes: int = ..., state: PumpState = ...) -> FlowReading:", self.stub)

    def test_a_trailing_cancellation_token_is_not_a_parameter(self):
        self.assertIn("def prime(self, microlitres: float, settle: datetime.timedelta) -> None:", self.stub)

    def test_nullable_members_are_optional(self):
        self.assertIn("def get_maximum_flow_rate(self) -> float | None:", self.stub)

    def test_streams_are_subscriptions(self):
        self.assertIn("def flow(self) -> Subscription[FlowReading]:", self.stub)

    def test_devices_are_attributes_of_the_instrument(self):
        self.assertIn("class _Pump1(SyringePump, Protocol):", self.stub)
        self.assertIn("    Pump1: _Pump1", self.stub)
        self.assertIn("    pump1: _Pump1", self.stub)


class NamingTests(unittest.TestCase):
    def test_device_aliases_are_snake_case(self):
        self.assertEqual("x_motor", _snake("XMotor"))
        self.assertEqual("temperature_sensor1", _snake("TemperatureSensor1"))
        self.assertEqual("bench", _snake("Bench"))


if __name__ == "__main__":
    unittest.main()
