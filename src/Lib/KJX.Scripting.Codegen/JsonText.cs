using System;
using System.Collections.Generic;
using System.Text;

namespace KJX.Scripting.Codegen;

/// <summary>
/// A small deterministic JSON writer. The descriptor is hashed and checked against golden files,
/// so byte-for-byte stability across platforms matters more than features: line endings are
/// always \n and indentation is always two spaces.
/// </summary>
internal sealed class JsonText
{
    private readonly StringBuilder _text = new();
    private readonly Stack<Container> _containers = new();
    private int _indent;

    private sealed class Container
    {
        public bool IsArray;
        public bool HasItems;
    }

    /// <summary>Creates a writer whose output is nested at the given indentation level.</summary>
    public JsonText(int initialIndent = 0)
    {
        _indent = initialIndent;
    }

    public JsonText BeginObject() => Begin(isArray: false);

    public JsonText EndObject() => End(isArray: false);

    public JsonText BeginArray() => Begin(isArray: true);

    public JsonText EndArray() => End(isArray: true);

    /// <summary>Writes a property name; the next call writes its value.</summary>
    public JsonText Key(string name)
    {
        Separate();
        _text.Append(Quote(name)).Append(": ");
        return this;
    }

    public JsonText String(string value)
    {
        if (value == null)
            return Raw("null");

        AppendValue(Quote(value));
        return this;
    }

    public JsonText Bool(bool value) => Raw(value ? "true" : "false");

    /// <summary>Writes a value that is already JSON, such as a number.</summary>
    public JsonText Raw(string json)
    {
        AppendValue(json);
        return this;
    }

    /// <summary>Writes a property only when the value is present.</summary>
    public JsonText OptionalString(string name, string value)
    {
        if (!string.IsNullOrEmpty(value))
            Key(name).String(value);

        return this;
    }

    public override string ToString() => _text.ToString();

    private JsonText Begin(bool isArray)
    {
        Separate();
        _text.Append(isArray ? '[' : '{');
        _containers.Push(new Container { IsArray = isArray });
        _indent++;
        return this;
    }

    private JsonText End(bool isArray)
    {
        var container = _containers.Pop();
        _indent--;

        if (container.HasItems)
            NewLine();

        _text.Append(isArray ? ']' : '}');
        MarkWritten();
        return this;
    }

    /// <summary>
    /// Emits the comma and newline that precede an item. Property values follow their key on the
    /// same line, so only array items and property names are separated this way.
    /// </summary>
    private void Separate()
    {
        if (_containers.Count == 0)
            return;

        var container = _containers.Peek();

        // A property value follows "key": on the same line and needs no separator.
        if (EndsWithKey())
            return;

        if (container.HasItems)
            _text.Append(',');

        NewLine();
        container.HasItems = true;
    }

    private void AppendValue(string value)
    {
        Separate();
        _text.Append(value);
        MarkWritten();
    }

    private void MarkWritten()
    {
        if (_containers.Count > 0)
            _containers.Peek().HasItems = true;
    }

    private bool EndsWithKey() => _text.Length >= 2 &&
                                  _text[_text.Length - 1] == ' ' &&
                                  _text[_text.Length - 2] == ':';

    private void NewLine()
    {
        _text.Append('\n');
        _text.Append(' ', _indent * 2);
    }

    /// <summary>Quotes and escapes a JSON string.</summary>
    public static string Quote(string value)
    {
        var text = new StringBuilder(value.Length + 2);
        text.Append('"');

        foreach (var character in value)
        {
            switch (character)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\b': text.Append("\\b"); break;
                case '\f': text.Append("\\f"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                default:
                    if (character < ' ' || character > '~')
                        text.Append("\\u").Append(((int)character).ToString("x4"));
                    else
                        text.Append(character);
                    break;
            }
        }

        text.Append('"');
        return text.ToString();
    }

    /// <summary>Renders a string as a C# literal.</summary>
    public static string CSharpLiteral(string value)
    {
        if (value == null)
            return "null";

        var text = new StringBuilder(value.Length + 2);
        text.Append('"');

        foreach (var character in value)
        {
            switch (character)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\0': text.Append("\\0"); break;
                case '\a': text.Append("\\a"); break;
                case '\b': text.Append("\\b"); break;
                case '\f': text.Append("\\f"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                case '\v': text.Append("\\v"); break;
                default:
                    if (character < ' ' || character > '~')
                        text.Append("\\u").Append(((int)character).ToString("x4"));
                    else
                        text.Append(character);
                    break;
            }
        }

        text.Append('"');
        return text.ToString();
    }

    /// <summary>Renders a character as a C# literal.</summary>
    public static string CSharpCharLiteral(char value)
    {
        var literal = CSharpLiteral(value.ToString());
        var inner = literal.Substring(1, literal.Length - 2);
        return "'" + (inner == "'" ? "\\'" : inner) + "'";
    }

    /// <summary>Renders a comment safely, keeping doc text on one line.</summary>
    public static string CommentText(string value) =>
        value == null ? null : value.Replace("\r", string.Empty).Replace("\n", " ");
}
