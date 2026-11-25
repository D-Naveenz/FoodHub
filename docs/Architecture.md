# Architecture and learning guide

## Current implementation

FoodHub is an educational WinUI 3 customer application. The main window provides Home, Menu, Basket, Orders, Profile, About and Settings navigation. Ordering pages are clearly labeled skeletons. Accounts, sessions, editable contact details, addresses and profile-photo selection are implemented; ordering is not.

The account window is a separate owned native window. Its owner is disabled until it closes. The 900 × 600 effective-pixel account window hosts separate LoginPage and RegistrationPage views in a Frame. Click-handled HyperlinkButtons switch forms with native horizontal slide transitions while the red image banner remains fixed. Each page scrolls independently; the native close button dismisses the window. Profile stays in the main window. A `PersonPicture` shows the supplied guest image, a saved account photo, or initials derived from display name. Navigation profile-name visibility follows `NavigationView.IsPaneOpen` immediately, rather than waiting for the pane animation to finish; session validation still controls which name is available.

## Responsibilities and data flow

```text
WinUI page/window -> observable view model -> AccountService -> short-lived EF context -> SQL Server
                <- shared SessionState <- safe result/profile projection <- persisted state
```

`App.App()` constructs `ApplicationHost`, which builds one .NET Generic Host through Dhara.Extensions.WinUI.Hosting.WinUIApplicationBuilder using Microsoft.Extensions.Hosting 10.0.0, referenced by Dhara and available transitively to UI consumers. Core references portable hosting abstractions and JSON configuration packages. Core owns the base and selected environment JSON defaults as Content, copied through project references rather than duplicated in consuming projects. The host owns configuration, constructor injection, logging, and shutdown. Workflow/session/storage/settings/runtime services are singletons; each database operation still creates and disposes a context. UI services are resolved on the dispatcher. `ViewModelFactory` uses ActivatorUtilities to create page/window-owned view models outside the root container, so disposing a page releases its subscriptions and cancellation sources. Parameterless Frame pages resolve only their construction dependencies through `App.GetService<T>()`; view models receive their actual dependencies in constructors. The manual ApplicationServices facade was replaced by this composition root. `CommunityToolkit.Mvvm` provides `ObservableObject`, `RelayCommand` and `AsyncRelayCommand`; observable properties use explicit readable setters. No service locator package, generic repository, or third data project is required.

`FoodHub.Core` owns entities, EF mappings/migrations, password hashing, opaque token issuance, validation and customer workflows. `Dhara.Extensions.WinUI` owns reusable Windows paths, dispatching, native window helpers, theming and host/window lifecycle coordination. `FoodHub` owns native views, navigation, photo validation, Windows-protected session policy and theme persistence. Windows types do not enter Core.

`AuthenticationViewModel` owns transient form submission and cancellation and is shared between the two account pages. Each page attaches its own password controls while active, clears/detaches them when navigating away, and the frame retains no form history. PasswordBox values are read only at submission and cleared after submission/switching/closure; passwords do not enter persistent observable state. `ProfileViewModel` owns editable form state. Pages handle view lifetime and native picker/image behavior. Commands suppress duplicate submissions; session generation checks discard results after account changes.

## Account and session model

`User` owns email, normalized unique email, display name, versioned password hash, optional normalized PNG bytes and UTC creation time. `Customer` owns full name, phone and delivery addresses. Its nullable unique `UserId` allows one customer per account while preserving unclaimed legacy customers. Registration saves account, customer and session in one EF SaveChanges transaction. Public registration never attaches an existing customer or order history.

Microsoft's `PasswordHasher<User>` supplies salted, versioned password hashing and upgrade-on-login. Email comparison uses trimmed invariant uppercase normalization and a unique database index. Passwords are never trimmed. There is no email delivery, password recovery, staff role, external account provider or JWT signing key.

Sessions use random 32-byte opaque tokens. SQL Server stores only SHA-256 hex digests and UTC issue/expiry/revocation times. Normal sign-in is memory-only for eight hours or app exit; Stay signed in stores a Windows-user-protected token for a fixed seven days. Reads do not extend expiry. `%LOCALAPPDATA%/FoodHub/session.dat` contains only encrypted token bytes; profile data and passwords are never cached there.

Session restoration and private account operations validate the token in SQL Server. Database failure hides private state, retains a token for deliberate retry and shows a safe error. Expiry returns to Guest. Sign-out clears local state before attempting revocation. This desktop-to-database design is educational: possession of direct database privileges remains outside the account boundary.

## UI and persistence rules

All customer navigation uses the shell gate. Home, Menu and Basket allow guests. Orders, Checkout, Confirmation and Order Details require sign-in; the account window resumes the requested destination after success. Sign-out/expiry clears private navigation history. Profile and Settings are accessible to guests. System/Light/Dark appearance is stored per Windows user.

