using System.Text.Json;
using System.Text.Json.Nodes;
using KJX.Scripting.Runtime;

namespace KJX.Scripting.Tests;

/// <summary>
/// The descriptor is a contract: clients fetch it on connect and cache generated stubs under its
/// hash. Golden files make any change to it show up as a reviewable diff rather than as a
/// surprise at the far end of the wire.
/// </summary>
[TestFixture]
public class ScriptApiGeneratorTests
{
    [Test]
    public void Descriptor_for_a_streaming_interface_matches_the_golden_file()
    {
        var run = GeneratorHarness.Run(GeneratorHarness.Sample("StreamSample"));

        Assert.That(run.DiagnosticIds, Is.Empty);
        GeneratorHarness.AssertMatchesGolden(run.DescriptorJson, "StreamSample.descriptor.json");
    }

    [Test]
    public void Descriptor_for_an_interface_with_references_matches_the_golden_file()
    {
        var run = GeneratorHarness.Run(GeneratorHarness.Sample("ReferenceSample"));

        Assert.That(run.DiagnosticIds, Is.Empty);
        GeneratorHarness.AssertMatchesGolden(run.DescriptorJson, "ReferenceSample.descriptor.json");
    }

    [TestCase("StreamSample")]
    [TestCase("ReferenceSample")]
    public void Generated_code_compiles(string sample)
    {
        // Compile asserts on the diagnostics; reaching the end means the generated dispatch,
        // readers and writers all built.
        GeneratorHarness.Compile(GeneratorHarness.Sample(sample), sample);
    }

    [Test]
    public void The_hash_covers_the_descriptor_body()
    {
        var run = GeneratorHarness.Run(GeneratorHarness.Sample("StreamSample"));
        var document = JsonNode.Parse(run.DescriptorJson);

        Assert.That(run.DescriptorHash, Does.StartWith("sha256:"));
        Assert.That(document!["hash"]!.GetValue<string>(), Is.EqualTo(run.DescriptorHash));
    }

    [Test]
    public void The_same_surface_produces_the_same_hash()
    {
        var source = GeneratorHarness.Sample("StreamSample");
        var first = GeneratorHarness.Run(source, "FirstRun");
        var second = GeneratorHarness.Run(source, "SecondRun");

        Assert.That(second.DescriptorHash, Is.EqualTo(first.DescriptorHash));
    }

    [Test]
    public void A_changed_surface_produces_a_different_hash()
    {
        var source = GeneratorHarness.Sample("StreamSample");
        var extended = source.Replace(
            "    /// <summary>Stops the pump immediately.</summary>",
            "    /// <summary>Empties the syringe.</summary>\n    void Purge();\n\n    /// <summary>Stops the pump immediately.</summary>");

        Assert.That(extended, Is.Not.EqualTo(source), "The sample no longer contains the anchor this test edits.");
        Assert.That(GeneratorHarness.Run(extended).DescriptorHash,
            Is.Not.EqualTo(GeneratorHarness.Run(source).DescriptorHash));
    }

    [Test]
    public void Documentation_reaches_the_descriptor()
    {
        var run = GeneratorHarness.Run(GeneratorHarness.Sample("StreamSample"));
        var pump = Types(run).Single(type => type!["name"]!.GetValue<string>() == "syringe_pump");
        var prime = Members(pump).Single(member => member!["name"]!.GetValue<string>() == "prime");

        Assert.That(pump["doc"]!.GetValue<string>(), Is.EqualTo("A syringe pump."));
        Assert.That(prime["doc"]!.GetValue<string>(), Is.EqualTo("Draws liquid in."));
        Assert.That(prime["params"]![0]!["doc"]!.GetValue<string>(), Is.EqualTo("How much to draw."));
    }

    [Test]
    public void A_trailing_cancellation_token_is_not_a_parameter()
    {
        var run = GeneratorHarness.Run(GeneratorHarness.Sample("StreamSample"));
        var pump = Types(run).Single(type => type!["name"]!.GetValue<string>() == "syringe_pump");
        var prime = Members(pump).Single(member => member!["name"]!.GetValue<string>() == "prime");

        Assert.That(prime["params"]!.AsArray().Select(p => p!["name"]!.GetValue<string>()),
            Is.EqualTo(new[] { "microlitres", "settle" }));
    }

    [Test]
    public void Optional_parameters_carry_their_defaults()
    {
        var run = GeneratorHarness.Run(GeneratorHarness.Sample("StreamSample"));
        var pump = Types(run).Single(type => type!["name"]!.GetValue<string>() == "syringe_pump");
        var calibrate = Members(pump).Single(member => member!["name"]!.GetValue<string>() == "calibrate");

        var passes = calibrate["params"]![0];
        var state = calibrate["params"]![1];

        Assert.Multiple(() =>
        {
            Assert.That(passes!["required"]!.GetValue<bool>(), Is.False);
            Assert.That(passes["default"]!.GetValue<int>(), Is.EqualTo(3));
            Assert.That(state!["default"]!.GetValue<string>(), Is.EqualTo("Idle"),
                "Enum defaults travel as the name of the value, like every other enum on the wire.");
        });
    }

