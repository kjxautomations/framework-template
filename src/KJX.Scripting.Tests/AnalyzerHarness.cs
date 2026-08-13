using System.Collections.Immutable;
using KJX.Scripting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KJX.Scripting.Tests;

/// <summary>
/// Compiles a snippet in memory and runs the script API analyzer over it. Deliberately small:
/// the analyzer's contract is "which diagnostics come out of this source", so that is all this
/// exposes.
/// </summary>
internal static class AnalyzerHarness
{
    /// <summary>
    /// Every reference the test source might need. Taking the running test host's own reference
    /// set keeps the snippets free to use anything in the BCL without maintaining a list here.
    /// </summary>
    private static readonly Lazy<MetadataReference[]> References = new(() =>
    {
        var trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;

        var references = trustedAssemblies
            .Split(Path.PathSeparator)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        var scriptingAssembly = typeof(ScriptApiAttribute).Assembly.Location;
        if (!references.Any(reference => string.Equals(
                (reference as PortableExecutableReference)?.FilePath,
                scriptingAssembly,
                StringComparison.OrdinalIgnoreCase)))
        {
            references.Add(MetadataReference.CreateFromFile(scriptingAssembly));
        }

        return references.ToArray();
    });

    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using System.ComponentModel;
        using System.Threading;
        using System.Threading.Tasks;
        using KJX.Scripting;

        """;

    /// <summary>Runs the analyzer and returns the diagnostic ids it produced, in order.</summary>
    public static async Task<string[]> GetDiagnosticIdsAsync(string source)
    {
        var diagnostics = await RunAsync(source);
        return diagnostics.Select(diagnostic => diagnostic.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Asserts the analyzer produces exactly the expected diagnostic ids.</summary>
    public static async Task AssertDiagnosticsAsync(string source, params string[] expectedIds)
    {
        var diagnostics = await RunAsync(source);
        var actual = diagnostics.Select(diagnostic => diagnostic.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();

        Assert.That(actual, Is.EqualTo(expectedIds.OrderBy(id => id, StringComparer.Ordinal).ToArray()),
            "Diagnostics were:" + Environment.NewLine +
            (diagnostics.Count == 0
                ? "  (none)"
                : string.Join(Environment.NewLine, diagnostics.Select(diagnostic => "  " + diagnostic))));
    }

    /// <summary>Asserts the analyzer accepts the source without complaint.</summary>
    public static Task AssertCleanAsync(string source) => AssertDiagnosticsAsync(source);

    public static async Task<IReadOnlyList<Diagnostic>> RunAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            Preamble + source,
            new CSharpParseOptions(LanguageVersion.Latest));

        var compilation = CSharpCompilation.Create(
            assemblyName: "ScriptApiAnalyzerTests",
            syntaxTrees: new[] { syntaxTree },
            references: References.Value,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compilerErrors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (compilerErrors.Count > 0)
        {
            Assert.Fail("The test source did not compile:" + Environment.NewLine +
                        string.Join(Environment.NewLine, compilerErrors.Select(error => "  " + error)));
        }

        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new Analyzer.ScriptApiAnalyzer()));

        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
