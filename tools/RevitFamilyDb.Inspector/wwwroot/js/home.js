(function () {
  'use strict';

  // In strict mode i globali devono essere presi esplicitamente da window (definiti in inspector-common.js).
  var el = window.el;
  var esc = window.esc;
  var enc = window.enc;
  var api = window.api;
  var apiOptional = window.apiOptional;
  var apiOptionalJson = window.apiOptionalJson;
  var assertNotFileProtocol = window.assertNotFileProtocol;

  const LS_KEY = window.LS_KEY || 'revitFamilyDbApiKey';
  const selectedCategories = new Set();

  function splitFamilyAndType(fullName) {
    const src = (fullName || '').toString();
    const idx = src.indexOf(':');
    if (idx < 0) {
      return { family: src.trim(), type: '' };
    }
    return {
      family: src.substring(0, idx).trim(),
      type: src.substring(idx + 1).trim()
    };
  }

  let selectedFamilyId = null;

  function normCat(v) {
    return (v || '').toString().trim();
  }

  function updateCategoryButtonText() {
    const btn = el('btnCategoryFilter');
    const count = selectedCategories.size;
    btn.textContent = count === 0 ? 'Categorie: tutte' : 'Categorie: ' + count + ' selezionate';
  }

  function renderCategoryOptions(rows) {
    const counts = new Map();
    for (const r of rows) {
      const c = normCat(r.categoryName) || '(Senza categoria)';
      counts.set(c, (counts.get(c) || 0) + 1);
    }
    for (const c of selectedCategories) {
      if (!counts.has(c)) counts.set(c, 0);
    }
    const categories = [...counts.keys()].sort(function (a, b) { return a.localeCompare(b, 'it'); });
    const list = el('categoryList');
    list.innerHTML = '';
    for (const c of categories) {
      const row = document.createElement('label');
      row.className = 'cat-item';
      const chk = document.createElement('input');
      chk.type = 'checkbox';
      chk.checked = selectedCategories.has(c);
      chk.addEventListener('change', function () {
        if (chk.checked) selectedCategories.add(c);
        else selectedCategories.delete(c);
        updateCategoryButtonText();
        loadRows();
      });
      const txt = document.createElement('span');
      txt.textContent = c;
      const cnt = document.createElement('span');
      cnt.className = 'cat-count';
      cnt.textContent = '(' + counts.get(c) + ')';
      row.appendChild(chk);
      row.appendChild(txt);
      row.appendChild(cnt);
      list.appendChild(row);
    }
    updateCategoryButtonText();
  }

  function placeholder(kind, familyName, categoryName) {
    const ch = (categoryName || familyName || '?').trim().charAt(0).toUpperCase() || '?';
    const bg = (kind || '').toLowerCase() === 'system' ? '#d7e6ff' : '#e5f7e5';
    return '<span class="pv ph" style="background:' + bg + '">' + esc(ch) + '</span>';
  }

  function previewCell(r) {
    if (r.previewPath) {
      return '<span class="pv"><img src="/api/preview?path=' + enc(r.previewPath) + '" alt="preview" loading="lazy" data-kind="' + esc(r.familyKind) + '" data-family="' + esc(r.familyName) + '" data-category="' + esc(r.categoryName) + '" onerror="previewFallback(this)" /></span>';
    }
    return placeholder(r.familyKind, r.familyName, r.categoryName);
  }

  window.previewFallback = function (img) {
    try {
      const kind = (img && img.getAttribute('data-kind')) || '';
      const fam = (img && img.getAttribute('data-family')) || '';
      const cat = (img && img.getAttribute('data-category')) || '';
      if (img && img.parentElement) {
        img.parentElement.outerHTML = placeholder(kind, fam, cat);
      }
    } catch (e) { /* ignore */ }
  };

  async function loadQueueInfo() {
    try {
      const q = await api('/api/queue/pending-count');
      el('queueInfo').textContent = 'Coda Web→Revit (pending): ' + (q.pending != null ? q.pending : 0);
    } catch (e) {
      el('queueInfo').textContent = 'Coda: n/d';
    }
  }

  function refreshApiKeyUi() {
    const need = window.__queueNeedsKey === true;
    const has = !!localStorage.getItem(LS_KEY);
    el('apiKeyStatus').textContent = need
      ? (has ? 'API key salvata (locale).' : 'API key richiesta per la coda: clic «API key coda…».')
      : '';
  }

  async function loadMeta() {
    assertNotFileProtocol(function () {
      el('health').innerHTML =
        '<span style="color:#b00">Apri questa app solo tramite il server: nella cartella Inspector esegui <code>dotnet run</code> (o <code>run-inspector.ps1</code>) e usa l\'indirizzo <code>http://localhost:…</code> mostrato in console. Non aprire <code>index.html</code> da Esplora file.</span>';
    });

    const ver = await apiOptional('/api/version');
    const cfg = await apiOptionalJson('/api/config');
    window.__queueNeedsKey = !!(cfg && cfg.queueApiKeyRequired);
    refreshApiKeyUi();

    const h = await api('/api/health');
    const verLine = ver
      ? ' · <span class="muted">' + esc(ver.product) + ' ' + esc(ver.version) + '</span>'
      : '';
    el('health').innerHTML = '<span class="ok">Connesso</span> a DB: <b>' + esc(h.dbName) + '</b>' + verLine;

    const tables = await api('/api/tables');
    el('tables').textContent = tables.join(', ');
  }

  function bindRowClicks() {
    el('rows').querySelectorAll('tr').forEach(function (tr) {
      const ex = tr.querySelector('.btnExplore');
      if (ex) {
        ex.addEventListener('click', function (e) {
          e.stopPropagation();
          const id = parseFamilyIdFromRow(tr);
          if (id != null) openDetail(id);
        });
      }
      const q = tr.querySelector('.btnQueue');
      if (q) {
        q.addEventListener('click', function (e) {
          e.stopPropagation();
          const id = parseFamilyIdFromRow(tr);
          if (id != null) enqueueFamily(id);
        });
      }
    });
  }

  function parseFamilyIdFromRow(tr) {
    const raw = tr && tr.dataset ? tr.dataset.fid : undefined;
    if (raw === undefined || raw === '') return null;
    const id = parseInt(raw, 10);
    return Number.isFinite(id) && id > 0 ? id : null;
  }

  async function loadRows() {
    const p = new URLSearchParams();
    if (el('q').value.trim()) p.set('q', el('q').value.trim());
    if (el('discipline').value) p.set('discipline', el('discipline').value);
    if (el('kind').value) p.set('kind', el('kind').value);
    p.set('take', el('take').value || '300');

    const rawRows = await api('/api/families?' + p.toString());
    renderCategoryOptions(rawRows);
    const rows = rawRows.filter(function (r) {
      if (selectedCategories.size === 0) return true;
      const c = normCat(r.categoryName) || '(Senza categoria)';
      return selectedCategories.has(c);
    });
    el('count').textContent = 'Righe trovate: ' + rows.length;
    el('rows').innerHTML = rows.map(function (r) {
      const n = splitFamilyAndType(r.familyName);
      const fid = r.familyId != null && r.familyId > 0 ? ' data-fid="' + esc(r.familyId) + '"' : '';
      return '<tr' + fid + '>' +
        '<td>' + esc(r.familyId) + '</td>' +
        '<td class="preview-cell">' + previewCell(r) + '</td>' +
        '<td>' + esc(r.sourceDiscipline) + '</td>' +
        '<td>' + esc(r.categoryName) + '</td>' +
        '<td>' + esc(n.family) + '</td>' +
        '<td>' + esc(n.type) + '</td>' +
        '<td>' +
        '<button type="button" class="btnExplore">Esplora</button> ' +
        '<button type="button" class="btnQueue">In coda Revit</button>' +
        '</td></tr>';
    }).join('');
    bindRowClicks();
    await loadQueueInfo();
  }

  async function openDetail(familyId) {
    if (familyId == null || !Number.isFinite(familyId) || familyId <= 0) {
      window.alert('Nessun FamilyId valido per questa riga (dati incompleti nel DB).');
      return;
    }
    selectedFamilyId = familyId;
    const data = await api('/api/family/' + familyId);
    const f = data.family;
    el('mTitle').textContent = (f.familyName || '') + ' (' + (f.familyKind || '') + ')';
    const pv = f.previewPath
      ? '<div style="margin-bottom:10px"><img src="/api/preview?path=' + enc(f.previewPath) + '" alt="" style="max-height:180px;border:1px solid #ddd;border-radius:8px;" onerror="this.style.display=\'none\'" /></div>'
      : '';
    const meta =
      '<div style="font-size:13px;line-height:1.5;margin-bottom:10px;">' +
      '<div><b>FamilyId</b>: ' + esc(f.familyId) + '</div>' +
      '<div><b>Categoria</b>: ' + esc(f.categoryName) + '</div>' +
      '<div><b>Disciplina</b>: ' + esc(f.sourceDiscipline) + '</div>' +
      '<div><b>RfaPath</b>: ' + esc(f.rfaPath) + '</div>' +
      '<div><b>SourceModelPath</b>: ' + esc(f.sourceModelPath) + '</div>' +
      '<div><b>TypeId</b>: ' + esc(f.sourceElementTypeId) + '</div>' +
      '<div><b>PreviewPath</b>: ' + esc(f.previewPath) + '</div>' +
      '</div>';
    const plist = (data.parameters || []).length
      ? '<table class="param-table"><thead><tr><th>Parametro</th><th>Gruppo</th><th>Tipo</th><th>Valore</th></tr></thead><tbody>' +
        data.parameters.map(function (p) {
          return '<tr><td>' + esc(p.parameterName) + '</td><td class="muted">' + esc(p.parameterGroupName) + '</td><td class="muted">' + esc(p.storageType) + '</td><td>' + esc(p.stringValue) + '</td></tr>';
        }).join('') +
        '</tbody></table>'
      : '<div class="muted">Nessun parametro in DB (eseguire Push libreria → DB da Revit dopo aggiornamento).</div>';
    el('mBody').innerHTML = pv + meta + '<h3 style="font-size:15px;margin:12px 0 6px">Parametri (' + (data.parameters || []).length + ')</h3>' + plist;
    el('modal').classList.add('open');
  }

  async function enqueueFamily(familyId) {
    try {
      await api('/api/queue/enqueue', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ familyId: familyId })
      });
      window.alert('Accodata. In Revit: comando «Applica coda Web → progetto».');
      await loadQueueInfo();
    } catch (e) {
      window.alert('Errore: ' + (e && e.message ? e.message : e));
    }
  }

  el('mClose').addEventListener('click', function () { el('modal').classList.remove('open'); });
  el('mCloseText').addEventListener('click', function () { el('modal').classList.remove('open'); });
  el('modal').addEventListener('click', function (e) { if (e.target === el('modal')) el('modal').classList.remove('open'); });
  el('mEnqueue').addEventListener('click', function () {
    if (selectedFamilyId) enqueueFamily(selectedFamilyId);
  });

  el('btnApiKey').addEventListener('click', function () {
    const cur = localStorage.getItem(LS_KEY) || '';
    const v = window.prompt('API key per /api/queue (lasciare vuoto per rimuovere):', cur);
    if (v === null) return;
    const t = v.trim();
    if (t) localStorage.setItem(LS_KEY, t);
    else localStorage.removeItem(LS_KEY);
    refreshApiKeyUi();
    loadQueueInfo();
  });

  el('rows').addEventListener('dblclick', function (e) {
    if (e.target.closest('button')) return;
    const tr = e.target.closest('tr');
    if (!tr || tr.parentElement !== el('rows')) return;
    const id = parseFamilyIdFromRow(tr);
    if (id != null) openDetail(id);
  });

  el('reload').addEventListener('click', loadRows);
  el('btnCategoryFilter').addEventListener('click', function (e) {
    e.stopPropagation();
    el('categoryPop').classList.toggle('open');
  });
  el('catAll').addEventListener('click', function () {
    const all = el('categoryList').querySelectorAll('label.cat-item');
    selectedCategories.clear();
    for (const node of all) {
      const name = node.querySelector('span') ? node.querySelector('span').textContent : '';
      if (name) selectedCategories.add(name);
    }
    updateCategoryButtonText();
    loadRows();
  });
  el('catNone').addEventListener('click', function () {
    selectedCategories.clear();
    updateCategoryButtonText();
    loadRows();
  });
  document.addEventListener('click', function (e) {
    const wrap = e.target.closest('.filter-wrap');
    if (!wrap) {
      el('categoryPop').classList.remove('open');
    }
  });
  el('btnRefresh').addEventListener('click', function () {
    loadMeta().then(loadRows).catch(function (err) {
      el('health').textContent = 'Errore: ' + err;
    });
  });
  ['q', 'take'].forEach(function (id) {
    el(id).addEventListener('keydown', function (e) { if (e.key === 'Enter') loadRows(); });
  });
  ['discipline', 'kind'].forEach(function (id) {
    el(id).addEventListener('change', loadRows);
  });

  loadMeta().then(loadRows).catch(function (err) {
    el('health').textContent = 'Errore: ' + err;
  });
})();
