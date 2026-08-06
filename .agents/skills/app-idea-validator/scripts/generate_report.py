#!/usr/bin/env python3
"""Generate a standalone app-opportunity report from UTF-8 JSON."""

from __future__ import annotations

import html
import json
import math
import pathlib
import sys
from typing import Any


def die(message: str) -> None:
    raise SystemExit(f"Error: {message}")


def check_numbers(value: Any, path: str = "$") -> None:
    if isinstance(value, float) and not math.isfinite(value):
        die(f"non-finite number at {path}")
    if isinstance(value, dict):
        for key, item in value.items():
            check_numbers(item, f"{path}.{key}")
    elif isinstance(value, list):
        for index, item in enumerate(value):
            check_numbers(item, f"{path}[{index}]")


def validate_evidence(data: dict[str, Any]) -> None:
    """Enforce traceability for final reports."""
    if data.get("meta", {}).get("evidence_mode", "strict") != "strict":
        return

    sources = data.get("sources")
    evidence = data.get("evidence")
    if not isinstance(sources, list) or not sources:
        die("strict evidence mode requires a non-empty sources list")
    if not isinstance(evidence, list) or not evidence:
        die("strict evidence mode requires a non-empty evidence ledger")

    source_ids: set[str] = set()
    for index, source in enumerate(sources):
        path = f"$.sources[{index}]"
        if not isinstance(source, dict):
            die(f"{path} must be an object")
        source_id = str(source.get("id") or "").strip()
        if not source_id or source_id in source_ids:
            die(f"{path}.id must be present and unique")
        source_ids.add(source_id)
        for field in ("title", "publisher", "accessed_at", "supports", "relevance"):
            if not str(source.get(field) or "").strip():
                die(f"{path}.{field} is required")
        if not source.get("publication_date") and not str(
            source.get("publication_date_unknown_reason") or ""
        ).strip():
            die(f"{path} needs publication_date or publication_date_unknown_reason")
        url = str(source.get("url") or "").strip()
        if source.get("source_type") != "user_document" and not url.startswith(("https://", "http://")):
            die(f"{path}.url must be a direct http(s) URL")

    evidence_ids: set[str] = set()
    for index, item in enumerate(evidence):
        path = f"$.evidence[{index}]"
        if not isinstance(item, dict):
            die(f"{path} must be an object")
        evidence_id = str(item.get("id") or "").strip()
        if not evidence_id or evidence_id in evidence_ids:
            die(f"{path}.id must be present and unique")
        evidence_ids.add(evidence_id)
        classification = item.get("classification")
        if classification not in {"observed", "estimated", "calculated", "assumed"}:
            die(f"{path}.classification is invalid")
        for field in ("statement", "rationale", "confidence"):
            if not str(item.get(field) or "").strip():
                die(f"{path}.{field} is required")
        item_source_ids = item.get("source_ids") or []
        if classification in {"observed", "estimated"} and not item_source_ids:
            die(f"{path} requires source_ids for {classification} evidence")
        unknown_sources = set(item_source_ids) - source_ids
        if unknown_sources:
            die(f"{path} references unknown sources: {sorted(unknown_sources)}")
        if classification == "calculated" and (
            not str(item.get("formula") or "").strip() or not item.get("inputs")
        ):
            die(f"{path} calculated evidence requires formula and inputs")
        if classification == "assumed" and (
            not str(item.get("range") or "").strip()
            or not str(item.get("validation") or "").strip()
        ):
            die(f"{path} assumed evidence requires range and validation")

    for index, item in enumerate(evidence):
        for input_index, input_item in enumerate(item.get("inputs") or []):
            if not isinstance(input_item, dict):
                die(f"$.evidence[{index}].inputs[{input_index}] must be an object")
            referenced = input_item.get("evidence_id")
            if referenced and referenced not in evidence_ids:
                die(
                    f"$.evidence[{index}].inputs[{input_index}] references "
                    f"unknown evidence: {referenced}"
                )
            if not referenced and not str(input_item.get("assumption") or "").strip():
                die(
                    f"$.evidence[{index}].inputs[{input_index}] needs "
                    "evidence_id or assumption"
                )

    def walk(value: Any, path: str) -> None:
        if isinstance(value, dict):
            numeric_here = any(
                isinstance(item, (int, float)) and not isinstance(item, bool)
                for item in value.values()
            )
            if numeric_here:
                refs = value.get("evidence_ids")
                if not isinstance(refs, list) or not refs:
                    die(f"{path} contains material numbers but has no evidence_ids")
            refs = value.get("evidence_ids") or []
            unknown = set(refs) - evidence_ids
            if unknown:
                die(f"{path} references unknown evidence: {sorted(unknown)}")
            for key, item in value.items():
                if key != "evidence_ids":
                    walk(item, f"{path}.{key}")
        elif isinstance(value, list):
            for index, item in enumerate(value):
                walk(item, f"{path}[{index}]")

    for root in (
        "summary", "audience", "customer_evidence", "scores", "demand",
        "keywords", "competitors", "competitor_reviews", "market", "regions",
        "scenarios", "projection", "costs", "investor", "team", "roadmap",
        "risks", "experiments", "assumptions",
    ):
        if root in data:
            walk(data[root], f"$.{root}")


