using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using KJX.Scripting.Runtime;

namespace KJX.Scripting.Tests;

/// <summary>
/// Compiles the samples together with the generated dispatch, loads the result and calls it.
/// A descriptor that looks right and dispatch that does not work would still be a broken stage,
/// so these tests exercise the generated switch rather than reading it.
/// </summary>
[TestFixture]
public class ScriptApiDispatchTests
{
    private const string PumpImplementation = """

        public sealed class TestPump : ISyringePump
        {
            public string Log = "";
            public string Name => "P1";
            public double FlowRate { get; set; } = 1.0;
            public double? MaximumFlowRate { get; set; }
            public PumpState State => PumpState.Priming;

            public System.Threading.Tasks.Task PrimeAsync(double microlitres, System.TimeSpan settle,
                System.Threading.CancellationToken cancellationToken = default)
            {
                Log = $"prime {microlitres} {settle}";
                return System.Threading.Tasks.Task.CompletedTask;
            }

            public void Stop() => Log = "stop";

            public System.Threading.Tasks.Task<FlowReading> CalibrateAsync(int passes = 3, PumpState state = PumpState.Idle)
            {
                Log = $"calibrate {passes} {state}";
                return System.Threading.Tasks.Task.FromResult(
                    new FlowReading(passes, new System.DateTimeOffset(2026, 7, 31, 12, 0, 0, System.TimeSpan.Zero)));
            }

            public async System.Collections.Generic.IAsyncEnumerable<FlowReading> Flow(
                [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
            {
                for (var index = 0; index < 3; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return new FlowReading(index, new System.DateTimeOffset(2026, 7, 31, 12, 0, index, System.TimeSpan.Zero));
                    await System.Threading.Tasks.Task.Yield();
                }
            }
        }
        """;

    private const string RecipeImplementation = """

        public sealed class TestAcquisition : IAcquisition
        {
            public string Id => "acq1";
            public System.Collections.Generic.IReadOnlyList<int> Channels { get; set; } = new int[0];
            public void Arm() { }
            public System.Threading.Tasks.Task<double[]> ReadAsync(System.Threading.CancellationToken cancellationToken = default)
                => System.Threading.Tasks.Task.FromResult(new[] { 1.0, 2.0 });
        }

        public sealed class TestPump : IPump
        {
            public string Id => "pump1";
            public double FlowRate { get; set; }
        }

        public sealed class TestRunner : IRecipeRunner
        {
            public string Log = "";
            public TestAcquisition Started = new TestAcquisition();
            public IAcquisition[] Open => new IAcquisition[] { Started };

            public IAcquisition Start(int[] channels)
            {
                Started.Channels = channels;
                return Started;
            }

            public System.Threading.Tasks.Task RunAsync(IPump pump, IAcquisition source,
                System.Collections.Generic.IReadOnlyList<RecipeStep> steps,
                System.Threading.CancellationToken cancellationToken = default)
            {
                Log = $"{pump.Id} {source.Id} {steps.Count} {steps[0].Name} {steps[0].Duration}";
                return System.Threading.Tasks.Task.CompletedTask;
            }
        }
        """;

    private static Assembly _pumpAssembly;
    private static Assembly _recipeAssembly;

    [OneTimeSetUp]
    public void Compile()
    {
        _pumpAssembly = GeneratorHarness.Compile(
            GeneratorHarness.Sample("StreamSample") + PumpImplementation, "StreamDispatch");

        _recipeAssembly = GeneratorHarness.Compile(
            GeneratorHarness.Sample("ReferenceSample") + RecipeImplementation, "ReferenceDispatch");
    }

    [Test]
    public async Task A_property_getter_returns_its_value()
    {
        var result = await Invoke(_pumpAssembly, "syringe_pump", NewPump(), "get_name", "{}");

        Assert.That(result!.GetValue<string>(), Is.EqualTo("P1"));
    }

    [Test]
    public async Task A_property_setter_assigns_its_value()
    {
        var pump = NewPump();

        await Invoke(_pumpAssembly, "syringe_pump", pump, "set_flow_rate", """{"value": 2.5}""");
        var result = await Invoke(_pumpAssembly, "syringe_pump", pump, "get_flow_rate", "{}");

        Assert.That(result!.GetValue<double>(), Is.EqualTo(2.5));
    }

    [Test]
    public async Task A_null_argument_reaches_a_nullable_property()
    {
        var pump = NewPump();

        await Invoke(_pumpAssembly, "syringe_pump", pump, "set_maximum_flow_rate", """{"value": 9.5}""");
        var set = await Invoke(_pumpAssembly, "syringe_pump", pump, "get_maximum_flow_rate", "{}");

        await Invoke(_pumpAssembly, "syringe_pump", pump, "set_maximum_flow_rate", """{"value": null}""");
        var cleared = await Invoke(_pumpAssembly, "syringe_pump", pump, "get_maximum_flow_rate", "{}");

        Assert.Multiple(() =>
        {
            Assert.That(set!.GetValue<double>(), Is.EqualTo(9.5));
            Assert.That(cleared, Is.Null);
        });
    }

