# Architecture and learning guide

## Current implementation

FoodHub is an educational WinUI 3 customer application. The main window provides Home, Menu, Basket, Orders, Profile, About and Settings navigation. Menu, item details, basket, owned checkout, confirmation, receipts, history and simulated delivery are implemented alongside accounts, sessions, editable contact details, addresses and profile-photo selection.

The account window is a separate owned native window. Its owner is disabled until it closes. The 900 × 600 effective-pixel account window hosts separate LoginPage and RegistrationPage views in a Frame. Click-handled HyperlinkButtons switch forms with native horizontal slide transitions while the red image banner remains fixed. Each page scrolls independently; the native close button dismisses the window. Profile stays in the main window. A `PersonPicture` shows the supplied guest image, a saved account photo, or initials derived from display name. Navigation profile-name visibility follows `NavigationView.IsPaneOpen` immediately, rather than waiting for the pane animation to finish; session validation still controls which name is available.

## Responsibilities and data flow

```text
WinUI page/window -> observable view model -> AccountService -> short-lived EF context -> SQL Server
                <- shared SessionState <- safe result/profile projection <- persisted state
```

`App.App()` constructs `ApplicationHost`, which builds one .NET Generic Host through Dhara.Extensions.WinUI.Hosting.WinUIApplicationBuilder using Microsoft.Extensions.Hosting 10.0.0, referenced by Dhara and available transitively to UI consumers. Core references Dhara for shared configuration/storage. FoodHub owns bundled JSON, and Dhara’s generic build targets copy the selected defaults into application output. The host owns configuration, constructor injection, logging, and shutdown. Workflow/session/storage/settings/runtime services are singletons; each database operation still creates and disposes a context. UI services are resolved on the dispatcher. `ViewModelFactory` uses ActivatorUtilities to create page/window-owned view models outside the root container, so disposing a page releases its subscriptions and cancellation sources. Parameterless Frame pages resolve only their construction dependencies through `App.GetService<T>()`; view models receive their actual dependencies in constructors. The manual ApplicationServices facade was replaced by this composition root. `CommunityToolkit.Mvvm` provides `ObservableObject`, `RelayCommand` and `AsyncRelayCommand`; observable properties use explicit readable setters. No service locator package, generic repository, or third data project is required.

`FoodHub.Core` owns entities, EF mappings/migrations, password hashing, opaque token issuance, validation and customer workflows. `Dhara.Extensions.WinUI` owns reusable Windows paths, dispatching, native window helpers, theming and host/window lifecycle coordination. `FoodHub` owns native views, navigation, photo validation, Windows-protected session policy and theme persistence. Core and its tool consumers now target Windows to use Dhara native storage; Core still constructs no XAML UI.

`AuthenticationViewModel` owns transient form submission and cancellation and is shared between the two account pages. Each page attaches its own password controls while active, clears/detaches them when navigating away, and the frame retains no form history. PasswordBox values are read only at submission and cleared after submission/switching/closure; passwords do not enter persistent observable state. `ProfileViewModel` owns editable form state. Pages handle view lifetime and native picker/image behavior. Commands suppress duplicate submissions; session generation checks discard results after account changes.

## Account and session model

`User` owns email, normalized unique email, display name, versioned password hash, optional normalized PNG bytes and UTC creation time. `Customer` owns full name, phone and delivery addresses. Its nullable unique `UserId` allows one customer per account while preserving unclaimed legacy customers. Registration saves account, customer and session in one EF SaveChanges transaction. Public registration never attaches an existing customer or order history.

Microsoft's `PasswordHasher<User>` supplies salted, versioned password hashing and upgrade-on-login. Email comparison uses trimmed invariant uppercase normalization and a unique database index. Passwords are never trimmed. There is no email delivery, password recovery, staff role, external account provider or JWT signing key.

Sessions use random 32-byte opaque tokens. SQL Server stores only SHA-256 hex digests and UTC issue/expiry/revocation times. Normal sign-in is memory-only for eight hours or app exit; Stay signed in stores a Windows-user-protected token for a fixed seven days. Reads do not extend expiry. `%LOCALAPPDATA%/dashe/FoodHub/session.dat` contains only encrypted token bytes; profile data and passwords are never cached there.

