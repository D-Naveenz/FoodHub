# Changelog

## Unreleased

- Added Core's custom desktop host builder with automatic build-configuration environments and detailed XML API documentation.
- Separated general JSON configuration from database setup; appearance and database overrides now share one atomic local-settings writer.
- Removed settings import/migration and application environment-variable overrides. Appearance applies immediately; database changes still require restart.

## 0.1.0 — 2026-11-24

Initial development milestone:

- WinUI navigation shell and separate login/registration window.
- Local accounts, encrypted remembered sessions, profiles, photos and saved addresses.
- Generic Host configuration and dependency injection, hosted window lifecycle, and packaged/unpackaged local storage.
- Persistent database settings and Development-only access to the storage folder.
- One initial EF Core migration and explicit disposable-development database reset tooling.

Menu, basket, checkout, tracking and order-history pages remain navigation skeletons.

Release metadata is 0.1.0; Windows package, assembly and file versions are 0.1.0.0. Git tagging is deferred until after the merge to main.
