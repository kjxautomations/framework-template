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
                    // for speed, cache the names of all the properties that are settable via config
                    // i.e. have the Group attribute that drives the dynamic UI
                    var properties = obj.Instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => ConfigLoader.IsSupportedType(p.PropertyType))
                        .Where(p => p.CanWrite)
                        .Where(p => p.GetCustomAttribute<GroupAttribute>() != null)
                        .Select(p => p.Name)
                        .ToList();
                    npc.PropertyChanged += (sender, args) =>
                    {
                        if (!properties.Contains(args.PropertyName))
                            return;
                        lock (_initialValues)
                        {
                            // see if the value is different from the initial value, and update _changedValues accordingly
                            if (_initialValues[section.Name].TryGetValue(args.PropertyName, out var initialValue))
                            {
                                var currentValue = obj.Instance.GetType().GetProperty(args.PropertyName)
                                    ?.GetValue(obj.Instance, null);
                                if (!Equals(initialValue, currentValue))
                                {
                                    if (!_changedValues.ContainsKey(section.Name))
                                        _changedValues[section.Name] = new();
                                    _changedValues[section.Name][args.PropertyName] = currentValue;
                                }
                                else
                                {
                                    if (_changedValues.TryGetValue(section.Name, out var sectionValues))
                                    {
                                        sectionValues.Remove(args.PropertyName);
                                        if (sectionValues.Count == 0)
                                        {
                                            _changedValues.Remove(section.Name);
                                        }
                                    }
                                }
                            }
                            HasDirtyValues = _changedValues.Any();
                        }
                    };
                }
                else
                {
                    HasObjectsThatDoNotImplementINotifyPropertyChanged = true;
                    lock (_objectsWithoutINotifyPropertyChanged)
                        _objectsWithoutINotifyPropertyChanged[section.Name] = obj.Instance;
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

    private Dictionary<string, Dictionary<string, object>> _initialValues = new();
    private Dictionary<string, Dictionary<string, object>> _changedValues = new();
    private Dictionary<string, object> _objectsWithoutINotifyPropertyChanged = new();
    private bool _hasDirtyValues;
    private bool _hasObjectsThatDoNotImplementINotifyPropertyChanged;

    private void SaveInitialValues(ConfigSection section, object obj)
    {
        // save the values of the publicly settable properties of the object. Only save types that are 
        // settable via config (i.e. simple types) and have the Group attribute
        var properties = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => ConfigLoader.IsSupportedType(p.PropertyType))
            .Where(p => p.CanWrite)
            .Where(p => p.GetCustomAttribute<GroupAttribute>() != null)
            .ToDictionary(p => p.Name, p => p.GetValue(obj, null));
        lock (_initialValues)
            _initialValues[section.Name] = properties;
        
    }
    public Dictionary<string, Dictionary<string, object>> GetInitialValues()
    {
        lock (_initialValues)
        {
            return new(_initialValues);
        }
    }

    public Dictionary<string, Dictionary<string, object>> GetChangedValues()
    {
        // get all the changed values from the objects that don't support INotifyPropertyChanged
        var changedValues = new Dictionary<string, Dictionary<string, object>>();
        foreach (var section in _objectsWithoutINotifyPropertyChanged)
        {
            var properties = section.Value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => ConfigLoader.IsSupportedType(p.PropertyType))
                .Where(p => p.CanWrite)
                .Where(p => p.GetCustomAttribute<GroupAttribute>() != null)
                .Where(p => !Equals(_initialValues[section.Key][p.Name], p.GetValue(section.Value, null)))
                .ToDictionary(p => p.Name, p => p.GetValue(section.Value, null));
            changedValues[section.Key] = properties;
        }
        // add the changed values from the objects that support INotifyPropertyChanged
        foreach (var section in _changedValues)
        {
            changedValues[section.Key] = new(section.Value);
        }
        return changedValues;
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
    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
    #endregion
}