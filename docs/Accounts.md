# Accounts and profile behavior

## Operations and interfaces

`AccountService` accepts a context factory and an optional TimeProvider for deterministic expiry tests. RegisterAsync/LoginAsync issue SessionTicket results; ValidateAsync restores/revalidates; LogoutAsync revokes. UpdateBasicsAsync, UpdatePhotoAsync, SaveAddressAsync and DeleteAddressAsync require a bearer token and derive customer ownership in SQL Server. No private operation accepts a caller-selected customer identity.

Results distinguish validation, invalid credentials, expiry, database failure and conflict. ProfileDetails contains safe display/contact/photo/address data, never password hashes. SessionTicket contains the raw token only for the application session coordinator; never log or serialize it as general profile data.

## Login and registration presentation

The owned account window opens at approximately 900 × 600 effective pixels, constrained to the current display. A fixed branded banner sits beside a Frame containing separate login and registration pages. The login submit button is followed by “New Customer? Register”, where Register is a HyperlinkButton with a click handler. Registration offers the matching “Already a customer? Log in” link. Native slide transitions move only the form page; both pages scroll when their content exceeds the available height.

There is no Cancel button; the native window close button dismisses the account window and restores its owner. Shared non-secret form state survives switching, while passwords are cleared before navigation and on close. Switching is disabled during submission, and form pages are not retained in navigation history.

## Validation

- Display name: required, up to 80 characters. Full name: required, up to 150. Phone: required, up to 32.
- Email: one syntactically valid address, up to 254 characters; trimmed and normalized case-insensitively for uniqueness. The mailbox need not exist. Email changes and email verification are deferred.
- Password: 12–128 characters at registration; exact matching confirmation. Spaces are preserved and values are never silently truncated.
- Address: label up to 40, line 1 up to 200, city up to 100, all required. Optional line 2 up to 200 and postal code up to 20. Existing addresses can be edited; referenced addresses cannot be deleted.
- Photo: selectable PNG/JPEG, at most 5 MB and 4096 pixels on either edge. Decode actual content rather than trusting the extension; normalize orientation and save a PNG at most 512 pixels on the longest edge. Picker cancellation makes no changes.

Names/phone and addresses are edited after registration. Photos are optional. Accounts cannot claim the two legacy seeded customer identities through registration. Password recovery/change, account deletion and staff authorization are deferred.

## Session lifecycle

Unchecked Stay signed in keeps a token only in memory (eight hours maximum); app restart starts as Guest. Checked saves a Windows LOCAL=user protected token for seven days. SQL contains the SHA-256 digest, never the bearer token. Reads never renew expiry. Corrupt/decryption-failed storage is treated as Guest.

SQL Server must validate a remembered token before displaying private data. During outages, private profile state is hidden and retry remains available. Invalid/revoked/expired tokens are cleared. SessionState checks expiry while the application remains open and discards workflow results after account changes. Each page/window cancels its own pending work when closed.

## Learning boundaries

This is local educational authentication using direct SQL Server access, not a remotely secured service. A production deployment would put authorization and database credentials behind a server. JWT is deliberately not used in this slice. This project neither calls Foodhub APIs nor sends email.
