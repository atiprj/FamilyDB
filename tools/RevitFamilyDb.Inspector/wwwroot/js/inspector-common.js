(function (global) {
  'use strict';

  global.LS_KEY = 'revitFamilyDbApiKey';

  global.el = function (id) {
    return document.getElementById(id);
  };

  global.esc = function (v) {
    return (v ?? '').toString()
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;')
      .replaceAll("'", '&#39;');
  };

  global.enc = function (v) {
    return encodeURIComponent(v ?? '');
  };

  global.queueAuthHeaders = function () {
    const k = localStorage.getItem(global.LS_KEY);
    if (!k) return {};
    return { Authorization: 'Bearer ' + k };
  };

  global.api = function (path, opt) {
    const o = opt || {};
    const headers = new Headers(o.headers || {});
    if (path.indexOf('/api/queue') === 0) {
      const qh = global.queueAuthHeaders();
      if (qh.Authorization) headers.set('Authorization', qh.Authorization);
    }
    return fetch(path, { ...o, headers }).then(async function (r) {
      if (!r.ok) {
        const t = await r.text();
        throw new Error(t || r.statusText);
      }
      const ct = r.headers.get('content-type');
      if (ct && ct.indexOf('application/json') >= 0) return r.json();
      return r.text();
    });
  };

  global.apiOptional = async function (path) {
    try {
      const r = await fetch(path);
      if (!r.ok) return null;
      const ct = r.headers.get('content-type');
      if (ct && ct.indexOf('application/json') >= 0) return r.json();
      return null;
    } catch {
      return null;
    }
  };

  global.apiOptionalJson = async function (path) {
    try {
      const r = await fetch(path);
      if (!r.ok) return null;
      return await r.json();
    } catch {
      return null;
    }
  };

  global.assertNotFileProtocol = function (onError) {
    if (window.location.protocol === 'file:') {
      if (typeof onError === 'function') {
        onError();
      }
      throw new Error('Pagina aperta come file locale (file://)');
    }
  };
})(typeof window !== 'undefined' ? window : globalThis);
