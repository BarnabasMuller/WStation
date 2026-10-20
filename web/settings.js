'use strict';
/* Beállítások fül: nyelv, betűméret, mértékegységek, ütemezés, biztonsági mentés, változásnapló; frissítés-ellenőrzés */

// ------------------------------------------------------------------ változásnapló (kliens oldali bejegyzések)
const CL_NAMES = {
  theme: 'Téma', lang: 'Nyelv', minFont: 'Legkisebb betűméret', 'units.temp': 'Hőmérséklet mértékegysége', 'units.rain': 'Csapadék mértékegysége',
  'units.wind': 'Szél mértékegysége', 'units.pres': 'Légnyomás mértékegysége'
};
function logChange(area, key, from, to, revert) {
  return post('/api/changelog', [{ area, key, from: from == null ? null : String(from), to: to == null ? null : String(to), revert: revert || null }]).catch(() => { });
}
function logUi(key, from, to) {
  if (String(from) === String(to)) return;
  logChange('Megjelenés', CL_NAMES[key] || key, from == null ? '(alap)' : from, to, { type: 'ui', key, value: from == null ? null : from });
}

// két objektum eltérései pontozott útvonalakkal (a szárazság-beállítások naplózásához)
function diffObj(a, b, path, out) {
  if (Array.isArray(a) || Array.isArray(b)) {
    const ka = Array.isArray(a) && a.length && a[0] && a[0].id !== undefined;
    if (ka) {
      const ia = {}; (a || []).forEach(x => ia[x.id] = x);
      const ib = {}; (b || []).forEach(x => ib[x.id] = x);
      Object.keys(Object.assign({}, ia, ib)).forEach(id => {
        if (!ia[id]) out.push({ path: path.concat(['#' + id]), from: null, to: '(új: ' + (ib[id].name || id) + ')' });
        else if (!ib[id]) out.push({ path: path.concat(['#' + id]), from: '(' + (ia[id].name || id) + ')', to: '(törölve)', obj: ia[id] });
        else diffObj(ia[id], ib[id], path.concat(['#' + id]), out);
      });
      return;
    }
    if (JSON.stringify(a) !== JSON.stringify(b)) out.push({ path, from: JSON.stringify(a), to: JSON.stringify(b), raw: a });
    return;
  }
  if (a && b && typeof a === 'object' && typeof b === 'object') {
    Object.keys(Object.assign({}, a, b)).forEach(k => diffObj(a[k], b[k], path.concat([k]), out));
    return;
  }
  if (a !== b) out.push({ path, from: a, to: b, raw: a });
}
function droughtPathLabel(path, cfg) {
  return path.map(p => {
    if (p[0] === '#') { const pr = (cfg.profiles || []).find(x => x.id === p.slice(1)); return pr ? pr.name : p.slice(1); }
    const lbl = (typeof PROFILE_PARAMS !== 'undefined' && (PROFILE_PARAMS[p] || GLOBAL_PARAMS[p] || SOIL_PARAMS[p] || RUNOFF_PARAMS[p] || (typeof FWI_PARAMS !== 'undefined' && (FWI_PARAMS[p] || VPD_PARAMS[p] || GDD_PARAMS[p])))) || null;
    return lbl ? lbl.label + (SYM[p] ? ' (' + SYM[p] + ')' : '') : ({ global: 'közös', soil: 'talaj', runoff: 'lefolyás', fwi: 'FWI', vpd: 'VPD', gdd: 'GDD', profiles: 'profilok', log: 'öntözésnapló', programs: 'öntözési programok', calib: 'csapadék-kalibráció', water: 'vízigény' })[p] || p;
  }).join(' › ');
}
function logDroughtDiff(before, after) {
  if (!before) return;
  const out = [];
  diffObj(before, after, [], out);
  const entries = out.filter(d => d.path[0] !== 'log' || d.path.length < 3).slice(0, 60).map(d => ({
    area: 'Szárazság', key: droughtPathLabel(d.path, after),
    from: d.from == null ? null : typeof d.from === 'number' ? num(d.from, d.from % 1 ? 3 : 0) : String(d.from),
    to: d.to == null ? null : typeof d.to === 'number' ? num(d.to, d.to % 1 ? 3 : 0) : String(d.to),
    revert: d.path[0] === 'log' ? null : { type: 'drought', path: d.path, value: d.obj || (d.raw === undefined ? null : d.raw) }
  }));
  if (entries.length) post('/api/changelog', entries).catch(() => { });
}

