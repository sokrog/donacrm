# DONA CRM decision log

## D-001 — Mobile visual direction

- **Question:** Which Home-screen direction should guide the MAUI Blazor Hybrid app?
- **Options considered:** Command Orbit, Editorial Nebula, Mission Control, Pulse Ledger, Focus Beam, Merchandise Cosmos.
- **Selection:** Command Orbit.
- **Rationale:** It offers the best balance between low cognitive load and immediate access to financial signals, operational warnings, quick actions, and recent activity.
- **Constraints:** Keep the Command Orbit hierarchy and restrained density. A compact “next action” cue may be borrowed from Focus Beam, but the app must not become a single-task-only interface.

## D-002 — Local authoritative storage

- **Question:** What should be the authoritative on-device store?
- **Options:** SQLite; JSON files; staged JSON-to-SQLite migration.
- **Selection:** SQLite.
- **Rationale:** Structured migrations, indexed queries, transactional updates, and concurrent workflows fit the CRM domain better than growing JSON snapshots.
- **Constraints:** Existing repository contracts and backup semantics must be preserved. SQLite remains behind repository abstractions so business rules and Razor components do not depend directly on the database library.

## D-003 — Google Sheets relationship

- **Question:** Should Google Sheets remain primary storage, become an explicit synchronization target, or be limited to import/export?
- **Options:** local-first with explicit sync; automatic two-way sync; Google-primary with offline cache; import/export only.
- **Selection:** Local-first with explicit user-visible synchronization.
- **Rationale:** The user asked to continue with the recommended approach after selecting SQLite. Explicit synchronization is easier to reason about offline, conflicts, costs, credentials, and data ownership than hidden background writes.
- **Constraints:** No hidden data transfer. Sync failures must never partially apply authoritative inventory, payment, sale, or purchase changes.

## D-004 — Razor UI sharing

- **Question:** How much UI should the web and mobile apps share?
- **Options:** shared `Dona.Crm.UI` components plus platform shells; shared Core only with separate UIs; replace the web UI.
- **Selection:** Shared `Dona.Crm.UI` components plus separate MAUI and Web hosts.
- **Rationale:** The user selected option 1 and clarified that the solution must run on iPhone, Android, and the web while preserving the functionality of the existing browser application.
- **Constraints:** `Dona.Crm.App` hosts the shared Razor UI in `BlazorWebView` on Android, iOS, and Windows. `Dona.Crm.Web.Client` hosts the same functional UI as WebAssembly/PWA in the browser. The former server-rendered `Dona.Crm.Web` host was removed after parity extraction.

## D-005 — Version-control workflow

- **Question:** How should the MAUI Blazor Hybrid work be isolated and recorded?
- **Selection:** Work in a dedicated branch and create thematic commits throughout implementation.
- **Branch:** `CommandOrbit-maui-blazor-hybrid` (Git-compatible normalization of “CommandOrbit maui blazor hybrid”).
- **Constraints:** Each commit should represent a coherent, verified change. Unrelated user work must not be included.

## D-006 — Mobile Google connection

- **Question:** How should the iOS and Android app connect to the same Google Sheets document and Google Drive account as the Web application?
- **Options:** one Google OAuth account for Sheets and Drive; copy the Web service-account JSON plus separate Drive OAuth; route all access through a hosted DONA backend.
- **Selection:** One Google OAuth account for Sheets and Drive, with the existing spreadsheet selected by URL or ID.
- **Rationale:** The user can grant the signed-in account editor access to the existing spreadsheet without placing a service-account private key on the phone. One account also reduces setup steps.
- **Constraints:** SQLite remains authoritative and synchronization is explicit. Browser authorization uses Google Identity Services, Android uses Google Play Services AuthorizationClient, iOS uses the system browser with PKCE and an application callback, and Windows uses PKCE with a loopback callback. No DONA authorization broker or user-entered server address is part of the architecture. Tokens are stored only in platform secure storage. Access to a pre-existing folder entered by ID requires the restricted `drive` scope; `drive.file` would require adding Google Picker to explicitly share that folder with the app.

## D-007 — First synchronization direction

- **Question:** Which data-transfer direction should be enabled first after previewing SQLite and Google Sheets?
- **Options:** Google Sheets to SQLite; SQLite to Google Sheets; both directions immediately.
- **Selection:** Google Sheets to SQLite first.
- **Rationale:** It unlocks the user's existing table on mobile without risking partial writes across the authoritative product, purchase, sale, payment, and inventory sheets.
- **Constraints:** The app fetches a complete remote snapshot, compares counts by business section, checks that its fingerprint has not changed, and then replaces all local collections in one SQLite transaction. Upload to Google remains unavailable until a durable operation journal can make multi-sheet writes recoverable.
