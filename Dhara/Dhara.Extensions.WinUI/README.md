# Dhara.Extensions.WinUI

Reusable WinUI 3 infrastructure extracted from a working desktop application. This is currently a local project reference; NuGet publication, signing and release automation are deferred.

The library targets `net10.0-windows10.0.26100.0` with a minimum Windows version of 17763. It references Microsoft.Extensions.Hosting 10.0.0 and Windows App SDK. The executable remains responsible for WinUI runtime deployment and generated startup. The library has no FoodHub or database dependencies and makes no trimming-compatibility claim.

## Hosting an existing Application

Use `Dhara.Extensions.WinUI.Hosting`. Keep the template-generated WinUI entry point and derive your App from `Microsoft.UI.Xaml.Application`. WinUI constructs App; do not register another Application for DI to instantiate. No custom App interface or base class is required.

```csharp
using Dhara.Extensions.WinUI.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

public partial class App : Application
{
    private readonly IWinUIApplicationLifetime<MainWindow> _application;

    public App()
    {
        InitializeComponent();
        var builder = WinUIApplication.CreateBuilder(this,
            new WinUIApplicationOptions { ApplicationDataName = "MyApplication" });
        builder.UseWinUIApplication<MainWindow>();
        // Register application services and IWinUIStartupParticipant implementations here.
        var host = builder.Build();
        _application = host.Services.GetRequiredService<IWinUIApplicationLifetime<MainWindow>>();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try { await _application.StartAsync(args); }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Startup failure has already scheduled cleanup. Present/log a safe application error here.
            await _application.RequestShutdownAsync();
        }
    }
}
```

Build returns the standard IHost. WinUIApplicationBuilder also implements IHostApplicationBuilder and exposes Configuration, Services, Logging, Metrics, Environment, Properties and ConfigureContainer. Replacement containers supply their own validation policy. UseWinUIApplication permits one main-window registration and constructs the window through DI on the UI dispatcher. MainWindow is unavailable until creation; constructor dependencies must not try to resolve it prematurely.

When using the lifecycle bridge, it owns starting, stopping and disposing the host. Do not additionally call host.Run, RunAsync, StopAsync or Dispose. The generated WinUI entry point owns the message loop; there is no hidden polling loop, second UI thread, or tray mode. Closing the main window ends the application after cleanup. To request shutdown from an application service, use IHostApplicationLifetime.StopApplication or IWinUIApplicationLifetime.RequestShutdownAsync.

### Startup and shutdown order

Register singleton IWinUIStartupParticipant implementations in the required order. Their default methods are optional:

1. During host startup, BeforeWindowCreatedAsync initializes state needed by constructors.
2. Resolve and register the main window on the existing dispatcher.
3. After host startup completes, BeforeWindowActivatedAsync prepares state before the UI is active.
4. Activate the main window, then run AfterWindowActivatedAsync with the original launch arguments.

Participants run sequentially on the UI dispatcher, including across ordinary awaited continuations. Honor their cancellation token. Handle recoverable application/domain errors inside a participant; an unhandled exception aborts startup and schedules cleanup. Long-running independent work belongs in ordinary IHostedService/BackgroundService registrations; background services must dispatch native UI operations explicitly.

User close, programmatic Window.Close, explicit shutdown and host-stop signals converge on one task. The controller cancels startup, closes registered secondary windows while dependencies remain available, stops services and disposes the host, then queues final main-window closure after native close callbacks unwind. The default shutdown budget is five seconds. Uncooperative services receive cancellation and their errors/timeouts are logged; arbitrary work cannot be forcibly terminated safely. Do not rely on graceful cleanup after process termination or OS-level forced shutdown.

## Configuration and application data

The consumer's AssemblyConfigurationAttribute determines the environment: Debug → Development, Release → Production, other names unchanged. DOTNET_ENVIRONMENT and application-setting environment variables are ignored. By default the consuming App type supplies the assembly; ApplicationAssembly can explicitly select metadata from another consumer assembly.

Settings load once, without watchers, from lowest to highest priority:

1. `appsettings.json` in ContentRootPath (default AppContext.BaseDirectory).
2. `appsettings.{Environment}.json` in that directory.
3. `appsettings.json` in the writable application-data root.

The consumer owns its bundled files and their output/publish rules; Dhara ships no application settings. A malformed/inaccessible configuration file yields an empty snapshot and a safe WinUIConfigurationStatus.Error, rather than silently falling through. An application can still show repair UI and choose how to persist repaired settings. File persistence, authentication and restart-vs-immediate setting behavior are application responsibilities.

ApplicationDataPaths selects Windows.Storage.ApplicationData.Current.LocalFolder only when the process has actual package identity. Otherwise it uses LocalApplicationData/ApplicationDataName. The library does not grant package identity. LocalStorageRoot and ContentRootPath can isolate tests or implement another deliberate storage policy. Resolving paths does not write files. ILocalStorageFolderLauncher creates/opens the configured root in Explorer.

IAppRuntimeEnvironment and IHostEnvironment.IsPackaged report actual Windows process identity independently of build configuration. Unexpected native detection errors propagate.

## Windows helpers

- `Threading.IUIThreadDispatcher` captures an existing queue and marshals synchronous/asynchronous callbacks with completion/error propagation. InvokeAsync can run inline on the UI thread; TryEnqueue always queues. Cancellation suppresses work that has not begun; cancellation after a callback begins is cooperative. Queue rejection completes the task with an error.
- `Windowing.IWindowRegistry` tracks initialized main/secondary windows. Register secondary windows explicitly; closed windows unregister. All registry access is on the UI dispatcher. IsShuttingDown lets application-specific close handlers avoid reopening UI during cleanup.
- `Windowing.WindowThemeService` applies ElementTheme to registered windows and later registrations. It does not read or write settings. Register a window after its root content is initialized.
- `Windowing.WindowExtensions` provides GetWindowHandle, SetOwner, SetInputEnabled, CenterOnOwner and InitializeOwnedUI. These dispatcher-bound methods supply native mechanics; the application owns modal state, focus restoration and exception-safe owner re-enabling. CenterOnOwner interprets dimensions as effective pixels and clamps to the owner's monitor work area. InitializeOwnedUI initializes WinRT pickers without displaying them.

For example, a modal owner should disable input only after creating/initializing its child, restore input on both child closure and failed activation, and register the child for shutdown/theming. Business outcomes and navigation remain in the application.

The main window and registered secondary windows share one UI thread in this initial version. Only one lifecycle bridge is supported per Application. App activation redirection, single-instance handling, service/tray operation, automatic window discovery and arbitrary multithreaded windows are deferred.

References: [WinUI Application.Start](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.application.start), [Generic Host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host), [DispatcherQueue](https://learn.microsoft.com/en-us/windows/apps/develop/dispatcherqueue), and [WinRT picker ownership](https://learn.microsoft.com/en-us/windows/apps/develop/ui/display-ui-objects).
