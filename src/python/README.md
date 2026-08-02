# kjx-instrument

The Python side of the instrument's scripting API. This is the only hand-written client code:
what a script can call comes from the descriptor the instrument serves on connect, which is
generated from the C# interfaces. See [KJX.Scripting](../Lib/KJX.Scripting/README.md) for the
design and the wire protocol.

## Install

```
pip install -e src/python
```

The only dependency is `websockets`.

## Connect

```python
from kjx_instrument import connect

inst = connect()                              # the unix socket on the instrument itself
inst = connect("bench-01:7443", token="...")  # a remote instrument, over TLS
```

With no arguments the client looks for the instrument's unix socket. Access to it is controlled
by the socket's file permissions, so no token is involved. A remote connection needs the bearer
token the host was configured with.

## Call things

Devices are attributes, named as they are in `system_config.ini`, with a snake_case alias:

```python
inst.XMotor.home()
inst.x_motor.move_to(2.5)          # the same device
print(inst.XMotor.get_position())
```

A device answers to every interface it was configured with. `XMotor` is declared with both
`IMotor` and `ISupportsHoming`, so it has `home()`; a motor configured without the homing
interface does not, and asking for it raises `AttributeError` without a round trip.

## Streams

```python
with inst.TemperatureSensor1.readings() as readings:
    for reading in readings:
        print(reading["timestamp"], reading["value"])
```

Each subscription has its own bounded queue on the instrument. A client that cannot keep up loses
the oldest values rather than making the device wait; `readings.dropped` counts what was missed.

## Handles

A call that returns an object hands back a handle, which holds something on the instrument until
it is released. Use `with`:

```python
with inst.Bench.claim("washing") as claim:
    claim.use()
```

Forgetting `with` is a type error against the generated stubs. If it happens anyway, the handle
is released when Python collects it, and if the script crashes outright the instrument releases
everything the session held when the connection drops.

## Sessions

One session at a time has control of the instrument. It is taken implicitly by the first call
that changes something, and lost after a period of silence, so a laptop that goes to sleep does
not wedge the instrument. Other sessions can read and subscribe:

```python
watching = connect(...)
watching.XMotor.get_position()   # fine
watching.XMotor.move_to(1.0)     # raises ControlRequired
```

## Editor support

On connect the client writes `.pyi` stubs for the instrument it is talking to, under
`~/.cache/instrument/<descriptor-hash>/`, and puts that directory on `sys.path`. The path is also
in `inst.stub_path`, and the newest one is recorded in `~/.cache/instrument/latest.txt` so an
editor can be pointed at it.

The stubs describe each device with the capabilities it actually has, so completion on
`inst.XMotor.` lists `home` and completion on `inst.YMotor.` does not.

## Examples

[examples/](examples/) holds complete projects written against this client, each one a directory
you can open in PyCharm and run. [home_and_move](examples/home_and_move/) homes the X and Y motors
and then walks them out a millimetre at a time.

## Tests

`tests/test_units.py` needs nothing running. It covers the value conversions, the socket
discovery, and the stub emitter — the last of those renders from the C# generator's own golden
descriptor, so both ends of the pipeline are checked against each other:

```
PYTHONPATH=. python -m unittest tests.test_units -v
```

`tests/test_end_to_end.py` needs a live instrument, and reads `KJX_TEST_ENDPOINT`,
`KJX_TEST_TOKEN` and `KJX_TEST_SOCKET` from the environment. The C# test
`KJX.Tests.TestPythonClient` starts a host over the simulated devices, sets those, and runs the
whole suite, so `dotnet test` covers this too. It is skipped, with an explanation, on a machine
with no interpreter that can import `websockets`; set `KJX_PYTHON` to point at one.

