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
- **Selection:** Pending user decision.
- **Recommendation:** Share focused Razor components and design tokens through `Dona.Crm.UI`, while keeping separate navigation shells. This preserves the web app and avoids forcing desktop information architecture onto mobile.
- **Constraints:** Shared components must remain medium-appropriate; mobile and web shells may compose them differently.

## D-005 — Version-control workflow

- **Question:** How should the MAUI Blazor Hybrid work be isolated and recorded?
- **Selection:** Work in a dedicated branch and create thematic commits throughout implementation.
- **Branch:** `CommandOrbit-maui-blazor-hybrid` (Git-compatible normalization of “CommandOrbit maui blazor hybrid”).
- **Constraints:** Each commit should represent a coherent, verified change. Unrelated user work must not be included.
