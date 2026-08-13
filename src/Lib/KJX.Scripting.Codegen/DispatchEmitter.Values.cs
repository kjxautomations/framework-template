using System;
using System.Collections.Generic;
using System.Linq;

namespace KJX.Scripting.Codegen;

/// <summary>
/// The value half of the generator: for every type that crosses the boundary, a reader that
/// builds it from a <c>JsonElement</c> and a writer that turns it into a <c>JsonNode</c>. The
/// analyzer's closed type set is what makes writing these out exhaustively possible.
/// </summary>
internal sealed partial class DispatchEmitter
{
    /// <summary>Kinds simple enough to read and write inline, without a helper method.</summary>
    private static bool NeedsHelper(ApiTypeRef type) =>
        type.Nullable ||
        type.Kind is ApiTypeKinds.Array or ApiTypeKinds.List or ApiTypeKinds.Dto
            or ApiTypeKinds.Enum or ApiTypeKinds.Reference or ApiTypeKinds.TimeSpan;

    private string WriteValue(ApiTypeRef type, string expression) =>
        NeedsHelper(type)
            ? $"ScriptApiValues.Write{Require(type)}({expression}, references)"
            : $"JsonValue.Create({expression})";

    private string ReadValue(ApiTypeRef type, string element, string parameter, string member) =>
        NeedsHelper(type)
            ? $"ScriptApiValues.Read{Require(type)}({element}, {parameter}, {member}, references)"
            : $"ScriptApiJson.{ScalarReader(type.Kind)}({element}, {parameter}, {member})";

    /// <summary>Registers the helper pair for a type, and returns the name they share.</summary>
    private string Require(ApiTypeRef type)
    {
        var name = Mangle(type);

        // Added before the body is built so that a DTO containing itself terminates.
        if (_started.Add(name))
            _helpers[name] = BuildHelpers(type, name);

        return name;
    }

    private static string Mangle(ApiTypeRef type)
    {
        var core = type.Kind switch
        {
            ApiTypeKinds.Array => Mangle(type.Items) + "Array",
            ApiTypeKinds.List => Mangle(type.Items) + "List",
            ApiTypeKinds.Reference => Identifier(type.Name) + "Ref",
            ApiTypeKinds.Dto or ApiTypeKinds.Enum => Identifier(type.Name),
            _ => Identifier(type.Kind),
        };

        return type.Nullable ? core + "OrNull" : core;
    }

    /// <summary>The type a reader returns. Lists are read as arrays, which satisfy IReadOnlyList.</summary>
    private static string ReadType(ApiTypeRef type) =>
        type.Kind == ApiTypeKinds.List ? type.Items.Clr + "[]" : type.Clr;

    private static string ScalarReader(string kind) => kind switch
    {
        ApiTypeKinds.Boolean => "ToBoolean",
        ApiTypeKinds.SByte => "ToSByte",
        ApiTypeKinds.Byte => "ToByte",
        ApiTypeKinds.Int16 => "ToInt16",
        ApiTypeKinds.UInt16 => "ToUInt16",
        ApiTypeKinds.Int32 => "ToInt32",
        ApiTypeKinds.UInt32 => "ToUInt32",
        ApiTypeKinds.Int64 => "ToInt64",
        ApiTypeKinds.UInt64 => "ToUInt64",
        ApiTypeKinds.Single => "ToSingle",
        ApiTypeKinds.Double => "ToDouble",
        ApiTypeKinds.Decimal => "ToDecimal",
        ApiTypeKinds.Char => "ToChar",
        ApiTypeKinds.String => "ToStringValue",
        ApiTypeKinds.Guid => "ToGuid",
        ApiTypeKinds.DateTimeOffset => "ToDateTimeOffset",
        _ => throw new InvalidOperationException($"'{kind}' has no inline reader."),
    };

    private string BuildHelpers(ApiTypeRef type, string name)
    {
        if (type.Nullable)
            return BuildNullableHelpers(type, name);

        return type.Kind switch
        {
            ApiTypeKinds.Array or ApiTypeKinds.List => BuildSequenceHelpers(type, name),
            ApiTypeKinds.Dto => BuildDtoHelpers(type, name),
            ApiTypeKinds.Enum => BuildEnumHelpers(type, name),
            ApiTypeKinds.Reference => BuildReferenceHelpers(type, name),
            ApiTypeKinds.TimeSpan => BuildDurationHelpers(type, name),
            _ => throw new InvalidOperationException($"'{type.Kind}' does not need a helper."),
        };
    }

