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

## Making something scriptable

Three things have to line up, and they are independent of each other:

| What                                                                          | Where it is done                                              |
|-------------------------------------------------------------------------------|---------------------------------------------------------------|
| The **assembly** runs the analyzer and the generator, and its catalog reaches the host | [Setting up an assembly](#setting-up-an-assembly)             |
| The **interface** is marked `[ScriptApi]` and stays inside the permitted types | [`[ScriptApi]`](#scriptapi), [the permitted type set](#the-permitted-type-set) |
| The **object** is addressable: a configured device, a registration you named, or something a call returned | [Making an object addressable](#making-an-object-addressable) |

Only the second is a compile-time affair. Getting the first or the third wrong is quiet: the
member simply is not in `describe`, and the script gets an `AttributeError`.
[When something does not show up](#when-something-does-not-show-up) says which one it was.

## Setting up an assembly

Any assembly may declare `[ScriptApi]` interfaces — the device library, an application project, a
test project. It needs four references and one property:

```xml
<PropertyGroup>
  <!-- XML doc comments become the Python docstrings and the stub comments. -->
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <NoWarn>$(NoWarn);CS1591</NoWarn>
</PropertyGroup>

<ItemGroup>
  <ProjectReference Include="..\KJX.Scripting\KJX.Scripting.csproj" />
  <ProjectReference Include="..\KJX.Scripting.Runtime\KJX.Scripting.Runtime.csproj" />
  <!-- Enforces the script API rules on every [ScriptApi] interface in this project. -->
  <ProjectReference Include="..\KJX.Scripting.Analyzer\KJX.Scripting.Analyzer.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  <!-- Emits the descriptor and the dispatch tables for those same interfaces. -->
  <ProjectReference Include="..\KJX.Scripting.Codegen\KJX.Scripting.Codegen.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

What each one is for, and what its absence looks like:

| Reference                | Absence                                                                                   |
|--------------------------|-------------------------------------------------------------------------------------------|
| `KJX.Scripting`          | `[ScriptApi]` does not resolve                                                             |
| `KJX.Scripting.Runtime`  | KJXSG004, and nothing is generated                                                         |
| `KJX.Scripting.Analyzer` | Members outside the permitted set are dropped from the surface with a KJXSG003 warning, rather than rejected where they are declared |
| `KJX.Scripting.Codegen`  | No dispatch, no descriptor, no catalog — the interfaces compile and mean nothing            |

`KJX.Scripting.Rpc` is *not* on that list. Only the application that hosts the endpoint needs it.

### Hand the catalog to the host

The generator emits `ScriptApiRegistry` and `ScriptApiCatalog` into `<RootNamespace>.Generated`
— the assembly name when there is no root namespace — one pair per assembly, so two assemblies
with a scripting surface never collide on a type name. **An assembly is
served only if its catalog is passed to `ScriptApiHost.Create`**, which is the step that is easy to
miss when the interfaces move to a project of their own:

```csharp
ScriptingHost = ScriptApiHost.Create(
    Container,
    options,
    new IScriptApiCatalog[]
    {
        KJX.Devices.Generated.ScriptApiCatalog.Instance,
        MyCompany.Instrument.Generated.ScriptApiCatalog.Instance,   // a second surface
    },
    Container.Resolve<ILoggerFactory>());
```

The catalogs are merged into one surface, so an interface in one assembly may take or return a
`[ScriptApi]` interface from another. Leave one out and the types it declares are unknown to the
host: devices registered under them disappear from `describe`, and a member that returns one of
them hands the client a reference to a type it cannot describe.

To read what was generated, build with
`/p:EmitCompilerGeneratedFiles=true /p:CompilerGeneratedFilesOutputPath=obj/generated`.

## Making an object addressable

A script reaches an object in one of three ways. The first two put it in the device namespace,
where it is permanent and shared; the third mints a handle that belongs to one session.

### A device from `system_config.ini`

The ordinary case, and no code. `ConfigurationHandler` registers the type under each
`_interfaceN` line and tags the registration with the section name; the host enumerates the
container and keeps whatever the catalogs know about:

```ini
[XMotor]
_type = KJX.Devices.SimulatedLinearStepperMotor, KJX.Devices
_interface1 = KJX.Devices.IMotor, KJX.Devices
_interface3 = KJX.Devices.ISupportsHoming, KJX.Devices
```

**The `_interfaceN` lines are the surface, not the class.** A driver that implements
`ISupportsHoming` but is configured without it does not home from a script — see
[Capabilities](#capabilities). Lines naming an interface that is not `[ScriptApi]`, such as
`ISupportsInitialization`, are ignored here and do no harm.

### A registration you write yourself

Anything in the container is a candidate, not only things that came from the configuration file. A
recipe library, a run manager, a calibration service: give the registration a `Name` and it is
addressable by that name.

```csharp
builder.RegisterType<RecipeLibrary>()
       .As<IRecipeLibrary>()                      // a [ScriptApi] interface
       .Keyed<IRecipeLibrary>("Recipes")          // how the host resolves it
       .WithMetadata("Name", "Recipes")           // what makes it visible, and its wire id
       .SingleInstance();                         // the script and the UI share one object
```

That appears as `dev/Recipes`, and in Python as `inst.Recipes`. Three details are load-bearing:

- **`WithMetadata("Name", …)`** is the whole opt-in. A registration without it is skipped, which
  is why the container's plumbing — loggers, view models, factories — never leaks into the device
  namespace.
- **`SingleInstance()`**, because the host resolves the object once, while the host itself is being
  built, and holds it for the life of the process. Without it, the script talks to an instance
  nobody else has.
- **`Keyed<T>(name)`** is tried before a plain type resolve, so a named registration wins over any
  other registration of the same service. It is what `ConfigurationHandler` does, and it is the
  reason two registrations of one interface do not confuse the lookup.

As with a configured device, the surface is what you registered it `As`, not what the class
implements: register it under each `[ScriptApi]` interface you want a script to reach.



The device namespace is built when `ScriptApiHost.Create` runs, so register before the host is
built. A registration added afterwards is not seen, and a device that throws while being activated
takes the whole host down with it — logged, with the application carrying on without a scripting
endpoint.

### An object a call returned

Not everything wants to be a singleton in a container. An open acquisition, a claimed channel, a
recipe being edited — these are created per script, and a member that returns a `[ScriptApi]`
interface is all it takes:

```csharp
[ScriptApi]
public interface IRecipeLibrary
{
    /// <summary>Opens a recipe for editing. The caller has to release it.</summary>
    /// <param name="name">A recipe on the instrument.</param>
    IRecipe Open(string name);
}
```

`Open` returns a plain object — `new Recipe(...)` — and the host mints a handle for it. Nothing has
to be registered, disposed or tracked by the implementation. See
[Objects that refer to objects](#objects-that-refer-to-objects) for what that means on both sides.

## Objects that refer to objects

Any `[ScriptApi]` interface may be used as a parameter or a return type. On the wire that is a
reference, `{"$ref": "dev/XMotor", "$type": "motor"}`, and it is the only way one script object
reaches another.

### Returning one

What the host does with the object depends on whether it already knows it:

| The object                              | Comes back as                    | Released                          |
|-----------------------------------------|----------------------------------|-----------------------------------|
| Is a configured device or a named registration | `dev/<name>`, its existing id | Never                             |
| Is anything else                        | `h/<n>`, a fresh handle          | By the session that minted it     |

So a member may hand back a device: what crosses is the device's own `dev/<name>`, nothing is
minted, and releasing it does nothing on the instrument. Returning the same object twice returns
the same id both times — handing one object out under two ids would let a script hold what it
thought were two things.

A handle is disposed when it is released, if what it points at implements `IDisposable` or
`IAsyncDisposable`. That is the supported way to clean up: implement it on the class, and let
release, session teardown or the idle sweep call it. It does not have to be on the interface —
`IDisposable` and `IAsyncDisposable` are exempt from the surface, so declaring them there is
harmless but exports nothing.

**The declared return type is what the script gets.** A handle records every `[ScriptApi]`
interface its object implements, so it can be passed back as any of them, but the Python proxy is
built from the type the member declares. `IRecipe Open(string)` gives a script a recipe and nothing
more, whatever else the class implements. Declare the interface you mean.

Return a single reference, not a collection of them. `IReadOnlyList<IRecipe>` compiles and the host
mints a handle per element, but the Python client hands those back as raw `{"$ref": …}` dictionaries
rather than as objects. A member that returns a name and one that opens it by name is the shape
that works today.

### Passing one in

A device proxy and a handle are both accepted wherever a `[ScriptApi]` parameter is declared:

```csharp
/// <summary>Runs a recipe on a motor.</summary>
void Run(IRecipe recipe, IMotor motor);
```

```python
with inst.Recipes.open("wash") as recipe:
    recipe.add_step("home", 0.0)
    inst.Recipes.run(recipe, inst.XMotor)      # a handle and a device, in one call
```

The host resolves the id against the device registry and *this session's* handle table, then checks
that the target really answers to the declared type, following inheritance. What it will not do is
take the `$type` on the wire at its word. Three refusals worth knowing:

- A handle from another connection, or one already released — `-32001`/`-32002`, `HandleReleased`
  in Python. Handle tables are per session; there is no global one.
- An object of the wrong type — `-32003`, naming the type expected and the types the target has.
- A reference passed to a *different* instrument connection — refused by the client before it is
  sent, since the ids of two instruments have nothing to do with each other.

### A whole example

An assembly with a library in the container and recipes that are not:

```csharp
[ScriptApi]
public interface IRecipeLibrary
{
    /// <summary>The recipes on the instrument.</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>Opens a recipe for editing. The caller has to release it.</summary>
    /// <param name="name">A name from <see cref="Names"/>.</param>
    IRecipe Open(string name);

    /// <summary>Runs a recipe on a motor.</summary>
    /// <param name="recipe">An open recipe.</param>
    /// <param name="motor">The motor to run it against.</param>
    Task RunAsync(IRecipe recipe, IMotor motor, CancellationToken cancellationToken = default);
}

/// <summary>One recipe, held open by the session that opened it.</summary>
[ScriptApi]
public interface IRecipe
{
    /// <summary>What the recipe is called.</summary>
    string Name { get; }

    /// <summary>Appends a step.</summary>
    /// <param name="name">What the step is called.</param>
    /// <param name="position">Where the motor should end up.</param>
    void AddStep(string name, double position);

    /// <summary>Writes the recipe back.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
```

`RecipeLibrary` is registered with a `Name`; `Recipe` is `internal sealed`, implements
`IAsyncDisposable`, and is never registered anywhere. From Python:

```python
inst = connect()

print(inst.Recipes.get_names())

with inst.Recipes.open("wash") as recipe:
    recipe.add_step("home", 0.0)
    recipe.save()
    inst.Recipes.run(recipe, inst.XMotor)
```

The `with` block releases the recipe, which disposes it. If the script never gets there — an
exception, a killed interpreter, a dropped network — the session's teardown does the same thing.

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

A method is assumed to change the instrument and a getter is assumed not to, which is what decides
whether a session without the control lease may call it. Nothing annotates that; write a read as a
property and it stays readable to everyone.

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

Handles work the other way round: a handle answers to every `[ScriptApi]` interface its object
actually implements, because there is no configuration to consult. What the *client* offers is
still only the declared return type.

## The permitted type set

The analyzer holds the surface to types that can be described, serialized and rebuilt:

primitives, `string`, `decimal`, `DateTimeOffset`, `TimeSpan`, `Guid`, enums, `record` and
`record struct` DTOs composed of those, single-dimension arrays and `IReadOnlyList<T>` of those,
nullable forms of any of them, and `[ScriptApi]` interfaces at parameter or return position.

On the wire: enums travel as the name of the value, `DateTimeOffset` as ISO 8601, `TimeSpan` as
`[-][d.]hh:mm:ss[.fffffff]`, references as `{"$ref": "dev/XMotor", "$type": "motor"}`.

A DTO is a value and a reference is an identity, and the two do not mix: a `[ScriptApi]` interface
buried inside a DTO is KJXSA006. A DTO that arrives as an argument also has to be rebuildable from
its JSON form, through a matching constructor or settable properties, which is KJXSG002.

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

A handle nobody has touched for longer than `HandleIdleTimeout` is released as well, even while
the session is alive and heartbeating: a script can be running happily and still be sitting on a
claimed channel it forgot about.

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
interface must not stop someone operating the instrument by hand. Which means a mistake in this
area shows up as a log line and an instrument with no scripting, not as a crash.

## When something does not show up

| Symptom                                                              | Usually                                                                                     |
|----------------------------------------------------------------------|---------------------------------------------------------------------------------------------|
| The interfaces compile but no dispatch or catalog exists              | The codegen is not referenced with `OutputItemType="Analyzer"`, or KJXSG004 — no runtime reference |
| A device is not in `describe` at all                                  | Its registration has no `Name` metadata; or none of the interfaces it is registered under are `[ScriptApi]`; or its assembly's catalog was not passed to `ScriptApiHost.Create` |
| A device is there but one member is missing                           | The interface declaring it is not among the device's `_interfaceN` lines, or is not marked   |
| A member disappeared after an edit                                    | KJXSG003 in the build log: it fell outside the permitted type set                            |
| Nothing is scriptable and the log says the host did not start         | Two types claim one wire name; or a device is registered under two script interfaces that declare the same member; or a device threw while being activated |
| Python raises `AttributeError` on a member you know exists            | The device is not configured with that interface, or the object came back as a handle typed by its declared return type |
| An argument is refused as "not a device or a live handle"             | The handle belongs to another session or has been released                                   |
| Doc strings are missing from the stubs                                | `GenerateDocumentationFile` is not set on the declaring project                              |

`describe` is the thing to look at first — it is exactly what the client believes, and
`inst.descriptor.devices` in Python is the same document.

## Where things live

| Project                   | Holds                                                          |
|---------------------------|----------------------------------------------------------------|
| `KJX.Scripting`           | The `[ScriptApi]` attribute and the naming rules (netstandard2.0) |
| `KJX.Scripting.Analyzer`  | The Roslyn analyzer that enforces the rules above               |
| `KJX.Scripting.Codegen`   | The generator: descriptor, dispatch tables, value readers and writers |
| `KJX.Scripting.Runtime`   | What the generated code is written against                     |
| `KJX.Scripting.Rpc`       | The JSON-RPC host, sessions, handles, the control lease         |
| `src/python/kjx_instrument` | The Python client and stub emitter — the only hand-written client code |

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

- A collection of object references — `IReadOnlyList<IRecipe>` — dispatches and mints handles
  correctly, but the Python client decodes it as a list of raw reference dictionaries instead of
  proxies, and the stub says otherwise. Return one reference at a time until the client catches up.
- `ICamera` is not marked. `System.Drawing.Size` is outside the permitted type set, and image
  transfer needs a design of its own — a frame per JSON-RPC call is a poor fit.
- Host options are constructed in `App.axaml.cs` rather than read from `system_config.ini`, to keep
  them out of the configuration system's dirty-value tracking. Moving them to a `[Scripting]`
  section is small.
- TLS is wired but has no test covering it.
- The Python client's unix-socket path has not run on Windows, because CPython there does not
  expose `AF_UNIX`. The host side of that transport is tested from .NET, and Linux has `AF_UNIX`.