// ------------------------------------------------------------------ nézet újrarajzolása (nyelv, mértékegység, betűméret után)
function refreshView() {
  if (!META) return;
  renderHeader(); renderToday(); renderFooter();
  if (chart) {
    const r = visibleRange();
    chart.dispose(); chart = null;
    if (currentTab === 'idovonal') { buildChart(); applyData(); chart.dispatchAction({ type: 'dataZoom', startValue: r[0], endValue: r[1] }); }
  }
  if (typeof renderWeek === 'function') renderWeek();
  showTab(currentTab, false);
}

// ------------------------------------------------------------------ nyelv
async function applyLang() {
  document.documentElement.lang = lang();
  buildFormatters();
  if (typeof applyI18n === 'function') {
    if (!applyLang._list) {
      applyLang._list = true; await i18nLoadList();
      // a Letöltő nyelve kövesse a felületét (pl. a telepítőben választott nyelv után)
      api('/api/dlsettings').then(d => { if (d.hasIni && d.Nyelv !== lang()) post('/api/dlsettings', { Nyelv: lang() }).catch(() => { }); }).catch(() => { });
    }
    await applyI18n(); i18nWatch();
  }
  $('langSel').value = lang();
}
$('langSel').addEventListener('change', async e => {
  const old = UI.lang || 'hu';
  UI.lang = e.target.value; saveUi();
  logUi('lang', old, UI.lang);
  // a Letöltő üzenetei és riasztásai is ezen a nyelven szóljanak
  post('/api/dlsettings', { Nyelv: UI.lang }).catch(() => { });
  await applyLang(); refreshView();
});

// ------------------------------------------------------------------ betűméret
function applyFont() {
  const m = +UI.minFont || 0;
  document.documentElement.style.setProperty('--minf', m ? m + 'px' : '0px');
  $('fontRange').value = m;
  $('fontVal').textContent = m ? m + ' px' : 'alap';
  const sel = $('fontSel');
  sel.value = [0, 14, 16, 18].includes(m) ? String(m) : '';
  if (sel.value === '' ) { if (!sel.querySelector('option[value=""]')) sel.insertAdjacentHTML('beforeend', '<option value="">egyedi</option>'); sel.value = ''; }
}
function setFont(m, log) {
  m = Math.max(0, Math.min(22, Math.round(m)));
  if (m > 0 && m < 12) m = 12;
  const old = UI.minFont || 0;
  if (old === m) return;
  UI.minFont = m; saveUi(); applyFont();
  if (log) logUi('minFont', old ? old + ' px' : 'alap', m ? m + ' px' : 'alap');
  clearTimeout(window._fontT); window._fontT = setTimeout(refreshView, 150);
}
$('fontSel').addEventListener('change', e => { if (e.target.value !== '') setFont(+e.target.value, true); });
$('fontRange').addEventListener('input', e => setFont(+e.target.value, false));
$('fontRange').addEventListener('change', e => logUi('minFont', null, (+e.target.value || 0) ? e.target.value + ' px' : 'alap'));
document.addEventListener('keydown', e => {
  if (!e.ctrlKey || e.altKey) return;
  if (e.key === '+' || e.key === '=' || e.code === 'NumpadAdd') { e.preventDefault(); setFont(Math.max(12, (UI.minFont || 12) + 1), true); }
  if (e.key === '-' || e.code === 'NumpadSubtract') { e.preventDefault(); setFont((UI.minFont || 0) <= 12 ? 0 : UI.minFont - 1, true); }
  if (e.key === '0' && (UI.minFont || 0)) { e.preventDefault(); setFont(0, true); }
});

