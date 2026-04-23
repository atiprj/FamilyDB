(async function () {
  'use strict';

  var el = window.el;
  var esc = window.esc;
  var api = window.api;
  var apiOptional = window.apiOptional;
  var assertNotFileProtocol = window.assertNotFileProtocol;

  try {
    assertNotFileProtocol(function () {
      el('health').innerHTML =
        '<span style="color:#b00">Apri questa app solo tramite il server: nella cartella Inspector esegui <code>dotnet run</code> (o <code>run-inspector.ps1</code>) e usa l\'indirizzo <code>http://localhost:…</code> mostrato in console. Non aprire <code>index.html</code> da Esplora file.</span>';
    });

    const ver = await apiOptional('/api/version');
    const h = await api('/api/health');
    const verLine = ver
      ? ' · <span class="muted">' + esc(ver.product) + ' ' + esc(ver.version) + '</span>'
      : '';
    el('health').innerHTML =
      '<span class="ok">Connesso</span> a DB: <b>' + esc(h.dbName) + '</b>' + verLine;

    const q = await api('/api/quality');
    el('qualityMetrics').innerHTML =
      '<div class="metric-row"><span class="label">Loadable senza .rfa reale:</span> <b>' + esc(q.loadableWithoutRealRfa) + '</b></div>' +
      '<div class="metric-row"><span class="label">System senza TypeId:</span> <b>' + esc(q.systemWithoutTypeId) + '</b></div>' +
      '<div class="metric-row"><span class="label">Senza SourceModelPath:</span> <b>' + esc(q.missingSourceModelPath) + '</b></div>' +
      '<div class="metric-row"><span class="label">Senza anteprima (PreviewPath):</span> <b>' + esc(q.missingPreviewPath) + '</b></div>';
  } catch (err) {
    el('health').textContent = 'Errore: ' + (err && err.message ? err.message : err);
    el('qualityMetrics').textContent = '—';
  }

  el('btnRefresh').addEventListener('click', function () {
    window.location.reload();
  });
})();
