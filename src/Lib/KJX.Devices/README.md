# Devices

## About
A core library that contains patterns and implementations for the back-end device logic. 
Devices can be injected into their corresponding views in the DevicesUI library.

## Scripting

Most of the device interfaces are marked `[ScriptApi]`, which is all it takes to make them
callable from Python. See [KJX.Scripting](../KJX.Scripting/README.md) for the whole picture.

| Interface           | Wire type         |
|---------------------|-------------------|
| `IDevice`           | `device`          |
| `IMotor`            | `motor`           |
| `IStepperMotor`     | `stepper_motor`   |
| `ILed`              | `led`             |
| `ISensor`           | `sensor`          |
| `ISupportsHoming`   | `supports_homing` |

`ISupportsInitialization` is not marked, and does not need to be: it is inherited by `IDevice`, so
`initialize`, `shutdown`, `get_is_initialized` and `get_initialization_group` are part of every
device's surface already.

`ICamera` is **not** marked. `System.Drawing.Size` is outside the permitted type set, and moving a
frame per JSON-RPC call is a poor fit for image transfer; it needs a design of its own.

### Capabilities are per instance

`ISupportsHoming` is a capability a driver mixes in, not a kind of device — `MotorBaseSupportsHoming`
exists so that some motors home and others do not. Which interfaces an instance exposes is decided
by its configuration:

```ini
[XMotor]
_interface1 = KJX.Devices.IMotor, KJX.Devices
_interface3 = KJX.Devices.ISupportsHoming, KJX.Devices    ; this motor homes

[YMotor]
_interface1 = KJX.Devices.IMotor, KJX.Devices             ; this one does not
```

A script sees exactly that: `XMotor` has `home`, `YMotor` does not.

### Sensors publish a stream, not an event

`ISensor` exposes readings as `IAsyncEnumerable<SensorReading>` rather than an event, because a
callback cannot cross the script boundary:

```csharp
IAsyncEnumerable<SensorReading> Readings(CancellationToken cancellationToken = default);
```

Every read is published, **including reads that produced the same value as the one before**, so a
plot of the stream shows the true sample rate rather than only the changes. A driver calls
`SensorBase.PublishReading()` after setting `Value`; each subscriber gets its own bounded queue and
loses its oldest readings rather than slowing the device down.

In-process consumers subscribe the same way the wire does:

```csharp
await foreach (var reading in sensor.Readings(cancellationToken))
    Plot(reading.Value, reading.Timestamp);
```

`OverrideSensorValue` lives on `ISupportsSensorOverride`, which is deliberately **not** marked: it
takes a callback, and replacing a sensor's value is not something a script should be able to do.
That separate-interface trick is the supported way to keep a member off the scripting surface.

### Writing a new device

Mark the interface, keep its members inside the permitted type set, and the analyzer will tell you
at compile time if something cannot cross the boundary. Everything else — dispatch, the descriptor,
the Python proxies and stubs — is generated.
