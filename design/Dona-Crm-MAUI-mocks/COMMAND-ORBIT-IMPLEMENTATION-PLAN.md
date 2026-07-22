# DONA CRM mobile implementation plan

## Selected direction

**Command Orbit** is the visual north star. The implementation may borrow one compact “next action” treatment from Focus Beam, but the hierarchy, density, navigation, and overall atmosphere remain Command Orbit.

## Preservation rules

- Keep `Dona.Crm.Web` working throughout the migration.
- Treat existing domain models, repository contracts, services, tests, and Russian business terminology as the source of truth.
- Move code before rewriting it: extraction must preserve behavior and keep existing tests green.
- Target only `net10.0-android` and `net10.0-ios` in the MAUI host.
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

## Visual acceptance rule

A phase is not complete until its simulator or emulator screenshot matches the intended hierarchy, spacing, contrast, touch reach, and information density. Build success alone is insufficient.

## Intentional implementation differences

- The production shell fills the real device viewport; the decorative phone frame and direction caption remain mockup-only review aids.
- Bottom navigation is implemented in the shared Razor layout, while the existing Web layout and routes remain unchanged. Mobile and Web therefore share components without being forced into the same information architecture or visual design.
- The first shell iteration uses representative DONA records only to validate hierarchy. Authoritative SQLite-backed values, loading, offline, empty, and error states belong to the Home vertical slice in phase 3.
- Android was visually verified on the Pixel 9 Pro emulator through DevFlow at both the top and bottom of the long Home screen. The iOS target compiles successfully, but an iPhone simulator screenshot remains pending until a Mac simulator is available to this Windows workspace.
