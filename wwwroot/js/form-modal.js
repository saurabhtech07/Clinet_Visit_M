/* ============================================================
   form-modal.js
   Add/Edit forms ko poore page ki jagah ek popup (modal) me kholta hai.
   - Har <a> jiska path /Create ya /Edit ho, use modal me load karta hai.
   - Form ke andar hi script chalti hai (re-execute) so existing
     handlers (Ajax.submit -> loadPage("/List")) waise hi kaam karte hain.
   - Save hone par list page wapas main content area me load hota hai.
   Depends on: bootstrap (Modal), Ajax (ajax-helper.js), window.loadPage.
   ============================================================ */
(function () {
    if (window.FormModal) return;

    // Sirf form wale links match karo: /Client/Create, /Client/Edit/5, /X/Create?...
    var FORM_LINK = /\/(Create|Edit)(\/|\?|$)/i;

    var modalEl = null, bodyEl = null, titleEl = null, backBtn = null, bsModal = null;
    var lastUrl = "";

    function byId(id) { return document.getElementById(id); }

    function ensure() {
        if (bsModal) return true;
        modalEl = byId("appFormModal");
        bodyEl = byId("appFormModalBody");
        titleEl = byId("appFormModalTitle");
        backBtn = byId("appFormModalBack");
        if (!modalEl || !bodyEl) return false;

        bsModal = new bootstrap.Modal(modalEl);
        if (backBtn) backBtn.addEventListener("click", function () { hide(); });
        modalEl.addEventListener("hidden.bs.modal", function () {
            if (bodyEl) bodyEl.innerHTML = "";
            setSize(null);   // normal form width par wapas aa jao
        });
        return true;
    }

    function isOpen() { return !!(modalEl && modalEl.classList.contains("show")); }
    function show() { if (ensure()) bsModal.show(); }
    function hide() { if (bsModal) bsModal.hide(); }

    // Injected HTML ke <script> tags ko dobara chalaye (innerHTML scripts run nahi karta).
    function runScripts(root) {
        root.querySelectorAll("script").forEach(function (old) {
            var s = document.createElement("script");
            if (old.src) { s.src = old.src; s.async = false; }
            else { s.textContent = old.textContent; }
            old.parentNode.replaceChild(s, old);
        });
    }

    // Agar response poora page hai (layout ke saath) to sirf content area nikalo.
    // Agar kisi view me div unbalanced ho (extra </div>) to uska baaki content
    // content-area ke bahar chala jata hai - usse bhi saath le lete hain.
    function contentOf(html) {
        var doc;
        try { doc = new DOMParser().parseFromString(html, "text/html"); }
        catch (e) { return html; }

        var area = doc.getElementById("mainContentArea");
        if (!area) return html;

        var parts = [area.innerHTML];
        var node = area.nextElementSibling;              // spillover nodes (agar koi ho)
        while (node && node.id !== "appFormModal") {
            parts.push(node.outerHTML);
            node = node.nextElementSibling;
        }
        return parts.join("");
    }

    function titleOf(html, fallback) {
        try {
            var doc = new DOMParser().parseFromString(html, "text/html");
            var area = doc.getElementById("mainContentArea") || doc;
            var h = area.querySelector("h1, h2, h3, h4, h5");
            if (h && h.textContent.trim()) return h.textContent.trim();
        } catch (e) { /* ignore */ }
        return fallback || "Form";
    }

    // Modal header me title already aa jata hai (titleOf usi heading se banata hai).
    // Wahi heading form ke andar bhi rehti hai -> title 2 baar dikhta tha. Use chhupa do.
    function hideDuplicateTitle(title) {
        if (!title || !bodyEl) return;
        var hs = bodyEl.querySelectorAll("h1, h2, h3, h4, h5");
        for (var i = 0; i < hs.length; i++) {
            var h = hs[i];
            if (h.textContent.trim() !== title) continue;
            var p = h.parentElement;
            if (p && p !== bodyEl && p.children.length === 1 && p.textContent.trim() === title) {
                p.style.display = "none";   // wrapper sirf title ke liye tha
            } else {
                h.style.display = "none";
            }
            break;
        }
    }

    // Modal width variants: wide for detail popups, narrow for compact popups
    function setSize(opts) {
        var dlg = modalEl && modalEl.querySelector(".modal-dialog");
        if (!dlg) return;
        dlg.classList.remove("app-modal-wide", "app-modal-narrow");
        if (opts && opts.wide) dlg.classList.add("app-modal-wide");
        else if (opts && opts.narrow) dlg.classList.add("app-modal-narrow");
    }

    function load(url, label, opts) {
        if (!ensure()) { window.location.href = url; return Promise.resolve(); }

        lastUrl = url;
        setSize(opts);
        if (titleEl) titleEl.textContent = (label && label.length < 40) ? label : "Loading…";
        bodyEl.innerHTML = '<div class="text-center py-5 text-muted">' +
            '<div class="spinner-border spinner-border-sm me-2"></div>Loading…</div>';
        show();

        return fetch(url, { headers: { "X-Requested-With": "XMLHttpRequest" } })
            .then(function (r) {
                if (!r.ok) throw new Error("HTTP " + r.status);
                return r.text();
            })
            .then(function (html) {
                bodyEl.innerHTML = contentOf(html);
                runScripts(bodyEl);
                var title = titleOf(html, (label && label.length < 40) ? label : "Form");
                if (titleEl) titleEl.textContent = title;
                hideDuplicateTitle(title);
                bodyEl.scrollTop = 0;
                var dlg = modalEl.querySelector(".modal-dialog");
                if (dlg) dlg.scrollTop = 0;
            })
            .catch(function (err) {
                console.error("Form modal load failed:", err);
                if (window.Ajax && Ajax.toast) Ajax.toast("Could not load the form.", "error");
                hide();
            });
    }

    function open(url, label, opts) { return load(url, label, opts); }

    // Current list page ko main content area me wapas load karo (original loadPage se).
    function refreshCurrent() {
        var url = window.location.pathname + window.location.search;
        var lp = window.loadPage;
        var orig = lp && lp.__fmOriginal;
        if (typeof orig === "function") orig(url);
        else if (typeof lp === "function") lp(url);
        else window.location.reload();
    }

    // loadPage ko wrap karo: modal khula ho to form usi modal me load ho,
    // normal page (list/report) ho to modal band karke main area me jaaye.
    function wrapLoadPage() {
        if (typeof window.loadPage !== "function" || window.loadPage.__fmWrapped) return;
        var orig = window.loadPage;
        var wrapped = function (url, label) {
            if (isOpen()) {
                var path = String(url || "").split("?")[0];
                if (FORM_LINK.test(path)) return load(url, label);
                hide();
            }
            return orig.apply(this, arguments);
        };
        wrapped.__fmWrapped = true;
        wrapped.__fmOriginal = orig;
        window.loadPage = wrapped;
    }

    // ---- Add / Edit links ko intercept karo ----
    document.addEventListener("click", function (e) {
        if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        var a = e.target.closest("a");
        if (!a) return;
        if (a.classList.contains("nav-link")) return;              // sidebar nav -> normal
        if (a.hasAttribute("data-no-modal") || a.hasAttribute("download")) return;
        var target = a.getAttribute("target");
        if (target && target !== "_self") return;
        var href = a.getAttribute("href");
        if (!href || href.charAt(0) === "#") return;
        if (/^(javascript|mailto|tel):/i.test(href)) return;

        var path = href.split("?")[0];
        if (!FORM_LINK.test(path)) return;

        e.preventDefault();
        open(href, (a.textContent || "").trim());
    });

    // ---- Fallback: jis form ka apna handler na ho ----
    document.addEventListener("submit", function (e) {
        var form = e.target;
        if (!form || form.tagName !== "FORM") return;
        if (!isOpen() || !modalEl.contains(form)) return;
        if (e.defaultPrevented) return;               // form ka apna Ajax handler already chal chuka
        e.preventDefault();
        var action = form.getAttribute("action") || lastUrl || window.location.pathname;
        if (window.Ajax && Ajax.submit) {
            Ajax.submit({
                url: action,
                form: form,
                onSuccess: function () { hide(); refreshCurrent(); }
            });
        }
    });

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", wrapLoadPage);
    else wrapLoadPage();

    window.FormModal = { open: open, load: load, show: show, hide: hide, isOpen: isOpen };
})();