    private string BuildNullableHelpers(ApiTypeRef type, string name)
    {
        var inner = type.WithoutNull();
        var value = type.IsValueType ? "value.Value" : "value";
        var read = ReadType(type);

        var code = new CodeBuilder();
        code.Doc($"Writes {type.Clr}, which may be null.");
        code.Line($"internal static JsonNode Write{name}({type.Clr} value, IScriptApiReferences references) =>");
        code.Indent().Line($"value == null ? null : {WriteValue(inner, value)};").Outdent();
        code.Line();
        code.Doc($"Reads {type.Clr}, which may be null.");
        code.Line($"internal static {read} Read{name}(JsonElement element, string parameter, string member, IScriptApiReferences references) =>");
        code.Indent().Line($"ScriptApiJson.IsNull(element) ? ({read})null : {ReadValue(inner, "element", "parameter", "member")};").Outdent();

        return code.ToString();
    }

    private string BuildSequenceHelpers(ApiTypeRef type, string name)
    {
        var items = type.Items;
        var code = new CodeBuilder();

        code.Doc($"Writes a sequence of {items.Clr}.");
        using (code.Block($"internal static JsonNode Write{name}({type.Clr} value, IScriptApiReferences references)"))
        {
            using (code.Block("if (value == null)"))
                code.Line("return null;");

            code.Line();
            code.Line("var result = new JsonArray();");
            using (code.Block("foreach (var item in value)"))
                code.Line($"result.Add({WriteValue(items, "item")});");

            code.Line();
            code.Line("return result;");
        }

        code.Line();
        code.Doc($"Reads a sequence of {items.Clr}.");
        using (code.Block($"internal static {items.Clr}[] Read{name}(JsonElement element, string parameter, string member, IScriptApiReferences references)"))
        {
            code.Line("var source = ScriptApiJson.RequireArray(element, parameter, member);");
            code.Line($"var result = new {items.Clr}[source.GetArrayLength()];");
            code.Line("var index = 0;");
            code.Line();
            using (code.Block("foreach (var item in source.EnumerateArray())"))
                code.Line($"result[index++] = {ReadValue(items, "item", "parameter + \"[]\"", "member")};");

            code.Line();
            code.Line("return result;");
        }

