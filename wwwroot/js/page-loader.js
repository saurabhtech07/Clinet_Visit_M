/* Slim page loader shown while one table view navigates to another. */
(function () {
    if (window.PageLoader) return;

    var MIN_VISIBLE_MS = 250;   /* avoid a flash on fast responses */

    var el = null;
    var textEl = null;
    var shownAt = 0;
    var hideTimer = null;

    function build() {
        el = document.createElement("div");
        el.className = "app-loader";
        el.id = "appLoader";
        el.setAttribute("role", "status");
        el.setAttribute("aria-live", "polite");
        el.setAttribute("aria-label", "Loading");
        el.innerHTML =
            '<div class="app-loader__bar"></div>' +
            '<div class="app-loader__pill">' +
            '<div class="app-loader__spin"></div>' +
            '<span class="app-loader__text"></span>' +
            '</div>';
        document.body.appendChild(el);
        textEl = el.querySelector(".app-loader__text");
    }

    function show(text) {
        if (!el) build();
        if (hideTimer) { clearTimeout(hideTimer); hideTimer = null; }
        if (textEl) textEl.textContent = text || "Loading…";
        if (!el.classList.contains("is-active")) {
            shownAt = Date.now();
            el.classList.add("is-active");
        }
    }

    function hide(immediate) {
        if (!el) return;
        if (hideTimer) { clearTimeout(hideTimer); hideTimer = null; }
        var wait = immediate ? 0 : Math.max(0, MIN_VISIBLE_MS - (Date.now() - shownAt));
        hideTimer = setTimeout(function () {
            el.classList.remove("is-active");
            hideTimer = null;
        }, wait);
    }

    /* Keeps the loader up for the whole life of a promise. */
    function wrap(promise, text) {
        show(text);
        return Promise.resolve(promise)
            .then(function (v) { hide(); return v; })
            .catch(function (e) { hide(); throw e; });
    }

    /* A real navigation wipes the DOM, so drop the loader immediately. */
    function drop() { if (el) el.classList.remove("is-active"); }
    window.addEventListener("beforeunload", drop);
    window.addEventListener("pagehide", drop);

    window.PageLoader = { show: show, hide: hide, wrap: wrap };
})();
