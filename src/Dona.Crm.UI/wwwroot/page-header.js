export function observe(header, boundary) {
    if (!header?.isConnected || !boundary?.isConnected) return { dispose() {} };
    let frame = 0;
    const refresh = () => {
        if (frame) return;
        frame = requestAnimationFrame(() => {
            frame = 0;
            header.classList.toggle('has-content-above', boundary.getBoundingClientRect().top < -2);
            document.documentElement.style.setProperty('--dona-sticky-header-height', `${header.offsetHeight}px`);
        });
    };
    const resize = new ResizeObserver(refresh);
    resize.observe(header);
    resize.observe(document.body);
    document.addEventListener('scroll', refresh, { capture: true, passive: true });
    window.addEventListener('resize', refresh);

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
            cancelAnimationFrame(frame);
            resize.disconnect();
            document.removeEventListener('scroll', refresh, true);
            window.removeEventListener('resize', refresh);
            header.removeEventListener('click', closeMenu);
            document.removeEventListener('submit', focusInvalid, true);
            for (const cleanup of validations) cleanup();
        }
    };
}
