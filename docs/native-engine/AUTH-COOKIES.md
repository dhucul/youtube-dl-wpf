# Native cookie imports

The C# engine imports Firefox and supported Chromium-family cookie records from real read-only SQLite snapshots on Windows. The unused byte-scanning placeholder has been removed. Exported Netscape cookie files also preserve their intended scope and expiry. No Python, yt-dlp, browser automation, or additional SQLite package is used. Chromium decryption uses standard Windows current-user data protection and .NET cryptography.

## Firefox selection and isolation

- `firefox` locates a Windows profile from Firefox's registered profile and installation metadata. Existing installation defaults take priority, followed by declared profile defaults. Multiple defaults or a fallback profile list are ordered by cookie-database modification time with a deterministic path tie-break.
- `firefox:Profile Name` selects a registered name or profile folder name. A full directory or `cookies.sqlite` path selects that source directly, without searching other profile directories. Windows paths and spaces are preserved.
- `firefox:Profile::Work` or `firefox:Profile::2` selects one named or numeric Firefox container. An omitted container, or `::none`, selects the default container only. An ambiguous/missing name fails. Localized built-in names without a stored name require the numeric ID.
- Other containers are not merged into that HTTP session. `::all` fails because a single cookie jar cannot preserve multiple accounts with the same host/name/path.
- Partition keys, first-party isolation, private-browsing attributes, unrecognized origin isolation and the explicit partitioned-cookie flag are excluded. Skipped isolated or malformed cookies produce count-only diagnostics. No cookie names or values appear in those messages.

The reader queries the real `moz_cookies` table and validates its columns. It retains active values, host-only/domain scope, paths, secure and HttpOnly flags, and Unix expiration times. Expired rows are omitted. The browser's in-memory/session-store-only cookies are not imported.

Windows' serviced `winsqlite3.dll` is loaded from the system directory. A read-only connection and one read transaction preserve a consistent snapshot, including committed changes in an existing write-ahead log. The reader does not copy cookie files into temporary folders, change journal modes, or write rows. SQLite can participate in its normal shared-memory locking protocol. Lock contention is bounded to 500 ms; query work is canceled or stopped at its time limit. Cookie source preparation runs outside the WPF dispatcher.

Current bounds are 256 MiB each for the database and WAL, 50,000 stored rows, bounded schema/field sizes, and a ten-second query budget. The imported jar has enough capacity to retain every accepted row instead of silently applying the default twenty-cookie per-host limit. Missing, malformed, oversized or locked databases fail explicitly.

## Chromium-family profiles

`chrome`, `edge`, `brave`, `chromium`, `opera` and `vivaldi` resolve their standard Windows profile roots. A suffix such as `chrome:Profile 1` accepts a profile folder or a unique display name. An absolute profile directory, user-data root or `Cookies` database path can also be selected. `Network/Cookies` is preferred during profile discovery, while an explicit database path is honored exactly.

Default selection uses the recorded last-used profile, then the `Default` folder, or a sole available profile. Missing recorded defaults and ambiguous selections fail instead of choosing another account. Both parent user-data `Local State` files and Opera-style colocated state are recognized. Chromium selectors do not accept Firefox container syntax.

Supported cookie values include plaintext records, legacy Windows DPAPI records, and Windows `v10` AES-256-GCM records whose key is stored under `os_crypt.encrypted_key` with a DPAPI prefix. The key is unwrapped by Windows for the current user; keys and decrypted byte buffers are cleared after use. GCM authentication failures and decryption failures terminate the import without returning a partial jar. Schema 24's decrypted host digest is checked before its value can enter the jar. Windows-epoch microsecond expiration times retain their precision; session and empty values remain valid.

Application-bound `v20` and unknown encrypted formats return an explicit unsupported-format error with the existing cookies-file export path. No elevation service, browser injection, application impersonation, alternate account, or client rotation is attempted. Partitioned records remain excluded with a diagnostic, and conflicting source-bound identities are refused. Tested Chromium database versions are 23 and 24; older matching schemas have bounded parsing, while future versions are explicitly unsupported.

## Exported cookie files

The reader supports Netscape tab-separated records, including `#HttpOnly_` rows, host-only versus subdomain flags, secure/path restrictions, session expiry zero and empty values. Already expired entries are discarded; they cannot become session cookies again. Invalid records fail with a line number and no authentication value. Input is bounded to 16 MiB and 50,000 records.

A selected cookies file takes priority over browser import, matching the app's existing argument behavior. A missing selected file fails instead of falling back to browser cookies or an anonymous request. `none` is recognized without case sensitivity. Unimplemented browser/keyring choices still fail before HTTP access when no file takes precedence.

Imported cookies and subsequent website `Set-Cookie` responses share the same native HTTP session across discovery, selected playlist entries and media transfer. Import limitations appear in native inspection warnings and download logs. The migration adapter preserves analysis overrides and per-download cookie choices; the production WPF backend has not switched yet.

## Verification

On October 8, 2026, fifteen deterministic import checks used isolated fixture files and databases. They cover scope/flags/expiry, empty values, 400 cookies on one host, malformed and missing inputs, profile/default/name/path selection, container separation, partition refusal, SQLite schema/corruption checks, locking, cancellation, oversized counts and real local HTTP discovery/download behavior.

A fixture with an open SQLite writer exposed a committed uncheckpointed value through the native reader; database and WAL bytes were unchanged afterward. A loopback HTTP exchange received imported credentials during analysis and transfer, retained a newly issued session cookie, and never received the excluded partitioned credential. The WPF adapter also has a regression check for authentication mapping. No personal browser profile or real account cookies were read during this work; live signed-in site verification remains pending.

Eight additional Chromium checks generate their own Windows-protected keys and encrypted cookie records. They verify profile selection across all six browser names, exact database paths, microsecond expiry and flags, legacy DPAPI decryption, authenticated AES-GCM, schema-24 host binding, tampering and unsupported-format failures, encrypted WAL updates, conflicting identities and schema limits. A real loopback download receives the decrypted fixture credential; a second download uses an explicit cookie file despite an unsupported browser setting, proving file precedence.

## Remaining work

Application-bound browser workflows, additional encrypted formats/platforms/schemas, partition- and source-bound HTTP semantics, Firefox session-store-only cookies, richer container selection, complete upstream option semantics and authenticated live-site verification remain unfinished. Full authentication parity and production cutover remain pending.

Primary references: [Mozilla cookie storage schema](https://github.com/mozilla/gecko-dev/blob/master/netwerk/cookie/CookiePersistentStorage.cpp), [Firefox profiles](https://support.mozilla.org/en-US/kb/profiles-where-firefox-stores-user-data), [SQLite read-only WAL behavior](https://sqlite.org/wal.html), and [Microsoft's Windows SQLite interface](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/winsqlite/winsqlite3.h).

Chromium references: [Windows key format](https://github.com/chromium/chromium/blob/main/components/os_crypt/async/browser/dpapi_key_provider.cc), [cookie storage and host binding](https://github.com/chromium/chromium/blob/main/net/extras/sqlite/sqlite_persistent_cookie_store.cc), and [Windows data unprotection](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptunprotectdata). The extraction and import code is independent C#.
