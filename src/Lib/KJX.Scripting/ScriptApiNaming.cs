using System;
using System.Text;

namespace KJX.Scripting;

/// <summary>
/// Translates C# identifiers into the names used on the wire.
/// </summary>
/// <remarks>
/// <para>
/// This file is compiled into the analyzer and the source generator as well as the runtime, so
/// that all three agree on naming by construction rather than by convention. Keep it dependency
/// free and side-effect free.
/// </para>
/// <para>
/// The compile-time copies define SCRIPTAPI_SHARED_SOURCE so that they do not publish a second
/// public <c>KJX.Scripting.ScriptApiNaming</c> into any project that references both assemblies.
/// </para>
/// </remarks>
#if SCRIPTAPI_SHARED_SOURCE
internal
#else
public
#endif
static class ScriptApiNaming
{
    private const string AsyncSuffix = "Async";

    /// <summary>
    /// The default wire type name for an interface: the leading <c>I</c> is dropped when it is
    /// followed by another capital, and the remainder is converted to snake_case.
    /// </summary>
    public static string WireTypeName(string interfaceName)
    {
        if (string.IsNullOrEmpty(interfaceName))
            throw new ArgumentException("Interface name is required.", nameof(interfaceName));

        var name = interfaceName;
        if (name.Length >= 2 && name[0] == 'I' && char.IsUpper(name[1]))
            name = name.Substring(1);

        return ToSnakeCase(name);
    }

    /// <summary>
    /// The wire name for a method: a trailing <c>Async</c> is stripped, then snake_case.
    /// </summary>
    public static string MethodName(string methodName)
    {
        if (string.IsNullOrEmpty(methodName))
            throw new ArgumentException("Method name is required.", nameof(methodName));

        var name = methodName;
        if (name.Length > AsyncSuffix.Length && name.EndsWith(AsyncSuffix, StringComparison.Ordinal))
            name = name.Substring(0, name.Length - AsyncSuffix.Length);

        return ToSnakeCase(name);
    }

    /// <summary>The wire name of a property's getter, e.g. <c>get_flow_rate</c>.</summary>
    public static string PropertyGetterName(string propertyName) => "get_" + ToSnakeCase(Required(propertyName));

    /// <summary>The wire name of a property's setter, e.g. <c>set_flow_rate</c>.</summary>
    public static string PropertySetterName(string propertyName) => "set_" + ToSnakeCase(Required(propertyName));

    /// <summary>The wire name of a parameter.</summary>
    public static string ParameterName(string parameterName) => ToSnakeCase(Required(parameterName));

    /// <summary>
    /// PascalCase to snake_case. Runs of capitals are kept together, so <c>IOPin</c> becomes
    /// <c>io_pin</c> and <c>HTTPServer</c> becomes <c>http_server</c>.
    /// </summary>
    public static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (c == '_')
            {
                AppendSeparator(builder);
                continue;
            }

            if (!char.IsUpper(c))
            {
                builder.Append(c);
                continue;
            }

            if (i > 0)
            {
                var previous = name[i - 1];
                var startsNewWord =
                    char.IsLower(previous) || char.IsDigit(previous) ||
                    (char.IsUpper(previous) && i + 1 < name.Length && char.IsLower(name[i + 1]));

                if (startsNewWord)
                    AppendSeparator(builder);
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private static void AppendSeparator(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[builder.Length - 1] != '_')
            builder.Append('_');
    }

    private static string Required(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Name is required.", nameof(name));
        return name;
    }
}
