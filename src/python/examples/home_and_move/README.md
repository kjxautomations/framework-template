# home_and_move

Homes the X and Y motors, then walks them out 1 mm at a time, a second apart, to 10 mm.

Everything is in [home_and_move.py](home_and_move.py). Open this directory as a PyCharm project
and run it — a run configuration is checked in, so it appears in the toolbar as **Home and move**.

## Set up

Point PyCharm at an interpreter (**Settings → Project → Python Interpreter**), then install the
client into it:

```
pip install -r requirements.txt
```

That installs [kjx-instrument](../../README.md) from this repository, along with `websockets`.
PyCharm also offers this itself when it notices `requirements.txt`.

## Have something to talk to

The script drives whatever instrument is running, simulated devices included:

```
dotnet run --project ../../../KJX.ProjectTemplate.Engineering
```

Both applications serve scripts. Their `system_config.ini` gives `XMotor` and `YMotor` the
`ISupportsHoming` interface, which is where `home()` comes from.

**On Linux or macOS** that is all: the application leaves a unix socket behind, the script finds
it, and the socket's file permissions are the access control. Run one application at a time —
with two sockets there, the client will not guess between them.

**On Windows** CPython has no `AF_UNIX`, so the script cannot use that socket and needs a network
endpoint instead. In `App.axaml.cs`, in `StartScriptingHost`, give the host a port and a token:

```csharp
var options = ScriptApiHostOptions.ForLocalInstrument("kjx-engineering");
options.Port = 7443;
options.Token = "bench-token";
```

Then run the script against it. A host with no certificate configured serves plain WebSocket, so
say `ws://` explicitly — a bare `host:port` means TLS:

```
python home_and_move.py --endpoint ws://127.0.0.1:7443 --token bench-token
```

`KJX_INSTRUMENT_ENDPOINT` and `KJX_INSTRUMENT_TOKEN` work instead of the arguments, and PyCharm
can set them under **Edit Configurations → Environment variables**.

## What it does

```
Connected. Type stubs for this instrument are in C:\Users\you\.cache\instrument\sha256-1f4c....
Homing XMotor.
Homing YMotor.
XMotor is at 1.000.
YMotor is at 1.000.
...
XMotor is at 10.000.
YMotor is at 10.000.
Done. Each motor moved 10.
```

Homing sets a motor's position to 0, so the script moves to absolute targets — 1 mm, 2 mm, up to
10 mm — rather than adding a millimetre to wherever it thinks the motor is. `move_to` returns when
the motor has stopped, so the moves happen one after another and the code reads as a procedure.

What it drives and how far are the four constants at the top of the script — `MOTORS`, `STEP`,
`MOVES` and `DWELL`. Change them there. The only arguments are `--endpoint` and `--token`, and
they exist because a connection from another machine, or from Windows, needs them.

## Completion in the editor

On connect the client writes `.pyi` stubs describing *this* instrument, and prints where they
went. To get completion on `inst.XMotor.`, add that directory in **Settings → Project → Project
Structure → Add Content Root**. It is regenerated whenever the instrument's API changes, under a
new hash, and the newest one is recorded in `~/.cache/instrument/latest.txt`.

The stubs know which devices have which capabilities: completion on a motor configured without
`ISupportsHoming` has no `home`.

## If it does not connect

| It says                                          | Because                                                             |
|--------------------------------------------------|---------------------------------------------------------------------|
| `No instrument socket found at ...`               | Nothing is running, or you are on Windows — use `--endpoint`.        |
| `More than one instrument is running: ...`        | Both applications are up, or one left its socket file behind.        |
| `This platform has no unix domain sockets`        | Windows again: `--endpoint ws://127.0.0.1:7443 --token ...`.         |
| `'ws://...' needs a token`                        | The host refuses to listen on a port without one, so pass `--token`. |
| `This instrument has no device 'XMotor'`          | The name does not match a section in `system_config.ini`.            |
| `'dev/YMotor' has no member 'home'`               | That motor is configured without `ISupportsHoming`.                  |
| `Another session has control of the instrument`   | Something else is connected and holding the control lease.           |
