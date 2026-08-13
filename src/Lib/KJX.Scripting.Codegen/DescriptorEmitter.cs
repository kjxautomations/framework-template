using System;
using System.Security.Cryptography;
using System.Text;

namespace KJX.Scripting.Codegen;

/// <summary>
/// Writes the API descriptor: the whole scripting surface as JSON, with a content hash. Clients
/// fetch this on connect, and the Python side caches generated stubs under the hash, so the hash
/// has to change exactly when the surface does and not otherwise.
/// </summary>
internal static class DescriptorEmitter
{
    /// <summary>The descriptor format version, bumped when the shape of this document changes.</summary>
    public const int FormatVersion = 1;

    /// <summary>The descriptor JSON and the hash of its body.</summary>
    public static (string Json, string Hash) Emit(ApiModel model)
    {
        var api = WriteApi(model);
        var hash = Hash(api);

        var document = new StringBuilder();
        document.Append("{\n");
        document.Append("  \"hash\": ").Append(JsonText.Quote(hash)).Append(",\n");
        document.Append("  \"api\": ").Append(api).Append('\n');
        document.Append("}\n");

        return (document.ToString(), hash);
    }

    /// <summary>
    /// The hash covers the api body exactly as it is written, so two builds of the same surface
    /// produce the same hash on any platform.
    /// </summary>
    private static string Hash(string api)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(new UTF8Encoding(false).GetBytes(api));

        var text = new StringBuilder("sha256:", 7 + bytes.Length * 2);
        foreach (var value in bytes)
            text.Append(value.ToString("x2"));

        return text.ToString();
    }

    private static string WriteApi(ApiModel model)
    {
        // Indented one level in, because it is nested under "api" in the document.
        var json = new JsonText(initialIndent: 1);

        json.BeginObject();
        json.Key("version").Raw(FormatVersion.ToString());

        json.Key("types").BeginArray();
        foreach (var type in model.Types)
            WriteType(json, type);
        json.EndArray();

        json.Key("dtos").BeginArray();
        foreach (var dto in model.Dtos)
            WriteDto(json, dto);
        json.EndArray();

        json.Key("enums").BeginArray();
        foreach (var item in model.Enums)
            WriteEnum(json, item);
        json.EndArray();

        json.EndObject();

        return json.ToString();
    }

    private static void WriteType(JsonText json, ApiType type)
    {
        json.BeginObject();
        json.Key("name").String(type.Name);
        json.Key("clr").String(Clr(type.Clr));
        json.OptionalString("doc", type.Doc);

        if (type.Extends.Count > 0)
        {
            json.Key("extends").BeginArray();
            foreach (var name in type.Extends)
                json.String(name);
            json.EndArray();
        }

        json.Key("members").BeginArray();
        foreach (var member in type.Members)
            WriteMember(json, member);
        json.EndArray();

        json.EndObject();
    }

    private static void WriteMember(JsonText json, ApiMember member)
    {
        json.BeginObject();
        json.Key("name").String(member.Name);

        // "kind" is how the member is invoked on the wire; "access" is what it was written as,
        // which is what the Python stub emitter needs to rebuild properties.
        json.Key("kind").String(member.Kind == ApiMemberKind.Stream ? "stream" : "call");
        json.Key("access").String(member.Kind switch
        {
            ApiMemberKind.Get => "get",
            ApiMemberKind.Set => "set",
            ApiMemberKind.Stream => "stream",
            _ => "invoke",
        });

        json.Key("clr").String(member.ClrName);
        json.OptionalString("doc", member.Doc);

        json.Key("params").BeginArray();
        foreach (var parameter in member.Parameters)
        {
            json.BeginObject();
            json.Key("name").String(parameter.Name);
            json.Key("type");
            WriteTypeRef(json, parameter.Type);
            json.Key("required").Bool(!parameter.HasDefault);

            if (parameter.HasDefault)
                json.Key("default").Raw(parameter.DefaultJson);

            json.OptionalString("doc", parameter.Doc);
            json.EndObject();
        }

        json.EndArray();

        if (member.Returns != null)
        {
            json.Key(member.Kind == ApiMemberKind.Stream ? "yields" : "returns");
            WriteTypeRef(json, member.Returns);
        }

        json.EndObject();
    }

    private static void WriteTypeRef(JsonText json, ApiTypeRef type)
    {
        json.BeginObject();
        json.Key("kind").String(type.Kind);

        if (!string.IsNullOrEmpty(type.Name))
            json.Key("name").String(type.Name);

        if (type.Items != null)
        {
            json.Key("items");
            WriteTypeRef(json, type.Items);
        }

        if (type.Nullable)
            json.Key("nullable").Bool(true);

        json.EndObject();
    }

    private static void WriteDto(JsonText json, ApiDto dto)
    {
        json.BeginObject();
        json.Key("name").String(dto.Name);
        json.Key("clr").String(Clr(dto.Clr));
        json.OptionalString("doc", dto.Doc);

        json.Key("properties").BeginArray();
        foreach (var property in dto.Properties)
        {
            json.BeginObject();
            json.Key("name").String(property.Name);
            json.Key("type");
            WriteTypeRef(json, property.Type);
            json.OptionalString("doc", property.Doc);
            json.EndObject();
        }

        json.EndArray();
        json.EndObject();
    }

    private static void WriteEnum(JsonText json, ApiEnum item)
    {
        json.BeginObject();
        json.Key("name").String(item.Name);
        json.Key("clr").String(Clr(item.Clr));
        json.OptionalString("doc", item.Doc);

        json.Key("values").BeginArray();
        foreach (var value in item.Values)
        {
            json.BeginObject();
            json.Key("name").String(value.Name);
            json.Key("value").Raw(value.Value);
            json.EndObject();
        }

        json.EndArray();
        json.EndObject();
    }

    private static string Clr(string name) =>
        name?.Replace("global::", string.Empty);
}
