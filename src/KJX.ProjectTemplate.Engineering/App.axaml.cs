using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using KJX.Config;
using KJX.Core;
using KJX.Core.Interfaces;
using KJX.Core.ViewModels;
using KJX.ProjectTemplate.Engineering.ViewModels;
using KJX.ProjectTemplate.Engineering.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using ReactiveUI;
using Splat;
using Splat.Autofac;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using Path = System.IO.Path;

namespace KJX.ProjectTemplate.Engineering;

public partial class App : Application
{
    public IContainer Container { get; private set; }
    public ILogger Logger { get; private set; }
    
    public string ConfigPath { get; private set; }
    
    public ConfigurationHandler ConfigHandler { get; private set; }
    
    public override void Initialize()
    {
        InitAutoFac();
        AvaloniaXamlLoader.Load(this);
    }
    
    private void InitAutoFac()
    {
        //Build a new Autofac container.
        var builder = new ContainerBuilder();
        //Set up NLog for logging.
        builder.RegisterType<LoggerFactory>()
            .As<ILoggerFactory>()
            .SingleInstance();
        builder.RegisterGeneric(typeof(Logger<>))
            .As(typeof(ILogger<>))
            .SingleInstance();
        
        //Bring in types from the config
        var assembly = Assembly.GetExecutingAssembly();
        HashSet<ConfigSection> cfg;
        var assemblyPath = Path.GetDirectoryName(assembly.Location);
        ConfigPath = Path.Combine(assemblyPath, "system_config.ini");
        var systemsPath = Path.Combine(assemblyPath, "SystemConfigs");
        using (var stm = File.OpenRead(ConfigPath))
            cfg = ConfigLoader.LoadConfig(stm, systemsPath);
        
        ConfigHandler = new ConfigurationHandler();
        ConfigHandler.PopulateContainerBuilder(builder, cfg, true);
        
        //Creates and sets the Autofac resolver as the Locator
        var autofacResolver = builder.UseAutofacDependencyResolver();
        Locator.SetLocator(autofacResolver);
        //Register the resolver in Autofac so it can be later resolved
        builder.RegisterInstance(autofacResolver);
        
        //Initialize ReactiveUI components
        autofacResolver.InitializeSplat();
        autofacResolver.InitializeReactiveUI(RegistrationNamespace.Avalonia);
        //Replace the missing registrations
        RxApp.MainThreadScheduler = AvaloniaScheduler.Instance;
        Locator.CurrentMutable.RegisterConstant(new AvaloniaActivationForViewFetcher(), 
            typeof(IActivationForViewFetcher));
        Locator.CurrentMutable.RegisterConstant(new AutoDataTemplateBindingHook(),
            typeof(IPropertyBindingHook));
        
        //Register all view and view-model types
        foreach (var uiAssembly in new[] { Assembly.GetExecutingAssembly(), Assembly.Load("KJX.DevicesUI") })
        {
            builder.RegisterAssemblyTypes(uiAssembly)
                .Where(t => t.IsSubclassOf(typeof(ViewModelBase)))
                .SingleInstance();
            builder.RegisterAssemblyTypes(uiAssembly)
                .Where(t => t.IsSubclassOf(typeof(Window)));
        }
        
        //TODO: what to do about the notification service
        
        Container = builder.Build();
        
        //Add logging, configure NLog and load the XMLReader to read the nlog.config resource
        using var reader = XmlReader.Create(Assembly.GetExecutingAssembly().GetManifestResourceStream("KJX.ProjectTemplate.Engineering.nlog.config"));
        LogManager.Configuration = new XmlLoggingConfiguration(reader);
        var serviceProvider = new AutofacServiceProvider(Container);
        
        //Configure logging using NLog
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
#pragma warning disable CS0618 // Type or member is obsolete
        loggerFactory.AddNLog();
#pragma warning restore CS0618 // Type or member is obsolete

        Logger = Container.Resolve<ILogger<Application>>();

        
        //Resolve the services that need to be started
        var backgroundServices = Container.Resolve<IEnumerable<IBackgroundService>>();
        foreach (var svc in backgroundServices)
        {
            svc.Start();
        }
    }
    
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(ConfigHandler),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}