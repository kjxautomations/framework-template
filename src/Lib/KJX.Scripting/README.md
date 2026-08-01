# Scripting

Exposes an instrument's devices to Python over JSON-RPC, without anyone hand-writing a wire
format, a dispatcher, or a client stub.

## The one rule

**The C# interfaces are the single source of truth.** Everything else is generated from them: the
RPC dispatch, the API descriptor, and the Python type stubs. No generated artifact is checked in.

Mark an interface and it is scriptable:

```csharp
[ScriptApi]
public interface IMotor : IDevice
{
    double Position { get; }
    void MoveTo(double newPosition);
}
```

Add a device to `system_config.ini` and it is addressable:

```ini
[XMotor]
_type = KJX.Devices.SimulatedLinearStepperMotor, KJX.Devices
_interface1 = KJX.Devices.IMotor, KJX.Devices
_interface3 = KJX.Devices.ISupportsHoming, KJX.Devices
```

Then, from Python:

```python
inst = connect()
inst.XMotor.move_to(2.5)
inst.XMotor.home()          # because the configuration lists ISupportsHoming
```

Neither step involves writing any code on the wire, the host, or the client.

## `[ScriptApi]`

The only annotation the system requires or permits. It takes one optional string for the wire type
name; the default is the interface name minus a leading `I`, in snake_case.

```csharp
[ScriptApi]                  // -> "syringe_pump"
[ScriptApi("recipes")]       // -> "recipes"
```

Every public member of a marked interface is scriptable. There is no per-member opt-out. **To
exclude a member, put it on a separate unmarked interface** implemented by the same class —
`ISupportsSensorOverride` in `KJX.Devices` is there for exactly that reason.

Members inherited from *unmarked* base interfaces are part of the surface, which is how
`IDevice.IsBusy` and `ISupportsInitialization.Initialize()` become scriptable. Members inherited
from another `[ScriptApi]` interface are **not** re-exported; the descriptor records the
inheritance and the host dispatches them to the base type's own table.

### What a member becomes

| C# member shape                        | Wire form                        |
|----------------------------------------|----------------------------------|
| `Task` / `ValueTask`                   | call, no result                  |
| `Task<T>` / `ValueTask<T>`             | call returning T                 |
| `T Method(...)` (synchronous)          | call returning T                 |
| `IAsyncEnumerable<T>` method or property | subscription                   |
| get-only property                      | call `get_<name>`                |
| get/set property                       | `get_<name>` and `set_<name>`    |
| trailing `CancellationToken`           | supplied by the host, not by the script |
| `[ScriptApi]` type as parameter or return | object reference              |
| `event`, delegate, `Func<>`, `Action<>` | **compile error**               |

Names go PascalCase to snake_case, with a trailing `Async` stripped: `StopAsync()` is `stop`,
`MoveTo(double)` is `move_to`. XML doc comments become the Python docstrings and stub comments, so
document the interface and the rest follows.

### Infrastructure interfaces

Members declared on `INotifyPropertyChanged`, `INotifyPropertyChanging`, `INotifyDataErrorInfo`,
`IDisposable`, `IAsyncDisposable` and `IEquatable<T>` are neither exported nor diagnosed. Without
this, marking any device interface would fail to compile: `IDevice` inherits
`INotifyPropertyChanged` for the Avalonia bindings and the configuration's dirty tracking, and an
event is otherwise an error. The list is closed — an event you declare yourself is still an error.

## Capabilities

A device is addressed as the **set** of interfaces it was registered under, not as one type. This
matters because capabilities in this codebase are mixed in per instance:

```ini
[XMotor]
_interface1 = KJX.Devices.IMotor, KJX.Devices
_interface3 = KJX.Devices.ISupportsHoming, KJX.Devices    ; this motor homes

[YMotor]
_interface1 = KJX.Devices.IMotor, KJX.Devices             ; this one does not
```

`describe` reports it, and the Python client and the generated stubs both follow:

```json
{"id": "dev/XMotor", "types": ["motor", "supports_homing"]}
{"id": "dev/YMotor", "types": ["motor"]}
```

`inst.YMotor.home` raises `AttributeError` in the client, without a round trip. A device whose
interfaces declare the same member name is refused when the host starts, rather than resolving one
of them silently.

## The permitted type set

The analyzer holds the surface to types that can be described, serialized and rebuilt:

primitives, `string`, `decimal`, `DateTimeOffset`, `TimeSpan`, `Guid`, enums, `record` and
`record struct` DTOs composed of those, single-dimension arrays and `IReadOnlyList<T>` of those,
nullable forms of any of them, and `[ScriptApi]` interfaces at parameter or return position.

On the wire: enums travel as the name of the value, `DateTimeOffset` as ISO 8601, `TimeSpan` as
`[-][d.]hh:mm:ss[.fffffff]`, references as `{"$ref": "dev/XMotor", "$type": "motor"}`.

### Diagnostics

The analyzer is what keeps the generator total: if it compiles, the generator can emit dispatch,
a descriptor entry and a client proxy for it with no remaining cases. All are errors except
KJXSG003.

| Id       | Rejects                                                            |
|----------|--------------------------------------------------------------------|
| KJXSA001 | A type outside the permitted set                                    |
| KJXSA002 | Events                                                              |
| KJXSA003 | Generic methods and generic interfaces                              |
| KJXSA004 | `ref`, `out` and `in`                                               |
| KJXSA005 | Two members that reduce to the same wire name, including overloads  |
| KJXSA006 | A `[ScriptApi]` reference buried inside a DTO                       |
| KJXSA007 | Delegates                                                           |
| KJXSA008 | Indexers and static members                                         |
| KJXSG001 | Two types claiming the same wire name                               |
| KJXSG002 | A DTO that cannot be rebuilt from its JSON form                     |
| KJXSG003 | A member left out of dispatch (warning; only if a KJXSA was suppressed) |
| KJXSG004 | A project with `[ScriptApi]` interfaces but no reference to `KJX.Scripting.Runtime` |

## Wire protocol

JSON-RPC 2.0 over a WebSocket. Arguments are **by name only**; a positional `params` array is
rejected.

| Method                          | Does                                                     |
|---------------------------------|----------------------------------------------------------|
| `describe`                      | The whole surface plus the device list, and a content hash |
| `<target>.<member>`             | A call, e.g. `dev/XMotor.move_to`                        |
| `subscribe` / `unsubscribe`     | Opens and closes a stream                                |
| `release`                       | Releases one handle, or a batch of them                  |
| `acquire_control` / `release_control` / `heartbeat` | The control lease         |
| `$/cancelRequest`               | Notification: cancels an in-flight request by id         |
| `$/stream`                      | Notification from the host: a value, a drop count, completion or an error |

Errors use the standard codes where they fit and the implementation-defined range otherwise:

| Code    | Meaning                | Code    | Meaning                     |
|---------|------------------------|---------|-----------------------------|
| -32000  | target not found       | -32005  | wrong member kind           |
| -32001  | handle not found       | -32006  | control lease required      |
| -32002  | handle expired         | -32010  | the device threw            |
| -32003  | handle type mismatch   | -32600  | invalid request             |
| -32004  | handle is foreign      | -32601  | member not found            |
| -32700  | parse error            | -32602  | invalid params              |
| -32800  | request cancelled      |         |                             |

`error.data` names the member and the parameter that caused the problem, and what was expected.

## Sessions, handles and control

Every connection gets its own Autofac child scope and its own handle table.

**References come in two kinds.** `dev/<id>` is a configured device: stable for the life of the
process, never released. `h/<n>` is something the session created and holds — an open acquisition,
a claimed channel. Handles are minted automatically when a call returns a `[ScriptApi]` type.

**Releasing them, in order of reliance:**

1. Explicit `release`, which the Python client does from a `with` block. The primary idiom.
2. Session disposal, which is the backstop. A crashed script or a dropped socket releases
   everything that session held. This is tested by killing a client mid-operation.
