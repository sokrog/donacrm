# DONA UI styles

Entry point: `../dona.css` (linked from both hosts as `_content/Dona.Crm.UI/dona.css`), which `@import`s in this order:

1. `tokens.css` - all custom properties (colour, space, radius, weight, shadow, layout, type).
2. `base.css` - reset, html/body, fonts, headings, focus, reduced motion.
3. `layout.css` - `.dona-page` (+ `--wide` / `--form`, incl. the 1400px wide step), `.dona-stack`, `.dona-page-header`.
4. `components.css` - global `dona-*` components.

## Naming
- `dona-*` = global, defined here. Modifiers BEM-style: `dona-btn--primary`.
- Unprefixed class names = scoped, in the component's `.razor.css`.
- `orbit-*` = scoped class names inside the Orbit* components (OrbitSelect, OrbitDatePicker, InitialBadge); never used as global utilities.

## Rules
- Do: scoped CSS handles layout of one page only; use tokens (`var(--dona-space-4)`, `var(--dona-radius-md)`); reuse `dona-*` for anything on 2+ pages.
- Don't: hex/rgb literals, `!important`, new font weights besides 400/700, page-specific rules in global files.

## Tokens
- Space (4px grid): `--dona-space-1..8` = 4, 8, 12, 16, 20, 24, 32, 40px.
- Radius: `--dona-radius-sm/md/lg/xl/pill` = 10 / 12 / 18 / 22 / 999px (md = fields and buttons, lg = cards, xl = big sections).
- Weight: `--dona-weight-regular` 400, `--dona-weight-strong` 700. Both faces are bundled in `brand/fonts.css` (`manrope.ttf`, `manrope-bold.ttf`); other weights would be synthesized.
- Shadow: `--dona-shadow-sm/md/lg`, `--dona-shadow-brand`. Colour on brand fills: `--dona-on-brand`.
- Layout: `--dona-page-narrow` 520, `--dona-page-form` 960, `--dona-page-wide` 1180, `--dona-page-inline`, `--dona-section-gap`.
- Controls: `--dona-tap` 44px, `--dona-control-height` 48px.
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
    <article class="dona-kpi"><span>Выручка</span><strong>10 000 UZS</strong></article>
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

### More components
```html
<div class="dona-tabs"><button class="active">Поставщики <span>3</span></button><button>Посредники</button></div>
<div class="dona-alert dona-alert--warning">Проверьте…</div>   <!-- --danger -->
<p class="dona-note dona-note--intro">Подзаголовок страницы</p>  <!-- dona-note--sm = hint under a field -->
<p class="dona-empty">Пока ничего нет.</p>
<label class="dona-field dona-toggle"><input type="checkbox" /><span><strong>Заголовок</strong><small>Пояснение</small></span></label>
<button class="dona-btn dona-btn--link dona-btn--danger">Архивировать</button>   <!-- --muted for grey links -->

<button class="dona-list-row" type="button">                                     <!-- <a> or <button> -->
  <span class="dona-icon-tile dona-icon-tile--success">…</span>                  <!-- --sm --lg; tones --success --danger --warning --rose -->
  <span><strong>Имя</strong><small>Описание</small><em>Акцент</em></span>
  <span class="dona-list-row__value">12<small>заказов</small></span>             <!-- right-aligned value + caption -->
</button>

<div class="dona-confirm dona-confirm--boxed">                                   <!-- danger-tinted box; plain .dona-confirm inside .dona-actions -->
  <span>Удалить?</span><button class="dona-btn dona-btn--secondary">Нет</button><button class="dona-btn dona-btn--danger">Да</button>
</div>
<div class="dona-section-title"><div><p class="dona-eyebrow">Поставщик</p><h2>Карточка</h2></div></div>
```

### Action bar (`dona-actions`)
Buttons must be direct children. With one or two buttons they share a row (the primary gets twice the room). With three or more the primary goes first on its own full-width row (`:has(> :nth-child(3))`). Sticky above the tab bar on mobile, static on desktop; min button height 48px.

### Other rules
- Editor headings: plain `<h1>` inside `.dona-heading` gets the editor size (title-md); list pages use `<h1 class="dona-title">`.
- Blazor output is styled globally: `.validation-message`, `.validation-errors`, `.valid.modified`, `.invalid`.
- Also: `dona-card`, `dona-badge` (`--ok/--warning/--danger`), `dona-btn--ghost/--block/--icon`, `dona-back-button`, `dona-heading`.

### Modal, select trigger, round icon button
```html
<div class="dona-modal" role="presentation" @onclick="Close">                   <!-- --alert (z 700, confirmations), --viewer (z 1000, no blur) -->
  <section class="dona-modal__dialog" role="dialog" aria-modal="true" @onclick:stopPropagation="true">   <!-- --sm = 390px, default 430px -->
    <header class="dona-modal__header">
      <div><small class="dona-modal__eyebrow">Выбор значения</small><h2 class="dona-modal__title">Заголовок</h2></div>
      <button type="button" class="dona-modal__close" aria-label="Закрыть"><OrbitIcon Name="close" /></button>
    </header>
    <div class="dona-modal__body">…scrolls…</div>
    <div class="dona-modal__footer"><button class="dona-btn dona-btn--primary">Готово</button></div>
  </section>
</div>

<button type="button" class="dona-select-trigger dona-select-trigger--empty"><span>Не выбрано</span><OrbitIcon Name="down" /></button>   <!-- --accent = brand-coloured icon -->
<a class="dona-icon-button dona-icon-button--round" href="/x"><OrbitIcon Name="bell" /></a>                                                <!-- 48px circle; position: relative for badges -->
```
The modal is centred at every width. Component-specific rules (OrbitSelect desktop popover, date grid, lightbox image sizing) stay in the component's scoped CSS and are layered on top of these classes.

## Shared pricing and record components

`DonaHeaderActions` renders header buttons from one action list on desktop and an anchored three-dot disclosure below 900px. Use `.dona-page-header--actions` and `.dona-page-header-actions` for the header layout. The mobile panel closes after an action, on outside pointer input, when focus leaves it, or on Escape; Escape returns focus to the trigger. Its width, layer, spacing and appearance use central tokens. Native disclosure and regular buttons support keyboard navigation without custom menu roles.

`DonaNumberField` uses `.dona-number-field` for a labelled decimal input with a non-overlapping suffix and parse feedback. The value is cleared on invalid input, so an old valid value cannot be submitted accidentally. `DonaResponsiveTable` uses `.dona-record-table`: desktop columns become labelled records below the existing 900px breakpoint. Each body cell supplies `data-label`. `.dona-toolbar`, `.dona-selection` and `.dona-selection-row` provide reusable actions and selection layouts. All dimensions, colours and spacing use central tokens. Pricing pages have no scoped stylesheet or inline styles.