Session restoration and private account operations validate the token in SQL Server. Database failure hides private state, retains a token for deliberate retry and shows a safe error. Expiry returns to Guest. Sign-out clears local state before attempting revocation. This desktop-to-database design is educational: possession of direct database privileges remains outside the account boundary.

## UI and persistence rules

All customer navigation uses the shell gate. Home, Menu and Basket allow guests. Orders, Checkout, Confirmation and Order Details require sign-in; the account window resumes the requested destination after success. Sign-out/expiry clears private navigation history. Profile and Settings are accessible to guests. System/Light/Dark appearance is stored per Windows user.

Names and phone are editable; email is read-only. Every address operation derives its owner from the validated session. Referenced addresses cannot be deleted; edits do not rewrite saved order snapshots. Photos are decoded only after file-size/dimension checks, normalized for orientation, scaled to at most 512 pixels and saved as PNG bytes. Missing/corrupt photos fall back to initials.

Database operations are asynchronous and use a short-lived context. Do not block the UI with `.Result`/`.Wait()` or share a context concurrently. A shared, immutable JSON configuration snapshot supplies runtime, administrative-tool, and EF design-time settings; externally supplied DbContextOptions take precedence. See the hosting/settings section below for provider precedence and [Database](Database.md) for database restart behavior. No migrations run during app startup.

## Menu and ordering workflows

`MenuService` reads detached public catalogue/item projections. `IOrderingService`/`OrderingService` reviews checkout, reads owned history/receipts, and invokes the maintained `PlaceOrder` and `UpdateOrderStatus` procedure wrappers. Every private call validates the opaque token and derives ownership from SQL; navigation IDs and view-model values never establish ownership. Contexts remain short-lived, SQL owns authoritative receipt snapshots and transitions, and timestamps remain UTC until presentation. No schema migration is needed for these pages.

`BasketState` is a singleton, observable in-memory preview with unique meal IDs and quantities from 1 to 32767. A staged editor permits zero as removal; only Confirm changes the basket. SQL inputs never include zero or duplicate IDs. StoreSettings supplies currency/delivery fee; an empty basket has zero totals. Reconciliation retains removed/unavailable lines for explicit removal. Guest sign-in carries items forward; leaving/replacing an account clears them, while profile refreshes preserve them.

`MenuBrowseState` retains search/category/availability/sort/scroll preferences without retaining a page. `MenuViewModel` loads and filters meals; `JourneyViewModel` owns cancellable ordering reads/reviews. `ViewModelFactory` creates these page-owned instances. Thin view code handles native controls, image fallback, navigation and rendering. A shared `QuantityEditor` stages edits; a separate card-body button prevents purchase buttons from invoking item navigation.

The app pins Toolkit Labs TokenView and TransitionHelper to 0.1.251217-build.2433 from NuGet. These experimental components provide filter tokens and compact/editor morphs. Native ItemsRepeater inside ScrollViewer uses StaggeredLayout from CommunityToolkit.WinUI.Controls.Primitives 8.2.251219. Cards have natural heights with 12-pixel spacing and a desired column width of 260; no tile reserves expansion space. Native connected animations link item images across navigation when their source/target is realized. Windows animation preferences are honored, and editing/navigation still work without motion. Menu artwork is fictional, bundled SVG; only paths beneath Assets/Menu are accepted, and invalid or failed images use a local fallback.

`CheckoutCoordinator` owns dispatched placement independently of the page. It shares an in-flight task, retains confirmed order IDs until a receipt is displayed, and clears the basket only after a confirmed write. Page cancellation does not cancel placement. Session generations suppress results from departed accounts. Checkout re-reads current prices, availability, contact and owned addresses; changed values require another explicit review/confirmation. SQL can still change between review and procedure execution: the saved receipt always shows authoritative committed values.

