using System.Text.Json;
using System.Text.Json.Nodes;
using Autofac;
using KJX.Scripting.Runtime;
using Microsoft.Extensions.Logging;

namespace KJX.Scripting.Rpc;

/// <summary>
/// One connected client. Everything the session created lives in its own Autofac scope and its
/// own handle table, so ending the session — cleanly or otherwise — releases all of it.
/// </summary>
public sealed class ScriptSession : IAsyncDisposable
{
    private readonly ILifetimeScope _scope;
    private readonly SessionManager _sessions;

    internal ScriptSession(
        string id,
        string principal,
        bool isLocal,
        ILifetimeScope scope,
        SessionManager sessions,
        ScriptApiSurface surface,
        IScriptDeviceSource devices,
        bool traceHandleAllocations)
    {
        Id = id;
        Principal = principal;
        IsLocal = isLocal;
        _scope = scope;
        _sessions = sessions;
        Handles = new HandleTable(traceHandleAllocations);
        References = new SessionReferences(surface, devices, Handles);
        Surface = surface;
        Devices = devices;
    }

    /// <summary>The session id, which appears in every audit record.</summary>
    public string Id { get; }

    /// <summary>Who is on the other end: a token subject, or the local user over the unix socket.</summary>
    public string Principal { get; }

    /// <summary>True when the client connected over the unix domain socket.</summary>
    public bool IsLocal { get; }

    /// <summary>Objects this session created and has not released.</summary>
    public HandleTable Handles { get; }

    /// <summary>Turns wire references into objects and back, for this session.</summary>
    public IScriptApiReferences References { get; }

    /// <summary>What the host knows how to dispatch.</summary>
    public ScriptApiSurface Surface { get; }

    /// <summary>The configured devices.</summary>
    public IScriptDeviceSource Devices { get; }

    /// <summary>The session's own dependency scope.</summary>
    public ILifetimeScope Scope => _scope;

    /// <summary>True while this session holds exclusive control of the instrument.</summary>
    public bool HasControl => _sessions.Lease.IsHeldBy(this);

    /// <summary>Finds a device or handle by wire id.</summary>
    public ScriptTarget ResolveTarget(string id)
    {
        if (id != null && Devices.Devices.TryGetValue(id, out var device))
            return device;

        if (id != null && id.StartsWith("h/", StringComparison.Ordinal) && Handles.TryGet(id, out var handle))
            return handle;

        throw new ScriptApiException(
            id != null && id.StartsWith("h/", StringComparison.Ordinal)
                ? ScriptApiErrorCodes.HandleNotFound
                : ScriptApiErrorCodes.TargetNotFound,
            $"'{id}' is not a device or a live handle",
            new JsonObject { ["target"] = id });
    }

    /// <summary>
    /// Releases the handle table first, then the scope. Disposal order matters: the handles hold
    /// the hardware claims, and the scope holds whatever created them.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _sessions.Ended(this);

        await Handles.DisposeAsync().ConfigureAwait(false);
        await _scope.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Hands out sessions and keeps the control lease. Sessions beyond the one holding control can
/// still read the instrument and subscribe to its streams.
/// </summary>
public sealed class SessionManager
{
    private readonly ILifetimeScope _root;
    private readonly ScriptApiSurface _surface;
    private readonly IScriptDeviceSource _devices;
    private readonly ScriptApiHostOptions _options;
    private readonly ILogger<SessionManager> _logger;
    private readonly List<ScriptSession> _sessions = new();
    private long _nextId;

    /// <summary>Creates the manager.</summary>
    public SessionManager(
        ILifetimeScope root,
        ScriptApiSurface surface,
        IScriptDeviceSource devices,
        ScriptApiHostOptions options,
        ILogger<SessionManager> logger = null)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _devices = devices ?? throw new ArgumentNullException(nameof(devices));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;

        Lease = new ControlLease(options.ControlLeaseTimeout);
    }

    /// <summary>The exclusive control lease.</summary>
    public ControlLease Lease { get; }

    /// <summary>Every session currently connected.</summary>
    public IReadOnlyList<ScriptSession> Active
    {
        get
        {
            lock (_sessions)
                return _sessions.ToList();
        }
    }

    /// <summary>Starts a session in its own child scope.</summary>
    public ScriptSession Begin(string principal, bool isLocal)
    {
        var id = "s/" + Interlocked.Increment(ref _nextId);
        var scope = _root.BeginLifetimeScope("script-session");

        var session = new ScriptSession(
            id, principal, isLocal, scope, this, _surface, _devices, _options.TraceHandleAllocations);

        lock (_sessions)
            _sessions.Add(session);

        _logger?.LogInformation("Session {Session} opened for {Principal} ({Transport}).",
            id, principal, isLocal ? "local" : "remote");

        return session;
    }

    internal void Ended(ScriptSession session)
    {
        lock (_sessions)
            _sessions.Remove(session);

        Lease.Release(session);
        _logger?.LogInformation("Session {Session} closed, {Handles} handle(s) released.", session.Id, session.Handles.Count);
    }
}

/// <summary>
/// Exclusive control of the instrument. One session holds it; the rest are read-only. A holder
/// that stops talking loses it, so a laptop that goes to sleep mid-run does not wedge the
/// instrument.
/// </summary>
public sealed class ControlLease
{
    private readonly object _gate = new();
    private readonly TimeSpan _timeout;

    private ScriptSession _holder;
    private DateTimeOffset _lastSeen;

