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
            new WinUIApplicationOptions { PublisherName = "MyCompany", ApplicationDataName = "MyApplication" });
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

Host configuration loads from lowest to highest priority:

1. `appsettings.json` in ContentRootPath (default AppContext.BaseDirectory).
2. `appsettings.{Environment}.json` in that directory.

The executable owns these files and their output/publish rules. Dhara ships no application JSON and provides no API to write host files. `ReloadOnChange` defaults to false; set it to true to opt into the framework JSON providers' file watchers. `ApplicationConfiguration.Load` and `ApplicationEnvironment.Resolve` are also usable by console/tool consumers without creating a host or XAML Application. An initial malformed/inaccessible file produces an empty root and safe `ApplicationConfigurationStatus.Error`; correcting host files is an external action. The status describes initial loading, not subsequent watcher events. The consumer chooses when to capture configuration into immutable workflow snapshots.

`Storage.ILocalSettingsStore` holds runtime overrides independently of IConfiguration. Features read their local key/group first and use host defaults when absent. One registered `LocalSettingsStore` serializes this process's writes; it does not synchronize competing processes. `Read`, `ReadGroup`, `WriteAsync`, `RemoveAsync` and `UpdateGroupAsync` never reload or write host files. Invalid types throw `LocalSettingsValueException`; native access failures become safe IOException messages. Only an explicit `repairInvalid: true` group update may replace a malformed group; it cannot suppress storage access failures. Cancellation is honored before the synchronous native commit; that commit cannot be interrupted.

`WindowsLocalSettingsContainer` uses Microsoft.Windows.Storage.ApplicationData containers for packaged and unpackaged processes. Groups use a typed string envelope stored by one native assignment, preserving related values atomically. The older Windows.Storage.ApplicationDataCompositeValue is incompatible with these SDK containers. Groups support strings, Boolean/numeric primitives, Char, Guid, DateTimeOffset, TimeSpan and arrays of those types; arbitrary objects and nested groups are rejected before commit. Native size limits apply: use the file service for larger data. Serialized group data lives inside native LocalSettings, never in an appsettings/settings JSON file. LocalSettings is for preferences, not secrets. Tests/custom backends can supply `SettingsContainer`; `LocalStorageRoot` affects files only, so isolated tests should supply both.

`ApplicationDataContext` opens GetDefault for real package identity, otherwise GetForUnpackaged(PublisherName, ApplicationDataName). Choose a stable explicit publisher/product identity: it determines native settings and file locations. `ApplicationDataPaths` exposes the executable ContentRoot and native LocalPath; unpackaged files use LocalApplicationData/publisher/product. Disposing the context releases handles without deleting data. `LocalStorageService` supplies cancellable file reads, atomic replacement and deletion separately from native preferences. `ILocalStorageFolderLauncher` creates/opens the configured file root in Explorer; it does not open the native settings store.

The executable owns Windows App SDK deployment and initialization. Console consumers need no WinUI Application or dispatcher to use storage; include the native runtime and initialize C#/WinRT wrappers. Tools loaded into a different executable may additionally need the startup executable's activation manifest and resource directory, as FoodHub.Database's EF factory demonstrates. No automatic legacy imports or alternate-store fallback occurs.

IAppRuntimeEnvironment and IHostEnvironment.IsPackaged report actual Windows process identity independently of build configuration. Unexpected native detection errors propagate.

## Windows helpers

- `Threading.IUIThreadDispatcher` captures an existing queue and marshals synchronous/asynchronous callbacks with completion/error propagation. InvokeAsync can run inline on the UI thread; TryEnqueue always queues. Cancellation suppresses work that has not begun; cancellation after a callback begins is cooperative. Queue rejection completes the task with an error.
- `Windowing.IWindowRegistry` tracks initialized main/secondary windows. Register secondary windows explicitly; closed windows unregister. All registry access is on the UI dispatcher. IsShuttingDown lets application-specific close handlers avoid reopening UI during cleanup.
- `Windowing.WindowThemeService` applies ElementTheme to registered windows and later registrations. It does not read or write settings. Register a window after its root content is initialized.
- `Windowing.WindowExtensions` provides GetWindowHandle, SetOwner, SetInputEnabled, CenterOnOwner and InitializeOwnedUI. These dispatcher-bound methods supply native mechanics; the application owns modal state, focus restoration and exception-safe owner re-enabling. CenterOnOwner interprets dimensions as effective pixels and clamps to the owner's monitor work area. InitializeOwnedUI initializes WinRT pickers without displaying them.

For example, a modal owner should disable input only after creating/initializing its child, restore input on both child closure and failed activation, and register the child for shutdown/theming. Business outcomes and navigation remain in the application.

The main window and registered secondary windows share one UI thread in this initial version. Only one lifecycle bridge is supported per Application. App activation redirection, single-instance handling, service/tray operation, automatic window discovery and arbitrary multithreaded windows are deferred.

References: [WinUI Application.Start](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.application.start), [Generic Host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host), [DispatcherQueue](https://learn.microsoft.com/en-us/windows/apps/develop/dispatcherqueue), and [WinRT picker ownership](https://learn.microsoft.com/en-us/windows/apps/develop/ui/display-ui-objects).

Storage references: [Windows App SDK ApplicationData](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.storage.applicationdata), [unpackaged application data](https://github.com/microsoft/WindowsAppSDK/blob/main/specs/applicationdata/ApplicationData.md), and [settings guidance](https://learn.microsoft.com/en-us/windows/apps/design/app-settings/store-and-retrieve-app-data).