    [Test]
    public async Task An_awaited_call_binds_its_arguments()
    {
        var pump = NewPump();

        await Invoke(_pumpAssembly, "syringe_pump", pump, "prime",
            """{"microlitres": 12.5, "settle": "00:00:01.500"}""");

        Assert.That(Log(pump), Is.EqualTo("prime 12.5 00:00:01.5000000"));
    }

    [Test]
    public async Task Absent_optional_arguments_fall_back_to_the_declared_defaults()
    {
        var pump = NewPump();

        var result = await Invoke(_pumpAssembly, "syringe_pump", pump, "calibrate", "{}");

        Assert.Multiple(() =>
        {
            Assert.That(Log(pump), Is.EqualTo("calibrate 3 Idle"));
            Assert.That(result!["microlitres"]!.GetValue<double>(), Is.EqualTo(3));

            // Timestamps stay typed in the node tree and become ISO 8601 when the response is
            // serialized, which is the form the wire and the Python client see.
            Assert.That(result["taken"]!.ToJsonString(), Does.StartWith("\"2026-07-31T12:00:00"));
        });
    }

    [Test]
    public async Task An_enum_argument_travels_as_its_name()
    {
        var pump = NewPump();

        await Invoke(_pumpAssembly, "syringe_pump", pump, "calibrate", """{"passes": 1, "state": "Dispensing"}""");

        Assert.That(Log(pump), Is.EqualTo("calibrate 1 Dispensing"));
    }

    [Test]
    public async Task An_enum_result_travels_as_its_name()
    {
        var result = await Invoke(_pumpAssembly, "syringe_pump", NewPump(), "get_state", "{}");

        Assert.That(result!.GetValue<string>(), Is.EqualTo("Priming"));
    }

    [Test]
    public async Task A_stream_yields_every_element()
    {
        var readings = new List<JsonNode>();
        using var arguments = JsonDocument.Parse("{}");

        await foreach (var reading in Dispatcher(_pumpAssembly, "syringe_pump")
                           .Subscribe(NewPump(), "flow", arguments.RootElement, new References(), CancellationToken.None))
        {
            readings.Add(reading);
        }

        Assert.That(readings, Has.Count.EqualTo(3));
        Assert.That(readings.Select(reading => reading!["microlitres"]!.GetValue<double>()),
            Is.EqualTo(new[] { 0.0, 1.0, 2.0 }));
    }