A transport/cancellation failure after placement dispatch is uncertain. There is no automatic retry or new schema/idempotency contract. The customer must inspect successfully refreshed history and acknowledge the duplicate-order risk before another attempt. A known order ID with a failed receipt read is retried as a read only. Database/schema/configuration failures have safe typed results without raw SQL payloads.

Orders groups active/past cards newest first. Confirmation and details share the saved-receipt/timeline view. Demo progression requests 10 → 20 → 30 → 40; cancellation requests 10 → 90. SQL validates ownership and transitions transactionally, including competing actions. Refresh reloads saved state after actions and conflicts; there is no GPS, automatic progression or polling.

See [Database](Database.md), [Verification](Verification.md), and [Roadmap](Development_Roadmap.md).

## Runtime identity and configuration lifetime

If local-storage/host construction fails before the main window exists, App.OnLaunched shows a native ContentDialog with a safe error message and a Close button. A minimal, service-independent window supplies the loaded XamlRoot required by ContentDialog; it contains no custom error view. Dismissing the dialog or closing its host exits the application. Recoverable configuration/session-preparation failures still leave the normal Guest shell and Settings available.

`IAppRuntimeEnvironment.IsPackaged` and `IHostEnvironment.IsPackaged()` query actual Windows process package identity using GetCurrentPackageFullName. They are independent of Debug/Release and Development/Production; unexpected native errors are reported rather than converted into false. Dhara ApplicationDataPaths selects native package LocalPath or unpackaged LocalAppData/dashe/FoodHub using actual identity. Dhara ILocalStorageService/LocalStorageService owns cancellable file I/O and atomic replacement. Native LocalSettings is separate from this file root. session.dat encryption is unchanged. No legacy settings or authentication are imported.

The host content root is AppContext.BaseDirectory. App builds Dhara's host using its existing Application instance, and OnLaunched forwards to IWinUIApplicationLifetime<MainWindow>. Dhara owns native window creation and close monitoring, then stops/disposes the host before queuing final closure. FoodHubStartupParticipant loads appearance before creation, prepares remembered authentication before activation and restores the session after activation. Generated WinUI Main remains enabled. Host file watching is optional and off by default. Database Settings constructs Windows-authentication strings with SqlConnectionStringBuilder, tests a candidate asynchronously without modifying SQL, and saves the override and reset marker together in one native LocalSettings value. A persistent reset marker removes remembered sign-in before restoration on the next launch. Failed removal leaves the marker intact and restoration suppressed. The active database and current authenticated work remain unchanged until restart; appearance applies immediately after a successful save.

Missing/malformed configuration and missing schema are Configuration outcomes; unreachable SQL is a Database outcome. Both hide private session state. ILogger receives only categories and numeric SQL codes, never raw exceptions or connection payloads. Invalid local database values block fallback but leave Settings available for explicit Save/Reset repair. Invalid host files require external correction; local saves cannot repair them.

Published builds disable trimming because EF Core runtime model reflection and constructor-based service creation require metadata. Release publishing exposed EF IL2026/IL2104 warnings with the previous template trimming setting; retaining metadata avoids shipping a build with those known runtime risks. Shutdown defers the final native close through DispatcherQueue after host stop/disposal, because reentrant WinUI Close calls are ignored.

