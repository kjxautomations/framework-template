namespace KJX.Scripting.Tests;

/// <summary>
/// The analyzer is what keeps the source generator total, so these tests are written as the
/// specification of the rejection rules rather than as coverage of the implementation.
/// </summary>
[TestFixture]
public class ScriptApiAnalyzerTests
{
    // ---------------------------------------------------------------- accepted shapes

    [Test]
    public Task Accepts_an_interface_of_supported_members() => AnalyzerHarness.AssertCleanAsync("""
        public enum PumpMode { Idle, Priming }

        public readonly record struct Reading(double Value, DateTimeOffset Timestamp);

        [ScriptApi]
        public interface ISyringePump
        {
            string Name { get; }
            double FlowRate { get; set; }
            double? UpperLimit { get; set; }
            PumpMode Mode { get; }
            TimeSpan Runtime { get; }
            Guid Id { get; }
            decimal Cost { get; }
            void Prime();
            Task StopAsync();
            Task<Reading> ReadAsync();
            ValueTask<int> CountAsync();
            IReadOnlyList<Reading> History { get; }
            double[] Calibration { get; }
        }
        """);

    [Test]
    public Task Accepts_a_trailing_cancellation_token() => AnalyzerHarness.AssertCleanAsync("""
        [ScriptApi]
        public interface IPump
        {
            Task PrimeAsync(double volume, CancellationToken cancellationToken = default);
        }
        """);

    [Test]
    public Task Accepts_an_async_enumerable_method_as_a_stream() => AnalyzerHarness.AssertCleanAsync("""
        public readonly record struct Reading(double Value, DateTimeOffset Timestamp);

        [ScriptApi]
        public interface ISensor
        {
            IAsyncEnumerable<Reading> Readings(CancellationToken cancellationToken = default);
        }
        """);

    [Test]
    public Task Accepts_an_async_enumerable_property_as_a_stream() => AnalyzerHarness.AssertCleanAsync("""
        [ScriptApi]
        public interface ISensor
        {
            IAsyncEnumerable<double> Readings { get; }
        }
        """);

    [Test]
    public Task Accepts_object_references_at_parameter_and_return_position() => AnalyzerHarness.AssertCleanAsync("""
        [ScriptApi]
        public interface IPump { }

        [ScriptApi]
        public interface IRecipe
        {
            void Run(IPump pump);
            void RunAll(IPump[] pumps);
            IReadOnlyList<IPump> Pumps { get; }
            IPump Primary { get; }
        }
        """);

    [Test]
    public Task Accepts_a_recursive_dto() => AnalyzerHarness.AssertCleanAsync("""
        public record Step(string Name, Step Next);

        [ScriptApi]
        public interface IRecipe
        {
            Step Head { get; }
        }
        """);

    [Test]
    public Task Accepts_a_nested_dto_of_supported_types() => AnalyzerHarness.AssertCleanAsync("""
        public record struct Point(double X, double Y);
        public record Path(string Name, IReadOnlyList<Point> Points, Point? Origin);

        [ScriptApi]
        public interface IStage
        {
            Task FollowAsync(Path path);
        }
        """);

    [Test]
    public Task Ignores_interfaces_that_are_not_marked() => AnalyzerHarness.AssertCleanAsync("""
        public interface INotScriptable
        {
            event Action<double> Updated;
            T Convert<T>(object value);
            void Configure(Func<double> source);
        }
        """);

    // ---------------------------------------------------------------- inheritance

    [Test]
    public Task Includes_members_of_unmarked_base_interfaces() => AnalyzerHarness.AssertDiagnosticsAsync("""
        public interface IHasEvent
        {
            event Action Fired;
        }

        [ScriptApi]
        public interface IThing : IHasEvent
        {
            void Poke();
        }
        """, "KJXSA002");

    [Test]
    public Task Does_not_re_export_members_of_a_marked_base_interface() => AnalyzerHarness.AssertDiagnosticsAsync("""
        [ScriptApi]
        public interface IBase
        {
            event Action Fired;
        }

        [ScriptApi]
        public interface IDerived : IBase
        {
            void Poke();
        }
        """, "KJXSA002");

    [Test]
    public Task A_marked_base_does_not_collide_with_the_derived_interface() => AnalyzerHarness.AssertCleanAsync("""
        [ScriptApi]
        public interface IBase
        {
            void Stop();
        }

        [ScriptApi]
        public interface IDerived : IBase
        {
            void Start();
        }
        """);