    /// <summary>Creates a lease with the given inactivity timeout.</summary>
    public ControlLease(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    /// <summary>The session holding control, or null.</summary>
    public ScriptSession Holder
    {
        get
        {
            lock (_gate)
                return _holder;
        }
    }

    /// <summary>True when the session holds control right now.</summary>
    public bool IsHeldBy(ScriptSession session)
    {
        lock (_gate)
            return ReferenceEquals(_holder, session) && !IsStale();
    }

    /// <summary>
    /// Takes control if it is free, already ours, or held by a session that has gone quiet for
    /// longer than the timeout.
    /// </summary>
    public bool TryAcquire(ScriptSession session)
    {
        lock (_gate)
        {
            if (_holder != null && !ReferenceEquals(_holder, session) && !IsStale())
                return false;

            _holder = session;
            _lastSeen = DateTimeOffset.UtcNow;
            return true;
        }
    }

    /// <summary>Records that the holder is still there. Any call from it counts, not just a heartbeat.</summary>
    public void Touch(ScriptSession session)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_holder, session))
                _lastSeen = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>Gives up control, if this session had it.</summary>
    public void Release(ScriptSession session)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_holder, session))
                _holder = null;
        }
    }

    /// <summary>How long the current holder has been quiet.</summary>
    public TimeSpan SinceLastSeen
    {
        get
        {
            lock (_gate)
                return _holder == null ? TimeSpan.Zero : DateTimeOffset.UtcNow - _lastSeen;
        }
    }

    private bool IsStale() => DateTimeOffset.UtcNow - _lastSeen > _timeout;
}

/// <summary>
/// Resolves the two reference forms for one session. Devices are shared and permanent; handles
/// belong to the session and die with it.
/// </summary>
internal sealed class SessionReferences : IScriptApiReferences
{
    private readonly ScriptApiSurface _surface;
    private readonly IScriptDeviceSource _devices;
    private readonly HandleTable _handles;
    private readonly Dictionary<object, ScriptTarget> _deviceByInstance;

    public SessionReferences(ScriptApiSurface surface, IScriptDeviceSource devices, HandleTable handles)
    {
        _surface = surface;
        _devices = devices;
        _handles = handles;

        _deviceByInstance = new Dictionary<object, ScriptTarget>(ReferenceEqualityComparer.Instance);
        foreach (var device in devices.Devices.Values)
            _deviceByInstance[device.Instance] = device;
    }

    public object Resolve(JsonElement reference, string expectedWireTypeName, string parameterName)
    {
        if (reference.ValueKind != JsonValueKind.Object || !reference.TryGetProperty("$ref", out var idElement) ||
            idElement.ValueKind != JsonValueKind.String)
        {
            throw ScriptApiException.WrongArgumentType(
                parameterName, parameterName, $"a reference to '{expectedWireTypeName}'", ScriptApiJson.Describe(reference));
        }

        var id = idElement.GetString();
        var target = Find(id, parameterName);

        if (!Answers(target, expectedWireTypeName))
        {
            throw ScriptApiException.HandleTypeMismatch(
                parameterName, expectedWireTypeName, $"'{string.Join(", ", target.WireTypes)}'");
        }

        return target.Instance;
    }

    public JsonNode Describe(object target, string declaredWireTypeName)
    {
        if (target == null)
            return null;

        if (!_deviceByInstance.TryGetValue(target, out var known))
        {
            // Not a configured device, so it is something this session created: give it a handle
            // and let the session's lifetime be the backstop for releasing it.
            known = _handles.Mint(target, WireTypesOf(target, declaredWireTypeName));
        }

        return new JsonObject { ["$ref"] = known.Id, ["$type"] = declaredWireTypeName };
    }

    private ScriptTarget Find(string id, string parameterName)
    {
        if (id != null && _devices.Devices.TryGetValue(id, out var device))
            return device;

        if (id != null && _handles.TryGet(id, out var handle))
            return handle;

        throw new ScriptApiException(
            ScriptApiErrorCodes.HandleNotFound,
            $"'{id}' is not a device or a live handle",
            new JsonObject { ["parameter"] = parameterName, ["reference"] = id });
    }

    /// <summary>True when the target is addressable as the declared type, following inheritance.</summary>
    private bool Answers(ScriptTarget target, string wireTypeName)
    {
        foreach (var declared in target.WireTypes)
        {
            if (Extends(declared, wireTypeName))
                return true;
        }

        return false;
    }

    private bool Extends(string wireTypeName, string sought)
    {
        if (string.Equals(wireTypeName, sought, StringComparison.Ordinal))
            return true;

        if (!_surface.TryGetDispatcher(wireTypeName, out var dispatcher))
            return false;

        foreach (var inherited in dispatcher.Extends)
        {
            if (Extends(inherited, sought))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Everything a newly minted handle should answer to: the declared type of the position it
    /// came back from, plus any other script API interface the object happens to implement.
    /// </summary>
    private IReadOnlyList<string> WireTypesOf(object instance, string declaredWireTypeName)
    {
        var types = new SortedSet<string>(StringComparer.Ordinal) { declaredWireTypeName };

        foreach (var wireType in _surface.WireTypeNames)
        {
            if (_surface.TryGetDispatcher(wireType, out var dispatcher) &&
                dispatcher.TargetType.IsInstanceOfType(instance))
            {
                types.Add(wireType);
            }
        }

        return types.ToList();
    }
}
