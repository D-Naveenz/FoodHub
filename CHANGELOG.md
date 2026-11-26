# Changelog

## Unreleased

## 0.2.0 — 2026-11-26

- Extracted reusable WinUI hosting, lifecycle, storage, dispatcher, theme and native window helpers into Dhara.Extensions.WinUI. Core targets Windows and consumes the shared infrastructure.
- Added automatic build-profile environments, read-only application JSON defaults and shared native LocalSettings for appearance/database overrides; removed legacy settings imports.
- Replaced the database console tool with PowerShell automation and direct Core EF tooling for explicit development targets.
- Displayed the local storage path as a selectable text label, added semantic database feedback severities, and replaced the bootstrap error text view with a native ContentDialog.

Release metadata is 0.2.0; Windows package, assembly and file versions are 0.2.0.0.

## 0.1.0 — 2026-11-24

Initial development milestone:

- WinUI navigation shell and separate login/registration window.
- Local accounts, encrypted remembered sessions, profiles, photos and saved addresses.
- Generic Host configuration and dependency injection, hosted window lifecycle, and packaged/unpackaged local storage.
- Persistent database settings and Development-only access to the storage folder.
- One initial EF Core migration and explicit disposable-development database reset tooling.

Menu, basket, checkout, tracking and order-history pages remain navigation skeletons.

Release metadata is 0.1.0; Windows package, assembly and file versions are 0.1.0.0. Git tagging is deferred until after the merge to main.