Names and phone are editable; email is read-only. Every address operation derives its owner from the validated session. Referenced addresses cannot be deleted; edits do not rewrite saved order snapshots. Photos are decoded only after file-size/dimension checks, normalized for orientation, scaled to at most 512 pixels and saved as PNG bytes. Missing/corrupt photos fall back to initials.

Database operations are asynchronous and use a short-lived context. Do not block the UI with `.Result`/`.Wait()` or share a context concurrently. A shared, immutable JSON configuration snapshot supplies runtime, administrative-tool, and EF design-time settings; externally supplied DbContextOptions take precedence. See the hosting/settings section below for provider precedence and [Database](Database.md) for database restart behavior. No migrations run during app startup.

## Ordering work remains deferred

Keep the basket in memory when that milestone is implemented. Scope order reads/writes to the authenticated customer. Use the retained `PlaceOrder` and `UpdateOrderStatus` contracts for order writes; SQL Server owns authoritative prices, receipt snapshots and status transitions. Store timestamps in UTC and display them in the user's local time. Delivery remains explicitly simulated.

See [Database](Database.md) for migration ownership, [Accounts](Accounts.md) for operation/validation details, and [Verification](Verification.md) for actual checks and remaining manual scenarios.

## Runtime identity and configuration lifetime

`IAppRuntimeEnvironment.IsPackaged` and `IHostEnvironment.IsPackaged()` query actual Windows process package identity using GetCurrentPackageFullName. They are independent of Debug/Release and Development/Production; unexpected native errors are reported rather than converted into false. Dhara ApplicationDataPaths selects package LocalFolder or unpackaged LocalAppData/FoodHub using the actual runtime identity. Core ILocalStorageService/LocalStorageService owns cancellable file I/O and atomic replacement. ApplicationPaths retains install/configuration path metadata; the Windows resolver injects its selected writable root. session.dat encryption is unchanged. No legacy settings or authentication are imported.

The host content root is AppContext.BaseDirectory. App builds Dhara's host using its existing Application instance, and OnLaunched forwards to IWinUIApplicationLifetime<MainWindow>. Dhara owns native window creation and close monitoring, then stops/disposes the host before queuing final closure. FoodHubStartupParticipant loads appearance before creation, prepares remembered authentication before activation and restores the session after activation. Generated WinUI Main remains enabled. Configuration loads once with no file watchers. Database Settings constructs Windows-authentication strings with SqlConnectionStringBuilder, tests a candidate asynchronously without modifying SQL, and saves via a temporary file followed by atomic replacement. A persistent reset marker removes remembered sign-in before restoration on the next launch. Failed removal leaves the marker intact and restoration suppressed. The active database and current authenticated work remain unchanged until restart; appearance applies immediately after a successful save.

Missing/malformed configuration and missing schema are Configuration outcomes; unreachable SQL is a Database outcome. Both hide private session state. ILogger receives only categories and numeric SQL codes, never raw exceptions or connection payloads. Invalid user JSON blocks database fallback but leaves Settings available to explicitly repair/reset it.

Published builds disable trimming because EF Core runtime model reflection and constructor-based service creation require metadata. Release publishing exposed EF IL2026/IL2104 warnings with the previous template trimming setting; retaining metadata avoids shipping a build with those known runtime risks. Shutdown defers the final native close through DispatcherQueue after host stop/disposal, because reentrant WinUI Close calls are ignored.

