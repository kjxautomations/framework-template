using System;
using System.Collections.Generic;

namespace KJX.Scripting.Codegen;

/// <summary>What a member looks like on the wire.</summary>
internal enum ApiMemberKind
{
    /// <summary>A method: request in, response out.</summary>
    Call,

    /// <summary>A property getter.</summary>
    Get,

    /// <summary>A property setter.</summary>
    Set,

    /// <summary>An IAsyncEnumerable member: a subscription.</summary>
    Stream,
}

/// <summary>The classification of a type on the wire. Also the "kind" field in the descriptor.</summary>
internal static class ApiTypeKinds
{
    public const string Void = "void";
    public const string Boolean = "bool";
    public const string SByte = "int8";
    public const string Byte = "uint8";
    public const string Int16 = "int16";
    public const string UInt16 = "uint16";
    public const string Int32 = "int32";
    public const string UInt32 = "uint32";
    public const string Int64 = "int64";
    public const string UInt64 = "uint64";
    public const string Single = "float32";
    public const string Double = "float64";
    public const string Decimal = "decimal";
    public const string Char = "char";
    public const string String = "string";
    public const string Guid = "guid";
    public const string DateTimeOffset = "timestamp";
    public const string TimeSpan = "duration";
    public const string Enum = "enum";
    public const string Dto = "dto";
    public const string Reference = "ref";
    public const string Array = "array";
    public const string List = "list";
}

/// <summary>One type as it appears in a signature.</summary>
internal sealed class ApiTypeRef
{
    /// <summary>One of <see cref="ApiTypeKinds"/>.</summary>
    public string Kind;

    /// <summary>The wire name of the enum, DTO or referenced interface, when the kind names one.</summary>
    public string Name;

    /// <summary>The element type, for arrays and lists.</summary>
    public ApiTypeRef Items;

    /// <summary>True when the value may be null.</summary>
    public bool Nullable;

    /// <summary>True when the underlying CLR type is a value type, which decides how null is handled.</summary>
    public bool IsValueType;

    /// <summary>The fully qualified CLR type, for generated code.</summary>
    public string Clr;

    /// <summary>A copy of this reference with the nullability removed.</summary>
    public ApiTypeRef WithoutNull() => new()
    {
        Kind = Kind,
        Name = Name,
        Items = Items,
        Nullable = false,
        IsValueType = IsValueType,
        Clr = Clr != null && Clr.EndsWith("?", StringComparison.Ordinal)
            ? Clr.Substring(0, Clr.Length - 1)
            : Clr,
    };
}

/// <summary>One parameter of a call.</summary>
internal sealed class ApiParameter
{
    public string Name;
    public string ClrName;
    public ApiTypeRef Type;
    public bool HasDefault;

    /// <summary>The default rendered as JSON, when the parameter has one.</summary>
    public string DefaultJson;

    /// <summary>The C# expression for the default, for generated code.</summary>
    public string DefaultExpression;

    public string Doc;
}

/// <summary>One member of the scriptable surface.</summary>
internal sealed class ApiMember
{
    public string Name;
    public string ClrName;
    public ApiMemberKind Kind;
    public ApiTypeRef Returns;
    public List<ApiParameter> Parameters = new();
    public string Doc;

    /// <summary>True when the CLR member returns Task or ValueTask and must be awaited.</summary>
    public bool IsAwaitable;

    /// <summary>True when the CLR method takes the host's CancellationToken as its last argument.</summary>
    public bool PassesCancellationToken;

    /// <summary>True when the member was written as a property rather than a method.</summary>
    public bool FromProperty;
}

/// <summary>One <c>[ScriptApi]</c> interface.</summary>
internal sealed class ApiType
{
    public string Name;
    public string Clr;
    public string Doc;
    public List<string> Extends = new();
    public List<ApiMember> Members = new();

    /// <summary>The identifier used for the generated dispatcher class.</summary>
    public string DispatcherName;
}

/// <summary>One property of a DTO.</summary>
internal sealed class ApiDtoProperty
{
    public string Name;
    public string ClrName;
    public ApiTypeRef Type;
    public string Doc;

    /// <summary>True when the property can be assigned in an object initializer.</summary>
    public bool IsSettable;
}

/// <summary>A record or record struct that crosses the boundary by value.</summary>
internal sealed class ApiDto
{
    public string Name;
    public string Clr;
    public string Doc;
    public List<ApiDtoProperty> Properties = new();

    /// <summary>
    /// The properties to pass to the constructor, in order. Empty means the DTO is built with an
    /// object initializer instead.
    /// </summary>
    public List<ApiDtoProperty> ConstructorProperties = new();

    /// <summary>True when the DTO can be reconstructed from its JSON form.</summary>
    public bool IsReadable;
}

/// <summary>One enum value.</summary>
internal sealed class ApiEnumValue
{
    public string Name;
    public string ClrName;
    public string Value;
}

/// <summary>An enum, which travels as the name of its value.</summary>
internal sealed class ApiEnum
{
    public string Name;
    public string Clr;
    public string Doc;
    public List<ApiEnumValue> Values = new();
}

/// <summary>Everything the generator learned about one assembly's scripting surface.</summary>
internal sealed class ApiModel
{
    public List<ApiType> Types = new();
    public List<ApiDto> Dtos = new();
    public List<ApiEnum> Enums = new();

    /// <summary>The namespace the generated code is placed in.</summary>
    public string GeneratedNamespace;
}
