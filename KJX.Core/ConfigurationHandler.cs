using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Autofac;
using Autofac.Core;
using Autofac.Core.Resolving.Pipeline;
using Autofac.Features.AttributeFilters;
using Avalonia.Input;
using KJX.Config;

namespace KJX.Core;

/// <summary>
/// Registers types based on the configuration system. Optionally, tracks objects as they are created
/// and captures their default values. This is useful for scenarios edits are made to the objects and they
/// are saved back to the configuration system.
/// </summary>
public class ConfigurationHandler : INotifyPropertyChanged
{
    public void PopulateContainerBuilder(ContainerBuilder builder, HashSet<ConfigSection> configItems, bool saveDefaults = false)
    {
        foreach (var item in configItems)
        {
            var section = item;
            var reg = builder.RegisterType(item.Type)
                .As(item.Type).WithParameter("name", item.Name);
            if (saveDefaults)
            {
                // if we're saving the default values for each object created, add a middleware to the pipeline
                // that will capture the default values of the object's properties. This is done by stripping out any 
                // parameters that are not setting required properties or constructor parameters, creating the object,
                // saving the values of the properties that were set, and then setting the remaining properties.
                reg = reg.ConfigurePipeline(p =>
                {
                    // Add middleware at the start of the registration pipeline.
                    p.Use(PipelinePhase.Activation, (context, next) =>
                    {
                        if (context.Instance != null)
                        {
                            // object already created, just call the next middleware
                            next(context);
                            return;
                        }
                        // Call the next middleware in the pipeline to create the object with all constructor arguments AND
                        // all properties that are "required" set.
                        next(context);
                        SaveInitialValues(section, context.Instance);
                    });
                });
            }
                    
                
            foreach (var itf in item.Interfaces)
            {
                // register the service both raw and with a key matching the config section name
                reg = reg.As(itf).Named(item.Name, itf).Keyed(item.Name, itf);
            }

            reg = reg.WithAttributeFiltering().WithMetadata("Name", item.Name);
            reg.OnActivated((obj) => 
            {
                // flag that there are dirty properties if any property changes
                if (obj.Instance is INotifyPropertyChanged npc)
                {
                    npc.PropertyChanged += (sender, args) =>
                    {
                        lock (_initialValues)
                        {
                            // see if the value is different from the initial value, and update _changedValues accordingly
                            if (_initialValues[section].TryGetValue(args.PropertyName, out var initialValue))
                            {
                                var currentValue = obj.Instance.GetType().GetProperty(args.PropertyName)?.GetValue(obj.Instance, null);
                                var key = new Tuple<ConfigSection, string>( section, args.PropertyName );
                                if (!Equals(_initialValues[section][args.PropertyName], currentValue))
                                {
                                    _changedValues.Add(key);
                                }
                                else 
                                {
                                    _changedValues.Remove(key);
                                }
                            }
                            HasDirtyValues = _changedValues.Any();
                        }
                    };
                }
                else
                {
                    HasObjectsThatDoNotImplementINotifyPropertyChanged = true;
                }
            });
            
            // autowire the properties for injection.
            foreach (var prop in item.Properties)
            {
                reg = reg.WithProperty(prop.Key, prop.Value);
            }
            reg = reg.PropertiesAutowired();

            reg.SingleInstance();
        }
    }

    private Dictionary<ConfigSection, Dictionary<string, object?>> _initialValues = new();
    private HashSet<Tuple<ConfigSection, string>> _changedValues = new();
    private bool _hasDirtyValues;
    private bool _hasObjectsThatDoNotImplementINotifyPropertyChanged;

    private void SaveInitialValues(ConfigSection section, object obj)
    {
        // save the values of the publicly settable properties of the object. Only save types that are 
        // settable via config (i.e. simple types)
        var properties = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => ConfigLoader.IsSupportedType(p.PropertyType))
            .Where(p => p.CanWrite)
            .ToDictionary(p => p.Name, p => p.GetValue(obj, null));
        lock (_initialValues)
            _initialValues[section] = properties;
        
    }
    public Dictionary<ConfigSection, Dictionary<string, object?>> GetInitialValues()
    {
        lock (_initialValues)
        {
            return new(_initialValues);
        }
    }

    public bool HasDirtyValues
    {
        get => _hasDirtyValues;
        private set => SetField(ref _hasDirtyValues, value);
    }

    public bool HasObjectsThatDoNotImplementINotifyPropertyChanged
    {
        get => _hasObjectsThatDoNotImplementINotifyPropertyChanged;
        private set => SetField(ref _hasObjectsThatDoNotImplementINotifyPropertyChanged, value);
    }

    #region INotifyPropertyChanged implementation
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
    #endregion
}