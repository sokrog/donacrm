# DONA CRM mobile implementation plan

## Selected direction

**Command Orbit** is the visual north star. The implementation may borrow one compact “next action” treatment from Focus Beam, but the hierarchy, density, navigation, and overall atmosphere remain Command Orbit.

## Historical preservation rules

- Keep `Dona.Crm.Web` working while extracting behavior; remove it after the shared clients reach functional parity.
- Treat existing domain models, repository contracts, services, tests, and Russian business terminology as the source of truth.
- Move code before rewriting it: extraction must preserve behavior and keep existing tests green.
- Target Android, iOS, and Windows in the MAUI host, plus WebAssembly/PWA in the browser host.
- Use `BlazorWebView`, Razor components, semantic HTML, CSS variables, and small focused JavaScript interop only where necessary.
- Add no CommunityToolkit package, large UI library, AI provider, telemetry service, or external data-sharing behavior.

## Phases

### 0. Decision gates

Choose the local storage strategy, the relationship to Google Sheets/Drive, and the amount of Razor UI shared with the web app. Record each choice in `DECISION-LOG.md` before scaffolding.

### 1. Extract platform-independent behavior

Create `Dona.Crm.Core` and move domain models, storage contracts, deterministic business rules, analytics, and platform-independent services into it. Keep web-specific implementations and endpoints in `Dona.Crm.Web`. Update namespaces and project references without changing behavior, then run the existing test suite.

Likely first extraction candidates:

- `Domain/*` models and display mappings;
- repository interfaces from `Storage/I*.cs`;
- calculations and workflow services that do not depend on `IWebHostEnvironment`, HTTP endpoints, or browser/server state;
- analytics, sale transitions, receiving, payments, returns, stock adjustment, search, status, currency, and numbering rules.

Web-specific code stays in place initially: JSON file paths based on `IWebHostEnvironment`, Google OAuth and Drive endpoints, static-file delivery, server component composition, and data-protection setup.

### 2. Establish the mobile shell

Create `Dona.Crm.UI` for shared Razor presentation and `Dona.Crm.App` as the .NET 10 MAUI Blazor Hybrid host. Establish Command Orbit design tokens, safe-area CSS, typography, inline SVG icon system, reduced-motion behavior, five-tab navigation, loading/offline/error surfaces, and the static Home hierarchy before wiring data.

Build Android and iOS targets. Deploy to the available simulator/emulator, capture screenshots, compare with `command-orbit.html`, and record intentional differences.

### 3. Complete the Home vertical slice

Wire real repositories and services into Home: time-aware greeting, revenue/profit signal, four compact KPIs, attention queue, quick actions, and recent activity. Implement loading, empty, offline, retry, error, long-text, large-type, and reduced-motion states. Validate all calculations through shared services rather than duplicating them in Razor.

### 4. Add workflows one slice at a time

Implement and verify in this order:

1. Catalog, variants, images, inventory, collections, and outfits.
2. Customers, sales, reservations, payments, and returns.
3. Purchases, receiving, suppliers, intermediaries, and landed costs.
4. Analytics, content plan, stock movements, backup, data connections, and settings under More.

Each slice includes business-rule tests, storage tests, responsive states, Android verification, iOS screenshot comparison, and accessibility checks.

### 5. Platform and local-first hardening

Add only approved native integrations: secure local credentials, file/photo selection, backup import/export, connectivity awareness, and protected Google connection flows. Test interrupted operations, restarts, partial sync, duplicate prevention, and recovery without silently changing authoritative records.

### 6. Release readiness

Run the full test suite, validate migrations and backups, profile startup and long lists, verify keyboard and screen-reader behavior, check Android/iOS safe areas, inspect final screenshots, and document every intentional departure from the Command Orbit mock.

## Next implementation plan

This plan continues the completed Command Orbit foundation. It supersedes the early constraints that limited the MAUI host to Android/iOS and required the old server-rendered Web UI to remain the long-term web application. The target product consists of the shared Blazor UI running as MAUI Blazor Hybrid on Android, iOS, and Windows, plus the same functional UI running as browser WebAssembly/PWA. The old Web project has been removed after the reusable behavior and tests were extracted.

### 7. Stabilize the shared UI

- Review every remaining route at phone, tablet, desktop browser, and Windows window widths.
- Finish the responsive alignment of Catalog, Marketing, Content Plan, and other dense screens.
- Standardize spacing between fields, validation messages, cards, sections, and sticky action areas through shared design tokens.
- Use shared styled components for autocomplete, modal dialogs, pickers, date pickers, confirmation prompts, notifications, and loading/empty/error states instead of browser-native controls where practical.
- Verify keyboard navigation, focus visibility, screen-reader semantics, reduced motion, and 200% text scaling.
- Add repeatable visual-regression routes for the most important responsive and state variants.

