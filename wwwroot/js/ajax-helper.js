/* Shared AJAX helper - loaded once from _Layout so it survives SPA navigation. */
(function () {
    if (window.Ajax) return;

    function token() {
        var el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : "";
    }

    function toast(message, type) {
        if (!message) return;
        var color = type === "error" ? "danger" : (type === "info" ? "primary" : "success");
        var id = "ajaxToast" + Date.now();
        var el = document.createElement("div");
        el.id = id;
        el.className = "position-fixed top-0 end-0 p-3";
        el.style.zIndex = "1080";
        el.innerHTML = '<div id="' + id + '" class="toast align-items-center text-bg-' + color + ' border-0 show" role="alert">' +
            '<div class="d-flex"><div class="toast-body"></div>' +
            '<button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast"></button></div></div>';
        document.body.appendChild(el);
        var node = el.firstElementChild;
        node.querySelector(".toast-body").textContent = message;
        setTimeout(function () {
            if (node && node.parentNode) node.parentNode.removeChild(node);
            if (el.parentNode) el.parentNode.removeChild(el);
        }, 3500);
    }

    function readError(res, fallback) {
        if (res && res.message) return res.message;
        if (res && res.title) return res.title;
        if (res && res.errors && typeof res.errors === "object") {
            for (var k in res.errors) { if (res.errors[k] && res.errors[k].length) return res.errors[k][0]; }
        }
        return fallback;
    }

    function get(url) {
        return fetch(url, { headers: { "X-Requested-With": "XMLHttpRequest" } })
            .then(function (r) { if (!r.ok) throw new Error("HTTP " + r.status); return r.text(); });
    }

    function post(url, formData) {
        return fetch(url, {
            method: "POST",
            headers: {
                "X-Requested-With": "XMLHttpRequest",
                "RequestVerificationToken": token()
            },
            body: formData
        }).then(function (r) {
            return r.text().then(function (text) {
                var data = null;
                try { data = text ? JSON.parse(text) : null; } catch (e) { data = null; }
                if (!r.ok) throw new Error(readError(data, "Request failed (HTTP " + r.status + ")"));
                if (!data) throw new Error("Invalid server response.");
                return data;
            });
        });
    }

    /* opts: { url, form, button, onSuccess(data), onError(msg), successMessage } */
    function submit(opts) {
        opts = opts || {};
        var form = typeof opts.form === "string" ? document.querySelector(opts.form) : opts.form;
        if (!form) { toast("Form not found.", "error"); return Promise.resolve(null); }

        var button = opts.button || form.querySelector('[type="submit"]');
        if (button) button.disabled = true;

        return post(opts.url, new FormData(form))
            .then(function (data) {
                if (button) button.disabled = false;
                if (data.success === false) throw new Error(readError(data, "Save failed."));
                toast(opts.successMessage || data.message || "Saved successfully.", "success");
                if (opts.onSuccess) opts.onSuccess(data);
                return data;
            })
            .catch(function (err) {
                if (button) button.disabled = false;
                toast(err.message, "error");
                if (opts.onError) opts.onError(err.message);
                return null;
            });
    }

    function postSimple(url, successMessage, onSuccess) {
        return post(url, new FormData())
            .then(function (data) {
                if (data.success === false) throw new Error(readError(data, "Request failed."));
                toast(successMessage || data.message || "Done.", "success");
                if (onSuccess) onSuccess(data);
                return data;
            })
            .catch(function (err) {
                toast(err.message, "error");
                return null;
            });
    }

    /* Delegated delete handler: intercepts any form posting to a /Delete/ route. */
    /* The form's own confirm() (inline onsubmit) runs first; if the user cancels, */
    /* the browser blocks submission and defaultPrevented is already true, so we bail. */
    function refreshTable() {
        if (typeof window.refreshCurrentTable === "function") {
            window.refreshCurrentTable();
        }
    }

    document.addEventListener("submit", function (e) {
        var form = e.target;
        if (!form || form.tagName !== "FORM" || e.defaultPrevented) return;
        var action = form.getAttribute("action") || "";
        if (!/\/Delete\//i.test(action)) return;

        e.preventDefault();
        post(action, new FormData(form))
            .then(function (data) {
                toast(data.message || "Deleted successfully.", "success");
                refreshTable();
            })
            .catch(function (err) { toast(err.message, "error"); });
    });

    /* Same, for <a> links pointing at a /Delete/ route (no form wrapper). */
    document.addEventListener("click", function (e) {
        var link = e.target.closest("a.ajax-delete");
        if (!link) return;
        e.preventDefault();
        if (!confirm(link.getAttribute("data-confirm") || "Are you sure you want to delete this?")) return;
        post(link.getAttribute("href"), new FormData())
            .then(function (data) {
                toast(data.message || "Deleted successfully.", "success");
                refreshTable();
            })
            .catch(function (err) { toast(err.message, "error"); });
    });

    window.Ajax = {
        token: token, toast: toast, get: get, post: post,
        submit: submit, postSimple: postSimple, refreshTable: refreshTable
    };
})();
