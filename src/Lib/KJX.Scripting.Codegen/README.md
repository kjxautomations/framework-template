# Scripting Codegen

## About
An incremental source generator that turns the `[ScriptApi]` interfaces of a project into
everything that follows from them:

- **`ScriptApiDescriptor.g.cs`** — the whole surface as JSON with a SHA-256 content hash: types,
  members, parameter names and types, defaults, doc text, enum values, DTO schemas, and which
  types are object references.
- **`<Type>Dispatcher.g.cs`** — a switch on wire name per interface, with typed argument
  extraction.
- **`ScriptApiValues.g.cs`** — readers and writers for every DTO, enum, collection and reference
  the surface uses.
- **`ScriptApiRegistry.g.cs`** — lookup by wire type name, and the catalog the RPC host consumes.

No reflection over parameters anywhere on the call path, which is what keeps it trim-safe and
leaves the NativeAOT path open.

The generated code reads arguments with `JsonElement` accessors and builds results with
`JsonValue` and `JsonObject` directly, rather than through a source-generated
`JsonSerializerContext`. Source generators cannot see each other's output, so a context emitted
here would never reach System.Text.Json's own generator; writing the readers and writers out is
what preserves the trim guarantee. The analyzer's closed type set is what makes doing so
exhaustively possible.

Referenced as an analyzer:

```xml
<ProjectReference Include="..\KJX.Scripting.Codegen\KJX.Scripting.Codegen.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

Its own diagnostics, `KJXSG001` to `KJXSG004`, cover what the analyzer cannot see from a single
interface: name collisions across the assembly, DTOs that cannot be rebuilt from their JSON form,
and a missing runtime reference.

See [KJX.Scripting](../KJX.Scripting/README.md) for the whole picture. The descriptor format is
pinned by golden files in `KJX.Scripting.Tests/TestData`.
