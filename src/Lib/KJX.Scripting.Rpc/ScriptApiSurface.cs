using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KJX.Scripting.Runtime;

namespace KJX.Scripting.Rpc;

/// <summary>A member, and the dispatch table that owns it.</summary>
public readonly struct ResolvedMember
{
    internal ResolvedMember(IScriptApiDispatcher dispatcher, ScriptApiMemberKind kind, ScriptApiMemberAccess access)
    {
        Dispatcher = dispatcher;
        Kind = kind;
        Access = access;
    }

    /// <summary>The dispatch table to invoke.</summary>
    public IScriptApiDispatcher Dispatcher { get; }

    /// <summary>Whether the member is a call or a subscription.</summary>
    public ScriptApiMemberKind Kind { get; }

    /// <summary>What the member was written as.</summary>
    public ScriptApiMemberAccess Access { get; }

    /// <summary>True when calling this member can change the state of the instrument.</summary>
    public bool ChangesState => Access is ScriptApiMemberAccess.Set or ScriptApiMemberAccess.Invoke;
}

/// <summary>
/// Everything the host knows how to dispatch, gathered from the generated catalogs. Resolution
/// works against the set of wire types a target carries, so a motor that also implements
/// ISupportsHoming answers <c>home</c> without any interface having to inherit the other.
/// </summary>
public sealed class ScriptApiSurface
{
    private readonly Dictionary<string, IScriptApiDispatcher> _byWireName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, IScriptApiDispatcher> _byClrType = new();
    private readonly JsonObject _api;

    /// <summary>Gathers the catalogs and merges their descriptors.</summary>
    public ScriptApiSurface(IEnumerable<IScriptApiCatalog> catalogs)
    {
        ArgumentNullException.ThrowIfNull(catalogs);

        var types = new JsonArray();
        var dtos = new JsonArray();
        var enums = new JsonArray();

        foreach (var catalog in catalogs)
        {
            foreach (var dispatcher in catalog.Dispatchers)
            {
                if (_byWireName.TryGetValue(dispatcher.WireTypeName, out var existing))
                {
                    throw new InvalidOperationException(
                        $"Two script API types share the wire name '{dispatcher.WireTypeName}': " +
                        $"'{existing.TargetType}' and '{dispatcher.TargetType}'. " +
                        "Pass an explicit name to [ScriptApi] on one of them.");
                }

                _byWireName.Add(dispatcher.WireTypeName, dispatcher);
                _byClrType[dispatcher.TargetType] = dispatcher;
            }

            var api = JsonNode.Parse(catalog.DescriptorJson)?["api"];
            Append(types, api?["types"]);
            Append(dtos, api?["dtos"]);
            Append(enums, api?["enums"]);
        }

        Sort(types);
        Sort(dtos);
        Sort(enums);

        _api = new JsonObject
        {
            ["version"] = 1,
            ["types"] = types,
            ["dtos"] = dtos,
            ["enums"] = enums,
        };
    }

    /// <summary>Every wire type name the host can dispatch.</summary>
    public IEnumerable<string> WireTypeNames => _byWireName.Keys;

    /// <summary>Finds a dispatch table by wire type name.</summary>
    public bool TryGetDispatcher(string wireTypeName, out IScriptApiDispatcher dispatcher) =>
        _byWireName.TryGetValue(wireTypeName ?? string.Empty, out dispatcher);

    /// <summary>Finds the dispatch table for a CLR interface, which is how devices are matched.</summary>
    public bool TryGetDispatcherForClrType(Type type, out IScriptApiDispatcher dispatcher) =>
        _byClrType.TryGetValue(type, out dispatcher);

    /// <summary>
    /// Finds the member across every type the target carries, following inherited script API
    /// types. Types are searched in sorted order so that resolution never depends on
    /// registration order.
    /// </summary>
    public bool TryResolveMember(IReadOnlyList<string> wireTypes, string member, out ResolvedMember resolved)
    {
        foreach (var wireType in wireTypes)
        {
            if (TryResolveInType(wireType, member, out resolved))
                return true;
        }

        resolved = default;
        return false;
    }