3. The Python finaliser, which only *queues* the id; the release rides along with the next call.

**One session holds control.** It is taken implicitly by the first member that changes state, and
lost after a period of silence, so a laptop that goes to sleep does not wedge the instrument.
Other sessions attach read-only: getters and subscriptions are always allowed, setters and methods
are not. What counts as changing state comes from what the member was written as, so no extra
annotation was needed.

Every invocation is audit-logged with the session, the principal, the target, the member, the
arguments, the result and the timings.

## Hosting

The host is built into both applications and starts after the container is built:

```csharp
var options = ScriptApiHostOptions.ForLocalInstrument("kjx-engineering");

ScriptingHost = ScriptApiHost.Create(
    Container, options, new IScriptApiCatalog[] { ScriptApiCatalog.Instance },
    Container.Resolve<ILoggerFactory>());

ScriptingHost.StartAsync().GetAwaiter().GetResult();
```

By default it listens only on a unix domain socket, where the socket's file permissions are the
access control. Serving the network is deliberate: setting `Port` **requires** a `Token`, and the
host refuses to start otherwise. Point `CertificatePath` at a PKCS#12 file for TLS.

If the host fails to start it is logged and the application carries on — losing the script
interface must not stop someone operating the instrument by hand.

## Where things live

| Project                   | Holds                                                          |
|---------------------------|----------------------------------------------------------------|
| `KJX.Scripting`           | The `[ScriptApi]` attribute and the naming rules (netstandard2.0) |
| `KJX.Scripting.Analyzer`  | The Roslyn analyzer that enforces the rules above               |
| `KJX.Scripting.Codegen`   | The generator: descriptor, dispatch tables, value readers and writers |
| `KJX.Scripting.Runtime`   | What the generated code is written against                     |
| `KJX.Scripting.Rpc`       | The JSON-RPC host, sessions, handles, the control lease         |
| `src/python/kjx_instrument` | The Python client and stub emitter — the only hand-written client code |

A project with `[ScriptApi]` interfaces references the attribute, the runtime, and the analyzer and
generator as analyzers:

```xml
<ProjectReference Include="..\KJX.Scripting\KJX.Scripting.csproj" />
<ProjectReference Include="..\KJX.Scripting.Runtime\KJX.Scripting.Runtime.csproj" />
<ProjectReference Include="..\KJX.Scripting.Analyzer\KJX.Scripting.Analyzer.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
<ProjectReference Include="..\KJX.Scripting.Codegen\KJX.Scripting.Codegen.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

Generated code lands in `<RootNamespace>.Generated`, so two assemblies with a scripting surface
never collide. To read it, build with
`/p:EmitCompilerGeneratedFiles=true /p:CompilerGeneratedFilesOutputPath=obj/generated`.

## Status

Built and tested: the attribute and analyzer, the descriptor and dispatch generator, the JSON-RPC
host, and the Python client and stub emitter.

Not built:

- **Generated C# client proxies**, and with them the Avalonia app running against a remote
  instrument. Before writing these, settle whether the proxies block: the device interfaces are
  synchronous (`void MoveTo(double)`), and a proxy that blocks the UI thread on a socket round trip
  is not what you want behind a touchscreen. Either the ViewModels already call off-thread, or the
  interfaces grow async variants.
- **The on-device script runner** — launching CPython under `systemd-run` with a scoped token, a
  memory cap and a CPU quota.

Known gaps:

- `ICamera` is not marked. `System.Drawing.Size` is outside the permitted type set, and image
  transfer needs a design of its own — a frame per JSON-RPC call is a poor fit.
- Host options are constructed in `App.axaml.cs` rather than read from `system_config.ini`, to keep
  them out of the configuration system's dirty-value tracking. Moving them to a `[Scripting]`
  section is small.
- TLS is wired but has no test covering it.
- The Python client's unix-socket path has not run on Windows, because CPython there does not
  expose `AF_UNIX`. The host side of that transport is tested from .NET, and Linux has `AF_UNIX`.
