
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
        this.page = 1;
        this.pageSize = opts.pageSize;
        this.search = '';
        this.openId = 0;
    }

    SavedFiles.prototype.refresh = function () {
        var self = this;
        var list = byId('savedList');
        if (list) { list.textContent = ''; list.appendChild(mkEmpty('Loading saved files...')); }

        var path = 'Import/Files?page=' + this.page + '&pageSize=' + this.pageSize +
                   (this.search ? '&search=' + encodeURIComponent(this.search) : '');

        return get(path).then(function (d) { self.render(d); })
            .catch(function (err) {
                if (list) { list.textContent = ''; list.appendChild(mkEmpty(err.message)); }
            });
    };

    SavedFiles.prototype.render = function (d) {
        var self = this;
        var list = byId('savedList');
        if (!list) return;

        var count = byId('savedCount');
        if (count) count.textContent = fmt(d.total);

        list.textContent = '';
        var files = d.files || [];
        if (!files.length) {
            list.appendChild(mkEmpty(this.search
                ? 'No saved file matches that search.'
                : 'Nothing saved yet. Open "' + this.opts.tabCurrentLabel + '", drop a file and press Save.'));
            this.renderListPager(d);
            return;
        }

        files.forEach(function (f) { list.appendChild(self.buildRow(f)); });
        this.renderListPager(d);
    };

    SavedFiles.prototype.buildRow = function (f) {
        var self = this;

        var row = document.createElement('div');
        row.className = 'imp-fileitem' + (f.fileId === this.openId ? ' is-open' : '');
        row.tabIndex = 0;
        row.setAttribute('role', 'button');

        var icon = document.createElement('span');
        icon.className = 'imp-fileicon';
        icon.textContent = extIcon(f.fileName);
        row.appendChild(icon);

        var meta = document.createElement('span');
        meta.className = 'imp-filemeta';

        var name = document.createElement('span');
        name.className = 'imp-filename';
        name.textContent = f.fileName;
        name.title = f.fileName;
        meta.appendChild(name);

        var bits = [fmt(f.rowCount) + ' rows', fmt(f.columnCount) + ' cols'];
        if (f.savedOnDisplay) bits.push(f.savedOnDisplay);
        if (f.savedByName) bits.push(f.savedByName);

        var sub = document.createElement('span');
        sub.className = 'imp-filesub';
        sub.textContent = bits.join('   \u00b7   ');
        meta.appendChild(sub);
        row.appendChild(meta);

        if (this.opts.allowDelete) {
            var del = document.createElement('button');
            del.type = 'button';
            del.className = 'imp-filedel';
            del.textContent = '\u2715';
            del.title = 'Delete this saved file';
            del.setAttribute('aria-label', 'Delete ' + f.fileName);
            del.addEventListener('click', function (e) { e.stopPropagation(); self.remove(f); });
            row.appendChild(del);
        }

        row.addEventListener('click', function () { self.open(f); });
        row.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); self.open(f); }
        });

        return row;
    };

    SavedFiles.prototype.renderListPager = function (d) {
        var box = byId('savedListPager');
        if (!box) return;
        box.textContent = '';

        var pages = d.totalPages || 0;
        if (pages <= 1) return;

        var self = this;

        var info = document.createElement('span');
        info.textContent = 'Page ' + d.page + ' of ' + pages + '   \u00b7   ' + fmt(d.total) + ' files';
        box.appendChild(info);

        var nav = document.createElement('span');

        var prev = document.createElement('button');
        prev.type = 'button';
        prev.textContent = 'Prev';
        prev.disabled = d.page <= 1;
        prev.addEventListener('click', function () { self.page--; self.refresh(); });
        nav.appendChild(prev);

        var next = document.createElement('button');
        next.type = 'button';
        next.textContent = 'Next';
        next.style.marginLeft = '6px';
        next.disabled = d.page >= pages;
        next.addEventListener('click', function () { self.page++; self.refresh(); });
        nav.appendChild(next);

        box.appendChild(nav);
    };

    SavedFiles.prototype.open = function (f) {
        var self = this;
        this.openId = f.fileId;
        setStatus('savedStatus', 'Loading ' + f.fileName + '...', null);

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
            }
            setStatus('savedStatus', (d.message || 'Deleted.') + ' Click a file to open it.', 'ok');
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
                global.ImportSave.refreshSaved();
            })
            .catch(function (err) {
                if (btn) { btn.disabled = false; btn.innerHTML = label; }
                setStatus('currentStatus', err.message, 'err');
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
