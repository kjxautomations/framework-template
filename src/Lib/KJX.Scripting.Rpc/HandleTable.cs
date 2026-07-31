using System.Diagnostics;
using System.Text.Json.Nodes;
using KJX.Scripting.Runtime;

namespace KJX.Scripting.Rpc;

/// <summary>
/// The session's handles: objects that were created during the session and have server-side
/// identity, such as an open acquisition or a claimed channel. Ids are sequential integers
/// because the table is per session and never shared.
/// </summary>
/// <remarks>
/// Release is expected to come from the client's context manager. Disposing the table is the
/// backstop, and it is the one that matters: a crashed script or a dropped socket must not leave
/// a hardware claim held.
/// </remarks>
public sealed class HandleTable : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<object, string> _byInstance = new(ReferenceEqualityComparer.Instance);
    private readonly bool _traceAllocations;

    private long _next;
    private bool _disposed;

    /// <summary>Creates an empty table.</summary>
    /// <param name="traceAllocations">Records the stack each handle was minted from.</param>
    public HandleTable(bool traceAllocations = false)
    {
        _traceAllocations = traceAllocations;
    }

    /// <summary>How many handles are currently live.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _byId.Count;
        }
    }

    /// <summary>Every live handle id.</summary>
    public IReadOnlyList<string> Ids
    {
        get
        {
            lock (_gate)
                return _byId.Keys.ToList();
        }
    }

    /// <summary>
    /// Mints a handle, or returns the one this object already has: handing the same object out
    /// twice must not give a script two ids for one thing.
    /// </summary>
    public ScriptTarget Mint(object instance, IReadOnlyList<string> wireTypes)
    {
        ArgumentNullException.ThrowIfNull(instance);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_byInstance.TryGetValue(instance, out var existing))
            {
                _byId[existing].Touch();
                return _byId[existing].Target;
            }

            var id = "h/" + ++_next;
            var target = new ScriptTarget(id, instance, wireTypes);

            _byId.Add(id, new Entry(target, _traceAllocations ? new StackTrace(fNeedFileInfo: true).ToString() : null));
            _byInstance.Add(instance, id);

            return target;
        }
    }

    /// <summary>Looks a handle up and marks it as used, which is what the idle timeout watches.</summary>
    public bool TryGet(string id, out ScriptTarget target)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(id ?? string.Empty, out var entry))
            {
                entry.Touch();
                target = entry.Target;
                return true;
            }
        }

        target = null;
        return false;
    }

    /// <summary>Where a handle was allocated, when allocation tracing is on.</summary>
    public string AllocationTrace(string id)
    {
        lock (_gate)
            return _byId.TryGetValue(id ?? string.Empty, out var entry) ? entry.AllocatedAt : null;
    }

    /// <summary>
    /// Releases a handle and disposes what it pointed at. Releasing an id that is not in the
    /// table is an error rather than a no-op, because it usually means the script is holding a
    /// stale reference.
    /// </summary>
    public async ValueTask ReleaseAsync(string id)
    {
        Entry entry;

        lock (_gate)
        {
            if (!_byId.TryGetValue(id ?? string.Empty, out entry))
            {
                throw new ScriptApiException(
                    ScriptApiErrorCodes.HandleNotFound,
                    $"'{id}' is not a handle in this session",
                    new JsonObject { ["handle"] = id });
            }

            _byId.Remove(id);
            _byInstance.Remove(entry.Target.Instance);
        }

        await DisposeTargetAsync(entry.Target.Instance).ConfigureAwait(false);
    }

    /// <summary>
    /// Releases handles that have not been touched for longer than the timeout. Scarce resources
    /// are freed even while the session itself is alive and heartbeating.
    /// </summary>
    public async ValueTask<IReadOnlyList<string>> ExpireIdleAsync(TimeSpan timeout)
    {
        List<Entry> expired;
        var cutoff = DateTimeOffset.UtcNow - timeout;

        lock (_gate)
        {
            expired = _byId.Values.Where(entry => entry.LastUsed < cutoff).ToList();

            foreach (var entry in expired)
            {
                _byId.Remove(entry.Target.Id);
                _byInstance.Remove(entry.Target.Instance);
            }
        }

        foreach (var entry in expired)
            await DisposeTargetAsync(entry.Target.Instance).ConfigureAwait(false);

        return expired.Select(entry => entry.Target.Id).ToList();
    }

    /// <summary>Releases everything. This is the backstop for a client that went away.</summary>
    public async ValueTask DisposeAsync()
    {
        List<Entry> remaining;

        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            remaining = _byId.Values.ToList();
            _byId.Clear();
            _byInstance.Clear();
        }

        foreach (var entry in remaining)
            await DisposeTargetAsync(entry.Target.Instance).ConfigureAwait(false);
    }

    private static async ValueTask DisposeTargetAsync(object instance)
    {
        switch (instance)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                return;

            case IDisposable disposable:
                disposable.Dispose();
                return;
        }
    }

    private sealed class Entry(ScriptTarget target, string allocatedAt)
    {
        public ScriptTarget Target { get; } = target;

        public string AllocatedAt { get; } = allocatedAt;

        public DateTimeOffset LastUsed { get; private set; } = DateTimeOffset.UtcNow;

        public void Touch() => LastUsed = DateTimeOffset.UtcNow;
    }
}