This phase is complete when shared components are used consistently and no supported viewport has unintended horizontal scrolling, clipped content, or bottom actions covering the end of a page.

### 8. Add unified Google authorization without an application broker

- Browser: use Google Identity Services and the browser OAuth flow.
- Android: use Google Play Services AuthorizationClient.
- iOS: use the system OAuth browser with PKCE and an application callback.
- Windows: use the system browser with PKCE and a loopback callback on `127.0.0.1`; do not depend on MAUI `WebAuthenticator`, which is not supported on Windows.
- Store refresh/access credentials only in platform secure storage. Never persist tokens in SQLite, browser local storage, logs, or exported backups.
- Replace technical connection settings with a guided flow: Google account, existing spreadsheet, Drive folder, permission check, and connection summary.
- Remove the broker URL and other broker-specific settings from the user-facing application.

### 9. Complete Google Drive photo support

- Let the user choose local upload, external image URL, or the connected Google Drive folder for each product image.
- Compress manually selected images before upload while preserving an acceptable catalog resolution and orientation.
- Show upload progress, retry failed transfers, cache thumbnails locally, and support replacement and deletion.
- Keep product saving local-first: temporary Drive unavailability must not discard the product or block later synchronization.

### 10. Connect an existing spreadsheet safely

- Discover and read the currently used visible sheets without requiring the user to recreate the spreadsheet.
- Show a migration preview with record counts, validation errors, unsupported values, and duplicates before changing cloud data.
- Create a backup before migration and create the private `_CommandOrbitSync` metadata sheet only after explicit confirmation.
- Prevent an empty or incomplete cloud snapshot from overwriting valid local data.
- Keep legacy sheets readable during the transition; archive or stop writing to them only through a separate, explicit action.

### 11. Implement synchronization v2 and conflict resolution

- Add record metadata: `Revision`, `UpdatedAt`, `UpdatedByDevice`, and deletion tombstones.
- Use an immutable operation journal for inventory, payments, returns, receiving, and other financially significant actions.
- Automatically merge changes to different records and independent fields of the same record.
- Present a clear comparison UI when two devices change the same field from the same base revision.
- Never use silent last-write-wins resolution for financial or inventory operations.
- Make synchronization idempotent and resilient to interruption, retries, duplicate delivery, and application restarts.

### 12. Add low-cost automatic synchronization

- Pull changes at application launch and resume, but no more often than once every five minutes unless the user requests it.
- Push local changes 45 seconds after the last edit while the application is online and in the foreground.
- Retry transient failures after approximately 1, 5, and 15 minutes without blocking local work.
- Keep explicit Compare and Sync actions for inspection and recovery.
- Show a compact status everywhere it matters: synchronized, pending changes, conflict, or error.

This polling and debounce model is the default because it avoids a continuously running custom broker and keeps Google API usage modest. Background and push-driven synchronization can be evaluated later only if real usage demonstrates a need.

### 13. Prepare the Windows release

- Verify Windows OAuth, secure storage, media picker, file import/export, external links, and window resizing.
- Finish Windows icons, splash assets, publisher identity, and package metadata.
- Produce and test an MSIX package, including clean install, update, data preservation, and uninstall behavior.
- Add a repeatable CI build for the Windows package alongside Android, iOS, and browser builds.

### 14. Cut over from the old Web application

- [x] Make the shared WebAssembly/PWA application the supported browser experience with functional parity for all retained workflows.
- [x] Extract portable Google Sheets synchronization, Google Drive images, backup contracts, and business regression tests.
- [x] Remove the old Web and Web test projects from the solution and repository.
- [x] Document the platform-specific Google OAuth setup without a broker, service account, or client secret.

### Recommended execution order

1. Unified Google authorization.
2. Google Drive photo workflow.
3. Existing spreadsheet discovery and migration.
4. Synchronization v2 and conflict resolution.
5. Low-cost automatic synchronization.
6. Windows MSIX production readiness.
7. Final old Web retirement. Completed.

## Visual acceptance rule

A phase is not complete until its simulator or emulator screenshot matches the intended hierarchy, spacing, contrast, touch reach, and information density. Build success alone is insufficient.

## Intentional implementation differences

- The production shell fills the real device viewport; the decorative phone frame and direction caption remain mockup-only review aids.
- Bottom navigation and desktop navigation are responsive variants of the shared Razor layout used by both MAUI and WebAssembly hosts.
- The first shell iteration uses representative DONA records only to validate hierarchy. Authoritative SQLite-backed values, loading, offline, empty, and error states belong to the Home vertical slice in phase 3.
- Android was visually verified on the Pixel 9 Pro emulator through DevFlow at both the top and bottom of the long Home screen. The iOS target compiles successfully, but an iPhone simulator screenshot remains pending until a Mac simulator is available to this Windows workspace.
