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

    return { acquire, release };
})();
