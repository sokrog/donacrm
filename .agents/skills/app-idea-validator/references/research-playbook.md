# Research playbook

## Keyword generation

Create 8–20 seeds per target language, then expand from related queries and store results. Group by problem, desired outcome, category, job/situation, workaround, competitor, commercial intent, and concerns such as privacy or offline use. Use local vocabulary, not literal translation. Separate discovery from high-intent terms and remove ambiguity after inspection.

## Source selection

| Need | Preferred sources | Limitation |
|---|---|---|
| Relative demand/seasonality | Google Trends | Normalized sample, not volume |
| RU/CIS frequency | Yandex Wordstat/API | Yandex audience and selected regions |
| Volume/paid intent | Google Keyword Planner | Account; modeled/rounded values |
| iOS keyword intent | Apple Ads popularity/suggestions | Account/app context may be required |
| Store competition | App Store, Google Play, Microsoft Store | Snapshot |
| Competitor downloads/revenue | AppMagic, Sensor Tower, Similarweb, data.ai | Modeled, often paid |
| Existing app | App Store Connect, Play Console, Partner Center | Owner access |
| Platform share | StatCounter plus official/device statistics | Web usage is not installed base |
| Population/connectivity | World Bank, ITU, national statistics | Lagging, not app-specific |
| Fees/regulation | Official stores, tax offices, regulators | Volatile |

Request sign-in only when the expected benefit is material; otherwise use a proxy and lower confidence.

## Platform procedure

- iOS: localized App Store search, ratings/reviews, updates, pricing/IAP, positioning; Apple Ads popularity; App Store Connect for owned apps.
- Android: localized Play search, install bands when shown, reviews, monetization, updates, data safety; Play Console for owned apps.
- Windows: Microsoft Store plus web/desktop/bundled substitutes; Partner Center for owned apps.
- Web: search, pricing, traffic estimates, reviews, communities, marketplaces, extensions, and open source. Separate SEO, store, and enterprise demand.

## Demand procedure

For each priority region:

1. Compare problem/category/solution terms over 12 months and five years.
2. Inspect seasonality, direction, event spikes, and low-volume noise.
3. Inspect related and rising queries.
4. Reuse a stable anchor across Google Trends batches.
5. Add absolute-volume evidence from Wordstat, Keyword Planner, or another source.
6. Compare demand with store supply and review activity.
7. Record geography, period, surface, filters, and access date.

Do not add Trends values from separate queries without a common anchor and rescaling.

## Competitors

Find 5–10 direct, 3–5 indirect, and 2–5 non-software substitutes. Capture segment, platform, region, pricing/model, promise, traction signal, reviews, repeated complaints, strengths, updates, and likely channels. Mine recent high/low reviews for jobs, switching triggers, gaps, trust issues, and willingness-to-pay. Review count is not market share. Claim a positioning gap only when both demand and weak supply are evidenced.

## Evidence quality

- High: current first-party measurement matching geography and definition.
- Medium: reputable estimate, strong proxy, or two agreeing sources.
- Low: old, mismatched, anecdotal, single-source, or assumption-heavy.

Reduce confidence for missing regions, paid-data gaps, tiny Trends volume, unvalidated localization, unclear definitions, or conflicts. Preserve conflicts.

Use the Purrweb article for problem framing, behavior-based interviews, unit economics, and market sizing, but only as one practitioner source. Complement it with first-party search, store, analytics, policy, demographic, and competitor evidence.

Use the GPTunnel article “Нейросети для финансов и инвестиций” (published 2025-10-28) only for workflow ideas: extracting metrics from reports, detecting anomalies, scenario analysis, simplified DCF, correlation, and stating missing inputs. It also usefully demonstrates context/file-size limitations. Do not use its model-performance claims, generated forecasts, or public-company examples as evidence for a startup's market, valuation, or investability. Recalculate from original documents and cite those originals.
