export function observe(header, boundary) {
    if (!header?.isConnected || !boundary?.isConnected) return { dispose() {} };
    let frame = 0;
    let disposed = false;
    let needsFit = true;
    const fitActions = () => {
        const bar = header.querySelector('.dona-page-command-bar');
        if (!bar) return;
        const focused = document.activeElement;
        header.classList.add('dona-header-measuring');
        const style = getComputedStyle(header);
        const children = Array.from(header.children).filter(child => child.getBoundingClientRect().width > 0);
        const required = children.reduce((sum, child) => sum + child.getBoundingClientRect().width, 0)
            + Math.max(0, children.length - 1) * (parseFloat(style.columnGap) || 0);
        const available = header.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
        header.classList.remove('dona-header-measuring');
        const compact = window.innerWidth < 900 || required > available - 2;
        const changed = header.classList.contains('dona-header-compact') !== compact;
        header.classList.toggle('dona-header-compact', compact);
        if (changed) {
            const menu = bar.querySelector('.dona-context-menu');
            menu?.removeAttribute('open');
            if (compact && focused?.closest('.dona-header-actions-desktop'))
                bar.querySelector('.dona-context-menu-trigger')?.focus({ preventScroll: true });
            else if (!compact && focused?.closest('.dona-context-menu')) {
                const target = menu && getComputedStyle(menu).display !== 'none'
                    ? menu.querySelector('summary')
                    : bar.querySelector('.dona-header-actions-desktop button, .dona-header-actions-desktop a');
                target?.focus({ preventScroll: true });
            }
        }
    };
    const refresh = () => {
        if (disposed || frame) return;
        frame = requestAnimationFrame(() => {
            frame = 0;
            if (needsFit) { needsFit = false; fitActions(); }
            header.classList.toggle('has-content-above', boundary.getBoundingClientRect().top < -2);
            document.documentElement.style.setProperty('--dona-sticky-header-height', `${header.offsetHeight}px`);
        });
    };
    const layoutChanged = () => { needsFit = true; refresh(); };
    const resize = new ResizeObserver(layoutChanged);
    resize.observe(header);
    resize.observe(document.body);
    const content = new MutationObserver(records => {
        if (records.some(record => record.type !== "attributes" || record.target !== header)) layoutChanged();
    });
    content.observe(header, { childList: true, characterData: true, subtree: true, attributes: true, attributeFilter: ["class", "disabled"] });
    document.fonts?.ready.then(layoutChanged);
    document.addEventListener('scroll', refresh, { capture: true, passive: true });
    window.addEventListener('resize', layoutChanged);

    const closeMenu = event => {
        const action = event.target.closest('button, a');
        const menu = action?.closest('.dona-context-menu[open]');
        if (menu && !action.disabled) {
            // Keep native submit/navigation behavior; only dismiss the disclosure.
            menu.removeAttribute('open');
            menu.querySelector('summary')?.focus({ preventScroll: true });
        }
    };
    header.addEventListener('click', closeMenu);
    const focusInvalid = event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) return;
        // Blazor renders validation messages asynchronously after submit.
        const pending = new MutationObserver(() => {
            const invalid = form.querySelector('[aria-invalid="true"], input.invalid, select.invalid, textarea.invalid');
            if (invalid) { invalid.focus(); invalid.scrollIntoView({ block: 'center', behavior: 'auto' }); pending.disconnect(); }
        });
        pending.observe(form, { subtree: true, attributes: true, childList: true });
        const timer = setTimeout(() => { pending.disconnect(); validations.delete(cleanup); }, 1500);
        const cleanup = () => { clearTimeout(timer); pending.disconnect(); };
        validations.add(cleanup);
    };
    const validations = new Set();
    document.addEventListener('submit', focusInvalid, true);
    refresh();
    return {
        dispose() {
            disposed = true;
            cancelAnimationFrame(frame);
            content.disconnect();
            resize.disconnect();
            document.removeEventListener('scroll', refresh, true);
            window.removeEventListener('resize', layoutChanged);
            header.removeEventListener('click', closeMenu);
            document.removeEventListener('submit', focusInvalid, true);
            for (const cleanup of validations) cleanup();
        }
    };
}
