"""End-to-end tests against a running instrument.

The C# test suite starts the host, points these at it and runs them. Nothing here is mocked: the
descriptor, the dispatch and the devices are the real ones.
"""

from __future__ import annotations

import datetime
import gc
import os
import socket
import unittest

from kjx_instrument import (
    ControlRequired,
    HandleReleased,
    InvalidCall,
    connect,
)

ENDPOINT = os.environ.get("KJX_TEST_ENDPOINT")
TOKEN = os.environ.get("KJX_TEST_TOKEN")
SOCKET = os.environ.get("KJX_TEST_SOCKET")


def _connect(**kwargs):
    return connect(ENDPOINT, token=TOKEN, **kwargs)


@unittest.skipIf(not ENDPOINT, "KJX_TEST_ENDPOINT is not set.")
class InstrumentTests(unittest.TestCase):
    def setUp(self) -> None:
        self.inst = _connect()
        self.addCleanup(self.inst.close)

    # -- the namespace ---------------------------------------------------------

    def test_devices_come_from_the_configuration(self):
        self.assertIn("dev/XMotor", self.inst.devices)
        self.assertIs(self.inst.x_motor, self.inst.XMotor, "the snake_case alias is the same device")
        self.assertIs(self.inst["dev/XMotor"], self.inst.XMotor)

    def test_a_capability_shows_up_on_the_device_that_has_it(self):
        self.assertIn("home", dir(self.inst.XMotor))
        self.assertNotIn("home", dir(self.inst.YMotor))

    def test_an_unknown_member_fails_without_a_round_trip(self):
        with self.assertRaises(AttributeError) as caught:
            self.inst.XMotor.levitate

        self.assertIn("levitate", str(caught.exception))

    def test_a_member_the_device_does_not_have_fails_locally(self):
        # YMotor is the same driver, configured without ISupportsHoming.
        with self.assertRaises(AttributeError):
            self.inst.YMotor.home

    def test_an_unknown_device_fails_locally(self):
        with self.assertRaises(AttributeError):
            self.inst.NoSuchThing

    # -- calls -----------------------------------------------------------------

    def test_calls_reach_the_device(self):
        self.inst.XMotor.home()
        self.assertTrue(self.inst.XMotor.get_is_homed())

        self.inst.XMotor.move_to(2.5)
        self.assertEqual(2.5, self.inst.XMotor.get_position())

    def test_arguments_can_be_passed_by_name(self):
        self.inst.XMotor.home()
        self.inst.XMotor.move_to(new_position=1.25)
        self.assertEqual(1.25, self.inst.XMotor.get_position())

    def test_a_bad_argument_is_refused_by_the_instrument(self):
        with self.assertRaises(InvalidCall) as caught:
            self.inst.XMotor.move_to("over there")

        self.assertEqual("new_position", caught.exception.data.get("parameter"))

    def test_too_many_arguments_are_refused_locally(self):
        with self.assertRaises(TypeError):
            self.inst.XMotor.move_to(1.0, 2.0)

    def test_durations_survive_the_round_trip(self):
        doubled = self.inst.Bench.doubled(datetime.timedelta(seconds=1.5))

        self.assertIsInstance(doubled, datetime.timedelta)
        self.assertEqual(datetime.timedelta(seconds=3), doubled)

    def test_inherited_members_are_callable(self):
        self.inst.XMotor.initialize()
        self.assertTrue(self.inst.XMotor.get_is_initialized())

    # -- streams ---------------------------------------------------------------

    def test_a_stream_yields_values_and_closes(self):
        with self.inst.Bench.ticks() as ticks:
            ticks.timeout = 30
            self.assertEqual(0, next(ticks))
            self.assertEqual(1, next(ticks))

    def test_a_sensor_stream_arrives_as_decoded_readings(self):
        with self.inst.TemperatureSensor1.readings() as readings:
            readings.timeout = 30
            self.inst.TemperatureSensor1.initialize()
            reading = next(readings)

        self.addCleanup(self.inst.TemperatureSensor1.shutdown)

        self.assertIsInstance(reading["value"], float)
        self.assertIsInstance(reading["timestamp"], datetime.datetime,
                              "timestamps are converted using the declared type")

    # -- handles ---------------------------------------------------------------

    def test_a_handle_is_a_context_manager(self):
        with self.inst.Bench.claim("washing") as claim:
            claim.use()
            self.assertEqual("washing", claim.get_reason())

        with self.assertRaises(HandleReleased):
            claim.use()

    def test_a_handle_can_be_passed_back_as_an_argument(self):
        with self.inst.Bench.claim("priming") as claim:
            self.assertEqual("priming", self.inst.Bench.reason_for(claim))

    def test_a_handle_cannot_be_used_on_another_instrument(self):
        other = _connect(emit_stubs=False)
        self.addCleanup(other.close)

        with self.inst.Bench.claim("mine") as claim:
            with self.assertRaises(ValueError) as caught:
                other.Bench.reason_for(claim)

        self.assertIn("another instrument", str(caught.exception))

    def test_a_dropped_handle_is_released_on_the_next_call(self):
        claim = self.inst.Bench.claim("forgotten")
        handle_id = claim._id

        del claim
        gc.collect()

        # The finaliser only queues the id; the release rides along with the next call.
        self.inst.XMotor.get_position()

        with self.assertRaises(HandleReleased):
            self.inst._transport.call(f"{handle_id}.use")

    # -- sessions --------------------------------------------------------------

    def test_a_second_session_can_read_but_not_change(self):
        self.inst.XMotor.home()

        watching = _connect(emit_stubs=False)
        self.addCleanup(watching.close)

        self.assertIsInstance(watching.XMotor.get_position(), float)

        with self.assertRaises(ControlRequired):
            watching.XMotor.move_to(1.0)

    def test_control_can_be_reported(self):
        self.inst.XMotor.home()
        self.assertTrue(self.inst.heartbeat()["has_control"])

    # -- stubs -----------------------------------------------------------------

    def test_stubs_are_written_and_importable(self):
        stub = self.inst.stub_path / "kjx_instrument_api.pyi"

        self.assertTrue(stub.exists(), f"no stub at {stub}")

        text = stub.read_text(encoding="utf-8")
        self.assertIn("class Motor(Device, Protocol):", text)
        self.assertIn("def move_to(self, new_position: float) -> None:", text)
        self.assertIn("class _XMotor(Motor, SupportsHoming, Protocol):", text,
                      "a device's stub combines every capability it was configured with")
        self.assertIn("XMotor: _XMotor", text)
        self.assertIn("def readings(self) -> Subscription[SensorReading]:", text)
        self.assertIn("AbstractContextManager[ScriptTestClaim]", text,
                      "forgetting 'with' on a handle should not type check")

        import kjx_instrument_api  # noqa: F401  the emitted path is on sys.path

    def test_the_descriptor_hash_identifies_the_surface(self):
        self.assertTrue(self.inst.descriptor_hash.startswith("sha256:"))


@unittest.skipIf(not SOCKET, "KJX_TEST_SOCKET is not set.")
@unittest.skipUnless(hasattr(socket, "AF_UNIX"), "This Python has no unix domain socket support.")
class UnixSocketTests(unittest.TestCase):
    def test_the_local_socket_needs_no_token(self):
        with connect(socket_path=SOCKET, emit_stubs=False) as inst:
            self.assertIn("dev/XMotor", inst.devices)


if __name__ == "__main__":
    unittest.main()