// ------------------------------------------------------------------ mértékegységek
function syncUnitButtons() {
  const u = units();
  document.querySelectorAll('.useg').forEach(s => s.querySelectorAll('button').forEach(b => b.classList.toggle('on', b.dataset.v === u[s.dataset.u])));
  if (typeof syncWindUnitButtons === 'function') syncWindUnitButtons();
}
document.querySelectorAll('.useg').forEach(s => s.querySelectorAll('button').forEach(b => b.addEventListener('click', () => {
  UI.units = Object.assign({}, UI.units || {});
  const key = s.dataset.u, old = units()[key];
  if (old === b.dataset.v) return;
  UI.units[key] = b.dataset.v; saveUi();
  logUi('units.' + key, old, b.dataset.v);
  syncUnitButtons(); refreshView();
})));

// ------------------------------------------------------------------ automatikus letöltés
const PRESET_MIN = { '15': 15, '30': 30, '60': 60, '180': 180 };
function schedShow() {
  const k = $('schedKind').value;
  $('schedInterval').classList.toggle('hidden', k !== 'interval');
  $('schedDays').classList.toggle('hidden', k !== 'weekly');
  $('schedTimeRow').classList.toggle('hidden', !(k === 'daily' || k === 'weekly' || (k === 'interval' && +$('schedUnit').value === 1440)));
}
function renderScheduleV2(s) {
  SCHED = s;   // az „elavult adat” sáv küszöbéhez
  $('schedNow').textContent = s.kind !== 'off' ? s.text + (s.ours ? '' : ' (másik mappa Letöltőjére mutat!)') : 'kikapcsolva';
  let k = s.kind;
  if (k === 'interval' && PRESET_MIN[String(s.minutes)]) k = String(s.minutes);
  $('schedKind').value = k;
  if (s.kind === 'interval') {
    const u = s.minutes % 1440 === 0 ? 1440 : s.minutes % 60 === 0 ? 60 : 1;
    $('schedUnit').value = u; $('schedN').value = s.minutes / u;
  }
  if (s.time) $('schedTime').value = s.time;
  $('schedDays').querySelectorAll('input').forEach(c => c.checked = (s.days || []).includes(c.value));
  $('schedApply').disabled = !s.hasDownloader;
  if (!s.hasDownloader) { $('schedMsg').className = 'msg err'; $('schedMsg').textContent = 'A Letöltő.exe nincs a program mappájában.'; }
  schedShow();
}
function schedSpec() {
  const k = $('schedKind').value;
  $('schedNErr').textContent = '';
  if (k === 'off') return { kind: 'off' };
  if (PRESET_MIN[k]) return { kind: 'interval', minutes: PRESET_MIN[k] };
  if (k === 'interval') {
    const n = String($('schedN').value).trim(), u = +$('schedUnit').value;
    if (!/^\d+$/.test(n) || +n < 1) { $('schedNErr').textContent = '⚠ pozitív egész szám kell'; return null; }
    const m = +n * u;
    if (m < 5) { $('schedNErr').textContent = '⚠ legalább 5 perc'; return null; }
    if (m > 1440 * 31) { $('schedNErr').textContent = '⚠ legfeljebb 31 nap'; return null; }
    if (m > 1440 && m % 1440) { $('schedNErr').textContent = '⚠ egy napnál hosszabb gyakoriság csak egész napokban'; return null; }
    if (u === 1 && m >= 60 && m % 60 && m < 1440) { /* pl. 90 perc – a Feladatütemező percben kezeli */ }
    return { kind: 'interval', minutes: m, time: $('schedTime').value || '06:00' };
  }
  const t = $('schedTime').value;
  if (!/^\d{2}:\d{2}$/.test(t)) { $('schedMsg').className = 'msg err'; $('schedMsg').textContent = 'Adj meg időpontot.'; return null; }
  if (k === 'daily') return { kind: 'daily', time: t };
  const days = [...$('schedDays').querySelectorAll('input:checked')].map(c => c.value);
  if (!days.length) { $('schedMsg').className = 'msg err'; $('schedMsg').textContent = 'Válassz legalább egy napot.'; return null; }
  return { kind: 'weekly', time: t, days };
}
['schedKind', 'schedUnit'].forEach(id => $(id).addEventListener('change', schedShow));
$('schedApply').addEventListener('click', async () => {
  const spec = schedSpec();
  if (!spec) return;
  const msg = $('schedMsg');
  msg.className = 'msg'; msg.textContent = 'Beállítás…';
  try {
    const s = await post('/api/schedule', spec);
    renderScheduleV2(s);
    msg.className = 'msg ' + (s.message && /nem sikerült|érvénytelen|válassz|kell/i.test(s.message) ? 'err' : 'ok');
    msg.textContent = s.message || 'Kész.';
  } catch (e) { msg.className = 'msg err'; msg.textContent = e.message; }
});