    [Test]
    public Task Exempts_INotifyPropertyChanged() => AnalyzerHarness.AssertCleanAsync("""
        public interface IDevice : INotifyPropertyChanged
        {
            bool IsBusy { get; }
        }

        [ScriptApi]
        public interface IMotor : IDevice
        {
            double Position { get; }
        }
        """);

    [Test]
    public Task Exempts_IDisposable_and_IAsyncDisposable() => AnalyzerHarness.AssertCleanAsync("""
        [ScriptApi]
        public interface IAcquisition : IDisposable, IAsyncDisposable
        {
            void Arm();
        }
        """);

    // ---------------------------------------------------------------- KJXSA001 unsupported types

    [TestCase("void Set(object value);")]
    [TestCase("void Set(DateTime when);")]
    [TestCase("void Set(double[,] grid);")]
    [TestCase("void Set(List<double> values);")]
    [TestCase("void Set(Dictionary<string, double> values);")]
    [TestCase("void Set(IEnumerable<double> values);")]
    [TestCase("object Get();")]
    [TestCase("void Set(System.IO.Stream stream);")]
    public Task Rejects_unsupported_types(string member) => AnalyzerHarness.AssertDiagnosticsAsync($$"""
        [ScriptApi]
        public interface IThing
        {
            {{member}}
        }
        """, "KJXSA001");

    [Test]
    public Task Rejects_a_plain_struct_that_is_not_a_record() => AnalyzerHarness.AssertDiagnosticsAsync("""
        public struct Size
        {
            public int Width { get; set; }
            public int Height { get; set; }
        }

        [ScriptApi]
        public interface ICamera
        {
            Size Resolution { get; set; }
        }
        """, "KJXSA001");

    [Test]
    public Task Rejects_an_interface_that_is_not_marked() => AnalyzerHarness.AssertDiagnosticsAsync("""
        public interface IImageBuffer
        {
            byte[] Buffer { get; }
        }

        [ScriptApi]
        public interface ICamera
        {
            IImageBuffer GetImage();
        }
        """, "KJXSA001");

    [Test]
    public Task Rejects_a_dto_with_an_unsupported_property() => AnalyzerHarness.AssertDiagnosticsAsync("""
        public record Inner(DateTime When);
        public record Outer(string Name, Inner Inner);

        [ScriptApi]
        public interface IThing
        {
            void Send(Outer outer);
        }
        """, "KJXSA001");

    [Test]
    public Task Rejects_a_cancellation_token_that_is_not_last() => AnalyzerHarness.AssertDiagnosticsAsync("""
        [ScriptApi]
        public interface IThing
        {
            Task GoAsync(CancellationToken cancellationToken, double distance);
        }
        """, "KJXSA001");

    // ---------------------------------------------------------------- KJXSA002 events

    [Test]
    public Task Rejects_events() => AnalyzerHarness.AssertDiagnosticsAsync("""
        [ScriptApi]
        public interface ISensor
        {
            event Action<double> ValueUpdated;
        }
        """, "KJXSA002");

    // ---------------------------------------------------------------- KJXSA003 generics

    [Test]
    public Task Rejects_generic_methods() => AnalyzerHarness.AssertDiagnosticsAsync("""
        [ScriptApi]
        public interface IThing
        {
            T Read<T>();
        }
        """, "KJXSA003");

    [Test]
    public Task Rejects_generic_interfaces() => AnalyzerHarness.AssertDiagnosticsAsync("""
        [ScriptApi]
        public interface IThing<T>
        {
            void Poke();
        }
        """, "KJXSA003");

    // ---------------------------------------------------------------- KJXSA004 by reference

    [TestCase("void Get(out double value);")]
    [TestCase("void Swap(ref double value);")]
    [TestCase("void Read(in double value);")]
    public Task Rejects_by_reference_parameters(string member) => AnalyzerHarness.AssertDiagnosticsAsync($$"""
        [ScriptApi]
        public interface IThing
        {
            {{member}}
        }
        """, "KJXSA004");

    // ---------------------------------------------------------------- KJXSA005 duplicate names

    [Test]
    public Task Rejects_overloads() => AnalyzerHarness.AssertDiagnosticsAsync("""
        [ScriptApi]
        public interface IMotor
        {
            void MoveTo(double position);
            void MoveTo(int steps);
        }
        """, "KJXSA005");

