using Autofac;
using Autofac.Features.AttributeFilters;
using KJX.Config;

namespace KJX.Core;

/// <summary>
/// Registers types based on the configuration system.
/// </summary>
public static class ConfigurationHandler
{
    public static void PopulateContainerBuilder(ContainerBuilder builder, HashSet<ConfigSection> configItems)
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
            foreach (var prop in item.Properties)
            {
                reg = reg.WithProperty(prop.Key, prop.Value);
            }

            // allow property injection too
            reg = reg.PropertiesAutowired();

            reg.SingleInstance();
        }
    }
    
}