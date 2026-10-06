
(function (global) {
    'use strict';

    function byId(id) { return document.getElementById(id); }

    function basePath() {
        var link = document.querySelector('link[href*="css/site.css"]');
        return (link && link.href) ? link.href.replace(/css\/site\.css.*$/, '') : '/';
    }

    function token() {
        var el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function headers(withJson) {
        var h = { 'X-Requested-With': 'XMLHttpRequest' };
        if (token()) h['RequestVerificationToken'] = token();
        if (withJson) h['Content-Type'] = 'application/json';
        return h;
    }

    function readJson(res) {
        return res.text().then(function (t) {
            var data = {};
            if (t) { try { data = JSON.parse(t); } catch (e) { data = { message: t }; } }
            if (!res.ok) {
                var err = new Error(data.message || ('Request failed (' + res.status + ').'));
                err.status = res.status;
                throw err;
            }
            return data;
        });
    }

    function get(path) {
        return fetch(basePath() + path, { headers: headers(false) }).then(readJson);
    }

    function postJson(path, body) {
        return fetch(basePath() + path, {
            method: 'POST',
            headers: headers(true),
            body: JSON.stringify(body)
        }).then(readJson);
    }

    function post(path) {
        return fetch(basePath() + path, { method: 'POST', headers: headers(false) }).then(readJson);
    }

    function fmt(n) { return Number(n || 0).toLocaleString(); }

    function setStatus(id, msg, kind) {
        var box = byId(id);
        if (!box) return;
        box.className = 'imp-status' + (kind ? ' is-' + kind : '');
        box.textContent = msg || '';
        box.style.display = msg ? '' : 'none';
    }

    function mkEmpty(text) {
        var n = document.createElement('div');
        n.className = 'imp-empty';
        n.textContent = text;
        return n;
    }

    function extIcon(fileName) {
        var ext = String(fileName || '').split('.').pop().toLowerCase();
        if (ext === 'csv') return '\uF1C3';   /* bi-filetype-csv */
        if (ext === 'xls') return '\uF1C2';   /* bi-filetype-xls */
        return '\uF1C4';                      /* bi-filetype-xlsx */
    }

    // ------------------------------------------------------------- saved files

    function SavedFiles(opts) {
        this.opts = opts;
        this.page = 1;              /* last page fetched */
        this.pageSize = 5;          /* first batch: 5 files, rest lazy-loaded */
        this.total = 0;
        this.totalPages = 1;
        this.busy = false;          /* guards concurrent lazy loads */
        this.io = null;             /* IntersectionObserver for lazy loading */
        this.tbody = null;          /* the <tbody> rows append to */
        this.search = '';
        this.sortBy = 'recent';
        this.period = 'all';
        this.openId = 0;
    }

    SavedFiles.prototype.fetchPage = function (page) {
        return get('Import/Files?page=' + page + '&pageSize=' + this.pageSize +
                   (this.search ? '&search=' + encodeURIComponent(this.search) : '') +
                   '&sortBy=' + encodeURIComponent(this.sortBy) +
                   '&period=' + encodeURIComponent(this.period));
    };

    SavedFiles.prototype.refresh = function () {
        var self = this;
        this.page = 1;
        this.disarmLazy();
        var list = byId('savedList');
        if (list) { list.textContent = ''; list.appendChild(mkEmpty('Loading saved files...')); }

        return this.fetchPage(1).then(function (d) { self.render(d); })
            .catch(function (err) {
                if (list) { list.textContent = ''; list.appendChild(mkEmpty(err.message)); }
            });
    };

    SavedFiles.prototype.render = function (d) {
        var self = this;
        this.total = d.total;
        this.totalPages = d.totalPages || 1;

        var count = byId('savedCount');
        if (count) count.textContent = fmt(d.total);

        var list = byId('savedList');
        if (!list) return;

        list.textContent = '';
        list.className = 'imp-save-table-wrap';

        var files = d.files || [];
        if (!files.length) {
            list.className = '';
            list.appendChild(mkEmpty(this.search
                ? 'No saved file matches that search.'
                : 'Nothing saved yet. Open "' + this.opts.tabCurrentLabel + '", drop a file and press Save.'));
            this.renderLazyFooter(d);
            return;
        }

        var table = document.createElement('table');
        table.className = 'imp-save-table';
        var thead = document.createElement('thead');
        var hr = document.createElement('tr');
        ['File', 'Rows', 'Cols', 'Saved By', 'Saved On', ''].forEach(function (h) {
            var th = document.createElement('th');
            th.textContent = h;
            hr.appendChild(th);
        });
        thead.appendChild(hr);
        this.tbody = document.createElement('tbody');
        table.appendChild(thead);
        table.appendChild(this.tbody);
        list.appendChild(table);

        var sentinel = document.createElement('div');
        sentinel.className = 'imp-lazy-sentinel';
        sentinel.id = 'savedLazySentinel';
        sentinel.textContent = 'Loading more...';
        list.appendChild(sentinel);

        files.forEach(function (f) { self.tbody.appendChild(self.buildRow(f)); });
        this.armLazy();
        this.renderLazyFooter(d);
    };

    SavedFiles.prototype.buildRow = function (f) {
        var self = this;

        var tr = document.createElement('tr');
        tr.className = f.fileId === this.openId ? 'is-open' : '';
        tr.setAttribute('data-id', f.fileId);
        tr.tabIndex = 0;
        tr.setAttribute('role', 'button');
        tr.title = 'Open ' + f.fileName;

        /* File cell: type icon + name */
        var tdName = document.createElement('td');
        tdName.className = 'imp-filecell';
        var icon = document.createElement('span');
        var ext = String(f.fileName || '').split('.').pop().toLowerCase();
        if (['csv', 'xlsx', 'xls'].indexOf(ext) === -1) ext = 'xlsx';
        icon.className = 'imp-fileicon is-' + ext;
        icon.textContent = extIcon(f.fileName);
        tdName.appendChild(icon);
        var name = document.createElement('span');
        name.className = 'imp-filename';
        name.textContent = f.fileName;
        name.title = f.fileName;
        tdName.appendChild(name);
        tr.appendChild(tdName);

        tr.appendChild(tblCell(fmt(f.rowCount), 'imp-num'));
        tr.appendChild(tblCell(fmt(f.columnCount), 'imp-num'));
        tr.appendChild(tblCell(f.savedByName || '\u2014'));
        tr.appendChild(tblCell(f.savedOnDisplay || '\u2014'));

        var tdAct = document.createElement('td');
        tdAct.className = 'imp-actions';
        var openBtn = document.createElement('button');
        openBtn.type = 'button';
        openBtn.className = 'imp-rowopen';
        openBtn.textContent = '\uF31F';   /* bi-eye-fill */
        openBtn.title = 'Open this file';
        openBtn.setAttribute('aria-label', 'Open ' + f.fileName);
        openBtn.addEventListener('click', function (e) { e.stopPropagation(); self.open(f); });
        tdAct.appendChild(openBtn);
        if (this.opts.allowDelete) {
            var del = document.createElement('button');
            del.type = 'button';
            del.className = 'imp-filedel';
            del.textContent = '\u2715';
            del.title = 'Delete this saved file';
            del.setAttribute('aria-label', 'Delete ' + f.fileName);
            del.addEventListener('click', function (e) { e.stopPropagation(); self.remove(f); });
            tdAct.appendChild(del);
        }
        tr.appendChild(tdAct);

        tr.addEventListener('click', function () { self.open(f); });
        tr.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); self.open(f); }
        });

        return tr;
    };

    function tblCell(text, cls) {
        var td = document.createElement('td');
        td.textContent = text;
        if (cls) td.className = cls;
        return td;
    }

    /* ---------------- lazy loading ---------------- */

    SavedFiles.prototype.disarmLazy = function () {
        if (this.io) { this.io.disconnect(); this.io = null; }
    };

    SavedFiles.prototype.armLazy = function () {
        var self = this;
        this.disarmLazy();
        var sentinel = byId('savedLazySentinel');
        if (!sentinel) { this.renderLazyFooter(); return; }

        if (this.page >= this.totalPages) {
            sentinel.style.display = 'none';
            this.renderLazyFooter();
            return;
        }
        sentinel.style.display = '';
        this.io = new IntersectionObserver(function (entries) {
            if (entries.some(function (e) { return e.isIntersecting; })) self.loadMore();
        }, { root: byId('savedList'), rootMargin: '120px 0px' });
        this.io.observe(sentinel);
    };

    SavedFiles.prototype.loadMore = function () {
        var self = this;
        if (this.busy || this.page >= this.totalPages) return;
        this.busy = true;
        var sentinel = byId('savedLazySentinel');
        if (sentinel) sentinel.textContent = 'Loading more...';

        this.fetchPage(this.page + 1).then(function (d) {
            self.busy = false;
            self.total = d.total;
            self.totalPages = d.totalPages || 1;
            self.page++;
            (d.files || []).forEach(function (f) {
                if (self.tbody) self.tbody.appendChild(self.buildRow(f));
            });
            self.armLazy();
            self.renderLazyFooter(d);
        }).catch(function () {
            self.busy = false;
            self.armLazy();
        });
    };

    SavedFiles.prototype.renderLazyFooter = function (d) {
        var box = byId('savedListPager');
        if (!box) return;
        box.textContent = '';
        var total = d ? d.total : this.total;
        if (!total) return;

        var loaded = Math.min(this.page * this.pageSize, total);
        var span = document.createElement('span');
        span.className = 'imp-lazyfooter';
        if (loaded >= total) {
            span.textContent = 'All ' + fmt(total) + ' file' + (total === 1 ? '' : 's') + ' loaded.';
        } else {
            span.textContent = 'Loaded ' + fmt(loaded) + ' of ' + fmt(total) +
                ' files \u00b7 keep scrolling to load more\u2026';
        }
        box.appendChild(span);
    };

    SavedFiles.prototype.open = function (f) {
        var self = this;
        this.openId = f.fileId;
        setStatus('savedStatus', 'Loading ' + f.fileName + '...', null);

        /* Highlight the opened row in the saved-files table. */
        var list = byId('savedList');
        if (list) {
            var openRows = list.querySelectorAll('tbody tr.is-open');
            for (var i = 0; i < openRows.length; i++) openRows[i].classList.remove('is-open');
            var cur = list.querySelector('tbody tr[data-id="' + f.fileId + '"]');
            if (cur) cur.classList.add('is-open');
        }

        var t = byId('savedOpenTitle');
        var m = byId('savedOpenMeta');
        if (t) {
            t.textContent = '';
            var ext = String(f.fileName || '').split('.').pop().toLowerCase();
            var iconCls = ext === 'csv' ? 'bi-filetype-csv' : ext === 'xls' ? 'bi-filetype-xls' : 'bi-filetype-xlsx';
            var ic = document.createElement('i');
            ic.className = 'bi ' + iconCls;
            t.appendChild(ic);
            t.appendChild(document.createTextNode(' ' + f.fileName));
        }
        if (m) m.textContent = fmt(f.rowCount) + ' rows \u00b7 ' + fmt(f.columnCount) + ' cols' +
            (f.savedOnDisplay ? ' \u00b7 ' + f.savedOnDisplay : '');

        /* One saved file is one row in the database, so it comes back whole and
           the grid pages and searches it locally - the same path a dropped file
           takes. No paging parameters to keep in step with the server. */
        get('Import/Rows?fileId=' + encodeURIComponent(f.fileId)).then(function (d) {
            self.opts.savedGrid.fileName = f.fileName;
            self.opts.savedGrid.ingest([d.headers || []].concat(d.rows || []));
            setStatus('savedStatus', f.fileName + ' loaded.', 'ok');
        }).catch(function (err) {
            setStatus('savedStatus', err.message, 'err');
        });
    };

    SavedFiles.prototype.remove = function (f) {
        if (!global.confirm('Delete "' + f.fileName + '"?\n\nThis also removes all ' + fmt(f.rowCount) +
                            ' saved rows. This cannot be undone.')) return;

        var self = this;
        setStatus('savedStatus', 'Deleting...', null);
        post('Import/Delete/' + encodeURIComponent(f.fileId)).then(function (d) {
            if (self.openId === f.fileId) {
                self.openId = 0;
                /* clearAll, not clear: that is the prototype method name.
                   It empties the grid's own status box, so it has to run
                   before the confirmation below or it wipes the message. */
                self.opts.savedGrid.clearAll();
                var t = byId('savedOpenTitle');
                var m = byId('savedOpenMeta');
                if (t) t.textContent = 'Pick a file from the list to view it here.';
                if (m) m.textContent = '';
            }
            setStatus('savedStatus', (d.message || 'Deleted.') + ' Click a file to open it.', 'ok');
            if (global.ImportToast) global.ImportToast('"' + f.fileName + '" deleted.', 'ok');
            self.refresh();
        }).catch(function (err) {
            setStatus('savedStatus', err.message, 'err');
        });
    };

    // ------------------------------------------------------------------- save

    function save() {
        var grid = global.ImportSave.grid;
        var opts = global.ImportSave.opts;
        if (!grid) return;

        var headers = grid.headers.slice();
        var rows = grid.allRecords;
        var fileName = grid.fileName;

        if (!headers.length || !rows.length) {
            setStatus('currentStatus', 'Nothing to save yet. Drop an Excel or CSV file first.', 'err');
            return;
        }
        if (rows.length > opts.maxRows) {
            setStatus('currentStatus', 'This file has ' + fmt(rows.length) + ' rows but the limit is ' +
                                       fmt(opts.maxRows) + '. Trim it and try again.', 'err');
            return;
        }

        var btn = byId('btnSave');
        var label = btn ? btn.innerHTML : '';
        if (btn) {
            btn.disabled = true;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Saving...';
        }
        setStatus('currentStatus', 'Saving ' + fmt(rows.length) + ' rows...', null);

        postJson('Import/Save', { fileName: fileName, headers: headers, rows: rows })
            .then(function (d) {
                if (btn) { btn.disabled = false; btn.innerHTML = label; }
                setStatus('currentStatus', (d.message || 'Saved.') +
                    ' It is now listed under "' + opts.tabSavedLabel + '".', 'ok');
                if (global.ImportToast) global.ImportToast(d.message || 'Saved to database.', 'ok');
                global.ImportSave.refreshSaved();
            })
            .catch(function (err) {
                if (btn) { btn.disabled = false; btn.innerHTML = label; }
                setStatus('currentStatus', err.message, 'err');
                if (global.ImportToast) global.ImportToast(err.message, 'err');
            });
    }

    // ------------------------------------------------------------------- boot

    global.ImportSave = {
        init: function (opts) {
            var self = this;

            this.opts = opts;
            this.grid = opts.currentGrid;
            this.saved = new SavedFiles(opts);

            var filesSearch = byId('savedSearch');
            if (filesSearch) {
                var t = null;
                filesSearch.addEventListener('input', function () {
                    clearTimeout(t);
                    t = setTimeout(function () {
                        self.saved.search = filesSearch.value;
                        self.saved.page = 1;
                        self.saved.refresh();
                    }, 250);
                });
            }

            var sortSel = byId('savedSort');
            if (sortSel) {
                sortSel.addEventListener('change', function () {
                    self.saved.sortBy = sortSel.value;
                    self.saved.page = 1;
                    self.saved.refresh();
                });
            }

            var periodRow = byId('savedPeriod');
            if (periodRow) {
                periodRow.addEventListener('click', function (e) {
                    var b = e.target && e.target.closest ? e.target.closest('.imp-filterchip') : null;
                    if (!b) return;
                    periodRow.querySelectorAll('.imp-filterchip').forEach(function (x) {
                        x.classList.remove('active');
                    });
                    b.classList.add('active');
                    self.saved.period = b.getAttribute('data-period') || 'all';
                    self.saved.page = 1;
                    self.saved.refresh();
                });
            }

            var resetBtn = byId('savedReset');
            if (resetBtn) {
                resetBtn.addEventListener('click', function () {
                    self.saved.search = '';
                    if (filesSearch) filesSearch.value = '';
                    self.saved.sortBy = 'recent';
                    if (sortSel) sortSel.value = 'recent';
                    self.saved.period = 'all';
                    if (periodRow) {
                        periodRow.querySelectorAll('.imp-filterchip').forEach(function (x) {
                            x.classList.toggle('active', x.getAttribute('data-period') === 'all');
                        });
                    }
                    self.saved.page = 1;
                    self.saved.refresh();
                    if (global.ImportToast) global.ImportToast('Filters reset.', 'ok');
                });
            }

            var saveBtn = byId('btnSave');
            if (saveBtn) saveBtn.addEventListener('click', save);

            this.saved.refresh();
            return this;
        },

        refreshSaved: function () {
            if (this.saved) { this.saved.page = 1; this.saved.refresh(); }
        },

        save: save
    };
})(window);
