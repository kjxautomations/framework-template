using System;
using System.Text;

namespace KJX.Scripting.Codegen;

/// <summary>An indenting text builder, so the generated sources are readable when something goes wrong.</summary>
internal sealed class CodeBuilder
{
    private readonly StringBuilder _text = new();
    private int _indent;

    public CodeBuilder Line(string text = "")
    {
        if (text.Length > 0)
            _text.Append(' ', _indent * 4).Append(text);

        _text.Append('\n');
        return this;
    }

    /// <summary>Writes a summary comment, when there is documentation to write.</summary>
    public CodeBuilder Doc(string text)
    {
        if (string.IsNullOrEmpty(text))
            return this;

        Line("/// <summary>" + Escape(JsonText.CommentText(text)) + "</summary>");
        return this;
    }

    public CodeBuilder Open(string text)
    {
        if (text.Length > 0)
            Line(text);

        Line("{");
        _indent++;
        return this;
    }

    public CodeBuilder Close(string suffix = "")
    {
        _indent--;
        Line("}" + suffix);
        return this;
    }

    public IDisposable Block(string text)
    {
        Open(text);
        return new Closer(this);
    }

    public CodeBuilder Indent()
    {
        _indent++;
        return this;
    }

    public CodeBuilder Outdent()
    {
        _indent--;
        return this;
    }

    public override string ToString() => _text.ToString();

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private sealed class Closer(CodeBuilder builder) : IDisposable
    {
        public void Dispose() => builder.Close();
    }
}
