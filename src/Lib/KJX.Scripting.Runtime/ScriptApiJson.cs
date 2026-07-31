using System.Globalization;
using System.Text.Json;

namespace KJX.Scripting.Runtime;

/// <summary>
/// Typed readers for incoming arguments. The generated dispatch calls these instead of a
/// serializer, so no argument is ever bound by reflection and every failure names the member and
/// the parameter that caused it.
/// </summary>
public static class ScriptApiJson
{
    /// <summary>Fetches a required argument, or throws with the parameter named.</summary>
    public static JsonElement Required(JsonElement arguments, string parameter, string member)
    {
        RequireArgumentObject(arguments, member);

        // A request with no params at all is the same as one whose params lack this argument.
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(parameter, out var value) ||
            value.ValueKind == JsonValueKind.Undefined)
        {
            throw ScriptApiException.MissingArgument(member, parameter);
        }

        return value;
    }

    /// <summary>
    /// Fetches an optional argument. A missing argument and an explicit null are both treated as
    /// "use the default", which is what a Python caller passing None expects.
    /// </summary>
    public static bool TryGet(JsonElement arguments, string parameter, string member, out JsonElement value)
    {
        RequireArgumentObject(arguments, member);

        if (arguments.ValueKind == JsonValueKind.Object &&
            arguments.TryGetProperty(parameter, out value) &&
            value.ValueKind != JsonValueKind.Undefined &&
            value.ValueKind != JsonValueKind.Null)
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>True when the value is JSON null, i.e. a nullable member's empty case.</summary>
    public static bool IsNull(JsonElement value) =>
        value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;

    /// <summary>Params are always by-name objects; positional arrays are rejected outright.</summary>
    public static void RequireArgumentObject(JsonElement arguments, string member)
    {
        if (arguments.ValueKind is JsonValueKind.Object or JsonValueKind.Undefined)
            return;

        throw ScriptApiException.ArgumentsNotAnObject(member, Describe(arguments));
    }

    /// <summary>Checks that a value is a JSON object before reading a DTO out of it.</summary>
    public static JsonElement RequireObject(JsonElement value, string parameter, string member)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw ScriptApiException.WrongArgumentType(member, parameter, "an object", Describe(value));

        return value;
    }

    /// <summary>Checks that a value is a JSON array before reading a list out of it.</summary>
    public static JsonElement RequireArray(JsonElement value, string parameter, string member)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw ScriptApiException.WrongArgumentType(member, parameter, "an array", Describe(value));

        return value;
    }

    /// <summary>Describes a value's shape for an error message, never its content.</summary>
    public static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Null => "null",
        _ => "nothing",
    };

    public static bool ToBoolean(JsonElement value, string parameter, string member) =>
        value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw ScriptApiException.WrongArgumentType(member, parameter, "a boolean", Describe(value)),
        };

    public static string ToStringValue(JsonElement value, string parameter, string member) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw ScriptApiException.WrongArgumentType(member, parameter, "a string", Describe(value));

    public static char ToChar(JsonElement value, string parameter, string member)
    {
        var text = ToStringValue(value, parameter, member);
        return text.Length == 1
            ? text[0]
            : throw ScriptApiException.WrongArgumentType(member, parameter, "a single-character string", "a string");
    }

    public static double ToDouble(JsonElement value, string parameter, string member) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var result)
            ? result
            : throw ScriptApiException.WrongArgumentType(member, parameter, "a number", Describe(value));

    public static float ToSingle(JsonElement value, string parameter, string member) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var result)
            ? result
            : throw ScriptApiException.WrongArgumentType(member, parameter, "a number", Describe(value));

    public static decimal ToDecimal(JsonElement value, string parameter, string member) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var result)
            ? result
            : throw ScriptApiException.WrongArgumentType(member, parameter, "a number", Describe(value));

    public static sbyte ToSByte(JsonElement value, string parameter, string member) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetSByte(out var result)
            ? result
            : throw ScriptApiException.WrongArgumentType(member, parameter, "an integer", Describe(value));

    public static byte ToByte(JsonElement value, string parameter, string member) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetByte(out var result)
            ? result
            : throw ScriptApiException.WrongArgumentType(member, parameter, "an integer", Describe(value));

    public static short ToInt16(JsonElement value, string parameter, string member) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt16(out var result)
            ? result
            : throw ScriptApiException.WrongArgumentType(member, parameter, "an integer", Describe(value));

    public static ushort ToUInt16(JsonElement value, string parameter, string member) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetUInt16(out var result)
            ? result
            : throw ScriptApiException.WrongArgumentType(member, parameter, "an integer", Describe(value));

    public static int ToInt32(JsonElement value, string parameter, string member) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : throw ScriptApiException.WrongArgumentType(member, parameter, "an integer", Describe(value));

    public static uint ToUInt32(JsonElement value, string parameter, string member) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out var result)
            ? result
            : throw ScriptApiException.WrongArgumentType(member, parameter, "an integer", Describe(value));

    public static long ToInt64(JsonElement value, string parameter, string member) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result)
            ? result
            : throw ScriptApiException.WrongArgumentType(member, parameter, "an integer", Describe(value));

    public static ulong ToUInt64(JsonElement value, string parameter, string member) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out var result)
            ? result
            : throw ScriptApiException.WrongArgumentType(member, parameter, "an integer", Describe(value));

    public static Guid ToGuid(JsonElement value, string parameter, string member) =>
        value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var result)
            ? result
            : throw ScriptApiException.WrongArgumentType(member, parameter, "a GUID string", Describe(value));

    public static DateTimeOffset ToDateTimeOffset(JsonElement value, string parameter, string member) =>
        value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var result)
            ? result
            : throw ScriptApiException.WrongArgumentType(member, parameter, "an ISO 8601 timestamp", Describe(value));

    /// <summary>
    /// Durations cross the wire as an ISO 8601 duration string, which is what Python's
    /// <c>datetime.timedelta</c> maps onto most cleanly.
    /// </summary>
    public static TimeSpan ToTimeSpan(JsonElement value, string parameter, string member)
    {
        var text = value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw ScriptApiException.WrongArgumentType(member, parameter, "a duration string", Describe(value));

        return TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out var result)
            ? result
            : throw ScriptApiException.WrongArgumentType(member, parameter, "a duration string", "a string");
    }

    /// <summary>Reads the name of an enum value, which is how enums travel on the wire.</summary>
    public static string ToEnumName(JsonElement value, string parameter, string member) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw ScriptApiException.WrongArgumentType(member, parameter, "an enum name", Describe(value));

    /// <summary>Reports an enum name that is not one of the declared values.</summary>
    public static ScriptApiException UnknownEnumValue(string parameter, string member, string enumName, string permitted) =>
        ScriptApiException.WrongArgumentType(member, parameter, $"one of {permitted}", $"'{enumName}'");

    /// <summary>Formats a duration for the wire.</summary>
    public static string FromTimeSpan(TimeSpan value) => value.ToString("c", CultureInfo.InvariantCulture);
}
