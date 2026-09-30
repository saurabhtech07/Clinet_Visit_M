/*!
 * ExcelCsvToGrid - reusable Excel/CSV -> grid component.
 * Parses fully in the browser (SheetJS for .xlsx/.xls, built-in RFC-4180
 * parser for .csv). No server round-trip, so IIS upload limits never apply.
 *
 * Usage:
 *   ExcelCsvToGrid.init({
 *       fileInputId: 'excelFile',
 *       dropZoneId : 'dropZone',
 *       gridId     : 'gridContainer',
 *       searchId   : 'gridSearch',
 *       infoId     : 'gridInfo',
 *       pagerId    : 'gridPager',
 *       pageSize   : 50
 *   });
 *   ExcelCsvToGrid.getRecords();  // every parsed row, as an array of arrays
 *   ExcelCsvToGrid.clear();       // drop the file and return to the empty state
 */
(function (global) {
    'use strict';

    var DEFAULTS = {
        fileInputId: 'excelFile',
        dropZoneId: 'dropZone',
        gridId: 'gridContainer',
        searchId: 'gridSearch',
        infoId: 'gridInfo',
        pagerId: 'gridPager',
        pageSize: 50,
        largeFileRows: 50000,
        emptyText: 'No rows to show.',
        statusId: 'gridStatus',
        clearId: 'gridClear',
        exportId: 'gridExport',
        maxFileBytes: 0,
        onRender: null            /* function(info) - lets the host page sync its own stat cards */
    };

    /* ------------------------------------------------------------------
     * CSV parser - RFC 4180 style.
     * Handles quoted fields, escaped quotes (""), CRLF/LF line endings and
     * newlines inside quotes. A naive split(',') corrupts any file written
     * by this project's own CSV export, so this must not be simplified.
     * ------------------------------------------------------------------ */
    function parseCsv(text) {
        if (text.charCodeAt(0) === 0xFEFF) text = text.slice(1);

        var rows = [], row = [], field = '', inQuotes = false, i = 0, len = text.length;

        while (i < len) {
            var ch = text.charAt(i);

            if (inQuotes) {
                if (ch === '"') {
                    if (text.charAt(i + 1) === '"') { field += '"'; i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                field += ch; i++; continue;
            }

            if (ch === '"') { inQuotes = true; i++; continue; }
            if (ch === ',') { row.push(field); field = ''; i++; continue; }
            if (ch === '\r') { i++; continue; }
            if (ch === '\n') {
                row.push(field); rows.push(row);
                row = []; field = ''; i++; continue;
            }
            field += ch; i++;
        }

        if (field !== '' || row.length) { row.push(field); rows.push(row); }
        return rows;
    }

    function isBlankRow(cells) {
        for (var i = 0; i < cells.length; i++) {
            var v = cells[i];
            if (v !== null && v !== undefined && String(v).trim() !== '') return false;
        }
        return true;
    }

    /* Unique, non-empty column names: "Amount", "Amount (2)". */
    function normalizeHeaders(cells) {
        var seen = {}, out = [];
        for (var i = 0; i < cells.length; i++) {
            var name = (cells[i] === null || cells[i] === undefined) ? '' : String(cells[i]).trim();
            if (!name) name = 'Column ' + (i + 1);
            if (seen[name] === undefined) { seen[name] = 0; out.push(name); }
            else { seen[name]++; out.push(name + ' (' + (seen[name] + 1) + ')'); }
        }
        return out;
    }

    function pad(n) { return (n < 10 ? '0' : '') + n; }

    function formatBytes(bytes) {
        if (!bytes && bytes !== 0) return '';
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
        return (bytes / 1048576).toFixed(2) + ' MB';
    }

    function Grid(cfg) {
        this.cfg = cfg;
        this.headers = [];
        this.allRecords = [];   /* every data row from the file */
        this.filtered = [];     /* rows left after the search box */
        this.sortCol = -1;
        this.sortAsc = true;
        this.page = 1;
        this.fileName = '';
        this.bind();
    }

    Grid.prototype.byId = function (id) { return id ? document.getElementById(id) : null; };

    Grid.prototype.bind = function () {
        var self = this;
        var input = this.byId(this.cfg.fileInputId);
        var zone = this.byId(this.cfg.dropZoneId);

        /* A grid that only shows saved data has no drop zone, so each part is
           optional: search, page size and buttons must still be wired up. */
        if (input && zone) {
            zone.addEventListener('click', function () { input.click(); });
            zone.addEventListener('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); input.click(); }
            });

            input.addEventListener('change', function () {
                if (input.files && input.files.length) self.loadFile(input.files[0]);
            });

            ['dragenter', 'dragover'].forEach(function (evt) {
                zone.addEventListener(evt, function (e) {
                    e.preventDefault(); e.stopPropagation();
                    zone.classList.add('is-dragover');
                });
            });
            ['dragleave', 'drop'].forEach(function (evt) {
                zone.addEventListener(evt, function (e) {
                    e.preventDefault(); e.stopPropagation();
                    if (evt === 'dragleave' && zone.contains(e.relatedTarget)) return;
                    zone.classList.remove('is-dragover');
                });
            });

            zone.addEventListener('drop', function (e) {
                var files = e.dataTransfer && e.dataTransfer.files;
                if (files && files.length) { self.loadFile(files[0]); input.value = ''; }
            });
        }

        var search = this.byId(this.cfg.searchId);
        if (search) {
            var t = null;
            search.addEventListener('input', function () {
                clearTimeout(t);
                t = setTimeout(function () { self.applySearch(search.value); }, 250);
            });
        }

        var clear = this.byId(this.cfg.clearId);
        if (clear) {
            clear.addEventListener('click', function () { self.clearAll(); });
        }

        var exp = this.byId(this.cfg.exportId);
        if (exp) {
            exp.addEventListener('click', function () { self.exportXlsx(); });
        }
    };

    Grid.prototype.loadFile = function (file) {
        var self = this;
        var name = (file.name || '').toLowerCase();
        var ext = name.slice(name.lastIndexOf('.') + 1);

            if (['csv', 'xlsx', 'xls', 'xlsm'].indexOf(ext) === -1) {
                this.warn('Unsupported file type. Use .csv, .xlsx or .xls.');
                return;
            }

            /* The parse runs on the main thread, so an oversized file would
               freeze the page before any row limit could be applied. */
            if (this.cfg.maxFileBytes && file.size > this.cfg.maxFileBytes) {
                this.warn('That file is ' + formatBytes(file.size) + '. The limit is ' +
                          formatBytes(this.cfg.maxFileBytes) + '.');
                return;
            }


        this.fileName = file.name;
        this.status('Reading ' + file.name + ' (' + formatBytes(file.size) + ')…');

        if (ext === 'csv') {
            var reader = new FileReader();
            reader.onload = function () { self.ingest(parseCsv(String(reader.result))); };
            reader.onerror = function () { self.warn('Could not read the file.'); };
            reader.readAsText(file);
            return;
        }

        if (typeof global.XLSX === 'undefined') {
            this.warn('Excel engine not loaded (xlsx.full.min.js).');
            return;
        }

        var r2 = new FileReader();
        r2.onload = function () {
            try {
                var wb = global.XLSX.read(new Uint8Array(r2.result), { type: 'array' });
                var sheet = wb.Sheets[wb.SheetNames[0]];
                if (!sheet) { self.warn('That workbook has no readable sheet.'); return; }
                self.ingest(global.XLSX.utils.sheet_to_json(sheet, { header: 1, raw: false, defval: '' }));
            } catch (err) {
                self.warn('Could not parse the workbook: ' + err.message);
            }
        };
        r2.onerror = function () { self.warn('Could not read the file.'); };
        r2.readAsArrayBuffer(file);
    };

    Grid.prototype.ingest = function (matrix) {
        if (!matrix || !matrix.length) { this.warn('That file is empty.'); return; }

        var headerIdx = 0;
        while (headerIdx < matrix.length && isBlankRow(matrix[headerIdx])) headerIdx++;
        if (headerIdx >= matrix.length) { this.warn('That file has no header row.'); return; }

        this.headers = normalizeHeaders(matrix[headerIdx]);

        var rows = [];
        for (var i = headerIdx + 1; i < matrix.length; i++) {
            if (isBlankRow(matrix[i])) continue;
            var row = matrix[i] || [];
            var padded = new Array(this.headers.length);
            for (var c = 0; c < this.headers.length; c++) {
                var v = row[c];
                padded[c] = (v === null || v === undefined) ? '' : (v instanceof Date ? v.toISOString().slice(0, 10) : v);
            }
            rows.push(padded);
        }

        this.allRecords = rows;
        this.filtered = rows.slice();
        this.sortCol = -1;
        this.page = 1;

        var search = this.byId(this.cfg.searchId);
        if (search) search.value = '';

        this.render();
        this.status(this.fileName + ' loaded.', 'ok');

        if (rows.length > this.cfg.largeFileRows) {
            this.warn('Large file: ' + rows.length.toLocaleString() + ' rows. Only the current page is drawn, so the browser stays responsive.');
        }
    };

    Grid.prototype.applySearch = function (term) {
        var q = (term || '').trim().toLowerCase();
        if (!q) { this.filtered = this.allRecords.slice(); }
        else {
            var rows = this.allRecords.filter(function (r) {
                for (var i = 0; i < r.length; i++) {
                    if (r[i] !== null && r[i] !== undefined && String(r[i]).toLowerCase().indexOf(q) !== -1) return true;
                }
                return false;
            });
            this.filtered = rows;
        }
        this.page = 1;
        this.render();
    };

    Grid.prototype.sortBy = function (col) {
        if (this.sortCol === col) this.sortAsc = !this.sortAsc;
        else { this.sortCol = col; this.sortAsc = true; }

        var asc = this.sortAsc;
        this.filtered.sort(function (a, b) {
            var av = a[col] === null || a[col] === undefined ? '' : String(a[col]).trim();
            var bv = b[col] === null || b[col] === undefined ? '' : String(b[col]).trim();

            if (av !== '' && bv !== '') {
                var an = Number(av), bn = Number(bv);
                if (!isNaN(an) && !isNaN(bn)) return asc ? an - bn : bn - an;

                var ad = Date.parse(av), bd = Date.parse(bv);
                if (!isNaN(ad) && !isNaN(bd)) return asc ? ad - bd : bd - ad;
            }
            return asc ? av.localeCompare(bv) : bv.localeCompare(av);
        });
        this.page = 1;
        this.render();
    };

    Grid.prototype.render = function () {
        var host = this.byId(this.cfg.gridId);
        if (!host) return;

        var exp = this.byId(this.cfg.exportId);
        if (exp) exp.disabled = !this.headers.length;

        /* No file loaded yet, or cleared: tear the table down instead of returning,
           otherwise the previous table would stay on screen after a reset. */
        if (!this.headers.length) {
            var blank = document.createElement('div');
            blank.className = 'imp-empty';
            blank.textContent = this.cfg.emptyText;
            host.textContent = '';
            host.appendChild(blank);
            var pagerBox = this.byId(this.cfg.pagerId);
            if (pagerBox) pagerBox.innerHTML = '';
            this.renderInfo(0);
            return;
        }

        var size = this.cfg.pageSize;
        var total = this.filtered.length;
        var pages = Math.max(1, Math.ceil(total / size));
        if (this.page > pages) this.page = pages;
        var start = (this.page - 1) * size;
        var pageRows = this.filtered.slice(start, start + size);

        var table = document.createElement('table');
        table.className = 'table imp-table mb-0 align-middle';

        /* head - built with textContent, never innerHTML (file data is untrusted) */
        var thead = document.createElement('thead');
        var htr = document.createElement('tr');
        htr.appendChild(cell('th', '#', 'imp-idx'));

        for (var i = 0; i < this.headers.length; i++) {
            var th = document.createElement('th');
            th.appendChild(document.createTextNode(this.headers[i]));

            var ic = document.createElement('span');
            ic.className = 'sort-icon';
            ic.textContent = (this.sortCol === i) ? (this.sortAsc ? '↑' : '↓') : '↕';
            th.appendChild(ic);

            if (this.sortCol === i) th.classList.add('active');

            var self = this;
            th.addEventListener('click', (function (idx) {
                return function () { self.sortBy(idx); };
            })(i));

            htr.appendChild(th);
        }
        thead.appendChild(htr);

        /* body */
        var tbody = document.createElement('tbody');
        if (!pageRows.length) {
            var er = document.createElement('tr');
            var etd = document.createElement('td');
            etd.colSpan = this.headers.length + 1;
            etd.className = 'imp-empty';
            etd.textContent = this.cfg.emptyText;
            er.appendChild(etd);
            tbody.appendChild(er);
        } else {
            var frag = document.createDocumentFragment();
            pageRows.forEach(function (r, ri) {
                var tr = document.createElement('tr');
                tr.appendChild(cell('td', String(start + ri + 1), 'imp-idx'));
                for (var c = 0; c < this.headers.length; c++) {
                    tr.appendChild(cell('td', r[c] === null || r[c] === undefined ? '' : String(r[c]), ''));
                }
                frag.appendChild(tr);
            }, this);
            tbody.appendChild(frag);
        }

        table.appendChild(thead);
        table.appendChild(tbody);

        host.innerHTML = '';
        host.appendChild(table);

        this.renderInfo(total);
        this.renderPager(pages);

        if (typeof this.cfg.onRender === 'function') {
            try {
                this.cfg.onRender({
                    totalRows: this.allRecords.length,
                    columns: this.headers.length,
                    matching: total,
                    page: this.page,
                    pages: pages,
                    fileName: this.fileName
                });
            } catch (err) {
                if (global.console) global.console.error('ExcelCsvToGrid onRender failed:', err);
            }
        }
    };

    function cell(tag, text, cls) {
        var n = document.createElement(tag);
        if (cls) n.className = cls;
        n.textContent = text;
        return n;
    }

    /* ------------------------------------------------------------- empty view */

    Grid.prototype.showEmpty = function (text) {
        var host = this.byId(this.cfg.gridId);
        if (!host) return;
        var blank = document.createElement('div');
        blank.className = 'imp-empty';
        blank.textContent = text || this.cfg.emptyText;
        host.textContent = '';
        host.appendChild(blank);
        var pagerBox = this.byId(this.cfg.pagerId);
        if (pagerBox) pagerBox.innerHTML = '';
        this.renderInfo(0);
    };

    Grid.prototype.renderInfo = function (shown) {
        var box = this.byId(this.cfg.infoId);
        if (!box) return;
        box.textContent = '';

        function stat(value, label) {
            var w = document.createElement('span');
            w.className = 'imp-stat';
            var b = document.createElement('b');
            b.textContent = value;
            var s = document.createElement('small');
            s.textContent = label;
            w.appendChild(b);
            w.appendChild(s);
            return w;
        }

        var totalRows = this.allRecords.length;
        box.appendChild(stat(totalRows.toLocaleString(), 'rows'));
        box.appendChild(stat(String(this.headers.length), 'columns'));

        if (shown !== totalRows) {
            box.appendChild(stat(shown.toLocaleString(), 'matching'));
        }
        if (this.fileName) {
            var f = document.createElement('span');
            f.className = 'imp-file';
            f.textContent = this.fileName;
            box.appendChild(f);
        }
    };

    Grid.prototype.renderPager = function (pages) {
        var box = this.byId(this.cfg.pagerId);
        if (!box) return;
        box.innerHTML = '';
        if (pages <= 1) return;

        var self = this;
        function btn(label, page, on, disabled) {
            var b = document.createElement('button');
            b.type = 'button';
            b.className = 'btn imp-page' + (on ? ' active' : '');
            b.textContent = label;
            b.disabled = !!disabled;
            if (!disabled) b.addEventListener('click', function () { self.page = page; self.render(); });
            return b;
        }

        var from = Math.max(1, this.page - 2);
        var to = Math.min(pages, from + 4);
        from = Math.max(1, to - 4);

        box.appendChild(btn('‹', this.page - 1, false, this.page === 1));
        for (var p = from; p <= to; p++) box.appendChild(btn(String(p), p, p === this.page, false));
        box.appendChild(btn('›', this.page + 1, false, this.page === pages));
    };

    Grid.prototype.status = function (msg, kind) {
        var box = this.byId(this.cfg.statusId);
        if (!box) return;
        box.className = 'imp-status' + (kind ? ' is-' + kind : '');
        box.textContent = msg || '';
        box.style.display = msg ? '' : 'none';
    };

    Grid.prototype.warn = function (msg) { this.status(msg, 'err'); };

    /* Changes the page size without re-binding listeners or losing data. */
    Grid.prototype.setPageSize = function (n) {
        var size = parseInt(n, 10);
        if (isNaN(size) || size < 1) return;
        this.cfg.pageSize = size;
        this.page = 1;
        this.render();
    };

    /* Writes whatever the search currently matches back out as an .xlsx file. */
    Grid.prototype.exportXlsx = function () {
        if (!global.XLSX) { this.warn('Excel engine is still loading. Try again in a moment.'); return; }

        var rows = this.filtered.length ? this.filtered : this.allRecords;
        if (!rows.length) { this.warn('Nothing to export yet. Load a file first.'); return; }

        var base = this.fileName ? this.fileName.replace(/\.[^.]+$/, '') : 'export';
        var aoa = [this.headers.slice()].concat(rows);
        var wb = global.XLSX.utils.book_new();
        global.XLSX.utils.book_append_sheet(wb, global.XLSX.utils.aoa_to_sheet(aoa), 'Sheet1');
        var out = global.XLSX.write(wb, { bookType: 'xlsx', type: 'array' });

        var url = URL.createObjectURL(new Blob([out], { type: 'application/octet-stream' }));
        var a = document.createElement('a');
        a.href = url;
        a.download = base + '.xlsx';
        document.body.appendChild(a);
        a.click();
        a.parentNode.removeChild(a);
        setTimeout(function () { URL.revokeObjectURL(url); }, 1000);

        this.status('Exported ' + rows.length.toLocaleString() + ' rows to ' + base + '.xlsx', 'ok');
    };

    /* Drops the loaded file and every derived state so the page is back to empty. */
    Grid.prototype.clearAll = function () {
        this.headers = [];
        this.allRecords = [];
        this.filtered = [];
        this.sortCol = -1;
        this.sortAsc = true;
        this.page = 1;
        this.fileName = '';

        var search = this.byId(this.cfg.searchId);
        if (search) search.value = '';

        var input = this.byId(this.cfg.fileInputId);
        if (input) input.value = '';

        var zone = this.byId(this.cfg.dropZoneId);
        if (zone) zone.classList.remove('is-dragover');

        this.render();
        this.status('', null);
    };

    /* ---------------------------------------------------------------- */
    var instance = null;

    function init(options) {
        var cfg = {};
        Object.keys(DEFAULTS).forEach(function (k) { cfg[k] = DEFAULTS[k]; });
        if (options) {
            Object.keys(options).forEach(function (k) { cfg[k] = options[k]; });
        }
        instance = new Grid(cfg);
        instance.render();
        return instance;
    }

    global.ExcelCsvToGrid = {
        init: init,
        parseCsv: parseCsv,
        normalizeHeaders: normalizeHeaders,
        getRecords: function () { return instance ? instance.allRecords : []; },
        getHeaders: function () { return instance ? instance.headers.slice() : []; },
        setPageSize: function (n) { if (instance) instance.setPageSize(n); },
        clear: function () { if (instance) instance.clearAll(); },
        getAllRecords: function () { return instance ? instance.allRecords : []; },
        getFileName: function () { return instance ? instance.fileName : ''; }
    };
})(typeof window !== 'undefined' ? window : this);