Reference: [Microsoft Generic Host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host) and [Windows process package identity](https://devblogs.microsoft.com/insidemsix/is-this-a-packaged-process/).

Normal app and test builds default WindowsAppSDKSelfContained to true, including the matching native Windows App SDK in the output and package. The SDK selects its appropriate registration-free initializer and omits the framework deployment-manager initializer. This happens before the Generic Host and is independent of .NET SelfContained. Explicitly overriding it to false requires provisioning the matching Windows App Runtime.

DevelopmentSettingsViewModel uses IHostEnvironment.IsDevelopment for both UI visibility and execution gating. Its injected Windows folder launcher opens ILocalStorageService.RootDirectory with StorageFolder/Launcher; Core remains Windows-independent. Settings are scoped to a Windows user/package and survive FoodHub account changes. Database setup is intentionally disposable-development focused; see Database.md for the one InitialCreate migration and explicit reset policy.


## Desktop hosting and application settings

Dhara.Extensions.WinUI.Hosting supplies WinUIApplication.CreateBuilder, WinUIApplicationBuilder, UseWinUIApplication<TMainWindow> and IWinUIApplicationLifetime<TMainWindow>. The library wraps HostApplicationBuilder and has no FoodHub or SQL dependencies. The existing App instance identifies the application; WinUI retains its generated entry point and UI message loop. No additional App base class/interface or second Application instance is needed. See [Dhara's usage and lifecycle guide](../Dhara/Dhara.Extensions.WinUI/README.md).

FoodHub references Dhara and Core independently. It adapts Dhara's ApplicationDataPaths and WinUIConfigurationStatus to Core's ApplicationPaths, ILocalStorageService and ApplicationConfigurationStatus, then calls AddFoodHubCore for account/database services. Core and database tools remain net10.0 and never reference Dhara's Windows assembly. Core keeps its portable application configuration loader for EF/administration. Contract tests compare environment selection, JSON values and malformed-file behavior across the UI and tooling implementations.

Both loaders select Debug → Development, Release → Production, or the unchanged custom configuration name from the consuming assembly's AssemblyConfigurationAttribute. Missing/unsafe metadata fails clearly. DOTNET_ENVIRONMENT is neither read nor modified. Package identity, launch profiles and architecture remain independent. Providers are base appsettings.json → bundled appsettings.{Environment}.json → local appsettings.json, without watchers or application environment/command-line overrides. The content root defaults to AppContext.BaseDirectory.

The executable owns its bundled JSON and build-copy rules. FoodHub's Directory.Build.targets declares the base/selected environment content in Core for transitive copy. Debug publishing includes Development settings; Release/custom profiles exclude them and stale environment files are removed from reused output/publish directories. Dhara ships no FoodHub defaults. To add a custom profile, select/declare its build configuration and optionally add corresponding bundled JSON.

```csharp
var builder = WinUIApplication.CreateBuilder(application,
    new WinUIApplicationOptions { ApplicationDataName = "FoodHub" });
builder.UseWinUIApplication<MainWindow>();
// Map reusable paths/status to Core contracts and register FoodHub's services/participants.
IHost host = builder.Build();
var applicationLifetime = host.Services.GetRequiredService<IWinUIApplicationLifetime<MainWindow>>();
// App.OnLaunched awaits applicationLifetime.StartAsync(args).
```

FoodHubStartupParticipant implements the optional before-create, before-activate and after-activate stages. Recoverable configuration/token-preparation failures leave Guest/Settings available; account-specific policy never enters Dhara. One main-window registration is allowed. Startup and shutdown are idempotent, and registered secondary windows are closed while dependencies remain usable. Native close requests and host-stop signals converge on one task with a five-second shutdown budget. MainWindow checks IWindowRegistry.IsShuttingDown before resuming account navigation from an owned-window close handler. Final close is queued after host cleanup to avoid reentrant native Close calls. Ordinary BackgroundService registrations run alongside the UI and receive cancellation on shutdown; all native work must use the captured dispatcher.

Dhara's IUIThreadDispatcher wraps the existing queue. WindowExtensions replaces the previous NativeWindows interop with generic HWND, owner/input, DPI-aware centering and picker-owner helpers. IWindowRegistry tracks explicit main/secondary registrations, and WindowThemeService applies runtime appearance. FoodHub retains its account-window modality/focus/navigation and profile-image limits/normalization. SessionStorage retains encrypted token format, validity checks and deletion policy. The former host/window/helper implementations were removed or moved because Dhara now owns those responsibilities; scaffold/copyright comments remain preserved.

Development validates dependencies/scopes. Debug logging follows the Logging configuration section; custom containers/providers can be registered before Build. The lifecycle bridge owns starting/stopping/disposing its host; consumers do not call host.Run or independently dispose it. The library enables nullable/XML documentation and removes template IsTrimmable claims; no NuGet publication, signing, tray lifetime or activation-redirection work is included.

LocalSettingsStore is one singleton shared by database and appearance writers. A semaphore serializes each read-modify-write operation; LocalStorageService writes a temporary file and replaces the destination atomically. This coordinates writers within one process, not competing processes. Unrelated JSON properties survive edits; comments/formatting are not preserved. Cancellation and failed writes do not publish successful UI state.

Appearance:Theme accepts System, Light or Dark, defaulting to System. AppearanceSettings maps these strings to WinUI ElementTheme, initializes before window construction and applies a successful save immediately to current windows. DatabaseSettings remains an immutable launch snapshot, so database saves require restart. File saves do not reload the host IConfiguration. Malformed local JSON blocks database fallback; appearance uses System. Ordinary appearance saves cannot discard malformed JSON; explicit database Save/Reset repairs it. Database reset removes only the connection override and requests remembered-token removal on next launch, preserving appearance and unrelated keys.

The local JSON also contains the durable session-reset marker. Encrypted session.dat remains separate. Legacy imports, completion markers and theme.txt handling have been removed without migration; obsolete files are left untouched. Documentation and XML examples describe this ownership and lifetime rather than introducing customer-facing teaching text.

References: [Generic Host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host), [configuration providers](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-providers), and [SDK assembly attributes](https://learn.microsoft.com/en-us/dotnet/standard/assembly/set-attributes-project-file).