    [Test]
    public Task Rejects_a_collision_created_by_stripping_Async() => AnalyzerHarness.AssertDiagnosticsAsync("""
        [ScriptApi]
        public interface IMotor
        {
            void Move();
            Task MoveAsync();
        }
        """, "KJXSA005");

    [Test]
    public Task Rejects_a_collision_between_a_property_getter_and_a_method() => AnalyzerHarness.AssertDiagnosticsAsync("""
        [ScriptApi]
        public interface IMotor
        {
            double Position { get; }
            double GetPosition();
        }
        """, "KJXSA005");

    [Test]
    public Task Rejects_a_collision_created_by_snake_casing() => AnalyzerHarness.AssertDiagnosticsAsync("""
        [ScriptApi]
        public interface IMotor
        {
            void MoveTo(double position);
            void Move_To(double position);
        }
        """, "KJXSA005");

    // ---------------------------------------------------------------- KJXSA006 references in DTOs

    [Test]
    public Task Rejects_an_object_reference_inside_a_dto() => AnalyzerHarness.AssertDiagnosticsAsync("""
        [ScriptApi]
        public interface IPump { }

        public record Job(string Name, IPump Pump);

        [ScriptApi]
        public interface IRecipe
        {
            void Run(Job job);
        }
        """, "KJXSA006");

    [Test]
    public Task Rejects_an_object_reference_nested_deeper_in_a_dto() => AnalyzerHarness.AssertDiagnosticsAsync("""
        [ScriptApi]
        public interface IPump { }

        public record Inner(IPump[] Pumps);
        public record Outer(Inner Inner);

        [ScriptApi]
        public interface IRecipe
        {
            Outer Describe();
        }
        """, "KJXSA006");

    // ---------------------------------------------------------------- KJXSA007 delegates

    [TestCase("void Configure(Func<double> source);")]
    [TestCase("void Configure(Action<double> sink);")]
    [TestCase("Action Get();")]
    [TestCase("void Configure(EventHandler handler);")]
    public Task Rejects_delegates(string member) => AnalyzerHarness.AssertDiagnosticsAsync($$"""
        [ScriptApi]
        public interface IThing
        {
            {{member}}
        }
        """, "KJXSA007");

    [Test]
    public Task Rejects_a_custom_delegate_type() => AnalyzerHarness.AssertDiagnosticsAsync("""
        public delegate void ReadingHandler(double value);

        [ScriptApi]
        public interface ISensor
        {
            void Subscribe(ReadingHandler handler);
        }
        """, "KJXSA007");

    // ---------------------------------------------------------------- KJXSA008 member kinds

    [Test]
    public Task Rejects_indexers() => AnalyzerHarness.AssertDiagnosticsAsync("""
        [ScriptApi]
        public interface IChannels
        {
            double this[int index] { get; }
        }
        """, "KJXSA008");

    [Test]
    public Task Rejects_static_members() => AnalyzerHarness.AssertDiagnosticsAsync("""
        [ScriptApi]
        public interface IThing
        {
            static abstract void Reset();
        }
        """, "KJXSA008");

    // ---------------------------------------------------------------- reporting

    [Test]
    public async Task Reports_inherited_problems_on_the_interface_that_pulled_them_in()
    {
        var diagnostics = await AnalyzerHarness.RunAsync("""
            public interface IHasEvent
            {
                event Action Fired;
            }

            [ScriptApi]
            public interface IThing : IHasEvent
            {
            }
            """);

        Assert.That(diagnostics, Has.Count.EqualTo(1));

        var line = diagnostics[0].Location.GetLineSpan().StartLinePosition.Line;
        var text = await diagnostics[0].Location.SourceTree!.GetTextAsync();

        Assert.That(text.Lines[line].ToString(), Does.Contain("IThing"),
            "The diagnostic should land on the marked interface, not on the base that declares the event.");
    }

    [Test]
    public async Task Reports_every_offending_member_not_just_the_first()
    {
        var ids = await AnalyzerHarness.GetDiagnosticIdsAsync("""
            [ScriptApi]
            public interface IThing
            {
                event Action Fired;
                void Configure(Func<double> source);
                T Read<T>();
            }
            """);

        Assert.That(ids, Is.EqualTo(new[] { "KJXSA002", "KJXSA003", "KJXSA007" }));
    }
}
