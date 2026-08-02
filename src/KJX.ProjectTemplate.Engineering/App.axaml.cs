using System;
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
using ReactiveUI.Avalonia;
using KJX.Config;
using KJX.Core;
using KJX.Core.Interfaces;
using KJX.Core.ViewModels;
using KJX.Devices.Generated;
using KJX.ProjectTemplate.Engineering.ViewModels;
using KJX.ProjectTemplate.Engineering.Views;
using KJX.Scripting.Rpc;
using KJX.Scripting.Runtime;
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

    /// <summary>
    /// Serves the scripting API for the devices this application owns. Hosted in-process for
    /// now: dispatch is a direct call into the same container the UI binds to.
    /// </summary>
    public ScriptApiHost ScriptingHost { get; private set; }

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
        using var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("KJX.ProjectTemplate.Engineering.nlog.config"));
        LogManager.Configuration = new XmlLoggingConfiguration(reader, "nlog.config");
        
        // Create a service collection to configure logging properly
        var services = new ServiceCollection();
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.AddNLog();
        });
        
        var serviceProvider = new AutofacServiceProvider(Container);

        // Get the logger factory (NLog is already configured through the service collection)
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        Logger = Container.Resolve<ILogger<Application>>();

        
        //Resolve the services that need to be started
        var backgroundServices = Container.Resolve<IEnumerable<IBackgroundService>>();
        foreach (var svc in backgroundServices)
        {
            svc.Start();
        }
    }
    
    /// <summary>
    /// Starts the scripting endpoint. A failure here is logged and the application carries on:
    /// losing the script interface must not stop someone operating the instrument by hand.
    /// </summary>
    private void StartScriptingHost()
    {
        try
        {
            // Local only by default. To serve remote clients, set Port and Token here, and point
            // CertificatePath at a certificate.
            var options = ScriptApiHostOptions.ForLocalInstrument("kjx-engineering");

            ScriptingHost = ScriptApiHost.Create(
                Container,
                options,
                new IScriptApiCatalog[] { ScriptApiCatalog.Instance },
                Container.Resolve<ILoggerFactory>());

            ScriptingHost.StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Logger?.LogError(exception, "The scripting host did not start.");
            ScriptingHost = null;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        StartScriptingHost();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(ConfigHandler),
            };

            desktop.ShutdownRequested += (_, _) => ScriptingHost?.StopAsync().GetAwaiter().GetResult();
        }

        base.OnFrameworkInitializationCompleted();
    }
}