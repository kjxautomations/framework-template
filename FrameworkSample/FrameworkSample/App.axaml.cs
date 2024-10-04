using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Autofac.Features.AttributeFilters;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Framework.Core;
using Framework.Config;
using Framework.Devices;
using Framework.Devices.Logic;
using Framework.Services;
using Framework.Core.ViewModels;
using FrameworkSample.Models;
using FrameworkSample.Services;
using FrameworkSample.ViewModels;
using FrameworkSample.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using ReactiveUI;
using Splat;
using Splat.Autofac;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace FrameworkSample;

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
        if (File.Exists(assembly.Location))
        {
            var assemblyPath = Path.GetDirectoryName(assembly.Location);
            var configPath = Path.Combine(assemblyPath, "simple_config.ini");
            cfg = ConfigLoader.LoadConfig(configPath);
        }
        else
        {
            using var resourceStream = assembly.GetManifestResourceStream("FrameworkSample.web_config.ini");
            cfg = ConfigLoader.LoadConfig(resourceStream);
        }
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
        foreach (var uiAssembly in new[] { Assembly.GetExecutingAssembly(), Assembly.Load("DevicesUI") })
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
        builder.RegisterType<RunInfo>().AsSelf().WithAttributeFiltering().SingleInstance();
        builder.RegisterType<SequencingService>().AsSelf().WithAttributeFiltering().SingleInstance();
        builder.RegisterType<TemperatureMonitoringService>().AsSelf().As<IBackgroundService>().WithAttributeFiltering().SingleInstance();

        Container = builder.Build();
        Logger = Container.Resolve<ILogger<Application>>();
        // add logging
        // Configure NLog
        // load an XMLReader to read the nlog.config embedded resource
        using var reader = XmlReader.Create(Assembly.GetExecutingAssembly().GetManifestResourceStream("FrameworkSample.nlog.config"));
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
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
            desktop.ShutdownRequested += DesktopOnShutdownRequested;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
        AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskSchedulerOnUnobservedTaskException;
        RxApp.DefaultExceptionHandler = new ObservableExceptionHandler((e) => HandleException(e, true));
    }

    private void DesktopOnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        var components = Container.Resolve<IEnumerable<ISupportsInitialization>>();
        Initializer.Shutdown(components);
    }

    public class ObservableExceptionHandler(Action<Exception> handler) : IObserver<Exception>
    {
        public void OnNext(Exception value)
        {
            handler(value);
        }

        public void OnError(Exception error)
        {
            handler(error);
        }

        public void OnCompleted()
        {
        }
    }

    private void TaskSchedulerOnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        HandleException(e.Exception, true);
    }

    private void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Exception exception = e.ExceptionObject as Exception;
        HandleException(exception, false);
    }

    private void HandleException(Exception? exception, bool showDialog)
    {
        Logger?.LogCritical(exception, "Unhandled exception");
        if (showDialog)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var dialog = new UnhandledExceptionWindow()
                    { DataContext = new UnhandledExceptionViewModel(exception) };

                dialog.ShowDialog(desktop.MainWindow);
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                var dialog = new UnhandledExceptionControl()
                    { DataContext = new UnhandledExceptionViewModel(exception) };
                var existingMain = singleViewPlatform.MainView;
                dialog.Close += () =>
                {
                    singleViewPlatform.MainView = existingMain;
                };
                singleViewPlatform.MainView = dialog;

            }
        }
    }
}