window.orbitModal = (() => {
    let lockCount = 0;
    let scrollY = 0;

    function acquire() {
        lockCount += 1;
        if (lockCount !== 1)
            return;

        scrollY = window.scrollY;
        document.body.style.top = `-${scrollY}px`;
        document.body.classList.add("orbit-modal-open");
    }

    function release() {
        if (lockCount === 0)
            return;

        lockCount -= 1;
        if (lockCount !== 0)
            return;

        document.body.classList.remove("orbit-modal-open");
        document.body.style.removeProperty("top");
        window.scrollTo({ top: scrollY, left: 0, behavior: "auto" });
    }

    const desktop = window.matchMedia("(min-width: 900px)");

    const anchored = new Map();
    function closeSelect(dialogId) {
        anchored.get(dialogId)?.();
        anchored.delete(dialogId);
    }
    // Anchors the select popover under its trigger (desktop only) and moves focus into it.
    function openSelect(triggerId, dialogId) {
        const trigger = document.getElementById(triggerId);
        const dialog = document.getElementById(dialogId);
        if (!trigger || !dialog)
            return;

        closeSelect(dialogId);
        const position = () => {
            if (!desktop.matches) return;
            const rect = trigger.getBoundingClientRect();
            const margin = 8;
            const below = window.innerHeight - rect.bottom - margin;
            const above = rect.top - margin;
            const flip = below < 200 && above > below;
            const width = Math.min(Math.max(rect.width, 220), window.innerWidth - margin * 2);
            const left = Math.max(margin, Math.min(rect.left, window.innerWidth - width - margin));
            dialog.style.setProperty("--select-left", `${left}px`);
            dialog.style.setProperty("--select-width", `${width}px`);
            dialog.style.setProperty("--select-top", flip ? "auto" : `${rect.bottom + 4}px`);
            dialog.style.setProperty("--select-bottom", flip ? `${window.innerHeight - rect.top + 4}px` : "auto");
            dialog.style.setProperty("--select-max", `${Math.max(0, Math.min(420, (flip ? above : below) - 4))}px`);
        };
        const frame = requestAnimationFrame(position);
        const observer = new ResizeObserver(position);
        observer.observe(trigger);
        observer.observe(document.documentElement);
        window.addEventListener("resize", position);
        window.addEventListener("scroll", position, true);
        desktop.addEventListener("change", position);
        anchored.set(dialogId, () => {
            cancelAnimationFrame(frame);
            observer.disconnect();
            window.removeEventListener("resize", position);
            window.removeEventListener("scroll", position, true);
            desktop.removeEventListener("change", position);
        });
        position();

        if (!dialog.dataset.keys) {
            dialog.dataset.keys = "1";
            dialog.addEventListener("keydown", event => {
                const keys = ["ArrowDown", "ArrowUp", "Home", "End"];
                if (!keys.includes(event.key))
                    return;
                const items = Array.from(dialog.querySelectorAll("[role=option]"));
                if (items.length === 0)
                    return;
                event.preventDefault();
                const index = items.indexOf(document.activeElement);
                let next;
                if (event.key === "Home") next = 0;
                else if (event.key === "End") next = items.length - 1;
                else if (event.key === "ArrowDown") next = index < 0 ? 0 : Math.min(items.length - 1, index + 1);
                else if (index <= 0) { const search = dialog.querySelector(".orbit-select-search"); if (search) { search.focus(); return; } next = 0; }
                else next = index - 1;
                items[next].focus();
            });
        }

        const search = dialog.querySelector(".orbit-select-search");
        const target = (desktop.matches ? search : null)
            ?? dialog.querySelector("[role=option].selected")
            ?? search
            ?? dialog.querySelector("[role=option]")
            ?? dialog;
        target.focus({ preventScroll: true });
        const selected = dialog.querySelector("[role=option].selected");
        if (selected) selected.scrollIntoView({ block: "nearest" });
    }

    function focusElement(id) {
        const element = document.getElementById(id);
        if (element) element.focus({ preventScroll: true });
    }

    function prepareAutocomplete(inputId) {
        const input = document.getElementById(inputId);
        if (!input || input.dataset.keys) return;
        input.dataset.keys = "1";
        input.addEventListener("keydown", event => {
            if (input.getAttribute("aria-expanded") !== "true") return;
            const list = document.getElementById(input.getAttribute("aria-controls"));
            if (["ArrowDown", "ArrowUp"].includes(event.key)
                || event.key === "Enter" && list?.querySelector("[role=option]"))
                event.preventDefault();
        });
    }

    const confirmations = new Map();
    function closeConfirmation(id) {
        confirmations.get(id)?.();
        confirmations.delete(id);
    }
    function openConfirmation(id) {
        const dialog = document.getElementById(id);
        if (!dialog) return;
        closeConfirmation(id);
        const previousFocus = document.activeElement;
        const buttons = Array.from(dialog.querySelectorAll("button:not(:disabled)"));
        const trap = event => {
            if (event.key !== "Tab" || buttons.length === 0) return;
            const first = buttons[0], last = buttons[buttons.length - 1];
            if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
            else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
        };
        dialog.addEventListener("keydown", trap);
        confirmations.set(id, () => {
            dialog.removeEventListener("keydown", trap);
            if (previousFocus?.isConnected) previousFocus.focus({ preventScroll: true });
        });
        buttons[0]?.focus({ preventScroll: true });
    }

    function closeActionMenu(id, returnFocus = true) {
        const menu = document.getElementById(id);
        if (!menu) return;
        menu.removeAttribute("open");
        if (returnFocus) menu.querySelector("summary")?.focus({ preventScroll: true });
    }
    document.addEventListener("toggle", event => {
        const menu = event.target;
        if (menu.matches?.(".dona-context-menu"))
            menu.querySelector("summary")?.setAttribute("aria-expanded", String(menu.open));
    }, true);
    document.addEventListener("pointerdown", event => {
        document.querySelectorAll(".dona-context-menu[open]").forEach(menu => {
            if (!menu.contains(event.target)) closeActionMenu(menu.id, false);
        });
    });
    document.addEventListener("keydown", event => {
        if (event.key !== "Escape") return;
        const menu = event.target.closest?.(".dona-context-menu[open]");
        if (menu) { event.preventDefault(); closeActionMenu(menu.id); }
    });
    document.addEventListener("focusout", event => {
        const menu = event.target.closest?.(".dona-context-menu[open]");
        if (menu && event.relatedTarget && !menu.contains(event.relatedTarget)) closeActionMenu(menu.id, false);
    });

    return { acquire, release, openSelect, closeSelect, focusElement, prepareAutocomplete, openConfirmation, closeConfirmation, closeActionMenu };
})();