    /// <summary>
    /// Refuses a target whose capabilities disagree: if two of its interfaces declare the same
    /// member, dispatching by name alone would silently pick one. That is a configuration
    /// mistake, and it is caught at startup rather than on the first call.
    /// </summary>
    public void RequireUnambiguous(ScriptTarget target)
    {
        var owners = new Dictionary<string, IScriptApiDispatcher>(StringComparer.Ordinal);

        foreach (var wireType in target.WireTypes)
        {
            foreach (var member in MembersOf(wireType))
            {
                if (!TryResolveInType(wireType, member, out var resolved))
                    continue;

                if (owners.TryGetValue(member, out var existing) && !ReferenceEquals(existing, resolved.Dispatcher))
                {
                    throw new InvalidOperationException(
                        $"'{target.Id}' is registered as both '{existing.WireTypeName}' and " +
                        $"'{resolved.Dispatcher.WireTypeName}', which both declare '{member}'. " +
                        "A script addresses members by name, so one of the interfaces has to go.");
                }

                owners[member] = resolved.Dispatcher;
            }
        }
    }

    /// <summary>Every member a target answers to, across all of its capabilities.</summary>
    public IEnumerable<string> MembersOf(ScriptTarget target) =>
        target.WireTypes.SelectMany(MembersOf).Distinct(StringComparer.Ordinal).OrderBy(m => m, StringComparer.Ordinal);

    /// <summary>
    /// The full descriptor served to clients: the merged API plus the devices this instrument
    /// actually has, and a hash over both. The device list is part of the hash because the Python
    /// stubs are generated from it.
    /// </summary>
    public string Describe(IEnumerable<ScriptTarget> devices)
    {
        var deviceArray = new JsonArray();

        foreach (var device in devices.OrderBy(device => device.Id, StringComparer.Ordinal))
        {
            var types = new JsonArray();
            foreach (var wireType in device.WireTypes)
                types.Add(wireType);

            deviceArray.Add(new JsonObject { ["id"] = device.Id, ["types"] = types });
        }

        var body = new JsonObject
        {
            ["api"] = _api.DeepClone(),
            ["devices"] = deviceArray,
        };

        var text = body.ToJsonString();
        var document = new JsonObject
        {
            ["hash"] = Hash(text),
            ["api"] = body["api"]!.DeepClone(),
            ["devices"] = body["devices"]!.DeepClone(),
        };

        return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private bool TryResolveInType(string wireType, string member, out ResolvedMember resolved)
    {
        resolved = default;

        if (!_byWireName.TryGetValue(wireType, out var dispatcher))
            return false;

        if (dispatcher.TryGetMemberKind(member, out var kind))
        {
            dispatcher.TryGetMemberAccess(member, out var access);
            resolved = new ResolvedMember(dispatcher, kind, access);
            return true;
        }

        foreach (var inherited in dispatcher.Extends)
        {
            if (TryResolveInType(inherited, member, out resolved))
                return true;
        }

        return false;
    }

    private IEnumerable<string> MembersOf(string wireType)
    {
        if (!_byWireName.TryGetValue(wireType, out var dispatcher))
            yield break;

        foreach (var member in dispatcher.MemberNames)
            yield return member;

        foreach (var inherited in dispatcher.Extends)
        {
            foreach (var member in MembersOf(inherited))
                yield return member;
        }
    }

    private static void Append(JsonArray target, JsonNode source)
    {
        if (source is not JsonArray array)
            return;

        foreach (var item in array)
            target.Add(item?.DeepClone());
    }

    private static void Sort(JsonArray array)
    {
        var sorted = array
            .Select(item => item?.DeepClone())
            .OrderBy(item => item?["name"]?.GetValue<string>() ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        array.Clear();
        foreach (var item in sorted)
            array.Add(item);
    }

    private static string Hash(string text)
    {
        var bytes = SHA256.HashData(new UTF8Encoding(false).GetBytes(text));

        var result = new StringBuilder("sha256:", 7 + bytes.Length * 2);
        foreach (var value in bytes)
            result.Append(value.ToString("x2"));

        return result.ToString();
    }
}