        return code.ToString();
    }

    private string BuildDtoHelpers(ApiTypeRef type, string name)
    {
        var dto = _dtos[type.Name];
        var code = new CodeBuilder();

        code.Doc($"Writes {dto.Clr}.");
        using (code.Block($"internal static JsonNode Write{name}({type.Clr} value, IScriptApiReferences references)"))
        {
            if (!type.IsValueType)
            {
                using (code.Block("if (value == null)"))
                    code.Line("return null;");

                code.Line();
            }

            code.Line("var result = new JsonObject();");
            foreach (var property in dto.Properties)
            {
                code.Line($"result[{JsonText.CSharpLiteral(property.Name)}] = " +
                          $"{WriteValue(property.Type, "value." + property.ClrName)};");
            }

            code.Line();
            code.Line("return result;");
        }

        code.Line();
        code.Doc($"Reads {dto.Clr}.");
        using (code.Block($"internal static {type.Clr} Read{name}(JsonElement element, string parameter, string member, IScriptApiReferences references)"))
        {
            if (!dto.IsReadable)
            {
                // The generator reports KJXSG002 for this; the body keeps the file compiling.
                code.Line($"throw ScriptApiException.WrongArgumentType(member, parameter, " +
                          $"{JsonText.CSharpLiteral($"a {dto.Name}")}, \"a value that cannot be constructed\");");
                return code.ToString();
            }

            code.Line("var source = ScriptApiJson.RequireObject(element, parameter, member);");

            var locals = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < dto.Properties.Count; i++)
            {
                var property = dto.Properties[i];
                var local = $"property{i}";
                var label = $"parameter + {JsonText.CSharpLiteral("." + property.Name)}";
                locals[property.ClrName] = local;

                if (property.Type.Nullable)
                {
                    code.Line($"var {local} = ScriptApiJson.TryGet(source, {JsonText.CSharpLiteral(property.Name)}, member, out var element{i})");
                    code.Indent();
                    code.Line($"? {ReadValue(property.Type, $"element{i}", label, "member")}");
                    code.Line($": ({ReadType(property.Type)})null;");
                    code.Outdent();
                }
                else
                {
                    code.Line($"var element{i} = ScriptApiJson.Required(source, {JsonText.CSharpLiteral(property.Name)}, member);");
                    code.Line($"var {local} = {ReadValue(property.Type, $"element{i}", label, "member")};");
                }
            }

            var constructorArguments = string.Join(", ",
                dto.ConstructorProperties.Select(property => locals[property.ClrName]));

            var assigned = new HashSet<string>(
                dto.ConstructorProperties.Select(property => property.ClrName),
                StringComparer.Ordinal);

            var initialized = dto.Properties
                .Where(property => !assigned.Contains(property.ClrName))
                .Select(property => $"{property.ClrName} = {locals[property.ClrName]}")
                .ToList();

            var initializer = initialized.Count == 0
                ? string.Empty
                : " { " + string.Join(", ", initialized) + " }";

            code.Line();
            code.Line($"return new {type.Clr}({constructorArguments}){initializer};");
        }

        return code.ToString();
    }

    private string BuildEnumHelpers(ApiTypeRef type, string name)
    {
        var declared = _enums[type.Name];
        var code = new CodeBuilder();

        code.Doc($"Writes {declared.Clr} as the name of its value.");
        using (code.Block($"internal static JsonNode Write{name}({type.Clr} value, IScriptApiReferences references)"))
        using (code.Block("switch (value)"))
        {
            foreach (var value in declared.Values)
            {
                code.Line($"case {type.Clr}.{value.ClrName}:");
                code.Indent().Line($"return JsonValue.Create({JsonText.CSharpLiteral(value.Name)});").Outdent();
            }

            code.Line("default:");
            code.Indent().Line("return JsonValue.Create(value.ToString());").Outdent();
        }

        code.Line();
        code.Doc($"Reads {declared.Clr} from the name of its value.");
        using (code.Block($"internal static {type.Clr} Read{name}(JsonElement element, string parameter, string member, IScriptApiReferences references)"))
        {
            code.Line("var name = ScriptApiJson.ToEnumName(element, parameter, member);");
            code.Line();
            using (code.Block("switch (name)"))
            {
                foreach (var value in declared.Values)
                {
                    code.Line($"case {JsonText.CSharpLiteral(value.Name)}:");
                    code.Indent().Line($"return {type.Clr}.{value.ClrName};").Outdent();
                }
            }

            code.Line();
            var permitted = string.Join(", ", declared.Values.Select(value => "'" + value.Name + "'"));
            code.Line($"throw ScriptApiJson.UnknownEnumValue(parameter, member, name, {JsonText.CSharpLiteral(permitted)});");
        }

        return code.ToString();
    }

    private string BuildReferenceHelpers(ApiTypeRef type, string name)
    {
        var wire = JsonText.CSharpLiteral(type.Name);
        var code = new CodeBuilder();

        code.Doc($"Describes {type.Clr} as an object reference.");
        code.Line($"internal static JsonNode Write{name}({type.Clr} value, IScriptApiReferences references) =>");
        code.Indent().Line($"references.Describe(value, {wire});").Outdent();
        code.Line();
        code.Doc($"Resolves an object reference to {type.Clr}.");
        code.Line($"internal static {type.Clr} Read{name}(JsonElement element, string parameter, string member, IScriptApiReferences references) =>");
        code.Indent().Line($"({type.Clr})references.Resolve(element, {wire}, parameter);").Outdent();

        return code.ToString();
    }

    private static string BuildDurationHelpers(ApiTypeRef type, string name)
    {
        var code = new CodeBuilder();

        code.Doc("Writes a duration as an ISO 8601 style string.");
        code.Line($"internal static JsonNode Write{name}({type.Clr} value, IScriptApiReferences references) =>");
        code.Indent().Line("JsonValue.Create(ScriptApiJson.FromTimeSpan(value));").Outdent();
        code.Line();
        code.Doc("Reads a duration.");
        code.Line($"internal static {type.Clr} Read{name}(JsonElement element, string parameter, string member, IScriptApiReferences references) =>");
        code.Indent().Line("ScriptApiJson.ToTimeSpan(element, parameter, member);").Outdent();

        return code.ToString();
    }
}
