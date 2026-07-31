using System.Runtime.CompilerServices;
using KJX.Scripting;

namespace KJX.Tests;

/// <summary>
/// A claim on something scarce. Returned by value from a call, so it crosses the wire as a
/// handle and has to be released.
/// </summary>
[ScriptApi]
public interface IScriptTestClaim
{
    /// <summary>Why the claim was taken.</summary>
    string Reason { get; }

    /// <summary>Uses the claim, which fails once it has been released.</summary>
    void Use();
}

/// <summary>
/// A device with the shapes the real device interfaces do not have yet: a call that awaits, a
/// stream, and a member that hands back an object with server-side identity.
/// </summary>
[ScriptApi]
public interface IScriptTestDevice
{
    /// <summary>How many claims have been taken.</summary>
    int ClaimCount { get; }

    /// <summary>Waits until something signals it, or until the caller gives up.</summary>
    Task<int> WaitForSignalAsync(CancellationToken cancellationToken = default);

    /// <summary>Counts upwards until the subscription ends.</summary>
    IAsyncEnumerable<int> Ticks(CancellationToken cancellationToken = default);

    /// <summary>Takes a claim, which the caller has to release.</summary>
    /// <param name="reason">Recorded on the claim.</param>
    IScriptTestClaim Claim(string reason);
}

/// <inheritdoc />
public sealed class ScriptTestClaim : IScriptTestClaim, IAsyncDisposable
{
    /// <summary>Creates a claim.</summary>
    public ScriptTestClaim(string reason) => Reason = reason;

    /// <inheritdoc />
    public string Reason { get; }

    /// <summary>How many times the claim was used.</summary>
    public int Uses { get; private set; }

    /// <summary>True once the claim has been released, however that happened.</summary>
    public bool IsReleased { get; private set; }

    /// <inheritdoc />
    public void Use()
    {
        if (IsReleased)
            throw new InvalidOperationException("The claim has been released.");

        Uses++;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        IsReleased = true;
        return ValueTask.CompletedTask;
    }
}

/// <inheritdoc />
public sealed class ScriptTestDevice : IScriptTestDevice
{
    private readonly TaskCompletionSource<int> _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<ScriptTestClaim> _claims = new();

    /// <inheritdoc />
    public int ClaimCount
    {
        get
        {
            lock (_claims)
                return _claims.Count;
        }
    }

    /// <summary>The claims handed out so far, so a test can see whether they were released.</summary>
    public IReadOnlyList<ScriptTestClaim> Claims
    {
        get
        {
            lock (_claims)
                return _claims.ToList();
        }
    }

    /// <summary>Completes whatever is waiting.</summary>
    public void Signal(int value) => _signal.TrySetResult(value);

    /// <inheritdoc />
    public async Task<int> WaitForSignalAsync(CancellationToken cancellationToken = default)
    {
        await using var registration = cancellationToken.Register(() => _signal.TrySetCanceled(cancellationToken));
        return await _signal.Task.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<int> Ticks([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var index = 0;

        while (true)
        {
            yield return index++;
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public IScriptTestClaim Claim(string reason)
    {
        var claim = new ScriptTestClaim(reason);

        lock (_claims)
            _claims.Add(claim);

        return claim;
    }
}
