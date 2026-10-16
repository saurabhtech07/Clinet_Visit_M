/* Searchable dropdown (lightweight, koi library nahi).
   Usage:  <select data-searchable data-placeholder="Search lead..."> ... </select>
   Native <select> chhupa rehta hai, isliye form submit / FormData waise hi kaam karta hai. */
(function () {
    if (window.SearchableSelect) return;

    var STYLE_ID = "ss-styles";

    function injectStyles() {
        if (document.getElementById(STYLE_ID)) return;
        var css =
            ".ss-wrap{position:relative}" +
            ".ss-wrap select.ss-native{display:none!important}" +
            ".ss-list{display:none;position:absolute;z-index:1080;left:0;right:0;top:calc(100% + 4px);" +
            "max-height:230px;overflow-y:auto;background:#fff;border:1px solid #e2e8f0;border-radius:9px;" +
            "box-shadow:0 10px 26px rgba(15,23,42,.14);padding:4px}" +
            ".ss-wrap.ss-open .ss-list{display:block}" +
            ".ss-item{padding:7px 10px;font-size:13px;border-radius:7px;cursor:pointer;color:#0f172a;" +
            "white-space:nowrap;overflow:hidden;text-overflow:ellipsis}" +
            ".ss-item:hover,.ss-item.ss-active{background:#eff6ff;color:#1d4ed8}" +
            ".ss-empty{padding:7px 10px;font-size:12.5px;color:#94a3b8}" +
            ".ss-input{cursor:text}";
        var el = document.createElement("style");
        el.id = STYLE_ID;
        el.textContent = css;
        document.head.appendChild(el);
    }

    function enhance(select) {
        if (!select || select.dataset.ssReady === "1") return;
        injectStyles();
        select.dataset.ssReady = "1";

        var wrap = document.createElement("div");
        wrap.className = "ss-wrap";
        select.parentNode.insertBefore(wrap, select);
        wrap.appendChild(select);
        select.classList.add("ss-native");

        var input = document.createElement("input");
        input.type = "text";
        input.className = "form-control ss-input";
        input.setAttribute("autocomplete", "off");
        input.setAttribute("placeholder", select.dataset.placeholder || "Search...");
        wrap.appendChild(input);

        var list = document.createElement("div");
        list.className = "ss-list";
        wrap.appendChild(list);

        function opts() { return Array.prototype.slice.call(select.options); }
        function text(o) { return (o.textContent || "").trim(); }

        function syncInput() {
            var o = select.options[select.selectedIndex];
            input.value = (o && o.value) ? text(o) : "";
        }

        function render(filter) {
            var f = (filter || "").toLowerCase().trim();
            list.innerHTML = "";
            var count = 0;
            opts().forEach(function (o) {
                if (!o.value) return;                                  // "-- Select --" skip
                if (f && text(o).toLowerCase().indexOf(f) === -1) return;
                var item = document.createElement("div");
                item.className = "ss-item" + (o.selected ? " ss-active" : "");
                item.textContent = text(o);
                item.addEventListener("mousedown", function (e) {
                    e.preventDefault();                                // input ka blur rok do
                    select.value = o.value;
                    select.dispatchEvent(new Event("change", { bubbles: true }));
                    syncInput();
                    list.innerHTML = "";
                    wrap.classList.remove("ss-open");
                });
                list.appendChild(item);
                count++;
            });
            if (!count) {
                var empty = document.createElement("div");
                empty.className = "ss-empty";
                empty.textContent = "No records found";
                list.appendChild(empty);
            }
        }

        input.addEventListener("focus", function () { render(""); wrap.classList.add("ss-open"); });
        input.addEventListener("input", function () { render(input.value); wrap.classList.add("ss-open"); });
        input.addEventListener("keydown", function (e) {
            if (e.key === "Escape") { wrap.classList.remove("ss-open"); syncInput(); }
            else if (e.key === "Enter") {
                var first = list.querySelector(".ss-item");
                if (first) { e.preventDefault(); first.dispatchEvent(new MouseEvent("mousedown", { bubbles: true })); }
            }
        });
        input.addEventListener("blur", function () {
            setTimeout(function () { wrap.classList.remove("ss-open"); syncInput(); }, 120);
        });

        syncInput();
    }

    function initAll(root) {
        (root || document).querySelectorAll("select[data-searchable]").forEach(enhance);
    }

    // Bahar click karne par band kar do
    document.addEventListener("mousedown", function (e) {
        document.querySelectorAll(".ss-wrap.ss-open").forEach(function (w) {
            if (!w.contains(e.target)) w.classList.remove("ss-open");
        });
    });

    window.SearchableSelect = { enhance: enhance, initAll: initAll };

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", function () { initAll(document); });
    else initAll(document);
})();