    [Test]
    public void A_missing_argument_names_the_parameter()
    {
        var error = Assert.CatchAsync<ScriptApiException>(async () =>
            await Invoke(_pumpAssembly, "syringe_pump", NewPump(), "prime", """{"settle": "00:00:01"}"""));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Code, Is.EqualTo(ScriptApiErrorCodes.InvalidParams));
            Assert.That(error.ErrorData!["parameter"]!.GetValue<string>(), Is.EqualTo("microlitres"));
            Assert.That(error.ErrorData["member"]!.GetValue<string>(), Is.EqualTo("syringe_pump.prime"));
        });
    }

    [Test]
    public void An_argument_of_the_wrong_type_names_what_was_expected()
    {
        var error = Assert.CatchAsync<ScriptApiException>(async () =>
            await Invoke(_pumpAssembly, "syringe_pump", NewPump(), "set_flow_rate", """{"value": "fast"}"""));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Code, Is.EqualTo(ScriptApiErrorCodes.InvalidParams));
            Assert.That(error.ErrorData!["expected"]!.GetValue<string>(), Is.EqualTo("a number"));
            Assert.That(error.ErrorData["actual"]!.GetValue<string>(), Is.EqualTo("a string"));
        });
    }

    [Test]
    public void An_unknown_member_is_not_found()
    {
        var error = Assert.CatchAsync<ScriptApiException>(async () =>
            await Invoke(_pumpAssembly, "syringe_pump", NewPump(), "levitate", "{}"));

        Assert.That(error!.Code, Is.EqualTo(ScriptApiErrorCodes.MemberNotFound));
    }

    [Test]
    public void Calling_a_stream_is_refused()
    {
        var error = Assert.CatchAsync<ScriptApiException>(async () =>
            await Invoke(_pumpAssembly, "syringe_pump", NewPump(), "flow", "{}"));

        Assert.That(error!.Code, Is.EqualTo(ScriptApiErrorCodes.WrongMemberKind));
    }

    [Test]
    public void Subscribing_to_a_call_is_refused()
    {
        using var arguments = JsonDocument.Parse("{}");

        var error = Assert.Catch<ScriptApiException>(() =>
            Dispatcher(_pumpAssembly, "syringe_pump")
                .Subscribe(NewPump(), "get_name", arguments.RootElement, new References(), CancellationToken.None));

        Assert.That(error!.Code, Is.EqualTo(ScriptApiErrorCodes.WrongMemberKind));
    }

    [Test]
    public async Task Object_references_are_resolved_on_the_way_in()
    {
        var runner = New(_recipeAssembly, "TestRunner");
        var pump = New(_recipeAssembly, "TestPump");
        var acquisition = Get(runner, "Started");

        var references = new References();
        references.Add("dev/pump1", "pump", pump);
        references.Add("h/1", "acquisition", acquisition);

        await Invoke(_recipeAssembly, "recipes", runner, "run", """
            {
              "pump": { "$ref": "dev/pump1", "$type": "pump" },
              "source": { "$ref": "h/1", "$type": "acquisition" },
              "steps": [ { "name": "wash", "duration": "00:00:30" } ]
            }
            """, references);

        Assert.That(Log(runner), Is.EqualTo("pump1 acq1 1 wash 00:00:30"));
    }

    [Test]
    public async Task Object_references_are_described_on_the_way_out()
    {
        var runner = New(_recipeAssembly, "TestRunner");
        var references = new References();

        var result = await Invoke(_recipeAssembly, "recipes", runner, "start", """{"channels": [1, 2]}""", references);

        Assert.Multiple(() =>
        {
            Assert.That(result!["$ref"]!.GetValue<string>(), Is.EqualTo("h/1"));
            Assert.That(result["$type"]!.GetValue<string>(), Is.EqualTo("acquisition"));
        });
    }

    [Test]
    public async Task A_member_of_an_inherited_script_api_type_dispatches_to_its_own_table()
    {
        var pump = New(_recipeAssembly, "TestPump");
        var registry = _recipeAssembly.GetTypes().Single(type => type.Name == "ScriptApiRegistry");
        var method = registry.GetMethod("TryGetMember", BindingFlags.Public | BindingFlags.Static);

        var arguments = new object[] { "pump", "get_id", null, null };
        var found = (bool)method!.Invoke(null, arguments)!;
        var owner = (IScriptApiDispatcher)arguments[2];

        Assert.That(found, Is.True);
        Assert.That(owner.WireTypeName, Is.EqualTo("hardware"),
            "get_id belongs to hardware, which pump extends.");

        using var empty = JsonDocument.Parse("{}");
        var result = await owner.InvokeAsync(pump, "get_id", empty.RootElement, new References(), CancellationToken.None);

        Assert.That(result!.GetValue<string>(), Is.EqualTo("pump1"));
    }

    private static IScriptApiDispatcher Dispatcher(Assembly assembly, string wireTypeName) =>
        GeneratorHarness.Dispatcher(assembly, wireTypeName);

    private static async Task<JsonNode> Invoke(
        Assembly assembly,
        string wireTypeName,
        object target,
        string member,
        string arguments,
        IScriptApiReferences references = null)
    {
        using var document = JsonDocument.Parse(arguments);

        return await Dispatcher(assembly, wireTypeName)
            .InvokeAsync(target, member, document.RootElement, references ?? new References(), CancellationToken.None);
    }

    private static object NewPump() => New(_pumpAssembly, "TestPump");

    private static object New(Assembly assembly, string typeName) =>
        Activator.CreateInstance(assembly.GetTypes().Single(type => type.Name == typeName))!;

    private static string Log(object instance) =>
        (string)instance.GetType().GetField("Log")!.GetValue(instance)!;

    private static object Get(object instance, string fieldName) =>
        instance.GetType().GetField(fieldName)!.GetValue(instance)!;

    /// <summary>
    /// Stands in for the RPC host's registry and handle table: resolves the two reference forms
    /// and mints a handle for anything leaving the boundary.
    /// </summary>
    private sealed class References : IScriptApiReferences
    {
        private readonly Dictionary<string, (string Type, object Target)> _byId = new(StringComparer.Ordinal);
        private int _nextHandle;

        public void Add(string id, string wireTypeName, object target) => _byId[id] = (wireTypeName, target);

        public object Resolve(JsonElement reference, string expectedWireTypeName, string parameterName)
        {
            var id = reference.GetProperty("$ref").GetString();

            if (!_byId.TryGetValue(id!, out var entry))
                throw new ScriptApiException(ScriptApiErrorCodes.HandleNotFound, $"'{id}' is not a live reference");

            if (entry.Type != expectedWireTypeName)
                throw ScriptApiException.HandleTypeMismatch(parameterName, expectedWireTypeName, entry.Type);

            return entry.Target;
        }

        public JsonNode Describe(object target, string declaredWireTypeName)
        {
            if (target == null)
                return null;

            var existing = _byId.FirstOrDefault(entry => ReferenceEquals(entry.Value.Target, target)).Key;
            if (existing == null)
            {
                existing = "h/" + ++_nextHandle;
                Add(existing, declaredWireTypeName, target);
            }

            return new JsonObject { ["$ref"] = existing, ["$type"] = declaredWireTypeName };
        }
    }
}
