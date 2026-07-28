(() => {
    const normalize = value => new URL(value, location.href).pathname + new URL(value, location.href).search;
    const stack = [normalize(location.href)];
    const pushState = history.pushState.bind(history);
    const replaceState = history.replaceState.bind(history);
    history.pushState = (state, title, url) => { const result = pushState(state, title, url); stack.push(normalize(url ?? location.href)); return result; };
    history.replaceState = (state, title, url) => { const result = replaceState(state, title, url); stack[stack.length - 1] = normalize(url ?? location.href); return result; };
    addEventListener('popstate', () => { const current = normalize(location.href); const index = stack.lastIndexOf(current); if (index >= 0) stack.splice(index + 1); else stack.push(current); });
    document.addEventListener('click', event => {
        const link = event.target.closest('a.orbit-back-button');
        if (!link || event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
        if (stack.length > 1) { event.preventDefault(); history.back(); }
    }, true);
})();
