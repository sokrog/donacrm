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

    // Anchors the select popover under its trigger (desktop only) and moves focus into it.
    function openSelect(triggerId, dialogId) {
        const trigger = document.getElementById(triggerId);
        const dialog = document.getElementById(dialogId);
        if (!trigger || !dialog)
            return;

        if (desktop.matches) {
            const rect = trigger.getBoundingClientRect();
            const margin = 8;
            const below = window.innerHeight - rect.bottom - margin;
            const above = rect.top - margin;
            const flip = below < 200 && above > below;
            const width = Math.max(rect.width, 220);
            const left = Math.max(margin, Math.min(rect.left, window.innerWidth - width - margin));
            dialog.style.setProperty("--select-left", `${left}px`);
            dialog.style.setProperty("--select-width", `${width}px`);
            dialog.style.setProperty("--select-top", flip ? "auto" : `${rect.bottom + 4}px`);
            dialog.style.setProperty("--select-bottom", flip ? `${window.innerHeight - rect.top + 4}px` : "auto");
            dialog.style.setProperty("--select-max", `${Math.max(160, Math.min(320, (flip ? above : below) - 4))}px`);
        }

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

    return { acquire, release, openSelect, focusElement };
})();