    [Test]
    public void Streams_are_declared_as_subscriptions()
    {
        var run = GeneratorHarness.Run(GeneratorHarness.Sample("StreamSample"));
        var pump = Types(run).Single(type => type!["name"]!.GetValue<string>() == "syringe_pump");
        var flow = Members(pump).Single(member => member!["name"]!.GetValue<string>() == "flow");

        Assert.Multiple(() =>
        {
            Assert.That(flow["kind"]!.GetValue<string>(), Is.EqualTo("stream"));
            Assert.That(flow["yields"]!["kind"]!.GetValue<string>(), Is.EqualTo("dto"));
            Assert.That(flow["yields"]!["name"]!.GetValue<string>(), Is.EqualTo("flow_reading"));
        });
    }

    [Test]
    public void References_are_declared_as_references_not_as_dtos()
    {
        var run = GeneratorHarness.Run(GeneratorHarness.Sample("ReferenceSample"));
        var recipes = Types(run).Single(type => type!["name"]!.GetValue<string>() == "recipes");
        var start = Members(recipes).Single(member => member!["name"]!.GetValue<string>() == "start");
        var runStep = Members(recipes).Single(member => member!["name"]!.GetValue<string>() == "run");

        Assert.Multiple(() =>
        {
            Assert.That(start["returns"]!["kind"]!.GetValue<string>(), Is.EqualTo("ref"));
            Assert.That(start["returns"]!["name"]!.GetValue<string>(), Is.EqualTo("acquisition"));
            Assert.That(runStep["params"]![0]!["type"]!["kind"]!.GetValue<string>(), Is.EqualTo("ref"));
            Assert.That(runStep["params"]![2]!["type"]!["kind"]!.GetValue<string>(), Is.EqualTo("list"),
                "A list of DTOs stays a list of DTOs.");
        });
    }

    [Test]
    public void An_explicit_wire_name_overrides_the_default()
    {
        var run = GeneratorHarness.Run(GeneratorHarness.Sample("ReferenceSample"));

        Assert.That(Types(run).Select(type => type!["name"]!.GetValue<string>()),
            Does.Contain("recipes").And.Not.Contains("recipe_runner"));
    }

    [Test]
    public void Inherited_script_api_members_are_recorded_as_inheritance_not_repeated()
    {
        var run = GeneratorHarness.Run(GeneratorHarness.Sample("ReferenceSample"));
        var pump = Types(run).Single(type => type!["name"]!.GetValue<string>() == "pump");

        Assert.Multiple(() =>
        {
            Assert.That(pump["extends"]!.AsArray().Select(name => name!.GetValue<string>()),
                Is.EqualTo(new[] { "hardware" }));
            Assert.That(Members(pump).Select(member => member!["name"]!.GetValue<string>()),
                Is.EqualTo(new[] { "get_flow_rate", "set_flow_rate" }),
                "get_id belongs to hardware and is dispatched there.");
        });
    }

    [Test]
    public void Two_types_that_want_the_same_wire_name_are_rejected()
    {
        var run = GeneratorHarness.Run("""
            using KJX.Scripting;

            namespace First { [ScriptApi] public interface IPump { void Stop(); } }
            namespace Second { [ScriptApi] public interface IPump { void Start(); } }
            """);

        Assert.That(run.DiagnosticIds, Is.EqualTo(new[] { "KJXSG001" }));
    }

    [Test]
    public void A_dto_that_cannot_be_rebuilt_is_rejected()
    {
        var run = GeneratorHarness.Run("""
            using KJX.Scripting;

            namespace Sample;

            public record Reading
            {
                private Reading(double value) { Value = value; }
                public double Value { get; }
                public static Reading Of(double value) => new Reading(value);
            }

            [ScriptApi]
            public interface ILogger
            {
                void Record(Reading reading);
            }
            """);

        Assert.That(run.DiagnosticIds, Is.EqualTo(new[] { "KJXSG002" }));
    }

    [Test]
    public void A_project_without_the_runtime_is_told_so()
    {
        // The sample compiles against KJX.Scripting only, which is what a project that forgot the
        // runtime reference looks like.
        var run = GeneratorHarness.RunWithoutRuntime("""
            using KJX.Scripting;

            namespace Sample;

            [ScriptApi]
            public interface IPump { void Stop(); }
            """);

        Assert.That(run.DiagnosticIds, Is.EqualTo(new[] { "KJXSG004" }));
    }

    private static IEnumerable<JsonNode> Types(GeneratorRun run) =>
        JsonNode.Parse(run.DescriptorJson)!["api"]!["types"]!.AsArray();

    private static IEnumerable<JsonNode> Members(JsonNode type) => type["members"]!.AsArray();
}
