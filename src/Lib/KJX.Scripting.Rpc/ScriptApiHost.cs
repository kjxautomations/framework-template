using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Autofac;
using KJX.Scripting.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KJX.Scripting.Rpc;

/// <summary>
/// Serves the scripting API over JSON-RPC: the same protocol on a unix domain socket for scripts
/// running on the instrument and on a TCP socket for remote clients. Built to be hosted inside
/// the application that owns the devices, so that dispatch is an in-process call.
/// </summary>
public sealed class ScriptApiHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ScriptApiHostOptions _options;
    private readonly ILogger<ScriptApiHost> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly string _unixSocketPath;

    private Task _handleSweeper;

    private ScriptApiHost(
        WebApplication app,
        ScriptApiHostOptions options,
        string unixSocketPath,
        ScriptApiSurface surface,
        IScriptDeviceSource devices,
        SessionManager sessions,
        ILogger<ScriptApiHost> logger)
    {
        _app = app;
        _options = options;
        _unixSocketPath = unixSocketPath;
        _logger = logger;

        Surface = surface;
        Devices = devices;
        Sessions = sessions;
    }

    /// <summary>Everything the host can dispatch.</summary>
    public ScriptApiSurface Surface { get; }

    /// <summary>The devices scripts can address.</summary>
    public IScriptDeviceSource Devices { get; }

    /// <summary>Connected sessions and the control lease.</summary>
    public SessionManager Sessions { get; }

    /// <summary>The addresses the host is listening on, once it has started.</summary>
    public IReadOnlyList<string> Endpoints =>
        _app.Services.GetService<IServer>()?.Features.Get<IServerAddressesFeature>()?.Addresses.ToList()
        ?? new List<string>();

    /// <summary>
    /// The URI a remote client connects to, or null when the host only serves the unix socket.
    /// Built from the configured endpoint rather than from what the server reports, because
    /// Kestrel describes a unix socket with a URI that cannot be dialled.
    /// </summary>
    public Uri WebSocketEndpoint
    {
        get
        {
            if (_options.Port <= 0)
                return null;

            var scheme = string.IsNullOrWhiteSpace(_options.CertificatePath) ? "ws" : "wss";
            return new Uri($"{scheme}://{_options.Address}:{_options.Port}{_options.Path}");
        }
    }

    /// <summary>
    /// Builds the host over an existing container. The container is the one that owns the
    /// devices: each session gets a child scope of it.
    /// </summary>
    /// <param name="container">The application's container, holding the configured devices.</param>
    /// <param name="options">How the endpoint is exposed.</param>
    /// <param name="catalogs">The generated scripting surfaces to serve.</param>
    /// <param name="loggerFactory">Where the host and the audit trail log to.</param>
    public static ScriptApiHost Create(
        ILifetimeScope container,
        ScriptApiHostOptions options,
        IEnumerable<IScriptApiCatalog> catalogs,
        ILoggerFactory loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(catalogs);

        loggerFactory ??= LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Information));

        if (options.Port > 0 && string.IsNullOrWhiteSpace(options.Token))
        {
            throw new InvalidOperationException(
                "A bearer token is required before the scripting host will listen on the network. " +
                "Set Token, or leave Port at 0 and use the unix socket.");
        }

        var surface = new ScriptApiSurface(catalogs);
        var devices = new AutofacScriptDeviceSource(
            container, surface, loggerFactory.CreateLogger<AutofacScriptDeviceSource>());

        var sessions = new SessionManager(
            container, surface, devices, options, loggerFactory.CreateLogger<SessionManager>());

        var audit = new LoggingScriptAudit(loggerFactory.CreateLogger<LoggingScriptAudit>());
        var logger = loggerFactory.CreateLogger<ScriptApiHost>();

        var unixSocketPath = PrepareUnixSocket(options, logger);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(loggerFactory);
        builder.Logging.ClearProviders();

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;

            if (options.Port > 0)
            {
                kestrel.Listen(IPAddress.Parse(options.Address), options.Port, listen =>
                {
                    var certificate = LoadCertificate(options);
                    if (certificate != null)
                        listen.UseHttps(certificate);
                });
            }

            if (unixSocketPath != null)
                kestrel.ListenUnixSocket(unixSocketPath);
        });

        var app = builder.Build();
        var host = new ScriptApiHost(app, options, unixSocketPath, surface, devices, sessions, logger);

        app.UseWebSockets();
        app.Map(options.Path, context => host.AcceptAsync(context, sessions, audit, logger));

        return host;
    }

    /// <summary>Starts listening.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _app.StartAsync(cancellationToken).ConfigureAwait(false);

        if (_unixSocketPath != null && !OperatingSystem.IsWindows())
        {
            // The socket's permissions are the access control for on-device clients.
            File.SetUnixFileMode(_unixSocketPath, _options.UnixSocketPermissions);
        }

        _handleSweeper = Task.Run(SweepIdleHandlesAsync, CancellationToken.None);

        _logger.LogInformation("Scripting host listening on {Endpoints}{Socket}.",
            string.Join(", ", Endpoints),
            _unixSocketPath == null ? string.Empty : $" and {_unixSocketPath}");
    }

    /// <summary>Stops listening and ends every session.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        await _app.StopAsync(cancellationToken).ConfigureAwait(false);

        if (_handleSweeper != null)
        {
            try
            {
                await _handleSweeper.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_unixSocketPath != null && File.Exists(_unixSocketPath))
            File.Delete(_unixSocketPath);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "The scripting host did not stop cleanly.");
        }

        _stopping.Dispose();
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private async Task AcceptAsync(HttpContext context, SessionManager sessions, IScriptAudit audit, ILogger logger)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("This endpoint speaks JSON-RPC over WebSocket.").ConfigureAwait(false);
            return;
        }

        // A connection over the unix socket has no remote address; its authority comes from the
        // socket's file permissions rather than from a token.
        var isLocal = context.Connection.RemoteIpAddress == null;

        if (!isLocal && !IsAuthorized(context))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            logger.LogWarning("Rejected a scripting connection from {Address} with no valid token.",
                context.Connection.RemoteIpAddress);
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);

        var principal = isLocal ? "local" : "token";
        var session = sessions.Begin(principal, isLocal);
        var connection = new ScriptRpcConnection(socket, session, sessions, _options, audit, logger);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token, context.RequestAborted);
        await connection.RunAsync(linked.Token).ConfigureAwait(false);
    }

    private bool IsAuthorized(HttpContext context)
    {
        var presented = context.Request.Headers.Authorization.ToString();

        if (presented.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            presented = presented.Substring("Bearer ".Length);
        else if (context.Request.Query.TryGetValue("token", out var query))
            presented = query.ToString();
        else
            presented = string.Empty;

        // Compared in fixed time so that a wrong token gives nothing away.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented),
            Encoding.UTF8.GetBytes(_options.Token ?? string.Empty));
    }

    /// <summary>
    /// Releases handles nobody has touched for a while. This runs independently of the control
    /// lease: a session can be alive and heartbeating and still be sitting on a claimed channel
    /// it forgot about.
    /// </summary>
    private async Task SweepIdleHandlesAsync()
    {
        if (_options.HandleIdleTimeout <= TimeSpan.Zero)
            return;

        var interval = TimeSpan.FromMilliseconds(Math.Max(1000, _options.HandleIdleTimeout.TotalMilliseconds / 4));
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(_stopping.Token).ConfigureAwait(false))
            {
                foreach (var session in Sessions.Active)
                {
                    var expired = await session.Handles.ExpireIdleAsync(_options.HandleIdleTimeout).ConfigureAwait(false);

                    if (expired.Count > 0)
                    {
                        _logger.LogInformation("Released {Count} idle handle(s) in session {Session}: {Handles}.",
                            expired.Count, session.Id, string.Join(", ", expired));
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Makes sure the socket path is usable, and returns it, or null when the host should simply
    /// not offer a local endpoint. A missing socket directory on a developer machine is not a
    /// reason to refuse to start.
    /// </summary>
    private static string PrepareUnixSocket(ScriptApiHostOptions options, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.UnixSocketPath))
            return null;

        try
        {
            var path = Path.GetFullPath(options.UnixSocketPath);
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // A socket file left behind by a crash would stop the bind.
            if (File.Exists(path))
                File.Delete(path);

            return path;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Cannot use the unix socket at '{Path}', so on-device clients will have to connect over TCP.",
                options.UnixSocketPath);

            return null;
        }
    }

    private static X509Certificate2 LoadCertificate(ScriptApiHostOptions options) =>
        string.IsNullOrWhiteSpace(options.CertificatePath)
            ? null
            : X509CertificateLoader.LoadPkcs12FromFile(options.CertificatePath, options.CertificatePassword);
}
