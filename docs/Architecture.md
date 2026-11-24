# Architecture and learning guide

## Current implementation

FoodHub is an educational WinUI 3 customer application. The main window provides Home, Menu, Basket, Orders, Profile, About and Settings navigation. Ordering pages are clearly labeled skeletons. Accounts, sessions, editable contact details, addresses and profile-photo selection are implemented; ordering is not.

The account window is a separate owned native window. Its owner is disabled until it closes. The 900 × 600 effective-pixel account window hosts separate LoginPage and RegistrationPage views in a Frame. Click-handled HyperlinkButtons switch forms with native horizontal slide transitions while the red image banner remains fixed. Each page scrolls independently; the native close button dismisses the window. Profile stays in the main window. A `PersonPicture` shows the supplied guest image, a saved account photo, or initials derived from display name.

## Responsibilities and data flow

```text
WinUI page/window -> observable view model -> AccountService -> short-lived EF context -> SQL Server
                <- shared SessionState <- safe result/profile projection <- persisted state
```

`ApplicationServices` explicitly composes services on the UI thread. `CommunityToolkit.Mvvm` provides `ObservableObject`, `RelayCommand` and `AsyncRelayCommand`; observable properties use explicit readable setters. No service locator package, generic repository, or third data project is required.

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

Database operations are asynchronous and use a short-lived context. Do not block the UI with `.Result`/`.Wait()` or share a context concurrently. `FOODHUB_CONNECTION_STRING` supplies runtime and design-time configuration; externally supplied DbContextOptions take precedence. No migrations run during app startup.

## Ordering work remains deferred

Keep the basket in memory when that milestone is implemented. Scope order reads/writes to the authenticated customer. Use the retained `PlaceOrder` and `UpdateOrderStatus` contracts for order writes; SQL Server owns authoritative prices, receipt snapshots and status transitions. Store timestamps in UTC and display them in the user's local time. Delivery remains explicitly simulated.

See [Database](Database.md) for migration ownership, [Accounts](Accounts.md) for operation/validation details, and [Verification](Verification.md) for actual checks and remaining manual scenarios.
