---
name: app-idea-validator
description: Validate and compare potential iOS, Android, Windows, and web application ideas through an adaptive interview, document intake, current market and keyword research, competitor and customer-evidence analysis, regional scoring, TAM/SAM/SOM, scenario-based unit economics, investor-readiness analysis, and a standalone traceable HTML report. Use when evaluating profitability, choosing platforms or regions, preparing a pre-MVP business case or investor pitch, auditing the evidence behind market or financial claims, researching through Google Trends, Yandex Wordstat, app stores, keyword tools, or app-intelligence services, or comparing app concepts.
---

# App Idea Validator

Produce a decision aid, not a promise of success. Separate facts, third-party estimates, calculations, and assumptions. Never invent unavailable metrics. Make every material number traceable.

## User-facing usage

Read [references/usage-ru.md](references/usage-ru.md) when the user asks how to start, install, invoke, provide inputs, or work through the interview. When invoked without enough idea context, give a brief usage explanation and then begin with the first question batch. Do not require the user to prepare JSON; the agent owns research, analysis data, validation, and HTML generation.

## Workflow

### 1. Frame the decision

Identify whether the user wants to validate one idea, compare ideas, choose regions/platforms, or revise an existing product. Reuse information already provided.

Run an adaptive interview in batches of at most five questions. Start with:

1. What problem does the app solve, and what does the user do today instead?
2. Who has this problem, how often, and in what situation?
3. Which platforms, countries, and languages are initially in scope?
4. What is the intended business model and plausible price?
5. What evidence, assets, team, budget, and deadline already exist?

Then ask only material follow-ups about differentiation, distribution, retention, buyer versus user, data/regulatory constraints, integrations, and success criteria.

For each uncertain question offer three paths naturally: answer from experience; let the agent research and use a labeled proxy; or skip it with lower confidence. Do not block on nonessential gaps. Summarize the brief and important assumptions before research.

Inventory available inputs before research. Ask for relevant files or links without demanding documents that do not exist:

- industry reports and datasets;
- competitor matrix, pricing, market-share evidence, reviews, and rejection reasons;
- interview transcripts, survey results, customer quotes, Personas, and JTBD;
- financial model, pricing/monetization strategy, cohort or funnel data;
- current pitch deck or business-plan text;
- MVP specification, product analytics, roadmap, and milestones;
- founder/team profiles, fundraising target, use of funds, legal/IP constraints.

Mark each item as provided, researched, missing, skipped, or not applicable, and state how each gap affects confidence.

### 2. Research

Read [references/research-playbook.md](references/research-playbook.md) and [references/evidence-standard.md](references/evidence-standard.md). Generate a localized keyword map covering problem, outcome, category, job-to-be-done, alternatives, competitors, and commercial intent.

Prefer sources in this order:

1. purpose-built connector or official API;
2. first-party store, product, advertising, statistics, or regulatory source;
3. authenticated browser session when sign-in is needed;
4. reputable research or app-intelligence provider;
5. labeled proxy or inference.

Use current web research for volatile facts. Record title, publisher, URL, publication/update date, access date, observation period, geography, exact page/table/section, metric definition, relevance, and limitation. If no publication date is visible, say so and record the reason; never substitute the access date. Cross-check consequential claims when practical. Never treat Google Trends' 0–100 index as volume or modeled competitor revenue as audited actuals. Do not bypass CAPTCHAs, access controls, or site terms.

### 3. Analyze

Mark material inputs as `observed`, `estimated`, `calculated`, or `assumed`. Evaluate problem strength; demand and intent; audience; direct and substitute competition; differentiation; platform and regional fit; monetization; acquisition; retention; feasibility; scope/costs; and legal/privacy/store-policy risk.

For every material number, percentage, date-dependent factual claim, score, benchmark, market size, cost, price, conversion, or forecast:

1. provide a nearby evidence reference;
2. state definition, unit, geography, and as-of period;
3. show formula and named inputs when calculated;
4. explain the proxy or assumption when not observed;
5. explain why the source is relevant;
6. include publication/update date and access date;
7. assign confidence and note conflicting evidence.

Do not cite a home page or generic article when a specific table, report, help page, listing, or dataset is available. A link is not proof unless it supports the exact claim.

Score 0–100:

| Dimension | Weight |
|---|---:|
| Problem strength | 15 |
| Demand evidence | 15 |
| Market potential | 10 |
| Competitive opening | 10 |
| Monetization | 15 |
| Acquisition viability | 10 |
| Retention potential | 10 |
| Delivery feasibility | 10 |
| Regulatory risk | 5 |

For competitive opening and regulatory risk, higher means more attractive. Explain scores. Adjust weights only when necessary and disclose it.

Keep confidence separate. Base it on coverage, freshness, geographic match, source quality, and agreement. Suggested verdicts: 75–100 validate an MVP; 60–74 promising but resolve named gaps; 40–59 run cheap tests first; below 40 do not build in the current form. Surface kill criteria separately.

### 4. Model market and economics

Read [references/financial-model.md](references/financial-model.md). Build conservative, base, and optimistic scenarios over at least 12 months. Show formulas and inputs for TAM/SAM/SOM, revenue, fees, refunds, variable costs, gross margin, CAC, LTV, burn, runway, and break-even where applicable.

Use ranges and avoid false precision. Verify current taxes, fees, and policies. Rank regions using demand, reachable audience, monetization proxy, competition, platform fit, localization, acquisition, and legal/operational friction. High search interest alone does not imply high revenue.

### 5. Prepare investor-ready analysis

When the user wants a pitch, fundraising case, or investor-ready report, read [references/investor-readiness.md](references/investor-readiness.md). Add company purpose, problem, solution, why now, market, alternatives, business model, traction, go-to-market, defensibility, team, financials, roadmap, vision, fundraising ask, use of funds, and milestones.

Do not manufacture traction, customer quotes, team credentials, partnerships, or market share. Distinguish a researched market narrative from proof that the startup has achieved it.

### 6. Recommend validation

Propose the smallest experiments that can disprove key assumptions: behavior-based interviews, landing page, capped keyword/ad smoke test, concierge/no-code prototype, pricing test, permitted store-page test, or retention cohort. State hypothesis, audience, method, metric, threshold, duration, approximate cost, and stop/continue rule.

### 7. Deliver HTML

Read [references/input-schema.md](references/input-schema.md). Populate JSON and run:

```powershell
python scripts/generate_report.py analysis.json output.html
```

Use an absolute script path outside the skill directory. Final reports must pass the generator's strict evidence validation. Deliver a standalone offline HTML file containing verdict/confidence; document coverage; product, Personas, JTBD, and customer quotes; demand/keywords; competition and review evidence; region ranking; TAM/SAM/SOM; three scenarios and projection; costs; investor narrative, team, roadmap, ask/use of funds; risks, assumptions, sensitivity and gaps; MVP/experiments; evidence ledger; and clickable source register.

Inspect for missing sections, broken HTML, non-finite numbers, and unsupported claims. Render or screenshot when tools allow. Return a clickable absolute path plus the decision, strongest evidence, largest uncertainty, and next action.

## Guardrails

- Research before profitability claims.
- Cite every material number and claim; preserve formulas and source metadata.
- Reject a final report when a material number lacks provenance.
- Say when authenticated or paid data would materially improve confidence.
- Never expose credentials, cookies, sessions, or proprietary data.
- Treat results as scenario analysis, not financial advice.
- Compare ideas with identical weights, horizon, currency, and evidence standards.