// ------------------------------------------------------------------ biztonsági mentés
function renderBackup(b) {
  $('bkDays').value = [0, 1, 7, 14, 30].includes(b.days) ? String(b.days) : '7';
  $('bkKeep').value = b.keep;
  $('bkLast').textContent = b.last ? b.last + ' (' + num(b.lastSize / 1024, 1) + ' MB, összesen ' + b.count + ' mentés)' + (b.due ? ' – esedékes' : '') : 'még nincs mentés';
}
async function loadBackup() { try { renderBackup(await api('/api/backup')); } catch (e) { } }
$('bkSave').addEventListener('click', async () => {
  const keep = String($('bkKeep').value).trim();
  $('bkErr').textContent = '';
  if (!/^\d+$/.test(keep) || +keep < 1 || +keep > 1000) { $('bkErr').textContent = '⚠ 1 és 1000 közötti egész szám'; return; }
  try {
    const r = await post('/api/dlsettings', { MentesNaponta: $('bkDays').value, MentesekSzama: keep });
    $('bkMsg').className = 'msg ' + (r.error ? 'err' : 'ok'); $('bkMsg').textContent = r.error || 'Mentve.';
    loadBackup();
  } catch (e) { $('bkMsg').className = 'msg err'; $('bkMsg').textContent = e.message; }
});
$('bkNow').addEventListener('click', async () => {
  $('bkMsg').className = 'msg'; $('bkMsg').textContent = 'Mentés készül…';
  $('bkNow').disabled = true;
  try { const b = await post('/api/backup'); renderBackup(b); $('bkMsg').className = 'msg ok'; $('bkMsg').textContent = 'Kész: ' + (b.last || ''); }
  catch (e) { $('bkMsg').className = 'msg err'; $('bkMsg').textContent = e.message; }
  finally { $('bkNow').disabled = false; }
});

// ------------------------------------------------------------------ változásnapló
let CL = [];
async function loadChangelog() {
  try { CL = await api('/api/changelog'); } catch (e) { CL = []; }
  renderChangelog();
}
function renderChangelog() {
  const q = ($('clFilter').value || '').toLowerCase();
  const rows = CL.map((e, i) => Object.assign({ i }, e)).filter(e => !q || [e.area, e.key, e.from, e.to].join(' ').toLowerCase().includes(q)).reverse().slice(0, 400);
  $('clList').innerHTML = rows.length ? '<table class="dtable"><thead><tr><th>Mikor</th><th>Hol</th><th>Mit</th><th>Régi</th><th>Új</th><th></th></tr></thead><tbody>' +
    rows.map(e => '<tr><td>' + esc(e.t) + '</td><td>' + esc(e.area || '') + '</td><td>' + esc(e.key || '') + '</td><td>' + esc(e.from == null ? '–' : e.from) + '</td><td><b>' + esc(e.to == null ? '–' : e.to) + '</b></td><td>' +
      (e.revert ? '<button class="btn small" data-rev="' + e.i + '" title="A régi érték visszaállítása">↶ visszaállít</button>' : '') + '</td></tr>').join('') + '</tbody></table>'
    : '<div class="muted" style="padding:10px">' + (q ? 'Nincs találat.' : 'Még nincs bejegyzés.') + '</div>';
  $('clList').querySelectorAll('[data-rev]').forEach(b => b.addEventListener('click', () => revertChange(CL[+b.dataset.rev])));
}
$('clFilter').addEventListener('input', renderChangelog);

