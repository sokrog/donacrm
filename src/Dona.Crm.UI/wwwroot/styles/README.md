# DONA UI styles

Entry point: `../dona.css` (linked from both hosts as `_content/Dona.Crm.UI/dona.css`), which `@import`s in this order:

1. `tokens.css` - all custom properties (colour, space, radius, weight, shadow, layout, type).
2. `base.css` - reset, html/body, fonts, headings, focus, reduced motion.
3. `layout.css` - `.dona-page`, `.dona-stack`, `.dona-page-header` (+ a marked LEGACY block of old page-class rules).
4. `components.css` - global `dona-*` components (+ a marked LEGACY block for `orbit-*` aliases that need `!important`).
5. `pages.css` - temporary page-specific leftovers; move into scoped files, then delete.

## Naming
- `dona-*` = global, defined here. Modifiers BEM-style: `dona-btn--primary`.
- Unprefixed class names = scoped, in the component's `.razor.css`.
- `orbit-*` = legacy aliases; removed in wave 4.

## Rules
- Do: scoped CSS handles layout of one page only; use tokens (`var(--dona-space-4)`, `var(--dona-radius-md)`); reuse `dona-*` for anything on 2+ pages.
- Don't: hex/rgb literals, `!important`, new font weights besides 400/700, page-specific rules in global files.

## Tokens
- Space (4px grid): `--dona-space-1..8` = 4, 8, 12, 16, 20, 24, 32, 40px.
- Radius: `--dona-radius-sm/md/lg/xl/pill` = 10 / 12 / 18 / 22 / 999px (md = fields and buttons, lg = cards, xl = big sections).
- Weight: `--dona-weight-regular` 400, `--dona-weight-strong` 700. Only Manrope Regular is bundled; strong is synthesized until a Bold face is added to `brand/fonts.css`.
- Shadow: `--dona-shadow-sm/md/lg`, `--dona-shadow-brand`. Colour on brand fills: `--dona-on-brand`.
- Layout: `--dona-page-narrow` 520, `--dona-page-form` 960, `--dona-page-wide` 1180, `--dona-page-inline`, `--dona-section-gap`.
- Controls: `--dona-tap` 44px, `--dona-control-height` 50/52px.
- Type: `--dona-type-*`.

## Cheat sheet
```html
<main class="dona-page dona-page--wide">            <!-- list page; editors: dona-page--form -->
  <header class="dona-page-header">
    <div><p class="dona-eyebrow">Каталог</p><h1 class="dona-title">Товары</h1></div>
    <a class="dona-icon-button dona-add-button" href="/products/new">+</a>
  </header>
  <div class="dona-search"><label for="q">Поиск</label><input id="q" /></div>
  <div class="dona-chips"><button class="active">Все</button><button>Мало</button></div>
  <section class="dona-kpis dona-kpis--3">
    <article class="dona-kpi"><span>Выручка</span><strong>10 000 ₽</strong></article>
  </section>
  <a class="dona-list-row" href="/x"><span class="dona-icon-tile">…</span><span><strong>Имя</strong><small>Описание</small></span><b>›</b></a>
  <p class="dona-state">Пусто</p>   <!-- dona-state--error -->
</main>

<form class="dona-stack">
  <section class="dona-section">
    <div class="dona-section-title"><div><h2>Заголовок</h2><p>Подзаголовок</p></div><button type="button">Добавить</button></div>
    <label class="dona-field">Название <input /></label>
    <div class="dona-field-row"><label class="dona-field">A <input /></label><label class="dona-field">B <input /></label></div>
  </section>
  <p class="dona-message dona-message--error">Ошибка</p>
  <div class="dona-actions">
    <button class="dona-btn dona-btn--primary">Сохранить</button>
    <a class="dona-btn dona-btn--secondary" href="/">Отмена</a>
    <button class="dona-btn dona-btn--danger">Удалить</button>
  </div>
</form>
```
Also: `dona-card`, `dona-confirm`, `dona-badge` (`--ok/--warning/--danger`), `dona-btn--ghost/--block/--icon`, `dona-back-button`, `dona-heading`.
