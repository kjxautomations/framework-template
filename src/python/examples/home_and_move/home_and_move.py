"""Homes the X and Y motors, then walks them out 1 mm at a time to 10 mm.

    python home_and_move.py
    python home_and_move.py --endpoint ws://127.0.0.1:7443 --token bench-token

With no arguments this connects to the instrument running on this machine. See the README for
running it against one on the network, which Windows needs.
"""

from __future__ import annotations

import argparse
import os
import sys
import time

from kjx_instrument import ConnectionLost, ControlRequired, InstrumentError, connect

MOTORS = ("XMotor", "YMotor")
"""The devices to drive, named as they are in system_config.ini."""

STEP = 1.0
"""One move, in the motor's units."""

MOVES = 10
"""How many moves to make. Ten steps of a millimetre is ten millimetres."""

DWELL = 1.0
"""Seconds to wait between one move and the next."""


def main() -> int:
    options = _parse_arguments()

    try:
        with _open_instrument(options) as instrument:
            print(f"Connected. Type stubs for this instrument are in {instrument.stub_path}.")

            # One session at a time may change anything, so ask for control up front: a refusal
            # is better here than part way through the run.
            if not instrument.acquire_control():
                print("Another session has control of the instrument.", file=sys.stderr)
                return 1

            motors = [getattr(instrument, name) for name in MOTORS]

            for name, motor in zip(MOTORS, motors):
                # Both are safe to repeat, and both are there because the configuration lists
                # ISupportsInitialization and ISupportsHoming for these devices.
                motor.initialize()
                print(f"Homing {name}.")
                motor.home()

            # Homing leaves a motor at 0, so the targets are absolute: 1 mm, 2 mm, ... 10 mm.
            for move in range(1, MOVES + 1):
                target = STEP * move

                for name, motor in zip(MOTORS, motors):
                    # move_to returns when the motor has stopped, so the moves are one after
                    # another and this reads as the procedure it is.
                    motor.move_to(target)
                    print(f"{name} is at {motor.get_position():.3f}.")

                if move < MOVES:
                    time.sleep(DWELL)

    except ControlRequired as taken:
        print(f"The instrument is under another session's control: {taken}", file=sys.stderr)
        return 1
    except ConnectionLost as unreachable:
        print(unreachable.message, file=sys.stderr)
        return 1
    except AttributeError as unsuitable:
        # A device that is not configured, or not configured with homing, fails here rather than
        # as a round trip: a device carries the interfaces its configuration gives it.
        print(unsuitable, file=sys.stderr)
        return 1
    except InstrumentError as refused:
        print(f"The instrument refused the run: {refused}", file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("Stopped. The motors are wherever the last move left them.", file=sys.stderr)
        return 130

    print(f"Done. Each motor moved {STEP * MOVES:g}.")
    return 0


def _open_instrument(options: argparse.Namespace):
    """Connects, using the socket on this machine unless an endpoint was given."""
    if options.endpoint and not options.token:
        raise ConnectionLost(
            f"'{options.endpoint}' needs a token. The instrument will not listen on the network "
            "without one, so pass --token."
        )

    return connect(options.endpoint or None, token=options.token or None)


def _parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=__doc__.splitlines()[0],
        epilog="With neither argument, this connects to the instrument running on this machine.",
    )

    parser.add_argument(
        "--endpoint",
        default=os.environ.get("KJX_INSTRUMENT_ENDPOINT"),
        help="host:port for a TLS host, or ws://host:port for one without a certificate. "
             "Left out, the client looks for the socket of an instrument on this machine.",
    )
    parser.add_argument(
        "--token",
        default=os.environ.get("KJX_INSTRUMENT_TOKEN"),
        help="The bearer token a network endpoint requires.",
    )

    return parser.parse_args()


if __name__ == "__main__":
    sys.exit(main())