async function revertChange(e) {
  const r = e.revert;
  if (!r || !confirm('Visszaállítod: „' + e.key + '” → ' + (e.from == null ? '(alap)' : e.from) + '?')) return;
  if (r.type === 'ui') {
    const parts = r.key.split('.');
    if (parts.length === 2) { UI[parts[0]] = Object.assign({}, UI[parts[0]] || {}); if (r.value == null) delete UI[parts[0]][parts[1]]; else UI[parts[0]][parts[1]] = r.value; }
    else if (r.value == null) delete UI[r.key]; else UI[r.key] = r.key === 'minFont' ? parseInt(r.value) || 0 : r.value;
    saveUi(); logUi(r.key, e.to, e.from);
    applyTheme(); applyFont(); applyLang(); syncUnitButtons(); refreshView();
  } else if (r.type === 'calib') {
    await post('/api/calib', JSON.parse(r.value));
    if (typeof waitReload === 'function') await waitReload();
  } else if (r.type === 'ini') {
    if (r.value == null) { toast('Ennek nincs korábbi értéke.'); return; }
    await post('/api/ini', { [r.key]: r.value });
    if (typeof waitReload === 'function') await waitReload();
  } else if (r.type === 'dl') {
    await post('/api/dlsettings', { [r.key]: r.value }); loadBackup();
  } else if (r.type === 'drought') {
    await getDrought();
    const cfg = JSON.parse(JSON.stringify(DR.config));
    let o = cfg;
    for (let i = 0; i < r.path.length - 1; i++) {
      const p = r.path[i];
      o = p[0] === '#' ? (o.find(x => x.id === p.slice(1))) : o[p];
      if (!o) { toast('Ez a beállítás már nem létezik.'); return; }
    }
    const last = r.path[r.path.length - 1];
    if (last[0] === '#') {
      // profil visszaállítása (törölt profil vissza / új profil törlése)
      const id = last.slice(1), arr = o, idx = arr.findIndex(x => x.id === id);
      if (r.value && idx < 0) arr.push(r.value); else if (!r.value && idx >= 0) arr.splice(idx, 1);
    } else o[last] = r.value;
    DR.config = cfg;
    await saveDroughtConfig('Visszaállítva.');
  }
  toast('Visszaállítva.');
  loadChangelog();
}

// ------------------------------------------------------------------ frissítés-ellenőrzés
async function checkUpdate() {
  try {
    const u = await api('/api/update');
    const b = $('updateBanner');
    if (!u.available) { b.classList.add('hidden'); return; }
    b.innerHTML = '⬆ Elérhető a <b>' + esc(u.version) + '</b> verzió (most: ' + esc(u.current) + '). <button class="btn primary small" id="updNow">Telepítés most</button> <span class="muted">A program bezárul, a telepítő frissíti; az adatok és a beállítások megmaradnak.</span>';
    b.classList.remove('hidden');
    $('updNow').addEventListener('click', async () => {
      if (!confirm('Bezárod a programot, és elindítod a ' + u.version + '-es telepítőt?')) return;
      await post('/api/update');
    });
  } catch (e) { }
}