def main() -> None:
    if len(sys.argv) != 3:
        die("usage: generate_report.py INPUT.json OUTPUT.html")
    source = pathlib.Path(sys.argv[1]).resolve()
    target = pathlib.Path(sys.argv[2]).resolve()
    try:
        data = json.loads(source.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError) as exc:
        die(f"cannot read JSON: {exc}")
    if not isinstance(data, dict):
        die("JSON root must be an object")
    check_numbers(data)
    validate_evidence(data)
    title = str(data.get("meta", {}).get("title") or "App opportunity report")
    payload = json.dumps(data, ensure_ascii=False, separators=(",", ":")).replace("</", "<\\/")
    document = TEMPLATE.replace("__TITLE__", html.escape(title, quote=True)).replace("__DATA__", payload)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(document, encoding="utf-8")
    print(target)


TEMPLATE = r'''<!doctype html>
<html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="color-scheme" content="dark"><title>__TITLE__</title>
<style>
:root{--bg:#07111f;--p:#0e1b2b;--p2:#12243a;--ink:#edf6ff;--muted:#91a7bd;--line:#263c52;--cyan:#55d6d0;--blue:#7aa2ff;--gold:#ffc857;--red:#ff748d;--green:#78e6a0}
*{box-sizing:border-box}html{scroll-behavior:smooth}body{margin:0;background:radial-gradient(circle at 80% 0,#163354 0,transparent 36rem),var(--bg);color:var(--ink);font:15px/1.55 Inter,ui-sans-serif,system-ui,-apple-system,"Segoe UI",sans-serif}a{color:var(--cyan)}
.wrap{width:min(1180px,calc(100% - 32px));margin:auto}.hero{padding:62px 0 30px}.eyebrow,.label{color:var(--cyan);font-size:11px;font-weight:800;letter-spacing:.12em;text-transform:uppercase}.hero h1{font-size:clamp(34px,6vw,66px);line-height:1.02;letter-spacing:-.045em;margin:12px 0;max-width:920px}.lede{color:var(--muted);font-size:18px;max-width:820px}.meta,.legend{display:flex;gap:10px;flex-wrap:wrap}.pill,.tag{border:1px solid var(--line);border-radius:999px;padding:5px 10px;background:#0a1726;color:var(--muted)}
.dashboard{display:grid;grid-template-columns:repeat(12,1fr);gap:16px;margin:22px 0 46px}.card,.item{background:linear-gradient(145deg,rgba(18,36,58,.96),rgba(10,24,39,.96));border:1px solid var(--line);border-radius:18px;padding:20px}.card{grid-column:span 4;box-shadow:0 18px 45px #0004}.card.wide{grid-column:span 8}.card.full{grid-column:1/-1}.metric{font-size:48px;font-weight:850;letter-spacing:-.04em}.metric small{font-size:15px;color:var(--muted)}.meter,.bar{height:9px;background:#07111f;border-radius:10px;overflow:hidden}.meter i,.bar i{display:block;height:100%;background:linear-gradient(90deg,var(--blue),var(--cyan));border-radius:inherit}
section{padding:16px 0 32px}h2{font-size:28px;letter-spacing:-.025em;margin:0 0 16px}h3{font-size:16px;margin:0 0 9px}.muted,.section-note{color:var(--muted)}.grid2{display:grid;grid-template-columns:1fr 1fr;gap:16px}.grid3,.market{display:grid;grid-template-columns:repeat(3,1fr);gap:16px}.stack{display:grid;gap:12px}.item p{margin:7px 0}.item strong.big{display:block;font-size:27px;margin:5px 0}
table{width:100%;border-collapse:separate;border-spacing:0;background:#0d1b2bbb;border:1px solid var(--line);border-radius:14px;overflow:hidden}th,td{text-align:left;padding:11px 12px;border-bottom:1px solid var(--line);vertical-align:top}th{color:var(--muted);font-size:10px;text-transform:uppercase;letter-spacing:.08em;background:#0c1928}tr:last-child td{border:0}.scroll{overflow:auto}.barrow{display:grid;grid-template-columns:minmax(140px,230px) 1fr 44px;gap:12px;align-items:center;margin:12px 0}.chart{min-height:270px;background:#091624;border:1px solid var(--line);border-radius:15px;padding:12px}.chart svg{width:100%;height:250px}.dot{display:inline-block;width:9px;height:9px;border-radius:50%;margin-right:5px}.scenario{position:relative;overflow:hidden}.scenario:before{content:"";position:absolute;inset:0 auto 0 0;width:4px;background:var(--blue)}.scenario:first-child:before{background:var(--gold)}.scenario:last-child:before{background:var(--green)}dl{display:grid;grid-template-columns:1fr auto;gap:7px}dt{color:var(--muted)}dd{margin:0;font-weight:700}.callout{border-left:4px solid var(--cyan);padding:14px 18px;background:#0e2537;border-radius:0 12px 12px 0}.empty{color:var(--muted);font-style:italic}.footer{padding:32px 0 58px;color:var(--muted);border-top:1px solid var(--line)}.print{position:fixed;right:20px;bottom:20px;border:1px solid var(--line);border-radius:999px;background:var(--p2);color:var(--ink);padding:10px 15px;cursor:pointer}
.cite{display:inline-block;margin-left:4px;font-size:10px;font-weight:800;text-decoration:none;color:var(--cyan)}.evidence{scroll-margin-top:18px}.status{display:inline-block;border-radius:999px;padding:3px 8px;background:#172c44;font-size:11px}.quote{border-left:3px solid var(--blue);padding-left:14px;font-style:italic}.warning{border-color:var(--gold);color:var(--gold)}
@media(max-width:850px){.card,.card.wide{grid-column:1/-1}.grid2,.grid3,.market{grid-template-columns:1fr}.hero{padding-top:38px}.barrow{grid-template-columns:110px 1fr 34px}}
@media print{body{background:#fff;color:#15202b;font-size:11px}.wrap{width:100%}.card,.item,table,.chart{background:#fff;color:#15202b;box-shadow:none;border-color:#ccd5dd;break-inside:avoid}.muted,.pill{color:#506273}.print{display:none}a{color:#164b8a}}
</style></head><body><main class="wrap">
<header class="hero"><div class="eyebrow">App opportunity intelligence</div><h1 id="title"></h1><p class="lede" id="one"></p><div class="meta" id="meta"></div></header>
<div class="dashboard" id="dash"></div>
<section><h2>Полнота исходных данных</h2><div class="scroll" id="coverage"></div></section>
<section><h2>Продукт и аудитория</h2><div class="grid2" id="product"></div></section>
<section><h2>Голос клиента и JTBD</h2><div class="grid2" id="customerEvidence"></div></section>
<section><h2>Оценка возможности</h2><div class="card full" id="scores"></div></section>
<section><h2>Спрос и ключевые слова</h2><p class="section-note" id="demand"></p><div class="scroll" id="keywords"></div></section>
<section><h2>Рынок: TAM / SAM / SOM</h2><div class="market" id="market"></div></section>
<section><h2>Приоритет регионов</h2><div class="scroll" id="regions"></div></section>
<section><h2>Конкурентная среда</h2><div class="grid2" id="competitors"></div></section>
<section><h2>Финансовые сценарии</h2><div class="grid3" id="scenarios"></div><div class="chart" style="margin-top:16px"><div class="legend"><span><i class="dot" style="background:#ffc857"></i>Консервативный</span><span><i class="dot" style="background:#7aa2ff"></i>Базовый</span><span><i class="dot" style="background:#78e6a0"></i>Оптимистичный</span></div><div id="projection"></div></div></section>
<section><h2>Затраты</h2><div class="grid2" id="costs"></div></section>
<section><h2>Инвестиционная история</h2><div class="grid2" id="investor"></div></section>
<section><h2>Команда и roadmap</h2><div class="grid2"><div class="stack" id="team"></div><div class="stack" id="roadmap"></div></div></section>
<section><h2>Риски и допущения</h2><div class="grid2"><div class="stack" id="risks"></div><div class="stack" id="assumptions"></div></div></section>
<section><h2>План проверки</h2><div class="stack" id="experiments"></div></section>
<section><h2>Рекомендации</h2><div class="callout"><ol id="recommendations"></ol></div></section>
<section><h2>Реестр доказательств</h2><div class="stack" id="evidence"></div></section>
<section><h2>Источники и пробелы</h2><div class="stack" id="sources"></div><div class="item" style="margin-top:16px"><h3>Что ещё неизвестно</h3><ul id="gaps"></ul></div></section>
<footer class="footer">Сценарная оценка, а не гарантия результата. Проверяйте критические допущения реальными экспериментами.</footer>
</main><button class="print" onclick="window.print()">Печать / PDF</button>
<script id="data" type="application/json">__DATA__</script><script>
const D=JSON.parse(document.getElementById("data").textContent),$=x=>document.getElementById(x),A=x=>Array.isArray(x)?x:[],O=x=>x&&typeof x==="object"?x:{};
const E=x=>String(x??"").replace(/[&<>"']/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c])),C=x=>Math.max(0,Math.min(100,Number(x)||0)),currency=()=>O(D.meta).currency||"USD";
const money=x=>x==null?"—":new Intl.NumberFormat("ru-RU",{style:"currency",currency:currency(),maximumFractionDigits:0}).format(Number(x)||0),pct=x=>x==null?"—":`${(Number(x)*100).toFixed(1)}%`;
const list=x=>A(x).length?`<ul>${A(x).map(v=>`<li>${E(v)}</li>`).join("")}</ul>`:`<p class="empty">Нет данных</p>`;
const table=(h,r)=>r.length?`<table><thead><tr>${h.map(E).map(x=>`<th>${x}</th>`).join("")}</tr></thead><tbody>${r.join("")}</tbody></table>`:`<p class="empty">Нет данных</p>`;
const link=(u,l)=>u?`<a href="${E(u)}" target="_blank" rel="noopener">${E(l||u)}</a>`:E(l||"—");
const evidenceMap=new Map(A(D.evidence).map(x=>[x.id,x])),sourceMap=new Map(A(D.sources).map(x=>[x.id,x]));
const cite=ids=>A(ids).map(id=>`<a class="cite" href="#evidence-${E(id)}" title="${E(O(evidenceMap.get(id)).statement)}">[${E(id)}]</a>`).join("");
const M=O(D.meta),S=O(D.summary),P=O(D.product);$("title").textContent=M.title||P.name||"Анализ идеи";$("one").textContent=S.one_liner||P.description||"";
$("meta").innerHTML=[M.generated_at&&`Дата: ${M.generated_at}`,M.currency&&`Валюта: ${M.currency}`,M.horizon_months&&`Горизонт: ${M.horizon_months} мес.`,A(P.platforms).length&&`Платформы: ${P.platforms.join(", ")}`,M.evidence_mode!=="strict"&&"ЧЕРНОВИК: evidence gate отключён"].filter(Boolean).map(x=>`<span class="pill ${M.evidence_mode!=="strict"&&String(x).startsWith("ЧЕРНОВИК")?"warning":""}">${E(x)}</span>`).join("");
const score=C(S.score),conf=C(S.confidence);$("dash").innerHTML=`<article class="card"><div class="label">Вердикт</div><h2>${E(S.verdict||"Недостаточно данных")}${cite(S.evidence_ids)}</h2><p class="muted">${E(P.business_model)}</p></article><article class="card"><div class="label">Привлекательность</div><div class="metric">${score}<small>/100</small>${cite(S.evidence_ids)}</div><div class="meter"><i style="width:${score}%"></i></div><p class="muted">${E(S.score_method)}</p></article><article class="card"><div class="label">Уверенность</div><div class="metric">${conf}<small>/100</small>${cite(S.evidence_ids)}</div><div class="meter"><i style="width:${conf}%"></i></div><p class="muted">${E(S.confidence_method)}</p></article><article class="card wide"><h3>Сильнейшие сигналы</h3>${list(S.strongest_evidence)}</article><article class="card"><h3>Критерии остановки</h3>${list(S.kill_criteria)}</article>`;
$("coverage").innerHTML=table(["Категория","Статус","Артефакт","Влияние пробела"],A(D.input_coverage).map(x=>`<tr><td>${E(x.category)}</td><td><span class="status">${E(x.status)}</span></td><td>${E(x.artifact)}</td><td>${E(x.impact)}</td></tr>`));
const audience=A(D.audience).map(a=>`<div class="item"><h3>${E(a.segment)}${cite(a.evidence_ids)}</h3><p>${E(a.persona||a.profile)}</p><p><span class="label">JTBD</span><br>Когда ${E(a.jtbd_situation)} → хочу ${E(a.jtbd_motivation)} → чтобы ${E(a.jtbd_outcome)}</p><p><span class="label">Покупатель / пользователь</span><br>${E(a.buyer)} / ${E(a.user)}</p><p><span class="label">Боль / частота</span><br>${E(a.pain)} · ${E(a.frequency)}</p><p><span class="label">Оплата</span><br>${E(a.willingness_to_pay)}</p></div>`).join("");
$("product").innerHTML=`<div class="item"><h3>${E(P.name||"Концепция")}${cite(P.evidence_ids)}</h3><p>${E(P.description)}</p><p><span class="label">Проблема</span><br>${E(P.problem)}</p><p><span class="label">Ценность</span><br>${E(P.value_proposition)}</p><h3>MVP</h3>${list(P.mvp_scope)}</div><div class="stack">${audience||'<p class="empty">Аудитория не описана</p>'}</div>`;
$("customerEvidence").innerHTML=A(D.customer_evidence).map(x=>`<article class="item"><p class="quote">“${E(x.quote)}”${cite(x.evidence_ids)}</p><p>${E(x.finding)}</p><p class="muted">${E(x.context)} · ${E(x.consent_or_anonymization)}</p></article>`).join("")||'<p class="empty">Первичные интервью и цитаты не предоставлены.</p>';
$("scores").innerHTML=A(D.scores).map(s=>`<div class="barrow" title="${E(s.reason)}"><span>${E(s.name)} <small class="muted">(${E(s.weight)}%)</small></span><div class="bar"><i style="width:${C(s.score)}%"></i></div><strong>${C(s.score)}${cite(s.evidence_ids)}</strong></div>`).join("")||'<p class="empty">Нет оценок</p>';
const dem=O(D.demand);$("demand").innerHTML=E([dem.finding,dem.trend&&`Тренд: ${dem.trend}`,dem.seasonality&&`Сезонность: ${dem.seasonality}`].filter(Boolean).join(" · "))+cite(dem.evidence_ids);
$("keywords").innerHTML=table(["Запрос","Локаль","Намерение","Источник","Спрос","Тренд"],A(D.keywords).map(k=>`<tr><td>${E(k.keyword)}${cite(k.evidence_ids)}</td><td>${E(k.locale)}</td><td>${E(k.intent)}</td><td>${E(k.source)}</td><td>${E(k.demand)}</td><td>${E(k.trend)}</td></tr>`));
const market=O(D.market);$("market").innerHTML=["tam","sam","som"].map(k=>{const x=O(market[k]);return `<div class="item"><span class="label">${k.toUpperCase()}</span><strong class="big">${E(x.label||(x.value!=null?money(x.value):"—"))}${cite(x.evidence_ids)}</strong><p>${E(x.method)}</p><p class="muted">${E(x.formula)}</p></div>`}).join("");
$("regions").innerHTML=table(["Регион","Итог","Спрос","Монетизация","Окно","Platform fit","Трение","Комментарий"],A(D.regions).map(r=>`<tr><td><strong>${E(r.name)}</strong>${cite(r.evidence_ids)}</td><td>${C(r.score)}</td><td>${C(r.demand)}</td><td>${C(r.monetization)}</td><td>${C(r.competition)}</td><td>${C(r.platform_fit)}</td><td>${C(r.friction)}</td><td>${E(r.notes)}<br><span class="muted">${E(r.score_method)}</span></td></tr>`));
$("competitors").innerHTML=A(D.competitors).map(c=>`<article class="item"><div class="label">${E(c.type)}</div><h3>${link(c.url,c.name)}${cite(c.evidence_ids)}</h3><p>${E(c.platforms)} · ${E(c.pricing)}</p><p><span class="label">Доля / масштаб</span><br>${E(c.market_share)} · ${E(c.traction)}</p><p><span class="label">Сильные стороны</span><br>${E(c.strengths)}</p><p><span class="label">Пробелы</span><br>${E(c.gaps)}</p></article>`).join("")||'<p class="empty">Конкуренты не исследованы</p>';
$("scenarios").innerHTML=A(D.scenarios).map(x=>`<article class="item scenario"><div class="label">${E(x.name)} ${cite(x.evidence_ids)}</div><dl><dt>Активные M12</dt><dd>${E(x.active_users_m12)}</dd><dt>Платящая конверсия</dt><dd>${pct(x.paid_conversion)}</dd><dt>ARPPU / мес.</dt><dd>${money(x.net_arppu_monthly)}</dd><dt>MRR M12</dt><dd>${money(x.mrr_m12)}</dd><dt>Выручка год 1</dt><dd>${money(x.year1_revenue)}</dd><dt>Затраты год 1</dt><dd>${money(x.year1_cost)}</dd><dt>Результат год 1</dt><dd>${money(x.year1_profit)}</dd><dt>CAC / LTV</dt><dd>${money(x.cac)} / ${money(x.ltv)}</dd><dt>Безубыточность</dt><dd>${x.break_even_month==null?"не достигнута":`месяц ${E(x.break_even_month)}`}</dd></dl><p>${E(x.formula_notes)}</p><p class="muted">${E(x.notes)}</p></article>`).join("")||'<p class="empty">Сценарии не рассчитаны</p>';
function chart(rows){if(!rows.length)return'<p class="empty">Нет проекции</p>';const keys=["conservative","base","optimistic"],colors=["#ffc857","#7aa2ff","#78e6a0"],vals=rows.flatMap(r=>keys.map(k=>Number(r[k])||0)),max=Math.max(...vals,1),w=900,h=230,p=32,pts=k=>rows.map((r,i)=>`${p+i*(w-2*p)/Math.max(rows.length-1,1)},${h-p-(Number(r[k])||0)*(h-2*p)/max}`).join(" "),grid=[0,.25,.5,.75,1].map(v=>`<line x1="${p}" y1="${h-p-v*(h-2*p)}" x2="${w-p}" y2="${h-p-v*(h-2*p)}" stroke="#263c52"/><text x="2" y="${h-p-v*(h-2*p)+4}" fill="#91a7bd" font-size="11">${E(money(max*v))}</text>`).join(""),refs=[...new Set(rows.flatMap(r=>A(r.evidence_ids)))];return`<svg viewBox="0 0 ${w} ${h}" role="img">${grid}${keys.map((k,i)=>`<polyline points="${pts(k)}" fill="none" stroke="${colors[i]}" stroke-width="4" stroke-linecap="round"/>`).join("")}</svg><p class="muted">Основание прогноза ${cite(refs)}</p>`}$("projection").innerHTML=chart(A(D.projection));
function cost(title,items,mul){const rows=A(items),total=rows.reduce((s,x)=>s+(Number(x.base)||0)*mul,0);return`<article class="item"><h3>${title}</h3>${table(["Статья","Низ.","База","Выс.","Основание"],rows.map(x=>`<tr><td>${E(x.name)}${cite(x.evidence_ids)}</td><td>${money(x.low)}</td><td>${money(x.base)}</td><td>${money(x.high)}</td><td>${E(x.basis)}</td></tr>`))}<p><strong>Базовый итог: ${money(total)}</strong> (расчёт из строк выше)</p></article>`}const costs=O(D.costs);$("costs").innerHTML=cost("Разовые",costs.one_time,1)+cost("Ежемесячные за горизонт",costs.monthly,Number(M.horizon_months)||12);
$("risks").innerHTML=A(D.risks).map(r=>`<article class="item"><div class="label">${E(r.likelihood)} вероятность · ${E(r.impact)} влияние</div><h3>${E(r.risk)}${cite(r.evidence_ids)}</h3><p>${E(r.mitigation)}</p></article>`).join("")||'<p class="empty">Риски не описаны</p>';
$("assumptions").innerHTML=`<article class="item"><h3>Ключевые допущения</h3>${A(D.assumptions).map(a=>`<p><strong>${E(a.claim)}${cite(a.evidence_ids)}</strong><br><span class="muted">${E(a.type)} · диапазон: ${E(a.range)} · чувствительность: ${E(a.sensitivity)} · проверка: ${E(a.validation)}</span></p>`).join("")||'<p class="empty">Нет данных</p>'}<h3>Крупнейшие неопределённости</h3>${list(S.largest_uncertainties)}</article>`;
$("experiments").innerHTML=A(D.experiments).map((e,i)=>`<article class="item"><div class="label">Эксперимент ${i+1} · ${E(e.duration)} · ${E(e.cost)} ${cite(e.evidence_ids)}</div><h3>${E(e.name)}</h3><p><strong>Гипотеза:</strong> ${E(e.hypothesis)}</p><p><strong>Метод:</strong> ${E(e.method)}</p><p><strong>Метрика / порог:</strong> ${E(e.metric)} — ${E(e.threshold)}</p><p><strong>Решение:</strong> ${E(e.decision)}</p></article>`).join("")||'<p class="empty">Эксперименты не предложены</p>';
const inv=O(D.investor);$("investor").innerHTML=`<article class="item"><h3>Purpose / Why now ${cite(inv.evidence_ids)}</h3><p>${E(inv.company_purpose)}</p><p><span class="label">Почему сейчас</span><br>${E(inv.why_now)}</p><p><span class="label">Traction</span><br>${E(inv.traction)}</p><p><span class="label">Go-to-market</span><br>${E(inv.go_to_market)}</p></article><article class="item"><h3>Защитимость и раунд</h3><p>${E(inv.defensibility)}</p><p><span class="label">Видение</span><br>${E(inv.vision)}</p><p><span class="label">Запрос</span><br>${E(inv.fundraising_ask)}</p><h3>Use of funds</h3>${list(inv.use_of_funds)}</article>`;
$("team").innerHTML=A(D.team).map(x=>`<article class="item"><h3>${E(x.name)} · ${E(x.role)}${cite(x.evidence_ids)}</h3><p>${E(x.relevant_evidence)}</p><p class="muted">Пробелы: ${E(x.gaps)}</p></article>`).join("")||'<p class="empty">Команда не описана</p>';
$("roadmap").innerHTML=A(D.roadmap).map(x=>`<article class="item"><div class="label">${E(x.period)} ${cite(x.evidence_ids)}</div><h3>${E(x.milestone)}</h3><p>Метрика: ${E(x.success_metric)}</p><p>Бюджет: ${E(x.budget)} · Зависимость: ${E(x.dependency)}</p></article>`).join("")||'<p class="empty">Roadmap не описан</p>';
$("recommendations").innerHTML=A(D.recommendations).map(x=>`<li>${E(x)}</li>`).join("")||"<li>Сначала закрыть пробелы.</li>";
$("evidence").innerHTML=A(D.evidence).map(x=>`<article class="item evidence" id="evidence-${E(x.id)}"><div class="label">${E(x.id)} · ${E(x.classification)} · confidence ${E(x.confidence)}</div><h3>${E(x.statement)}</h3><p><strong>${E(x.value)} ${E(x.unit)}</strong> · as of ${E(x.as_of)} · ${E(x.geography)}</p><p>${E(x.rationale)}</p>${x.formula?`<p><span class="label">Формула</span><br><code>${E(x.formula)}</code></p>`:""}<p>${A(x.source_ids).map(id=>{const s=O(sourceMap.get(id));return link(s.url,`${id}: ${s.title}`)}).join(" · ")||"Без внешнего источника — допущение/расчёт"}</p>${A(x.conflicts).length?`<p class="muted">Конфликты: ${E(x.conflicts.join("; "))}</p>`:""}</article>`).join("")||'<p class="empty">Реестр доказательств отсутствует</p>';
$("sources").innerHTML=A(D.sources).map(s=>`<article class="item" id="source-${E(s.id)}"><div class="label">${E(s.id)} · ${E(s.quality)} quality · ${E(s.geography)} · доступ ${E(s.accessed_at)}</div><h3>${link(s.url,s.title)}</h3><p>${E(s.publisher)} · опубликовано/обновлено: ${E(s.publication_date||"дата не указана")}</p><p><strong>Период данных:</strong> ${E(s.observation_period)} · <strong>место:</strong> ${E(s.location)}</p><p><strong>Подтверждает:</strong> ${E(s.supports)}</p><p><strong>Релевантность:</strong> ${E(s.relevance)}</p>${s.publication_date_unknown_reason?`<p class="muted">Почему нет даты: ${E(s.publication_date_unknown_reason)}</p>`:""}${s.limitation?`<p class="muted">Ограничение: ${E(s.limitation)}</p>`:""}</article>`).join("")||'<p class="empty">Источники не приложены</p>';$("gaps").innerHTML=A(D.research_gaps).map(x=>`<li>${E(x)}</li>`).join("")||"<li>Существенные пробелы не указаны.</li>";
</script></body></html>'''


if __name__ == "__main__":
    main()
