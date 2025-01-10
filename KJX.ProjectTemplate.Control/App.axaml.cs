using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Xml;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using KJX.Core;
using KJX.Config;
using KJX.Core.Interfaces;
using KJX.ProjectTemplate.Control.Models;
using KJX.ProjectTemplate.Control.Services;
using KJX.ProjectTemplate.Control.ViewModels;
using KJX.Core.Services;
using KJX.Core.ViewModels;
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
    public IContainer? Container { get; private set; }
    public ILogger? Logger { get; private set; }

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
        cfg = ConfigLoader.LoadConfig(configPath, systemsPath);
        ConfigurationHandler.PopulateContainerBuilder(builder, cfg);
        
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
        
        Container = builder.Build();
        Logger = Container.Resolve<ILogger<Application>>();
        // add logging
        // Configure NLog
        // load an XMLReader to read the nlog.config embedded resource
        using var reader = XmlReader.Create(Assembly.GetExecutingAssembly().GetManifestResourceStream("KJX.ProjectTemplate.Control.nlog.config"));
        LogManager.Configuration = new XmlLoggingConfiguration(reader);
        var serviceProvider = new AutofacServiceProvider(Container);

        // Configure logging using NLog
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        loggerFactory.AddNLog();
        
        // resolve the services that need to be started
        var backgroundServices = Container.Resolve<IEnumerable<IBackgroundService>>();
        foreach (var svc in backgroundServices)
        {
            svc.Start();
        }
        
        // start up the state machine
        var stateMachine = Container.Resolve<StateMachine>();
        stateMachine.SendTrigger(NavigationTriggers.Next);
    }
    

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Views.MainWindow() { DataContext = Container.Resolve<MainWindowViewModel>() };
        }

        base.OnFrameworkInitializationCompleted();
    }
}