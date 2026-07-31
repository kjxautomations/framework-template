using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Xml;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Autofac.Features.AttributeFilters;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;
using KJX.Core;
using KJX.Config;
using KJX.Core.Interfaces;
using KJX.ProjectTemplate.Control.Models;
using KJX.ProjectTemplate.Control.Services;
using KJX.ProjectTemplate.Control.ViewModels;
using KJX.Core.Services;
using KJX.Core.ViewModels;
using KJX.Devices.Generated;
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

namespace KJX.ProjectTemplate.Control;

public partial class App : Application
{
    public IContainer Container { get; private set; }
    public ILogger Logger { get; private set; }

    /// <summary>
    /// Serves the scripting API for the devices this application owns. Hosted in-process for
    /// now: dispatch is a direct call into the same container the UI binds to.
    /// </summary>
    public ScriptApiHost ScriptingHost { get; private set; }

    public override void Initialize()
    {
        InitAutofac();
        AvaloniaXamlLoader.Load(this);
    }
    private void InitAutofac()
    {
        // Build a new Autofac container.
        var builder = new ContainerBuilder();
        // set up NLog for logging
        builder.RegisterType<LoggerFactory>()
            .As<ILoggerFactory>()
            .SingleInstance();
        builder.RegisterGeneric(typeof(Logger<>))
            .As(typeof(ILogger<>))
            .SingleInstance();
        
        // bring in types from the config
        var assembly = Assembly.GetExecutingAssembly();
        HashSet<ConfigSection> cfg;
        var assemblyPath = Path.GetDirectoryName(assembly.Location);
        var configPath = Path.Combine(assemblyPath, "system_config.ini");
        var systemsPath = Path.Combine(assemblyPath, "SystemConfigs");
        using (var stm = File.OpenRead(configPath))
            cfg = ConfigLoader.LoadConfig(stm, systemsPath);
        (new ConfigurationHandler()).PopulateContainerBuilder(builder, cfg);
        
        // Creates and sets the Autofac resolver as the Locator
        var autofacResolver = builder.UseAutofacDependencyResolver();
        Locator.SetLocator(autofacResolver);
        // Register the resolver in Autofac so it can be later resolved
        builder.RegisterInstance(autofacResolver);

        // Initialize ReactiveUI components
        autofacResolver.InitializeSplat();
        autofacResolver.InitializeReactiveUI( RegistrationNamespace.Avalonia);
        // replace the missing registrations
        RxApp.MainThreadScheduler = AvaloniaScheduler.Instance;
        Locator.CurrentMutable.RegisterConstant(new AvaloniaActivationForViewFetcher(),
            typeof(IActivationForViewFetcher));
        Locator.CurrentMutable.RegisterConstant(new AutoDataTemplateBindingHook(), typeof(IPropertyBindingHook));

        // register all view and viewmodel types
        foreach (var uiAssembly in new[] { Assembly.GetExecutingAssembly()})
        {
            builder.RegisterAssemblyTypes(uiAssembly)
                .Where(t => t.IsSubclassOf(typeof(ViewModelBase)))
                .Where(t => !t.IsSubclassOf(typeof(StateViewModelBase<NavigationStates,NavigationTriggers>)))
                .SingleInstance();
            builder.RegisterAssemblyTypes(uiAssembly)
                .Where(t => t.IsSubclassOf(typeof(StateViewModelBase<NavigationStates,NavigationTriggers>)))
                .As<StateViewModelBase<NavigationStates,NavigationTriggers>>()
                .AsSelf()
                .SingleInstance();
            builder.RegisterAssemblyTypes(uiAssembly)
                .Where(t => t.IsSubclassOf(typeof(Window)));
        }
        // register the state machine
        builder.RegisterType<StateMachine>().AsSelf().SingleInstance();

        builder.RegisterType<NavigationService>().As<INavigationService<NavigationStates, NavigationTriggers>>()
            .SingleInstance();
        
        builder.RegisterType<InMemoryNotificationService>()
            .As<INotificationService>()
            .WithParameter("context", SynchronizationContext.Current)
            .SingleInstance();
#if (!AsTemplate)
        builder.RegisterType<RunInfo>().AsSelf().WithAttributeFiltering().SingleInstance();
        builder.RegisterType<SequencingService>().AsSelf().WithAttributeFiltering().SingleInstance();
        builder.RegisterType<TemperatureMonitoringService>().AsSelf().As<IBackgroundService>().WithAttributeFiltering().SingleInstance();
#endif
        Container = builder.Build();
        // add logging
        // Configure NLog
        // load an XMLReader to read the nlog.config embedded resource
        using var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("KJX.ProjectTemplate.Control.nlog.config"));
        LogManager.Configuration = new XmlLoggingConfiguration(reader, "nlog.config");
        
        // Create a service collection to configure logging properly
        var services = new ServiceCollection();
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.AddNLog();
        });
        
        var serviceProvider = new AutofacServiceProvider(Container);

        // Configure logging using NLog - no need to call AddNLog on ILoggerFactory anymore
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        Logger = Container.Resolve<ILogger<Application>>();

        // resolve the services that need to be started
        var backgroundServices = Container.Resolve<IEnumerable<IBackgroundService>>();
        foreach (var svc in backgroundServices)
        {
            svc.Start();
        }
        
        // start up the state machine
        var stateMachine = Container.Resolve<StateMachine>();
        stateMachine.SendTrigger(NavigationTriggers.Next).Wait();
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
            var options = ScriptApiHostOptions.ForLocalInstrument("kjx-control");

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
            desktop.MainWindow = new Views.MainWindow() { DataContext = Container.Resolve<MainWindowViewModel>() };

            desktop.ShutdownRequested += (_, _) => ScriptingHost?.StopAsync().GetAwaiter().GetResult();
        }

        base.OnFrameworkInitializationCompleted();
    }
}