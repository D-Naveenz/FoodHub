# Architecture and learning guide

## Current implementation

FoodHub is an educational WinUI 3 customer application. The main window provides Home, Menu, Basket, Orders, Profile, About and Settings navigation. Ordering pages are clearly labeled skeletons. Accounts, sessions, editable contact details, addresses and profile-photo selection are implemented; ordering is not.

The account window is a separate owned native window. Its owner is disabled until it closes. The 900 × 600 effective-pixel account window hosts separate LoginPage and RegistrationPage views in a Frame. Click-handled HyperlinkButtons switch forms with native horizontal slide transitions while the red image banner remains fixed. Each page scrolls independently; the native close button dismisses the window. Profile stays in the main window. A `PersonPicture` shows the supplied guest image, a saved account photo, or initials derived from display name. Navigation profile-name visibility follows `NavigationView.IsPaneOpen` immediately, rather than waiting for the pane animation to finish; session validation still controls which name is available.

## Responsibilities and data flow

```text
WinUI page/window -> observable view model -> AccountService -> short-lived EF context -> SQL Server
                <- shared SessionState <- safe result/profile projection <- persisted state
```

`ApplicationHost` builds one .NET Generic Host using Microsoft.Extensions.Hosting 10.0.0. The host owns configuration, constructor injection, logging, and shutdown. Workflow/session/storage/settings/runtime services are singletons; each database operation still creates and disposes a context. UI services are resolved on the dispatcher. `ViewModelFactory` uses ActivatorUtilities to create page/window-owned view models outside the root container, so disposing a page releases its subscriptions and cancellation sources. Parameterless Frame pages resolve only their construction dependencies through `App.GetService<T>()`; view models receive their actual dependencies in constructors. The manual ApplicationServices facade was replaced by this composition root. `CommunityToolkit.Mvvm` provides `ObservableObject`, `RelayCommand` and `AsyncRelayCommand`; observable properties use explicit readable setters. No service locator package, generic repository, or third data project is required.

`FoodHub.Core` owns entities, EF mappings/migrations, password hashing, opaque token issuance, validation and customer workflows. `FoodHub` owns native windows, navigation, photo decoding, Windows-protected storage and theme settings. Windows types do not enter Core.

`AuthenticationViewModel` owns transient form submission and cancellation and is shared between the two account pages. Each page attaches its own password controls while active, clears/detaches them when navigating away, and the frame retains no form history. PasswordBox values are read only at submission and cleared after submission/switching/closure; passwords do not enter persistent observable state. `ProfileViewModel` owns editable form state. Pages handle view lifetime and native picker/image behavior. Commands suppress duplicate submissions; session generation checks discard results after account changes.

## Account and session model

`User` owns email, normalized unique email, display name, versioned password hash, optional normalized PNG bytes and UTC creation time. `Customer` owns full name, phone and delivery addresses. Its nullable unique `UserId` allows one customer per account while preserving unclaimed legacy customers. Registration saves account, customer and session in one EF SaveChanges transaction. Public registration never attaches an existing customer or order history.

Microsoft's `PasswordHasher<User>` supplies salted, versioned password hashing and upgrade-on-login. Email comparison uses trimmed invariant uppercase normalization and a unique database index. Passwords are never trimmed. There is no email delivery, password recovery, staff role, external account provider or JWT signing key.

Sessions use random 32-byte opaque tokens. SQL Server stores only SHA-256 hex digests and UTC issue/expiry/revocation times. Normal sign-in is memory-only for eight hours or app exit; Stay signed in stores a Windows-user-protected token for a fixed seven days. Reads do not extend expiry. `%LOCALAPPDATA%/FoodHub/session.dat` contains only encrypted token bytes; profile data and passwords are never cached there.

Session restoration and private account operations validate the token in SQL Server. Database failure hides private state, retains a token for deliberate retry and shows a safe error. Expiry returns to Guest. Sign-out clears local state before attempting revocation. This desktop-to-database design is educational: possession of direct database privileges remains outside the account boundary.

## UI and persistence rules

All customer navigation uses the shell gate. Home, Menu and Basket allow guests. Orders, Checkout, Confirmation and Order Details require sign-in; the account window resumes the requested destination after success. Sign-out/expiry clears private navigation history. Profile and Settings are accessible to guests. System/Light/Dark appearance is stored per Windows user.

Names and phone are editable; email is read-only. Every address operation derives its owner from the validated session. Referenced addresses cannot be deleted; edits do not rewrite saved order snapshots. Photos are decoded only after file-size/dimension checks, normalized for orientation, scaled to at most 512 pixels and saved as PNG bytes. Missing/corrupt photos fall back to initials.

Database operations are asynchronous and use a short-lived context. Do not block the UI with `.Result`/`.Wait()` or share a context concurrently. A shared, immutable JSON/environment configuration snapshot supplies runtime, administrative-tool, and EF design-time settings; externally supplied DbContextOptions take precedence. See [Database](Database.md) for provider precedence and restart behavior. No migrations run during app startup.

## Ordering work remains deferred

Keep the basket in memory when that milestone is implemented. Scope order reads/writes to the authenticated customer. Use the retained `PlaceOrder` and `UpdateOrderStatus` contracts for order writes; SQL Server owns authoritative prices, receipt snapshots and status transitions. Store timestamps in UTC and display them in the user's local time. Delivery remains explicitly simulated.

See [Database](Database.md) for migration ownership, [Accounts](Accounts.md) for operation/validation details, and [Verification](Verification.md) for actual checks and remaining manual scenarios.

## Runtime identity and configuration lifetime

`IAppRuntimeEnvironment.IsPackaged` and `IHostEnvironment.IsPackaged()` query actual Windows process package identity using GetCurrentPackageFullName. They are independent of Debug/Release and Development/Production; unexpected native errors are reported rather than converted into false. No storage branch was introduced: the existing user-protected token implementation has unpackaged test coverage. ApplicationPaths centralizes install and writable user-data paths without moving session.dat or theme.txt.

The host content root is AppContext.BaseDirectory. Configuration loads once with no file watchers. Database Settings constructs Windows-authentication strings with SqlConnectionStringBuilder, tests a candidate asynchronously without modifying SQL, and saves via a temporary file followed by atomic replacement. A persistent reset marker removes remembered sign-in before restoration on the next launch. Failed removal leaves the marker intact and restoration suppressed. Appearance and current authenticated work remain unchanged until restart.

Missing/malformed configuration and missing schema are Configuration outcomes; unreachable SQL is a Database outcome. Both hide private session state. ILogger receives only categories and numeric SQL codes, never raw exceptions or connection payloads. Invalid user JSON blocks database fallback but leaves Settings available to explicitly repair/reset it.

Published builds disable trimming because EF Core runtime model reflection and constructor-based service creation require metadata. Release publishing exposed EF IL2026/IL2104 warnings with the previous template trimming setting; retaining metadata avoids shipping a build with those known runtime risks. Shutdown defers the final native close through DispatcherQueue after host stop/disposal, because reentrant WinUI Close calls are ignored.

Reference: [Microsoft Generic Host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host) and [Windows process package identity](https://devblogs.microsoft.com/insidemsix/is-this-a-packaged-process/).
