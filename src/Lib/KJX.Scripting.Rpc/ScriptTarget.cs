using Autofac;
using Autofac.Core;
using Microsoft.Extensions.Logging;

namespace KJX.Scripting.Rpc;

/// <summary>
/// Something a script can address. A target carries the set of script API interfaces it was
/// registered under, not one type: homing is a capability a motor may or may not have, and the
/// configuration is what decides.
/// </summary>
public sealed class ScriptTarget
{
    /// <summary>Creates a target.</summary>
    public ScriptTarget(string id, object instance, IReadOnlyList<string> wireTypes)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        WireTypes = wireTypes ?? throw new ArgumentNullException(nameof(wireTypes));
    }

    /// <summary>The wire id, such as <c>dev/XMotor</c> or <c>h/3</c>.</summary>
    public string Id { get; }

    /// <summary>The object the dispatch tables are invoked against.</summary>
    public object Instance { get; }

    /// <summary>The wire type names this target is addressable as, sorted.</summary>
    public IReadOnlyList<string> WireTypes { get; }
}

/// <summary>
/// Where the permanently addressable objects come from. These are the <c>dev/&lt;id&gt;</c>
/// references: stable for the life of the process, never released, no lifetime management.
/// </summary>
public interface IScriptDeviceSource
{
    /// <summary>Every configured device, keyed by wire id.</summary>
    IReadOnlyDictionary<string, ScriptTarget> Devices { get; }
}

/// <summary>
/// Builds the device namespace by asking the container what was registered. Adding a device to
/// the configuration exposes it to scripts with no code change, which is the whole point of
/// reading it from the registry rather than from a list somewhere.
/// </summary>
public sealed class AutofacScriptDeviceSource : IScriptDeviceSource
{
    /// <summary>The metadata key ConfigurationHandler stores the configured name under.</summary>
    public const string NameMetadataKey = "Name";

    private readonly Dictionary<string, ScriptTarget> _devices = new(StringComparer.Ordinal);

    /// <summary>Enumerates the container and keeps whatever the scripting surface knows about.</summary>
    public AutofacScriptDeviceSource(ILifetimeScope scope, ScriptApiSurface surface, ILogger<AutofacScriptDeviceSource> logger = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(surface);

        foreach (var registration in scope.ComponentRegistry.Registrations)
        {
            if (!registration.Metadata.TryGetValue(NameMetadataKey, out var value) || value is not string name ||
                string.IsNullOrWhiteSpace(name))
            {
                // Without a configured name there is nothing to address the device by.
                continue;
            }

            var scriptable = ServiceTypes(registration)
                .Where(type => surface.TryGetDispatcherForClrType(type, out _))
                .Distinct()
                .ToList();

            if (scriptable.Count == 0)
                continue;

            var instance = Resolve(scope, name, scriptable);
            if (instance == null)
            {
                logger?.LogWarning("Device '{Device}' is registered for scripting but could not be resolved.", name);
                continue;
            }

            var wireTypes = scriptable
                .Select(type =>
                {
                    surface.TryGetDispatcherForClrType(type, out var dispatcher);
                    return dispatcher.WireTypeName;
                })
                .Distinct(StringComparer.Ordinal)
                .OrderBy(wireType => wireType, StringComparer.Ordinal)
                .ToList();

            var id = "dev/" + name;
            var target = new ScriptTarget(id, instance, wireTypes);

            surface.RequireUnambiguous(target);
            _devices[id] = target;

            logger?.LogInformation("Device '{Device}' is scriptable as {Types}.", id, string.Join(", ", wireTypes));
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ScriptTarget> Devices => _devices;

    private static IEnumerable<Type> ServiceTypes(IComponentRegistration registration)
    {
        foreach (var service in registration.Services)
        {
            switch (service)
            {
                case IServiceWithType typed:
                    yield return typed.ServiceType;
                    break;
            }
        }
    }

    /// <summary>
    /// Resolves the one instance behind the registration. Devices are single instance, so any of
    /// their services returns the same object; the keyed registration is tried first because that
    /// is the shape ConfigurationHandler produces.
    /// </summary>
    private static object Resolve(ILifetimeScope scope, string name, IEnumerable<Type> serviceTypes)
    {
        foreach (var type in serviceTypes)
        {
            if (scope.TryResolveKeyed(name, type, out var keyed))
                return keyed;

            if (scope.TryResolveService(new TypedService(type), out var resolved))
                return resolved;
        }

        return null;
    }
}