// ------------------------------------------------------------------ helyszín (megjelenito.ini + a szárazság-modul magassága)
async function renderPlace() {
  const ini = await getIni(true), v = ini.values || {};
  const dr = typeof getDrought === 'function' ? await getDrought().catch(() => null) : null;
  const elev = dr && dr.config && dr.config.global ? dr.config.global.elevation : null;
  const fromData = META && META.latSource === 'adat';
  const f = (id, label, input, hint) => '<div class="pl-row"><label for="' + id + '">' + label + '</label>' + input + (hint ? '<div class="hint">' + hint + '</div>' : '') + '<div class="perr" id="' + id + 'Err"></div></div>';
  const numIn = (id, val, w) => '<input class="inp num" id="' + id + '" inputmode="decimal" style="width:' + (w || 90) + 'px" value="' + (val == null || val === '' ? '' : String(val).replace('.', decSep())) + '">';
  const t1 = tconv(parseFloat(String(v.HosegFigyelo1 || 25).replace(',', '.'))), t3 = tconv(parseFloat(String(v.HosegFigyelo3 || 27).replace(',', '.')));
  $('placeBox').innerHTML =
    f('plName', 'A hely neve', '<input class="inp" id="plName" maxlength="40" style="width:220px" value="' + esc(v.HelyNev || '') + '" placeholder="' + esc((META && META.neighborhood) || (META && META.station) || '') + '">',
      'Üresen az állomás WU-helyneve' + (META && META.neighborhood ? ' (most: ' + esc(META.neighborhood) + ')' : '') + '. A fejlécben és a jelentésekben látszik.') +
    f('plTz', 'Időzóna', '<select class="sel" id="plTz">' + (ini.zones || []).map(z => '<option value="' + esc(z) + '"' + (z === (v.Idozona || 'Europe/Budapest') ? ' selected' : '') + '>' + esc(z.replace(/_/g, ' ')) + '</option>').join('') + '</select>',
      'Ebben az időzónában kezdődik a nap, és ebben látszik minden időpont.') +
    f('plLat', 'Tartalék koordináták', numIn('plLat', v.TartalekSzelesseg) + ' É  ' + numIn('plLon', v.TartalekHosszusag) + ' K',
      fromData ? 'Most nem használt: az adatokban van koordináta (' + num(META.lat, 4) + '; ' + num(META.lon, 4) + ').' : '<b>Most ez számít</b>: az adatokban nincs koordináta (napkelte, hold, párolgás).') +
    f('plElev', 'Tengerszint feletti magasság', numIn('plElev', elev, 80) + ' m', 'A párolgás (ET₀) számításához.') +
    // a hőséggel kapcsolatos küszöbök egy helyen, megnevezve (6.30)
    '<h4 class="pl-sub">Hőség-küszöbök</h4>' +
    f('plHot', 'Hőségnap (napi maximum legalább)', numIn('plHot', +tconv(parseFloat(String(v.HosegNap || 30).replace(',', '.'))).toFixed(1), 70) + ' ' + tlabel(),
      'A Statisztika és a Jelentés hőségnapjai, a hőhullám.') +
    f('plH1', 'Hőség-figyelő (napi középhőmérséklet)', numIn('plH1', +t1.toFixed(1), 70) + ' ' + tlabel() + ' (I–II.)  ' + numIn('plH3', +t3.toFixed(1), 70) + ' ' + tlabel() + ' (III.)',
      'A magyar hőségriasztás szerint 25 és 27 °C. A „Hőség” jelzés az Áttekintés Figyelő kártyáján.') +
    f('plPlaus', 'Hihetőségi határ', numIn('plPlaus', +tconv(parseFloat(String(v.HihetoMaxHomerseklet || 41.9).replace(',', '.'))).toFixed(1), 70) + ' ' + tlabel(),
      'E fölötti hőmérséklet-csúcs a rekordoknál „ellenőrizetlen” jelzést kap (alapból az országos rekord, 41,9 °C). A Windows-értesítés hőség-küszöbe a Riasztások résznél állítható.') +
    f('plNorm', 'Havi sokéves csapadékátlag (mm, január–december)', '<input class="inp" id="plNorm" style="width:min(100%,420px)" value="' + esc(v.HaviCsapadekAtlag || '') + '">',
      'Pontosvesszővel elválasztott 12 szám. A havi jelentés ehhez méri, hogy egy hónap száraz vagy csapadékos volt. Alapból Budapest (1991–2020).') +
    f('plCur', 'Pénznem', '<input class="inp" id="plCur" maxlength="6" style="width:70px" value="' + esc(v.Penznem || 'Ft') + '">', 'A Víz alfül költségeihez.');
}
function plNum(id, lo, hi) {
  const el = $(id), s = String(el.value).trim().replace(',', '.'), x = /^-?\d+(\.\d+)?$/.test(s) ? parseFloat(s) : NaN;
  const bad = isNaN(x) || x < lo || x > hi;
  el.classList.toggle('bad', bad);
  const err = $(id + 'Err') || $(id.replace(/Lon$/, 'Lat').replace(/H3$/, 'H1') + 'Err');
  if (bad && err) err.textContent = '⚠ ' + num(lo, 0) + ' és ' + num(hi, 0) + ' között';
  return bad ? null : x;
}
$('placeSave').addEventListener('click', async () => {
  document.querySelectorAll('#placeBox .perr').forEach(e => e.textContent = '');
  const lat = plNum('plLat', -90, 90), lon = plNum('plLon', -180, 180), elev = plNum('plElev', -500, 9000);
  const tF = units().temp === 'F', tb = (lo, hi) => tF ? [lo * 1.8 + 32, hi * 1.8 + 32] : [lo, hi];
  const h1 = plNum('plH1', ...tb(15, 40)), h3 = plNum('plH3', ...tb(15, 40)), hot = plNum('plHot', ...tb(20, 45)), plaus = plNum('plPlaus', ...tb(25, 60));
  const name = $('plName').value.trim(), curc = $('plCur').value.trim();
  if (/[=#]/.test(name)) { $('plNameErr').textContent = '⚠ „=” és „#” nem lehet benne'; return; }
  if (!curc || /[=#]/.test(curc)) { $('plCurErr').textContent = '⚠ 1–6 karakter, pl. Ft, €, EUR'; return; }
  const normS = $('plNorm').value.trim(), normOk = normS.split(';').length === 12 && normS.split(';').every(x => /^\s*\d+([.,]\d+)?\s*$/.test(x));
  $('plNorm').classList.toggle('bad', !normOk);
  if (!normOk) { $('plNormErr').textContent = '⚠ 12 szám kell, pontosvesszővel (pl. 37;33;34;…)'; return; }
  if ([lat, lon, elev, h1, h3, hot, plaus].some(x => x == null)) { $('placeMsg').className = 'msg err'; $('placeMsg').textContent = 'Javítsd a pirossal jelölt mezőket – nem mentettem.'; return; }
  const msg = $('placeMsg'); msg.className = 'msg'; msg.textContent = 'Mentés…';
  try {
    await post('/api/ini', { HelyNev: name, Idozona: $('plTz').value, TartalekSzelesseg: lat, TartalekHosszusag: lon, HosegFigyelo1: +qback('temp', h1).toFixed(1), HosegFigyelo3: +qback('temp', h3).toFixed(1), HosegNap: +qback('temp', hot).toFixed(1), HihetoMaxHomerseklet: +qback('temp', plaus).toFixed(1), HaviCsapadekAtlag: normS.replace(/\s+/g, ''), Penznem: curc });
    if (typeof DR !== 'undefined' && DR && DR.config.global.elevation !== elev) { DR.config.global.elevation = elev; await saveDroughtConfig(); }
    const ready = typeof waitReload === 'function' ? await waitReload() : true;
    msg.className = 'msg ok'; msg.textContent = savedMsg(ready);
    renderPlace();
  } catch (e) { msg.className = 'msg err'; msg.textContent = '⚠ ' + e.message; }
});

// ------------------------------------------------------------------ felhőzet-ellenőrzés (METAR)
async function renderMetar() {
  const dl = await api('/api/dlsettings');
  const m = await api('/api/cloud/metar').catch(() => null);
  const on = String(dl.MetarEllenorzes || '').toLowerCase() === 'true';
  const code = dl.MetarKod || 'LHBP';
  const cat = ['derült', 'részben felhős', 'borult'];
  let res;
  if (!m || !m.pairs) {
    res = '<p class="muted">Még nincs összevethető adat. ' + (on ? 'A következő letöltéskor (vagy a „Letöltés most” gombbal) megjönnek a jelentések; a repülőtér kb. az utolsó 8–15 napot adja vissza.' : 'Kapcsold be, és a következő letöltéskor megjönnek a jelentések.') + '</p>';
  } else {
    const mx = m.matrix || [];
    res = '<p><b>' + num(m.pairs, 0) + '</b> összevetés (' + esc(m.icao) + ', ' + F.dayY.format(m.from) + ' – ' + F.dayY.format(m.to) + '): ' +
      'a kategória (derült / részben felhős / borult) <b>' + num(m.agree, 0) + ' %</b>-ban egyezik, átlagosan <b>' + num(m.meanAbsOkta, 1) + ' okta</b> az eltérés.</p>' +
      '<p class="small">Nappal: ' + num(m.dayPairs, 0) + ' összevetés, ' + num(m.dayAgree, 0) + ' % egyezés · éjjel: ' + num(m.nightPairs, 0) + ' összevetés, ' + num(m.nightAgree, 0) + ' % egyezés.</p>' +
      '<div class="tablewrap"><table class="dtable small"><caption class="muted">Sor: a repülőtér szerint · oszlop: a becslés szerint (darab)</caption><thead><tr><th></th>' + cat.map(c => '<th>' + c + '</th>').join('') + '</tr></thead><tbody>' +
      mx.map((r, i) => '<tr><th>' + cat[i] + '</th>' + r.map(x => '<td>' + x + '</td>').join('') + '</tr>').join('') + '</tbody></table></div>' +
      (m.suggestClearK != null && Math.abs(m.suggestClearK - m.curClearK) >= 0.02 ? '<p>Javaslat: a <b>derült-küszöb</b> legyen <b>' + num(m.suggestClearK, 2) + '</b> (most ' + num(m.curClearK, 2) + '; ' + num(m.clearN, 0) + ' derült időpont alapján). <button class="btn small" id="metarApply">Alkalmaz</button></p>' : '') +
      '<p class="hint">A repülőtér kb. 20 km-re lehet: a felhőzet helyben eltérhet, ezért ez statisztikai ellenőrzés, nem pillanatnyi.</p>';
  }
  $('metarBox').innerHTML =
    '<div class="row2"><label class="switch"><input type="checkbox" id="metarOn"' + (on ? ' checked' : '') + '><span class="slider"></span><span>Ellenőrzés a repülőtér jelentéseivel</span></label></div>' +
    '<div class="pl-row"><label for="metarCode">Repülőtér ICAO-kódja</label><input class="inp" id="metarCode" maxlength="4" style="width:80px;text-transform:uppercase" value="' + esc(code) + '"> <span class="hint">pl. LHBP (Budapest), LOWW (Bécs), LHDC (Debrecen)</span><div class="perr" id="metarCodeErr"></div></div>' +
    '<div class="row2"><button class="btn primary" id="metarSave">Mentés</button><button class="btn" id="metarNow">Letöltés most</button></div>' + res;
  $('metarSave').addEventListener('click', async () => {
    const c = $('metarCode').value.trim().toUpperCase();
    if (!/^[A-Z]{4}$/.test(c)) { $('metarCodeErr').textContent = '⚠ 4 betű, pl. LHBP'; return; }
    try { await post('/api/dlsettings', { MetarEllenorzes: $('metarOn').checked ? 'true' : 'false', MetarKod: c }); $('metarMsg').className = 'msg ok'; $('metarMsg').textContent = '✔ Mentve.'; renderMetar(); }
    catch (e) { $('metarMsg').className = 'msg err'; $('metarMsg').textContent = '⚠ ' + e.message; }
  });
  $('metarNow').addEventListener('click', async () => {
    $('metarMsg').className = 'msg'; $('metarMsg').textContent = 'Letöltés…'; $('metarNow').disabled = true;
    try { await post('/api/cloud/metar'); $('metarMsg').className = 'msg ok'; $('metarMsg').textContent = '✔ Kész.'; renderMetar(); }
    catch (e) { $('metarMsg').className = 'msg err'; $('metarMsg').textContent = '⚠ ' + e.message; }
    finally { if ($('metarNow')) $('metarNow').disabled = false; }
  });
  if ($('metarApply')) $('metarApply').addEventListener('click', async () => {
    try { await post('/api/ini', { FelhoDerultK: m.suggestClearK }); const ready = typeof waitReload === 'function' ? await waitReload() : true; $('metarMsg').className = 'msg ok'; $('metarMsg').textContent = ready ? '✔ Alkalmazva, újraszámolva.' : savedMsg(false); renderMetar(); }
    catch (e) { $('metarMsg').className = 'msg err'; $('metarMsg').textContent = '⚠ ' + e.message; }
  });
}

// ------------------------------------------------------------------ a Beállítások fül megnyitásakor
async function renderSettingsV2() {
  applyFont(); syncUnitButtons(); $('langSel').value = lang();
  try { renderScheduleV2(await api('/api/schedule')); } catch (e) { $('schedNow').textContent = 'nem lekérdezhető'; }
  loadBackup(); loadChangelog(); renderPlace().catch(e => { $('placeBox').textContent = 'Nem tölthető be: ' + e.message; });
  renderMetar().catch(e => { $('metarBox').textContent = 'Nem tölthető be: ' + e.message; });
  if (typeof renderCalib === 'function') renderCalib();
  if (typeof renderSuspect === 'function') renderSuspect($('suCard')).catch(e => { $('suCard').textContent = 'Nem tölthető be: ' + e.message; });
}
