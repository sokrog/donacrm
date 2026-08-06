# Evidence and numeric provenance standard

## Source record

For every external source capture:

- stable source ID;
- exact title and publisher/author;
- direct URL to the supporting page, table, file, dataset, or listing;
- publication or last-update date printed by the source;
- access date and observation/data period;
- geography, population, platform, and metric definition;
- exact page, table, chart, section, query, or filter;
- supported claim, source type, quality, and relevance;
- limitations, conflicts, paywall, or estimation method.

If no publication date is visible, use `publication_date: null` and provide `publication_date_unknown_reason`. Never call the access date the publication date.

## Evidence record

Assign every material claim or metric an evidence ID. Classify it as `observed`, `estimated`, `calculated`, or `assumed`. Record statement, value/unit, as-of period, geography, rationale, confidence, source IDs, and conflicts.

For `calculated`, include the exact formula and named inputs. Each input must point to another evidence ID or be labeled as an assumption.

For `assumed`, explain selection logic, plausible range, sensitivity, and validation method. An assumption is not made credible by an unrelated citation.

## Inline citation rules

- Place evidence markers immediately after the number or claim.
- Link markers to the evidence ledger and evidence to direct sources.
- Cite scores, chart series, and table rows, not only introductions.
- Show publication/update date and access date.
- Do not cite search results or generic home pages.
- Preserve disagreements; do not silently average incompatible definitions.

## Relevance test

Ask whether the source measures the exact concept, geography/population, period, platform/business model, and whether methodology is visible and current enough. Explain mismatches and lower confidence.

## Final-report gate

Reject final mode when:

- a material numeric object has no evidence IDs;
- an evidence ID is missing from the ledger;
- an observed/estimated item has no source;
- a calculated item lacks formula or inputs;
- a source lacks publication date without an unknown-date reason;
- a source lacks a direct URL, supported claim, or relevance explanation.

Draft mode may display gaps but must not present an investor-ready verdict.
