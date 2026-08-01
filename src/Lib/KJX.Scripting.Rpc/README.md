# Scripting RPC Host

## About
Serves the generated dispatch to scripts over JSON-RPC 2.0 on a WebSocket. Kestrel listens on a
unix domain socket for clients running on the instrument and, when a token is configured, on TCP
for remote ones. The protocol is identical on both.

Hosted inside the application that owns the devices, so dispatch is an in-process call into the
same container the UI binds to:

```csharp
var host = ScriptApiHost.Create(
    container,
    ScriptApiHostOptions.ForLocalInstrument("kjx-engineering"),
    new IScriptApiCatalog[] { ScriptApiCatalog.Instance },
    loggerFactory);

await host.StartAsync();
```

What it does:

- **Builds the device namespace from the container.** It enumerates the Autofac registry and keeps
  the services the generated catalogs know about, so adding a device to `system_config.ini`
  exposes it to scripts with no code change. A device carries the set of interfaces it was
  registered under, which is how a motor configured with `ISupportsHoming` answers `home` and one
  configured without it does not.
- **Gives every session its own scope and handle table.** Objects returned from a call become
  `h/<n>`; devices stay `dev/<id>` and are never released. Session disposal is the backstop that
  releases whatever a crashed script was holding.
- **Keeps one exclusive control lease.** Other sessions attach read-only, and a holder that goes
  quiet loses it.
- **Audit-logs every invocation** with the session, principal, target, member, arguments, result
  and timings.

Security posture: the unix socket is authorised by its file permissions; a TCP port requires a
bearer token, and `ScriptApiHost.Create` refuses to build a host that would listen on the network
without one. `ScriptApiHostOptions` also carries the control-lease timeout, the handle idle
timeout, and the bound on how far a slow client may fall behind before stream values are dropped.

See [KJX.Scripting](../KJX.Scripting/README.md) for the protocol and the error codes.
