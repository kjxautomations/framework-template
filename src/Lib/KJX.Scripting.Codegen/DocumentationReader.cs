using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace KJX.Scripting.Codegen;

/// <summary>
/// Pulls the text out of XML doc comments so it can become Python docstrings and stub comments.
/// Nothing else in the pipeline asks the author to write documentation twice.
/// </summary>
internal static class DocumentationReader
{
    /// <summary>The <c>&lt;summary&gt;</c> text of a symbol, whitespace normalized, or null.</summary>
    public static string Summary(ISymbol symbol) => Element(symbol, "summary");

    /// <summary>The <c>&lt;returns&gt;</c> text of a symbol, or null.</summary>
    public static string Returns(ISymbol symbol) => Element(symbol, "returns");

    /// <summary>The <c>&lt;param&gt;</c> texts of a symbol, keyed by parameter name.</summary>
    public static Dictionary<string, string> Parameters(ISymbol symbol)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var root = Parse(symbol);
        if (root == null)
            return result;

        foreach (var element in root.Descendants("param"))
        {
            var name = element.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name))
                continue;

            var text = Normalize(element);
            if (text != null)
                result[name] = text;
        }

        return result;
    }

    private static string Element(ISymbol symbol, string name)
    {
        var root = Parse(symbol);
        var element = root?.Descendants(name).FirstOrDefault();
        return element == null ? null : Normalize(element);
    }

    private static XElement Parse(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            return XDocument.Parse(xml).Root;
        }
        catch (Exception)
        {
            // Malformed documentation must not fail the build; the descriptor simply omits it.
            return null;
        }
    }

    /// <summary>
    /// Flattens an element to plain text: cref and paramref targets become their names, and the
    /// leading indentation every doc comment carries is removed.
    /// </summary>
    private static string Normalize(XElement element)
    {
        var text = new StringBuilder();
        Flatten(element, text);

        var lines = text.ToString()
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line.Trim())
            .ToList();

        while (lines.Count > 0 && lines[0].Length == 0)
            lines.RemoveAt(0);
        while (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        if (lines.Count == 0)
            return null;

        // Collapse the hard wrapping of the source comment into paragraphs.
        var paragraphs = new List<string>();
        var current = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                if (current.Length > 0)
                {
                    paragraphs.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            if (current.Length > 0)
                current.Append(' ');
            current.Append(line);
        }

        if (current.Length > 0)
            paragraphs.Add(current.ToString());

        return paragraphs.Count == 0 ? null : string.Join("\n\n", paragraphs);
    }

    private static void Flatten(XNode node, StringBuilder text)
    {
        switch (node)
        {
            case XText textNode:
                text.Append(textNode.Value);
                break;

            case XElement element:
                switch (element.Name.LocalName)
                {
                    case "see":
                    case "seealso":
                    case "paramref":
                    case "typeparamref":
                        text.Append(ReferenceName(element));
                        break;

                    case "para":
                        text.Append('\n').Append('\n');
                        foreach (var child in element.Nodes())
                            Flatten(child, text);
                        text.Append('\n').Append('\n');
                        break;

                    default:
                        foreach (var child in element.Nodes())
                            Flatten(child, text);
                        break;
                }

                break;
        }
    }

    private static string ReferenceName(XElement element)
    {
        var inner = element.Value;
        if (!string.IsNullOrWhiteSpace(inner))
            return inner.Trim();

        var target = element.Attribute("cref")?.Value ??
                     element.Attribute("name")?.Value ??
                     element.Attribute("href")?.Value ??
                     string.Empty;

        // crefs arrive as "T:Namespace.IThing"; the last identifier is the useful part.
        var colon = target.IndexOf(':');
        if (colon >= 0 && colon + 1 < target.Length)
            target = target.Substring(colon + 1);

        var dot = target.LastIndexOf('.');
        return dot >= 0 && dot + 1 < target.Length ? target.Substring(dot + 1) : target;
    }
}
