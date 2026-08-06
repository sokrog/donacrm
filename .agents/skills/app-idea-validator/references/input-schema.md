# HTML report input

Create UTF-8 JSON. Final reports use `evidence_mode: "strict"`. Money uses `meta.currency`; fractions use 0–1; scores use 0–100. Use `null`, not fabricated zeroes.

Every object containing a material number must carry `evidence_ids`. Every referenced ID must exist in `evidence`. Sources require publication/update date or an explicit reason why it is unavailable.

```json
{
  "meta": {
    "title": "...",
    "generated_at": "YYYY-MM-DD",
    "currency": "USD",
    "horizon_months": 12,
    "evidence_mode": "strict"
  },
  "summary": {
    "verdict": "...",
    "score": 68,
    "confidence": 61,
    "one_liner": "...",
    "strongest_evidence": ["..."],
    "largest_uncertainties": ["..."],
    "kill_criteria": ["..."],
    "score_method": "...",
    "confidence_method": "...",
    "evidence_ids": ["E-SCORE"]
  },
  "input_coverage": [
    {"category":"Customer interviews","status":"provided / researched / missing / skipped / not_applicable","artifact":"...","impact":"..."}
  ],
  "product": {
    "name":"...","description":"...","problem":"...","value_proposition":"...",
    "platforms":["iOS"],"business_model":"Subscription",
    "mvp_scope":["..."],"out_of_scope":["..."],"evidence_ids":["E-PROBLEM"]
  },
  "audience": [{
    "segment":"...","persona":"...","profile":"...","job":"...",
    "jtbd_situation":"When ...","jtbd_motivation":"I want to ...","jtbd_outcome":"So I can ...",
    "buyer":"...","user":"...","pain":"...","frequency":"...",
    "willingness_to_pay":"...","evidence_ids":["E-CUST-1"]
  }],
  "customer_evidence": [{
    "quote":"...","context":"Interview 4, role, date","finding":"...",
    "consent_or_anonymization":"...","evidence_ids":["E-CUST-1"]
  }],
  "scores": [{
    "name":"Demand evidence","score":70,"weight":15,"reason":"...",
    "evidence_ids":["E-DEMAND"]
  }],
  "demand": {
    "finding":"...","trend":"growing / stable / seasonal / declining / unclear",
    "seasonality":"...","notes":["..."],"evidence_ids":["E-DEMAND"]
  },
  "keywords": [{
    "keyword":"...","locale":"en-US","intent":"solution","source":"Google Trends",
    "demand":"Index 0–100, not volume","trend":"...","evidence_ids":["E-KW-1"]
  }],
  "competitors": [{
    "name":"...","type":"direct / indirect / substitute","platforms":"...",
    "pricing":"...","market_share":"unknown or sourced value","traction":"...",
    "strengths":"...","gaps":"...","url":"https://...","evidence_ids":["E-COMP-1"]
  }],
  "competitor_reviews": [{
    "competitor":"...","rating":2,"date":"YYYY-MM-DD","quote_or_paraphrase":"...",
    "theme":"...","url":"https://...","evidence_ids":["E-REV-1"]
  }],
  "market": {
    "tam":{"value":0,"label":"$0","method":"...","formula":"...","inputs":["..."],"evidence_ids":["E-TAM"]},
    "sam":{"value":0,"label":"$0","method":"...","formula":"...","inputs":["..."],"evidence_ids":["E-SAM"]},
    "som":{"value":0,"label":"$0","method":"...","formula":"...","inputs":["..."],"evidence_ids":["E-SOM"]},
    "limitations":["..."]
  },
  "regions": [{
    "name":"...","score":72,"demand":75,"monetization":80,"competition":45,
    "platform_fit":85,"friction":55,"notes":"...","score_method":"...",
    "evidence_ids":["E-REGION-1"]
  }],
  "scenarios": [{
    "name":"Base","active_users_m12":0,"paid_conversion":0.05,
    "net_arppu_monthly":0,"mrr_m12":0,"year1_revenue":0,
    "year1_cost":0,"year1_profit":0,"cac":0,"ltv":0,
    "break_even_month":null,"formula_notes":"...","notes":"...",
    "evidence_ids":["E-FIN-BASE"]
  }],
  "projection": [{
    "month":1,"conservative":0,"base":0,"optimistic":0,
    "evidence_ids":["E-PROJECTION"]
  }],
  "costs": {
    "one_time":[{"name":"MVP build","low":0,"base":0,"high":0,"basis":"...","evidence_ids":["E-COST-1"]}],
    "monthly":[{"name":"Cloud/APIs","low":0,"base":0,"high":0,"basis":"...","evidence_ids":["E-COST-2"]}]
  },
  "investor": {
    "company_purpose":"...","why_now":"...","traction":"...",
    "go_to_market":"...","defensibility":"...","vision":"...",
    "fundraising_ask":"...","use_of_funds":["..."],"evidence_ids":["E-INVESTOR"]
  },
  "team": [{
    "name":"...","role":"...","relevant_evidence":"...","gaps":"...",
    "evidence_ids":["E-TEAM-1"]
  }],
  "roadmap": [{
    "period":"0–6 months","milestone":"...","success_metric":"...",
    "budget":"...","dependency":"...","evidence_ids":["E-ROADMAP-1"]
  }],
  "risks":[{"risk":"...","likelihood":"high","impact":"high","mitigation":"...","evidence_ids":["E-RISK-1"]}],
  "experiments":[{"name":"...","hypothesis":"...","method":"...","metric":"...","threshold":"...","duration":"...","cost":"...","decision":"...","evidence_ids":["E-EXP-1"]}],
  "recommendations":["..."],
  "assumptions":[{"claim":"...","type":"assumed","range":"...","sensitivity":"high","validation":"...","evidence_ids":["E-ASSUME-1"]}],
  "evidence": [{
    "id":"E-TAM","classification":"calculated","statement":"...",
    "value":"...","unit":"USD/year","as_of":"YYYY","geography":"...",
    "rationale":"...","confidence":"medium","formula":"A × B",
    "inputs":[{"name":"A","value":"...","evidence_id":"E-A"},{"name":"B","value":"...","assumption":"..."}],
    "source_ids":["S-1"],"conflicts":["..."]
  }],
  "sources": [{
    "id":"S-1","title":"...","publisher":"...","url":"https://...",
    "publication_date":"YYYY-MM-DD or null",
    "publication_date_unknown_reason":"only when null",
    "accessed_at":"YYYY-MM-DD","observation_period":"...",
    "location":"page/table/section/query","geography":"...",
    "metric_definition":"...","supports":"...","source_type":"official / first_party / research / estimate / user_document",
    "quality":"high / medium / low","relevance":"...","limitation":"..."
  }],
  "research_gaps":["..."]
}
```

The generator validates numeric provenance in strict mode. Draft mode may be used during research, but the report must visibly say it is not investor-ready.
