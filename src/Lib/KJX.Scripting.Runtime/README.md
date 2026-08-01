# Scripting Runtime

## About
The small surface the generated dispatch is written against. Referenced as a normal library by any
project that declares `[ScriptApi]` interfaces.

- `IScriptApiDispatcher` — one generated dispatch table: its wire type name, the types it extends,
  its members, and how to invoke or subscribe to them.
- `IScriptApiCatalog` — one assembly's generated surface, which is what the host is handed.
- `IScriptApiReferences` — turns `{"$ref": ..., "$type": ...}` into a live object and back. The
  host implements it, because the device registry and the session's handle table are what know
  the answers; generated code never resolves a reference itself.
- `ScriptApiJson` — typed readers for incoming arguments. Every failure names the member and the
  parameter that caused it.
- `ScriptApiException` and `ScriptApiErrorCodes` — errors that map straight onto a JSON-RPC error
  object.

Nothing here uses reflection, so the whole call path stays trim-safe.

See [KJX.Scripting](../KJX.Scripting/README.md).
