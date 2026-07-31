using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using KJX.Scripting.Codegen;
using KJX.Scripting.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KJX.Scripting.Tests;

/// <summary>The result of running the generator over a piece of source.</summary>
internal sealed record GeneratorRun(
    string DescriptorJson,
    string DescriptorHash,
    IReadOnlyDictionary<string, string> Sources,
    IReadOnlyList<Diagnostic> Diagnostics,
    Compilation Compilation)
{
    /// <summary>The diagnostic ids the generator produced, sorted.</summary>
    public string[] DiagnosticIds =>
        Diagnostics.Select(diagnostic => diagnostic.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
}

/// <summary>
/// Drives the source generator the way the compiler does, and can load the result so tests can
/// call the dispatch tables rather than only reading them.
/// </summary>
internal static class GeneratorHarness
{
    private static readonly Lazy<MetadataReference[]> References = new(() =>
    {
        var trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;

        var paths = trustedAssemblies
            .Split(Path.PathSeparator)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            .ToList();

        foreach (var assembly in new[] { typeof(ScriptApiAttribute).Assembly, typeof(IScriptApiDispatcher).Assembly })
        {
            if (!paths.Contains(assembly.Location, StringComparer.OrdinalIgnoreCase))
                paths.Add(assembly.Location);
        }

        return paths.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToArray();
    });

    /// <summary>Reads one of the checked-in sample interfaces.</summary>
    public static string Sample(string name) =>
        File.ReadAllText(Path.Combine(TestDataDirectory(), name + ".cs.txt"));

    /// <summary>The directory holding sample sources and golden descriptors.</summary>
    public static string TestDataDirectory([CallerFilePath] string thisFile = null) =>
        Path.Combine(Path.GetDirectoryName(thisFile)!, "TestData");

    /// <summary>
    /// Runs the generator against a compilation that is missing the runtime assembly, which is
    /// what a project that forgot the reference looks like.
    /// </summary>
    public static GeneratorRun RunWithoutRuntime(string source) =>
        Run(source, "SampleWithoutRuntime", withRuntime: false);

    /// <summary>Runs the generator over source and returns everything it produced.</summary>
    public static GeneratorRun Run(string source, string assemblyName = "Sample", bool withRuntime = true)
    {
        var references = withRuntime
            ? References.Value
            : References.Value
                .Where(reference => !Path.GetFileName((reference as PortableExecutableReference)?.FilePath ?? string.Empty)
                    .Equals("KJX.Scripting.Runtime.dll", StringComparison.OrdinalIgnoreCase))
                .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver
            .Create(new ScriptApiGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);

        var result = driver.GetRunResult();

        var sources = result.Results
            .SelectMany(generatorResult => generatorResult.GeneratedSources)
            .ToDictionary(
                generated => generated.HintName,
                generated => generated.SourceText.ToString(),
                StringComparer.Ordinal);

        sources.TryGetValue("ScriptApiDescriptor.g.cs", out var descriptorSource);

        return new GeneratorRun(
            ExtractConstant(descriptorSource, "Json"),
            ExtractConstant(descriptorSource, "Hash"),
            sources,
            diagnostics,
            updated);
    }

    /// <summary>
    /// Compiles the sample together with everything the generator produced, and loads it. This is
    /// what proves the generated dispatch is not just plausible text.
    /// </summary>
    public static Assembly Compile(string source, string assemblyName = "Sample")
    {
        var run = Run(source, assemblyName);

        var errors = run.Compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.That(errors, Is.Empty, "The generated code did not compile:" + Environment.NewLine +
                                      string.Join(Environment.NewLine, errors.Select(error => "  " + error)));

        using var assembly = new MemoryStream();
        var emitted = run.Compilation.Emit(assembly);

        Assert.That(emitted.Success, Is.True, "Emit failed:" + Environment.NewLine +
                                              string.Join(Environment.NewLine, emitted.Diagnostics));

        return Assembly.Load(assembly.ToArray());
    }

    /// <summary>Finds the generated registry in a compiled sample.</summary>
    public static IReadOnlyList<IScriptApiDispatcher> Dispatchers(Assembly assembly)
    {
        var registry = assembly.GetTypes().Single(type => type.Name == "ScriptApiRegistry");
        var property = registry.GetProperty("Dispatchers", BindingFlags.Public | BindingFlags.Static);
        return (IReadOnlyList<IScriptApiDispatcher>)property!.GetValue(null);
    }

    /// <summary>Finds one dispatcher by its wire type name.</summary>
    public static IScriptApiDispatcher Dispatcher(Assembly assembly, string wireTypeName) =>
        Dispatchers(assembly).Single(dispatcher => dispatcher.WireTypeName == wireTypeName);

    /// <summary>
    /// Compares against a checked-in descriptor. Set UPDATE_GOLDEN=1 to rewrite the file after an
    /// intended change; the diff is then reviewed like any other source change.
    /// </summary>
    public static void AssertMatchesGolden(string actual, string goldenFileName)
    {
        var path = Path.Combine(TestDataDirectory(), goldenFileName);
        var normalized = actual.Replace("\r\n", "\n");

        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, normalized);
            Assert.Warn($"Rewrote {goldenFileName}. Review the diff before committing.");
            return;
        }

        Assert.That(File.Exists(path), Is.True,
            $"{goldenFileName} does not exist. Run the tests once with UPDATE_GOLDEN=1 to create it.");

        var expected = File.ReadAllText(path).Replace("\r\n", "\n");

        if (expected == normalized)
            return;

        var actualPath = Path.ChangeExtension(path, ".actual.json");
        File.WriteAllText(actualPath, normalized);

        Assert.Fail($"The descriptor no longer matches {goldenFileName}." + Environment.NewLine +
                    $"What was produced has been written to {actualPath}." + Environment.NewLine +
                    FirstDifference(expected, normalized));
    }

    private static string FirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');

        for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            var left = i < expectedLines.Length ? expectedLines[i] : "(end of file)";
            var right = i < actualLines.Length ? actualLines[i] : "(end of file)";

            if (left != right)
                return $"First difference on line {i + 1}:{Environment.NewLine}  expected: {left}{Environment.NewLine}  actual:   {right}";
        }

        return string.Empty;
    }

    /// <summary>Pulls a const string back out of the generated descriptor source.</summary>
    private static string ExtractConstant(string source, string name)
    {
        if (source == null)
            return null;

        var match = Regex.Match(source, $@"public const string {name} = (""(?:[^""\\]|\\.)*"");", RegexOptions.Singleline);
        if (!match.Success)
            return null;

        // Parsing the literal is how the value is recovered exactly, escapes and all.
        var tree = CSharpSyntaxTree.ParseText("var value = " + match.Groups[1].Value + ";");
        var literal = tree.GetRoot().DescendantTokens().First(token => token.IsKind(SyntaxKind.StringLiteralToken));
        return literal.ValueText;
    }
}
