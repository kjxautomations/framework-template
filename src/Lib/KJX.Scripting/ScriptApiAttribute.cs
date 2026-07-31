using System;

namespace KJX.Scripting;

/// <summary>
/// Marks an interface as part of the scripting API. This is the only annotation the scripting
/// system requires or permits: every public member of a marked interface is scriptable.
/// </summary>
/// <remarks>
/// <para>
/// To exclude a member, put it on a separate interface that is not marked with this attribute
/// and implement both from the same class. There is deliberately no per-member opt-out.
/// </para>
/// <para>
/// Members inherited from unmarked base interfaces are part of the surface. Members inherited
/// from another <see cref="ScriptApiAttribute"/> interface are not re-exported; the API
/// descriptor records the inheritance instead.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class ScriptApiAttribute : Attribute
{
    /// <summary>
    /// Marks the interface as scriptable, using the default wire type name: the interface name
    /// with a leading <c>I</c> removed, converted to snake_case. <c>ISyringePump</c> becomes
    /// <c>syringe_pump</c>.
    /// </summary>
    public ScriptApiAttribute()
    {
    }

    /// <summary>
    /// Marks the interface as scriptable with an explicit wire type name.
    /// </summary>
    /// <param name="wireTypeName">The name this interface is known by on the wire.</param>
    public ScriptApiAttribute(string wireTypeName)
    {
        WireTypeName = wireTypeName;
    }

    /// <summary>
    /// The explicit wire type name, or <c>null</c> when the name is derived from the interface name.
    /// </summary>
    public string WireTypeName { get; }
}
