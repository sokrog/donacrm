# DONA CRM — .NET MAUI Blazor Hybrid build prompt

I want to turn the existing **DONA CRM** solution in this repository into a polished **.NET MAUI Blazor Hybrid** app for iOS and Android. DONA CRM is a local-first CRM for a clothing and accessories business: product catalog, variants, inventory, purchasing from China, suppliers and intermediaries, customers, sales, returns, payments, collections, outfits, Instagram content planning, and business analytics.

Keep the visual language inspired by [MAUIverse](https://mauiverse.net): a refined cosmic dark theme with deep violet backgrounds, nebula-like purple and magenta light, restrained cyan highlights, subtle star fields, translucent glass surfaces, luminous gradients, and crisp typography. This is a DONA CRM product, not a MAUIverse clone: retain the **DONA** name, business content, information hierarchy, and practical CRM focus while borrowing MAUIverse's atmosphere and polish. Read MAUIVerse website to understand styles.

Before starting, inspect the existing repository, especially `README.md`, `PLAN.md`, the domain models, services, storage abstractions, Razor components, and tests. Treat the current behavior and data model as the source of truth. Also read the latest [.NET MAUI 10 documentation](https://learn.microsoft.com/en-us/dotnet/maui/?view=net-maui-10.0) and [.NET MAUI Blazor Hybrid documentation](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/?view=aspnetcore-10.0) so the implementation uses current APIs and recommended patterns.

## Platform and technology constraints

- Build a .NET 10 MAUI Blazor Hybrid app targeting **`net10.0-ios`** and **`net10.0-android`** only.
- Do not target Windows or Mac Catalyst.
- Use `BlazorWebView` and Razor components for the application UI, with native MAUI APIs only where they add real platform value.
- Preserve the existing business rules and repository abstractions. Reuse or extract existing domain models and services instead of creating a second, incompatible implementation.
- Prefer a structure such as:
  - `Dona.Crm.Core` — domain models, business rules, storage contracts, shared services, and platform-independent logic.
  - `Dona.Crm.UI` — reusable Razor components, layouts, design tokens, and shared UI behavior if sharing with the web app remains useful.
  - `Dona.Crm.App` — the MAUI Blazor Hybrid host, native platform integrations, resources, and app composition.
  - Keep `Dona.Crm.Web` working unless a migration step explicitly replaces it.
- Do not use `CommunityToolkit.Mvvm`, `CommunityToolkit.Maui`, or any other CommunityToolkit package.
- Do not introduce a large component library merely to obtain a theme. Build the visual system with Razor, semantic HTML, scoped CSS, CSS variables, and small focused JavaScript interop only when necessary.
- Do not use emojis anywhere in the UI. Use clean inline SVG icons, generated SVG assets, small brand-colored badges, or letter initials. If an icon is missing, create an SVG rather than substituting an emoji.
- Keep credentials and tokens out of source control. Existing Google Sheets and Google Drive secrets must remain local and protected.
- Design for intermittent connectivity and local-first use. Loading, empty, offline, retry, validation, and error states are required parts of each screen.

Use the **maui-skills** and **maui-devflow** tooling from [dotnet/maui-labs](https://github.com/dotnet/maui-labs). After every meaningful UI change, build the relevant projects, deploy to an iPhone simulator, capture a devflow screenshot, inspect it visually, and compare it with the chosen mockup. A successful build is not proof that the UI is correct. If the screenshot looks wrong, explain what is wrong and iterate before continuing. Also verify Android at sensible milestones so the design does not accidentally become iOS-only.

## Product structure

The mobile navigation should make the highest-frequency store workflows immediately reachable. Start with a bottom tab bar containing:

- **Home** — daily overview, alerts, quick actions, recent activity, and business health.
- **Catalog** — products, variants, collections, outfits, categories, and inventory.
- **Sales** — customers, sales, payments, returns, and reservations.
- **Purchases** — purchase orders, receiving, suppliers, intermediaries, and landed costs.
- **More** — analytics, content plan, stock movements, backup, data connections, and settings.

Do not force the desktop web navigation into a narrow mobile shell. Use mobile-native information hierarchy, comfortable touch targets, safe-area handling, bottom sheets or dialogs where appropriate, and focused forms that progressively reveal advanced fields. Preserve access to every important existing workflow even if secondary pages live under **More**.

## Visual direction

The app should feel like a premium modern operations app viewed through the MAUIverse aesthetic—not a recolored Bootstrap admin dashboard and not a Hello World template.

Use a deep near-black violet canvas, layered nebula glows, restrained glass panels, fine translucent borders, magenta-to-purple accents, and sparing cyan for informational states. Maintain excellent readability and accessible contrast. Decorative effects must never compete with prices, quantities, statuses, warnings, or primary actions. Motion should be subtle, purposeful, and respect reduced-motion preferences.

The Home screen should include:

- a compact DONA brand mark in the upper-left and a personalized, time-aware greeting;
- a prominent overview card showing today's revenue, profit, or the most important current business signal;
- a concise KPI row for sales, available stock, incoming purchases, and outstanding customer debt;
- an **Attention needed** area for low stock, unpaid sales, overdue or expected purchases, products without photos, and other actionable warnings;
- quick actions for adding a product, creating a sale, creating a purchase, and adjusting stock;
- recent activity with clean status badges, timestamps, amounts, and compact product imagery;
- believable content derived from the existing domain model and demo data rather than generic lorem ipsum.

Use inline SVG icons with a consistent stroke, optical size, and active/inactive treatment. Avoid excessive rounded cards, glowing outlines on every surface, ornamental circles with no meaning, and low-contrast icons. The cosmic styling should come from composition, light, depth, and color—not visual clutter.

## How to work

Before writing or changing a single line of MAUI or Blazor application code, produce **three distinctly different visual directions** as standalone HTML mockups of the mobile Home screen. They must differ meaningfully in composition, density, typography, hierarchy, navigation treatment, and use of the cosmic visual language—not merely in accent color.

These are starting points, not rigid templates. Make each option a credible product direction while keeping all three recognizably DONA and MAUIverse-inspired.

Create the mockups in a repository-local folder named `design/Dona-Crm-MAUI-mocks/` so they remain easy to review in this workspace. Each mockup must:

- be a self-contained `.html` file that opens cleanly in a browser;
- use a realistic iPhone-sized viewport while remaining responsive;
- show its direction name and a one-line design rationale at the top;
- include the same representative DONA CRM data so comparisons are fair;
- include the Home screen, bottom navigation, important states, and enough vertical content to judge scrolling rhythm;
- use no external framework and no emoji;
- use embedded or local CSS and SVG assets so it does not depend on a network connection.

Also create an `index.html` gallery that presents all three options with clear names and links to open each at full size. Open the gallery for me, summarize the three options and their tradeoffs, and then **stop and wait for me to choose one**. Do not scaffold the MAUI app, alter existing production UI, or begin implementation before I make that choice. The selected mockup becomes the visual north star.

After I choose a direction:

1. Write a short phased implementation plan that preserves current behavior and calls out any required extraction from `Dona.Crm.Web`.
2. Establish design tokens and the app shell first, then implement one complete vertical slice at a time.
3. Build, deploy, screenshot, inspect, and self-critique after each meaningful visual phase.
4. Test business logic and storage behavior in addition to visual verification.
5. Check empty, loading, offline, validation, error, and long-content states—not just the happy path.
6. Keep a concise record of intentional differences from the chosen mock and why they were necessary.

If I later push back on a visual decision—for example, an awkward shape, invisible icon, poor card hierarchy, or overly dense form—do not immediately commit to the first fix. Produce **three to five small standalone HTML alternatives** focused on that decision, let me choose, and then implement the selected alternative.

Do not call a phase finished if the simulator screenshot does not match the intended hierarchy, spacing, contrast, and polish. Be specific and honest when something still looks wrong, then iterate. Treat visual refinement, responsive behavior, and platform verification as core implementation work.

Start now by reading the repository and current documentation, then create the three Home-screen mockups and their gallery. **Do not scaffold or modify the application yet.**

## Proactive AI enhancement discovery

Act as both an implementation partner and an AI product strategist.

While reviewing the repository and designing each workflow, proactively identify useful AI-powered enhancements that I have not explicitly requested. Look for opportunities that could reduce repetitive work, improve decisions, detect risks, or make the CRM easier to operate.

Potential areas include:

- product descriptions, categorization, tagging, and image analysis;
- demand forecasting and low-stock predictions;
- sales, profit, and customer insights;
- anomaly detection for costs, margins, inventory, and payments;
- supplier and purchasing recommendations;
- content-plan ideas, captions, and scheduling assistance;
- natural-language search and reporting;
- data cleanup, duplicate detection, and assisted imports.

Do not implement an AI feature merely because it is technically possible. For every proposed enhancement:

1. Explain the user problem it solves.
2. Describe where it fits into the existing workflow.
3. State what data it needs.
4. Identify privacy, cost, latency, connectivity, and reliability implications.
5. Distinguish deterministic business logic from probabilistic AI output.
6. Describe the non-AI fallback.
7. Recommend whether it belongs in the MVP, a later phase, or should be rejected.

## Decision interviews

Before making a product, architecture, data, privacy, or design decision that has meaningful tradeoffs, interview me.

Ask focused questions in small groups rather than presenting a long questionnaire. Challenge vague or contradictory answers respectfully. If my answer creates a likely usability, security, cost, or maintenance problem, explain the concern and ask me to confirm the tradeoff.

For each important decision:

- present 2–4 concrete options;
- recommend one option and explain why;
- identify the consequences of each option;
- wait for my answer before implementing anything difficult to reverse.

Do not ask questions whose answers can be discovered reliably from the repository, documentation, or existing application behavior.

Maintain a decision log containing:

- the question;
- the options considered;
- my selection;
- the rationale;
- resulting implementation constraints.

## AI implementation gate

AI ideas are proposals until I explicitly approve them.

Do not add an AI SDK, external model provider, cloud dependency, vector database, telemetry service, or new data-sharing behavior without my approval. Do not send business, customer, product, image, credential, or financial data to an external AI service unless I have explicitly approved the provider and data policy.

Prototype approved AI interactions as standalone HTML mockups before integrating them into the application. Clearly label generated content, allow users to edit or reject it, and never let probabilistic output silently change inventory, prices, payments, orders, or other authoritative business records.