using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Autofac;
using KJX.Config;
using KJX.Core;
using KJX.Scripting.Rpc;
using KJX.Scripting.Runtime;
using Microsoft.Extensions.Logging;

namespace KJX.Tests;

/// <summary>
/// Runs the Python client's own test suite against a live host with the simulated devices. The
/// client is the only hand-written part of the pipeline, so this is where the whole chain gets
/// checked: C# interface, generated descriptor, generated dispatch, JSON-RPC, and the proxies the
/// client builds from the descriptor.
/// </summary>
[TestFixture]
public class TestPythonClient
{
    private const string Token = "python-test-token";

    private IContainer _container;
    private ScriptApiHost _host;
    private string _socketPath;
    private string _stubCache;

    [Test]
    public async Task The_python_client_passes_its_end_to_end_suite()
    {
        var python = FindPython();
        if (python == null)
        {
            Assert.Ignore("No Python interpreter with the 'websockets' package was found. " +
                          "Set KJX_PYTHON to one, or install it with 'pip install websockets'.");
        }

        var project = PythonProject();
        await StartHostAsync();

        var result = Run(
            python,
            "-m unittest discover -s tests -t . -v",
            project,
            new Dictionary<string, string>
            {
                ["PYTHONPATH"] = project,
                ["KJX_TEST_ENDPOINT"] = _host.WebSocketEndpoint.ToString(),
                ["KJX_TEST_TOKEN"] = Token,
                ["KJX_TEST_SOCKET"] = Socket.OSSupportsUnixDomainSockets ? _socketPath : null,
                ["KJX_INSTRUMENT_CACHE"] = _stubCache,
            });

        TestContext.Out.WriteLine(result.Output);

        Assert.That(result.ExitCode, Is.Zero, "The Python end-to-end suite failed:" + Environment.NewLine + result.Output);

        // Verbose unittest output names each test; a run that silently skipped everything would
        // otherwise pass.
        Assert.That(result.Output, Does.Contain("test_calls_reach_the_device"));
        Assert.That(result.Output, Does.Not.Contain("KJX_TEST_ENDPOINT is not set"),
            "The suite should have found the endpoint rather than skipping.");
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_host != null)
            await _host.DisposeAsync();

        _container?.Dispose();

        if (_stubCache != null && Directory.Exists(_stubCache))
            Directory.Delete(_stubCache, recursive: true);
    }

    private async Task StartHostAsync()
    {
        var builder = new ContainerBuilder();
        builder.RegisterType<LoggerFactory>().As<ILoggerFactory>().SingleInstance();
        builder.RegisterGeneric(typeof(Logger<>)).As(typeof(ILogger<>)).SingleInstance();

        using (var stream = File.OpenRead(Path.Combine("ConfigTestFiles", "ScriptApiDevices.ini")))
            new ConfigurationHandler().PopulateContainerBuilder(builder, ConfigLoader.LoadConfig(stream));

        _container = builder.Build();
        _socketPath = Path.Combine(Path.GetTempPath(), $"kjx-py-{Guid.NewGuid():N}.sock");
        _stubCache = Path.Combine(Path.GetTempPath(), $"kjx-stubs-{Guid.NewGuid():N}");

        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        _host = ScriptApiHost.Create(
            _container,
            new ScriptApiHostOptions
            {
                Address = "127.0.0.1",
                Port = port,
                Token = Token,
                UnixSocketPath = _socketPath,
                HandleIdleTimeout = TimeSpan.Zero,
            },
            [
                KJX.Devices.Generated.ScriptApiCatalog.Instance,
                KJX.Tests.Generated.ScriptApiCatalog.Instance,
            ],
            _container.Resolve<ILoggerFactory>());

        await _host.StartAsync();
    }

    /// <summary>Finds an interpreter that can import websockets, or null.</summary>
    private static string FindPython()
    {
        var candidates = new List<string>();

        var configured = Environment.GetEnvironmentVariable("KJX_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured))
            candidates.Add(configured);

        candidates.AddRange(["py", "python3", "python"]);

        foreach (var candidate in candidates)
        {
            try
            {
                var check = Run(candidate, "-c \"import websockets\"", Path.GetTempPath(), null, 60);
                if (check.ExitCode == 0)
                    return candidate;
            }
            catch (Exception)
            {
                // not an interpreter on this machine
            }
        }

        return null;
    }

    /// <summary>Walks up from the test assembly to the python project beside the solution.</summary>
    private static string PythonProject()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "python");
            if (File.Exists(Path.Combine(candidate, "kjx_instrument", "__init__.py")))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the python project from " + AppContext.BaseDirectory);
    }

    private static (int ExitCode, string Output) Run(
        string program,
        string arguments,
        string workingDirectory,
        IDictionary<string, string> environment,
        int timeoutSeconds = 300)
    {
        var start = new ProcessStartInfo(program, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var variable in environment ?? new Dictionary<string, string>())
        {
            if (variable.Value != null)
                start.Environment[variable.Key] = variable.Value;
        }

        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException($"Could not start '{program}'.");

        var output = new StringBuilder();
        process.OutputDataReceived += (_, data) => Append(output, data.Data);
        process.ErrorDataReceived += (_, data) => Append(output, data.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            process.Kill(entireProcessTree: true);
            return (-1, output + Environment.NewLine + $"'{program}' did not finish in {timeoutSeconds}s.");
        }

        process.WaitForExit();
        return (process.ExitCode, output.ToString());
    }

    private static void Append(StringBuilder output, string line)
    {
        if (line == null)
            return;

        lock (output)
            output.AppendLine(line);
    }
}
