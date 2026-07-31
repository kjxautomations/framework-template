namespace KJX.Scripting.Rpc;

/// <summary>
/// How the scripting endpoint is exposed. The defaults are what an instrument on a bench wants:
/// a unix socket for scripts running on the device, and nothing listening on the network until
/// someone configures a token.
/// </summary>
public sealed class ScriptApiHostOptions
{
    /// <summary>The path served over WebSocket. Both transports use the same one.</summary>
    public string Path { get; set; } = "/rpc";

    /// <summary>
    /// The unix domain socket for on-device clients, or null to not listen on one. Access is
    /// controlled by the socket's file permissions rather than by a token.
    /// </summary>
    public string UnixSocketPath { get; set; } = "/run/instrument/rpc.sock";

    /// <summary>
    /// The permissions to put on the unix socket. The default lets the owner and the group
    /// connect, which is how the script runner's account is granted access.
    /// </summary>
    public UnixFileMode UnixSocketPermissions { get; set; } =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite;

    /// <summary>The TCP port for remote clients, or 0 to not listen on the network.</summary>
    public int Port { get; set; }

    /// <summary>The address to bind the TCP listener to. Loopback unless deliberately widened.</summary>
    public string Address { get; set; } = "127.0.0.1";

    /// <summary>
    /// The bearer token remote clients must present. Required whenever <see cref="Port"/> is set:
    /// the host refuses to listen on the network without one.
    /// </summary>
    public string Token { get; set; }

    /// <summary>Path to a PKCS#12 certificate for TLS on the TCP listener.</summary>
    public string CertificatePath { get; set; }

    /// <summary>The password for <see cref="CertificatePath"/>.</summary>
    public string CertificatePassword { get; set; }

    /// <summary>
    /// How long a session may hold the control lease without a heartbeat. A laptop that goes to
    /// sleep must not wedge the instrument.
    /// </summary>
    public TimeSpan ControlLeaseTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a handle may sit unused before it is released, independently of the session's
    /// lease. Scarce resources are what this protects.
    /// </summary>
    public TimeSpan HandleIdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How many messages may be queued for a client before stream notifications start being
    /// dropped. Bounded on purpose: a slow consumer must not be able to grow this without limit.
    /// </summary>
    public int OutboundQueueDepth { get; set; } = 1024;

    /// <summary>The largest request the host will read, in bytes.</summary>
    public int MaximumMessageBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// Records where each handle was allocated, so a leak can be traced back to the call that
    /// made it. Off by default because capturing stacks is not free.
    /// </summary>
    public bool TraceHandleAllocations { get; set; }

    /// <summary>
    /// Options for an instrument that only serves scripts running on the same machine: a unix
    /// socket and nothing on the network. Widening this is a deliberate act, because a port with
    /// no token is refused.
    /// </summary>
    /// <param name="name">Distinguishes this application's socket from another's.</param>
    public static ScriptApiHostOptions ForLocalInstrument(string name) => new()
    {
        // Windows has unix sockets too, but no /run; the temp directory is the portable choice.
        UnixSocketPath = OperatingSystem.IsWindows()
            ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), name + ".rpc.sock")
            : $"/run/instrument/{name}.rpc.sock",
        Port = 0,
    };
}
