using System.Reflection;
using Autofac;
using Autofac.Core;
using Autofac.Features.AttributeFilters;
using KJX.Config;

namespace KJX.Core;

/// <summary>
/// Registers types based on the configuration system. Optionally, tracks objects as they are created
/// and captures their default values. This is useful for scenarios edits are made to the objects and they
/// are saved back to the configuration system.
/// </summary>
public class ConfigurationHandler
{
    public void PopulateContainerBuilder(ContainerBuilder builder, HashSet<ConfigSection> configItems, bool saveDefaults = false)
    {
        foreach (var item in configItems)
        {
            var reg = builder.RegisterType(item.Type)
                .As(item.Type).WithParameter("name", item.Name);
            foreach (var itf in item.Interfaces)
            {
                // register the service both raw and with a key matching the config section name
                reg = reg.As(itf).Named(item.Name, itf).Keyed(item.Name, itf);
            }

            reg = reg.WithAttributeFiltering().WithMetadata("Name", item.Name);
            if (saveDefaults)
            {
                var section = item;
                reg.OnActivating((obj) =>
                {
                    SaveDefaultValues(section, obj.Instance);
                });
            }
            foreach (var prop in item.Properties)
            {
                reg = reg.WithProperty(prop.Key, prop.Value);
            }

            // allow property injection too
            reg = reg.PropertiesAutowired();

            reg.SingleInstance();
        }
    }

    private Dictionary<ConfigSection, Dictionary<string, object?>> _defaults = new();
    private void SaveDefaultValues(ConfigSection section, object obj)
    {
        // save the values of the publicly settable properties of the object. Only save types that are 
        // settable via config (i.e. simple types)
        var properties = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsPrimitive || p.PropertyType == typeof(string))
            .Where(p => p.CanWrite)
            .ToDictionary(p => p.Name, p => p.GetValue(obj, null));
        lock (_defaults)
            _defaults[section] = properties;
        
    }
    public Dictionary<ConfigSection, Dictionary<string, object?>> GetDefaultValues()
    {
        lock (_defaults)
        {
            return new(_defaults);
        }
    }
}