Reference: [Microsoft Generic Host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host) and [Windows process package identity](https://devblogs.microsoft.com/insidemsix/is-this-a-packaged-process/).

Normal app and test builds default WindowsAppSDKSelfContained to true, including the matching native Windows App SDK in the output and package. The SDK selects its appropriate registration-free initializer and omits the framework deployment-manager initializer. This happens before the Generic Host and is independent of .NET SelfContained. Explicitly overriding it to false requires provisioning the matching Windows App Runtime.

DevelopmentSettingsViewModel uses IHostEnvironment.IsDevelopment for both UI visibility and execution gating. Its injected Windows folder launcher opens ILocalStorageService.RootDirectory with StorageFolder/Launcher; Core uses the Windows-targeted Dhara assembly. Settings are scoped to a Windows user/package and survive FoodHub account changes. Database setup is intentionally disposable-development focused; see Database.md for the one InitialCreate migration and explicit reset policy.


## Desktop hosting and application settings

Dhara.Extensions.WinUI.Hosting supplies WinUIApplication.CreateBuilder, WinUIApplicationBuilder, UseWinUIApplication<TMainWindow> and IWinUIApplicationLifetime<TMainWindow>. The library wraps HostApplicationBuilder and has no FoodHub or SQL dependencies. The existing App instance identifies the application; WinUI retains its generated entry point and UI message loop. No additional App base class/interface or second Application instance is needed. See [Dhara's usage and lifecycle guide](../Dhara/Dhara.Extensions.WinUI/README.md).

FoodHub and Core reference Dhara; Core targets the same Windows framework and can host EF tooling directly. Dhara owns one shared ApplicationConfiguration loader, ApplicationConfigurationStatus, ApplicationEnvironment, ApplicationDataPaths, LocalStorageService and LocalSettingsStore. AddFoodHubCore registers only FoodHub workflows and database/session policy. Neither database configuration nor the UI defines a second JSON loader.

ApplicationEnvironment maps the consuming assembly's AssemblyConfigurationAttribute: Debug → Development, Release → Production, custom names unchanged. Missing/unsafe metadata fails clearly; DOTNET_ENVIRONMENT is ignored. Host providers load base appsettings.json then appsettings.{Environment}.json from the executable directory. Runtime preferences never enter IConfiguration. Optional file watching uses standard framework providers; it is off by default, and database workflows retain their immutable launch snapshot even when enabled.

FoodHub owns the appsettings source files. FoodHub explicitly imports Dhara’s buildTransitive/Dhara.Extensions.WinUI.targets, which copies only base plus selected environment JSON to output/publish. Other executable consumers can set DharaAppSettingsDirectory to share sources; database scripts need no copied application JSON. The repository-specific Directory.Build.targets was removed. Core and Dhara ship no application JSON. Stale environment output files are removed when profiles share an output directory. Custom configurations may add corresponding JSON. The application cannot persist host-file edits; those files are corrected externally.

```csharp
var builder = WinUIApplication.CreateBuilder(application,
    new WinUIApplicationOptions { PublisherName = "dashe", ApplicationDataName = "FoodHub" });
builder.UseWinUIApplication<MainWindow>();
// Register FoodHub services and startup participants; Dhara already supplies configuration/storage.
IHost host = builder.Build();
var applicationLifetime = host.Services.GetRequiredService<IWinUIApplicationLifetime<MainWindow>>();
// App.OnLaunched awaits applicationLifetime.StartAsync(args).
```

FoodHubStartupParticipant implements the optional before-create, before-activate and after-activate stages. Recoverable configuration/token-preparation failures leave Guest/Settings available; account-specific policy never enters Dhara. One main-window registration is allowed. Startup and shutdown are idempotent, and registered secondary windows are closed while dependencies remain usable. Native close requests and host-stop signals converge on one task with a five-second shutdown budget. MainWindow checks IWindowRegistry.IsShuttingDown before resuming account navigation from an owned-window close handler. Final close is queued after host cleanup to avoid reentrant native Close calls. Ordinary BackgroundService registrations run alongside the UI and receive cancellation on shutdown; all native work must use the captured dispatcher.

Dhara's IUIThreadDispatcher wraps the existing queue. WindowExtensions replaces the previous NativeWindows interop with generic HWND, owner/input, DPI-aware centering and picker-owner helpers. IWindowRegistry tracks explicit main/secondary registrations, and WindowThemeService applies runtime appearance. FoodHub retains its account-window modality/focus/navigation and profile-image limits/normalization. SessionStorage retains encrypted token format, validity checks and deletion policy. The former host/window/helper implementations were removed or moved because Dhara now owns those responsibilities; scaffold/copyright comments remain preserved.

Development validates dependencies/scopes. Debug logging follows the Logging configuration section; custom containers/providers can be registered before Build. The lifecycle bridge owns starting/stopping/disposing its host; consumers do not call host.Run or independently dispose it. The library enables nullable/XML documentation and removes template IsTrimmable claims; no NuGet publication, signing, tray lifetime or activation-redirection work is included.

Runtime preferences use one native LocalSettingsStore in Dhara, separately from host defaults. Appearance.Theme is a scalar; the Database group contains ConnectionString and ResetSessionOnStartup. Feature services explicitly resolve local values before configuration defaults. Related database values are serialized with type tags into one native string assignment, since the older Windows.Storage composite type is incompatible with the SDK's container. Per-process writes are serialized; cross-process coordination is not provided. Access failures block database fallback; invalid database groups require explicit repair, preserving unrelated settings. Failed writes leave pending UI state and active appearance unchanged.

Packaged apps use their native package data context; unpackaged FoodHub uses GetForUnpackaged("dashe", "FoodHub"). Files are under %LOCALAPPDATA%/dashe/FoodHub; native settings are Windows-managed separately (registry for unpackaged apps). Folder Explorer access shows files, not native preferences. Encrypted session.dat stays a file; LocalSettings is not secret storage. Package and unpackaged identities remain separate, and changing FoodHub accounts does not change either store. Legacy local JSON, theme.txt and prior paths are ignored without migration.

PowerShell scripts now orchestrate EF CLI commands against Core directly. The small design-time factory uses explicit Windows-authentication server/database arguments with localhost/FoodHub_Dev defaults; automation does not load the application’s host files or native preferences. Core supplies the managed runtime configuration/dependencies needed by EF, so no console project, native activation context or WinUI message loop is needed. Reset remains restricted to the approved disposable target and is never performed by application startup. See Database.md for commands and verification.

References: [Generic Host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host), [configuration providers](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-providers), and [SDK assembly attributes](https://learn.microsoft.com/en-us/dotnet/standard/assembly/set-attributes-project-file).

## Compact menu presentation

The title and CommandBar remain above the scrolling viewport. Sort uses a MenuFlyout with exactly one checked choice, availability uses a Filter AppBarToggleButton, and native command overflow accommodates narrow windows. Category tokens form a content-sized transparent overlay at a higher stacking order. Scroll content has a top inset measured from the wrapped tokens plus 12 pixels, so the first row clears the tokens at offset zero while cards pass behind them during scrolling. Hidden feedback consumes no layout space.

QuantityEditor pairs Add/Edit with Confirm through TransitionHelper IDs, animates quantity/cancel controls independently, and changes its measured height over 300 milliseconds. A narrowly scoped dependent XAML height animation lets the staggered layout remeasure as the card grows; composition Offset animations smooth repositioning without changing layout bounds. This avoids scaling food images/text to fake expansion. Page-owned coordination cancels an old draft before opening another card. Recycled/unloaded cards stop animation, discard drafts, detach callbacks and restore natural height. Details and Basket keep their existing confirmation/cancellation semantics. Windows motion preferences select immediate state changes and disable menu reposition animations.

Menu projections use an ObservableCollection, explicit prepared-card rebinding and a fresh StaggeredLayout context when filtering/sorting actually changes the displayed sequence. This prevents cached index/element state from retaining stale card data. Busy/feedback updates retain the current source, preserving active drafts. Individual filter-chip surfaces are opaque themed brushes while the overlay itself stays transparent.

## Menu basket artwork

BasketSummaryButton composes the supplied green PNG and shopping-basket SVG inside a real native Button. Live labels show total units (with singular/plural wording) and the existing currency-formatted total, including delivery when applicable. Decorative images/text are hidden from the normal automation tree; the inner button has a complete dynamic accessible name and tooltip. Pointer states adjust opacity, and system focus visuals remain enabled.

The original 1176 × 401 artwork stays unchanged and scales uniformly from approximately 212 × 72 effective pixels. Caption measurement grows the control for long totals or text scaling, preserving the circular icon area; the Menu header moves the button below the title when the two cannot fit comfortably together. High contrast replaces artwork with native button styling and inherited system text colors. UISettings.ColorValuesChanged observes contrast changes on the UI dispatcher: AccessibilitySettings.HighContrastChanged failed with Element not found in the unpackaged desktop context during verification.
