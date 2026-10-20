'use strict';
/* Budaörsi időjárás – felület */

let TZ = 'Europe/Budapest';   // a META-ból felülíródik (Beállítások › Helyszín › Időzóna)
// a hely neve a felületen (Beállítások › Helyszín; alapból az állomás WU-helyneve)
function placeName() { return (typeof META !== 'undefined' && META && (META.place || META.station)) || ''; }
const $ = id => document.getElementById(id);
const cssVar = n => getComputedStyle(document.documentElement).getPropertyValue(n).trim();
const sleep = ms => new Promise(r => setTimeout(r, ms));
const H = 3600e3, D = 24 * H;

// ------------------------------------------------------------------ nyelv és formázók (a nyelv a Beállításokban állítható)
// a nyelvek a web/lang/<kód>.json fájlokból jönnek (i18n.js); itt csak a kód és a hozzá tartozó számformátum
function lang() { let u = null; try { u = UI; } catch (e) { } return (u && typeof u.lang === 'string' && /^[a-z]{2,3}(-[A-Za-z]{2})?$/.test(u.lang)) ? u.lang : 'hu'; }
function numLocale() {
  const L = lang();
  if (L === 'hu') return 'hu-HU';
  const m = typeof langMeta === 'function' ? langMeta(L) : {};
  try { return m.locale && Intl.NumberFormat.supportedLocalesOf([m.locale]).length ? m.locale : L; } catch (e) { return 'hu-HU'; }
}
const F = {};
function buildFormatters() {
  const L = numLocale();
  F.time = new Intl.DateTimeFormat(L, { timeZone: TZ, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
  F.day = new Intl.DateTimeFormat(L, { timeZone: TZ, month: 'short', day: 'numeric' });
  F.dayY = new Intl.DateTimeFormat(L, { timeZone: TZ, year: 'numeric', month: 'short', day: 'numeric' });
  F.month = new Intl.DateTimeFormat(L, { timeZone: TZ, year: 'numeric', month: 'short' });
  F.full = new Intl.DateTimeFormat(L, { timeZone: TZ, year: 'numeric', month: 'short', day: 'numeric', weekday: 'short', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
  F.ymd = new Intl.DateTimeFormat('en-CA', { timeZone: TZ, year: 'numeric', month: '2-digit', day: '2-digit' });
  F.hour = new Intl.DateTimeFormat('en-GB', { timeZone: TZ, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
}
buildFormatters();

function num(v, d = 1) {
  if (v === null || v === undefined || isNaN(v)) return '–';
  return Number(v).toLocaleString(numLocale(), { minimumFractionDigits: d, maximumFractionDigits: d });
}
function esc(s) { return String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c])); }
function signed(v, d = 1) { if (v === null || isNaN(v)) return '–'; return (v > 0 ? '+' : v < 0 ? '−' : '±') + num(Math.abs(v), d); }

function relTime(ms) {
  const m = Math.round((Date.now() - ms) / 60000);
  if (m < 1) return 'épp most';
  if (m < 60) return m + ' perce';
  const h = Math.round(m / 60);
  if (h < 48) return h + ' órája';
  return Math.round(h / 24) + ' napja';
}
function dayLabel(ms) {
  const today = F.ymd.format(Date.now()), d = F.ymd.format(ms);
  const y = F.ymd.format(Date.now() - D);
  if (d === today) return 'ma ' + F.time.format(ms);
  if (d === y) return 'tegnap ' + F.time.format(ms);
  return F.full.format(ms);
}

// ------------------------------------------------------------------ mértékegységek (belül mindig metrikus; csak a kijelzés vált)
function units() {
  const u = (typeof UI !== 'undefined' && UI.units) || {};
  return {
    temp: u.temp === 'F' ? 'F' : 'C',
    rain: u.rain === 'in' ? 'in' : 'mm',
    pres: u.pres === 'inHg' ? 'inHg' : 'hPa',
    wind: ['kmh', 'kn', 'bft', 'ms', 'mph'].includes(u.wind) ? u.wind : (UI && UI.windUnit) || (META && META.settings.windUnit === 'csomo' ? 'kn' : META && META.settings.windUnit === 'beaufort' ? 'bft' : 'kmh')
  };
}
// hőmérséklet (abszolút) és hőmérséklet-különbség
function tconv(c) { if (c == null || isNaN(c)) return null; return units().temp === 'F' ? c * 9 / 5 + 32 : c; }
function tdconv(c) { if (c == null || isNaN(c)) return null; return units().temp === 'F' ? c * 9 / 5 : c; }
function tlabel() { return units().temp === 'F' ? '°F' : '°C'; }
function ttext(c, dec = 1) { const v = tconv(c); return v == null ? '–' : num(v, dec) + ' ' + tlabel(); }
function tdtext(c, dec = 1) { const v = tdconv(c); return v == null ? '–' : num(v, dec) + ' ' + tlabel(); }
function tsigned(c, dec = 1) { const v = tdconv(c); return v == null ? '–' : signed(v, dec) + ' ' + tlabel(); }
// csapadék / vízmagasság (mm → inch: egy tizedessel több)
function rconv(mm) { if (mm == null || isNaN(mm)) return null; return units().rain === 'in' ? mm / 25.4 : mm; }
function rlabel() { return units().rain === 'in' ? 'in' : 'mm'; }
function rdec(dec) { return units().rain === 'in' ? dec + 1 : dec; }
function rtext(mm, dec = 1) { const v = rconv(mm); return v == null ? '–' : num(v, rdec(dec)) + ' ' + rlabel(); }
// küszöbérték kiírása (pl. ≥ 1 mm → ≥ 0,04 in)
function rthr(mm) { return units().rain === 'in' ? num(mm / 25.4, 2) + ' in' : num(mm, mm % 1 ? 1 : 0) + ' mm'; }
function rrlabel() { return units().rain === 'in' ? 'in/h' : 'mm/h'; }
function rrtext(mmh, dec = 1) { const v = rconv(mmh); return v == null ? '–' : num(v, units().rain === 'in' ? dec + 2 : dec) + ' ' + rrlabel(); }
// légnyomás (hPa → inHg: 0,02953)
function pconv(h) { if (h == null || isNaN(h)) return null; return units().pres === 'inHg' ? h * 0.0295300 : h; }
function plabel() { return units().pres === 'inHg' ? 'inHg' : 'hPa'; }
function pdec(dec) { return units().pres === 'inHg' ? dec + 1 : dec; }
function ptext(h, dec = 1) { const v = pconv(h); return v == null ? '–' : num(v, pdec(dec)) + ' ' + plabel(); }
function psigned(h, dec = 1) { const v = pconv(h); return v == null ? '–' : signed(v, pdec(dec)) + ' ' + plabel(); }

// általános mennyiség-átváltás: q = temp | tdiff | rain | rate | wind | pres | presd | gdd (más: változatlan)
const QTY = {
  temp: { c: v => tconv(v), l: () => tlabel(), d: d => d },
  tdiff: { c: v => tdconv(v), l: () => tlabel(), d: d => d },
  gdd: { c: v => tdconv(v), l: () => tlabel() + '·nap', d: d => d },
  rain: { c: v => rconv(v), l: () => rlabel(), d: d => rdec(d) },
  rate: { c: v => rconv(v), l: () => rrlabel(), d: d => rdec(d) },
  wind: { c: v => wconv(v), l: () => wlabel(), d: d => (windUnit() === 'kmh' || windUnit() === 'mph') ? d : Math.max(1, d) },
  pres: { c: v => pconv(v), l: () => plabel(), d: d => pdec(d) },
  presd: { c: v => pconv(v), l: () => plabel(), d: d => pdec(d) }
};
const UNIT_Q = { '°C': 'temp', 'mm': 'rain', 'mm/h': 'rate', 'km/h': 'wind', 'hPa': 'pres' };
function qv(q, v) { if (v == null || isNaN(v)) return null; return QTY[q] ? QTY[q].c(v) : v; }
function qu(q, unit) { return QTY[q] ? QTY[q].l() : (unit || ''); }
function qd(q, dec) { return QTY[q] ? QTY[q].d(dec) : dec; }
// visszaalakítás a kijelzett egységből a belső metrikusba (beviteli mezőkhöz)
function qback(q, x) {
  if (x == null || isNaN(x)) return null;
  const u = units();
  switch (q) {
    case 'temp': return u.temp === 'F' ? (x - 32) * 5 / 9 : x;
    case 'tdiff': case 'gdd': return u.temp === 'F' ? x * 5 / 9 : x;
    case 'rain': case 'rate': return u.rain === 'in' ? x * 25.4 : x;
    case 'pres': case 'presd': return u.pres === 'inHg' ? x / 0.0295300 : x;
    case 'wind': return ({ kn: x * 1.852, ms: x * 3.6, mph: x * 1.609344, bft: 0.836 * Math.pow(Math.max(0, x), 1.5) * 3.6 })[u.wind] || x;
  }
  return x;
}
function qtext(q, v, dec, unit) { const x = qv(q, v); return x == null ? '–' : num(x, qd(q, dec)) + (qu(q, unit) ? ' ' + qu(q, unit) : ''); }

// ------------------------------------------------------------------ betűméret (akadálymentesség)
// a kis betűk legalább UI.minFont px-esek; a közepesek arányosan nőnek, a nagy címek alig
function fs(px) {
  const m = (typeof UI !== 'undefined' && UI.minFont) || 0;
  if (!m) return px;
  if (px <= 13.5) return Math.max(px, m);
  if (px <= 16) return Math.max(px, m + 2);
  if (px <= 24) return Math.max(px, m + 6);
  return px;
}

// a grafikonok alap betűmérete (tengelyfeliratok stb.) is kövesse a beállítást
(function () {
  const init0 = echarts.init;
  echarts.init = function () {
    const c = init0.apply(this, arguments), so = c.setOption;
    c.setOption = function (o) {
      if (o && typeof o === 'object' && UI && UI.minFont) o.textStyle = Object.assign({ fontSize: fs(12) }, o.textStyle || {});
      if (o && typeof o === 'object' && typeof trOption === 'function') trOption(o, 0);
      return so.apply(this, arguments);
    };
    return c;
  };
})();

// ------------------------------------------------------------------ szél segédek
const BFT = [1, 6, 12, 20, 29, 39, 50, 62, 75, 89, 103, 118];
const BFT_NAME = ['szélcsend', 'gyenge szellő', 'enyhe szél', 'gyenge szél', 'mérsékelt szél', 'élénk szél', 'erős szél',
  'igen erős szél', 'viharos szél', 'erős vihar', 'teljes vihar', 'orkánszerű vihar', 'orkán'];
// egész fokozat (a fokozat nevéhez) a WMO-határok szerint
function beaufort(kmh) { if (kmh == null || isNaN(kmh)) return null; let b = 0; while (b < BFT.length && kmh >= BFT[b]) b++; return b; }
// tört Beaufort: a WMO-képlet (v = 0,836 · B^1,5 m/s) megfordítva, legfeljebb 12
function beaufortF(kmh) { if (kmh == null || isNaN(kmh)) return null; return Math.min(12, Math.pow(Math.max(0, kmh / 3.6) / 0.836, 2 / 3)); }
function windUnit() { return units().wind; }
function wconv(kmh) {
  if (kmh == null || isNaN(kmh)) return null;
  switch (windUnit()) {
    case 'kn': return kmh / 1.852;
    case 'bft': return beaufortF(kmh);
    case 'ms': return kmh / 3.6;
    case 'mph': return kmh / 1.609344;
    default: return kmh;
  }
}
function wlabel() { return ({ kmh: 'km/h', kn: 'csomó', bft: 'Bft', ms: 'm/s', mph: 'mph' })[windUnit()]; }
// alapból: km/h és mph 1 tizedes (nagy értéknél egész), csomó, m/s és Bft 1 tizedes; a dec paraméter felülírja
function wdec(dec, kmh) { if (dec != null) return dec; const u = windUnit(); return u === 'kmh' || u === 'mph' ? (kmh >= 30 ? 0 : 1) : 1; }
function wtext(kmh, dec) { const v = wconv(kmh); return v == null ? '–' : num(v, wdec(dec, kmh)) + ' ' + wlabel(); }
const DIRS = ['É', 'ÉÉK', 'ÉK', 'KÉK', 'K', 'KDK', 'DK', 'DDK', 'D', 'DDNy', 'DNy', 'NyDNy', 'Ny', 'NyÉNy', 'ÉNy', 'ÉÉNy'];
function dirText(deg) { if (deg == null || isNaN(deg)) return '–'; return DIRS[Math.round(((deg % 360) + 360) % 360 / 22.5) % 16]; }
const MONTH_ADJ = ['januári', 'februári', 'márciusi', 'áprilisi', 'májusi', 'júniusi', 'júliusi', 'augusztusi', 'szeptemberi', 'októberi', 'novemberi', 'decemberi'];

// ------------------------------------------------------------------ API
async function api(path) {
  const r = await fetch(path, { cache: 'no-store' });
  const j = await r.json();
  if (!r.ok && r.status !== 503) throw new Error(j.error || ('HTTP ' + r.status));
  return j;
}
// módosító kérés (a szerver csak a saját fejléccel fogadja el)
async function post(path, body) {
  const r = await fetch(path, { method: 'POST', cache: 'no-store', headers: { 'X-Idojaras': '1', 'Content-Type': 'application/json' }, body: body == null ? '' : JSON.stringify(body) });
  const j = await r.json().catch(() => ({}));
  if (!r.ok) throw new Error(j.error || ('HTTP ' + r.status));
  return j;
}

// felület-beállítások (appdata_megjelenítő\felulet.json)
let UI = {};
let uiTimer = 0;
function saveUi() { clearTimeout(uiTimer); uiTimer = setTimeout(() => post('/api/ui', UI).catch(e => toast('A beállítás nem menthető: ' + e.message)), 300); }

let META = null, TODAY = null, BASE = null, DETAIL = null, chart = null;
let hideSuspect = false, detailToken = 0, currentRange = '7d', span = 7 * D;

// ------------------------------------------------------------------ betöltés, hibák
function overlay(mode, title, text, actions) {
  const ov = $('overlay');
  if (mode === 'hide') { ov.classList.add('hidden'); return; }
  ov.classList.remove('hidden');
  $('ovSpinner').classList.toggle('hidden', mode !== 'loading');
  $('ovTitle').textContent = title;
  $('ovText').innerHTML = text || '';
  const a = $('ovActions'); a.innerHTML = '';
  (actions || []).forEach(x => {
    const b = document.createElement('button');
    b.className = 'btn' + (x.primary ? ' primary' : '');
    b.textContent = x.label; b.onclick = x.onClick; a.appendChild(b);
  });
}

let STATUS = null;
async function waitReady() {
  for (;;) {
    const s = await api('/api/status');
    STATUS = s;
    if (s.status !== 'loading' && s.status !== 'downloading') return s;
    const title = s.status === 'downloading' ? 'Új adatok letöltése…' : 'Adatok betöltése…';
    if (!META) overlay('loading', title, esc(s.progress || ''));
    else setBusy(s.status === 'downloading' ? 'Letöltés: ' + (s.progress || '…') : 'Betöltés…');
    await sleep(400);
  }
}

function setBusy(text) {
  const b = $('busy');
  if (!text) { b.classList.add('hidden'); return; }
  b.classList.remove('hidden');
  $('busyText').textContent = text;
}

function showProblem(s) {
  if (s.status === 'nodata') {
    const dl = s.hasDownloader;
    const msg = s.downloadSerial > 0 && !s.downloadOk ? '<b>' + esc(s.downloadMessage) + '</b>\n\n' : '';
    overlay('problem', 'Még nincs időjárás-adat',
      msg + esc(s.message) + '\n\nA mérési adatok helye:\n<code>' + esc(s.dataDir) + '</code>\n\n' +
      (dl ? 'Kattints az <b>Adatok letöltése</b> gombra. Ha még nem adtad meg az API kulcsot, előbb indítsd el egyszer a <b>Letöltő.exe</b>-t dupla kattintással.'
          : 'Tedd a Letöltő.exe-t a Megjelenítő mellé, vagy másold a data mappába az <b>idojaras_adatok.csv</b> fájlt.'),
      [dl ? { label: 'Adatok letöltése', primary: true, onClick: () => reload(true) } : null,
       { label: 'Újraolvasás', primary: !dl, onClick: () => reload(false) }].filter(Boolean));
  } else {
    overlay('problem', 'Hiba történt', esc(s.message || 'Ismeretlen hiba.'),
      [{ label: 'Újrapróbálás', primary: true, onClick: () => reload(false) }]);
  }
}

async function start() {
  try {
    const s = await waitReady();
    if (s.status !== 'ready') { showProblem(s); return; }
    await loadAll(true);
    overlay('hide');
  } catch (e) {
    overlay('problem', 'Hiba történt', esc(e.message), [{ label: 'Újrapróbálás', primary: true, onClick: () => location.reload() }]);
  }
}

async function loadAll(first) {
  META = await api('/api/meta');
  if (META.settings && META.settings.tz && META.settings.tz !== TZ) { TZ = META.settings.tz; buildFormatters(); }
  TODAY = await api('/api/today');
  renderHeader(); renderToday(); renderFooter();
  if (typeof onDataLoaded === 'function') onDataLoaded();
  BASE = await api('/api/series?n=2400&clean=' + (hideSuspect ? 1 : 0));
  if (chart) {
    chart.setOption({ xAxis: xAxes().map(a => ({ min: a.min, max: a.max })) });
    applyData(); refreshDetail();
  } else ensureChart();
  if (currentTab === 'beallitasok') renderSettings();
}

// az idővonal grafikonja csak látható fülön épülhet fel (különben 0 a mérete)
function ensureChart() {
  if (chart || !BASE || currentTab !== 'idovonal') return;
  buildChart();
  setRange(currentRange || '7d');
}

// download = true: a Letöltő lefuttatása, utána újraolvasás
async function reload(download) {
  const btn = $('btnRefresh');
  btn.disabled = true; btn.classList.add('spin');
  try {
    const before = STATUS ? STATUS.downloadSerial : 0;
    await post('/api/refresh?download=' + (download ? 1 : 0));
    const s = await waitReady();
    setBusy(null);
    if (s.status !== 'ready') { showProblem(s); return; }
    const hadData = !!META;
    await loadAll(!hadData);
    overlay('hide');
    const dlMsg = download && s.downloadSerial > before ? s.downloadMessage : '';
    const rows = META.newRows > 0 ? num(META.newRows, 0) + ' új mérés' : 'nincs új mérés';
    if (dlMsg && !s.downloadOk) toast('⚠ ' + dlMsg + ' · ' + rows, 9000);
    else toast('Frissítve · ' + rows);
  } catch (e) {
    setBusy(null);
    toast('Hiba a frissítéskor: ' + e.message);
  } finally {
    btn.disabled = false; btn.classList.remove('spin');
  }
}
let toastTimer = 0;
function toast(msg, ms) {
  const t = $('toast'); t.textContent = msg; t.classList.add('show');
  clearTimeout(toastTimer); toastTimer = setTimeout(() => t.classList.remove('show'), ms || 3200);
}

// ------------------------------------------------------------------ fejléc, lábléc
function renderHeader() {
  const n = TODAY.now || {};
  $('sub').textContent = META.station + (placeName() !== META.station ? ' · ' + placeName() : '') + ' · utolsó mérés: ' + dayLabel(Math.max(META.last, n.t || 0));
  const b = $('staleBanner');
  if (n.ageMin > 90) {
    b.textContent = 'Az utolsó mérés ' + relTime(n.t) + ' készült (' + F.full.format(n.t) + '). Kattints a Frissítés gombra az új adatok letöltéséhez.';
    b.classList.remove('hidden');
  } else b.classList.add('hidden');
}

function renderFooter() {
  const m = META;
  const parts = [
    num(m.n, 0) + ' mérés',
    F.dayY.format(m.first) + ' – ' + F.dayY.format(m.last),
    m.files.length + ' CSV fájl (' + m.files.join(', ') + ')',
    (m.fromCache ? 'betöltve a cache-ből' : 'feldolgozva') + ' ' + num(m.loadMs / 1000, 1) + ' mp alatt',
    num(m.flaggedRows, 0) + ' gyanús mérés jelölve',
    m.gaps.length + ' adathiány (> ' + m.settings.gapMinutes + ' perc)'
  ];
  let html = parts.map(esc).join(' · ') + ' · v' + esc(m.version);
  if (m.warnings.length) html += '<br><span class="warn">⚠ ' + m.warnings.map(esc).join('<br>⚠ ') + '</span>';
  $('foot').innerHTML = html;
}

// ------------------------------------------------------------------ Ma kártyák
function renderToday() {
  const n = TODAY.now || {}, t = TODAY.today || {}, y = TODAY.year || {};
  const st = META.settings;
  const bft = beaufort(n.wind);
  const p3 = TODAY.now ? TODAY.now.pres3h : null;
  let trend = 'nincs adat';
  if (p3 != null && !isNaN(p3)) {
    const a = Math.abs(p3);
    const dir = p3 > 0 ? 'emelkedik' : 'csökken';
    trend = a < 1 ? 'állandó' : a < 3 ? 'lassan ' + dir : a < 6 ? 'gyorsan ' + dir : 'nagyon gyorsan ' + dir;
  }
  const arrow = p3 == null || isNaN(p3) || Math.abs(p3) < 1 ? '→' : p3 > 0 ? '↗' : '↘';
  // ha az utolsó adat nem mai (pl. régen futott a letöltő), ne írjunk "ma"-t
  const isToday = t.date === F.ymd.format(Date.now());
  const ma = isToday ? 'ma' : 'aznap', Ma = isToday ? 'Ma' : 'Aznap';
  const dayName = t.date ? F.day.format(Date.parse(t.date + 'T12:00:00')) : '';
  const ty = n.tempYesterday;
  const yLine = ty != null && !isNaN(ty) && n.temp != null
    ? ' · ' + (isToday ? 'Tegnap ilyenkor' : '24 órával korábban') + ' <b>' + ttext(ty) + '</b> (' + tsigned(n.temp - ty) + ')' : '';

  const year = [];
  if (y.warmRank) year.push('Rangsor a legmelegebb napok között: <b>' + y.warmRank + '.</b> a(z) ' + y.days + ' idei napból (az ' + (isToday ? 'eddigi mai' : 'aznapi') + ' maximum alapján)');
  if (y.coldRank) year.push('Rangsor a leghidegebb éjszakák között: <b>' + y.coldRank + '.</b>');
  if (y.monthTmaxAvg != null && t.tmax != null) {
    const d = t.tmax - y.monthTmaxAvg;
    const mi = new Date(t.date + 'T12:00:00').getMonth();
    year.push((isToday ? 'A mai' : 'Az aznapi') + ' maximum <b>' + tdtext(Math.abs(d)) + '-kal ' + (d >= 0 ? 'magasabb' : 'alacsonyabb') + '</b> az idei ' + MONTH_ADJ[mi] + ' napok átlagánál (' + ttext(y.monthTmaxAvg) + ')');
  }
  year.push('Idén eddig: <b>' + y.hotDays + '</b> hőségnap (≥ ' + ttext(st.hotDay, 0) + ') · <b>' + y.tropicalNights + '</b> trópusi éjszaka · <b>' + y.frostDays + '</b> fagyos nap');
  year.push('Idei csapadék: <b>' + rtext(y.rain, 1) + '</b>, ' + y.rainDays + ' esős nappal (≥ ' + rtext(1, 0) + ')');
  if (y.maxT != null) year.push('Az év legmelegebb pillanata: <b>' + ttext(y.maxT) + '</b> (' + F.dayY.format(y.maxTt) + '), leghidegebb: <b>' + ttext(y.minT) + '</b> (' + F.dayY.format(y.minTt) + ')');

  $('today').innerHTML = `
    <div class="card hero">
      <div>
        <h3>${isToday ? 'Most' : 'Utolsó mérés'} · ${esc(placeName())}</h3>
        <div class="temp">${num(tconv(n.temp))}<sup>${tlabel()}</sup></div>
        <div class="line">Hőérzet <b>${ttext(n.feels)}</b> · Harmatpont <b>${ttext(n.dew)}</b>${yLine}</div>
      </div>
      <div>
        <div class="minmax">
          <span>${isToday ? 'Ma' : dayName} ▼ <b>${ttext(t.tmin)}</b> ${t.tminT ? F.time.format(t.tminT) : ''}</span>
          <span>▲ <b>${ttext(t.tmax)}</b> ${t.tmaxT ? F.time.format(t.tmaxT) : ''}</span>
        </div>
        <div class="when">Utolsó mérés: ${n.t ? dayLabel(n.t) + ' (' + relTime(n.t) + ')' : '–'}</div>
      </div>
    </div>

    <div class="card">
      <h3>Szél</h3>
      <svg class="compass" viewBox="0 0 64 64" aria-hidden="true">
        <circle cx="32" cy="32" r="26"/>
        <text x="32" y="11">É</text><text x="32" y="60">D</text><text x="57" y="35">K</text><text x="7" y="35">Ny</text>
        ${n.dir != null && !isNaN(n.dir) ? `<path class="arrow" d="M32 13 L38.5 36 L32 31.5 L25.5 36 Z" style="transform: rotate(${n.dir + 180}deg)"/>` : ''}
      </svg>
      <div class="big">${num(wconv(n.wind), 1)}<small>${wlabel()}</small></div>
      <div class="row"><span>Lökés <b>${wtext(n.gust)}</b></span><span>${windUnit() === 'kmh' ? '<b>' + num(n.wind / 1.852, 1) + '</b> csomó' : '<b>' + num(n.wind, 1) + '</b> km/h'}</span></div>
      <div class="line">${bft == null ? '' : '<b>' + num(beaufortF(n.wind), 1) + ' Bft</b> – ' + BFT_NAME[bft] + ' · '}${dirText(n.dir)} felől${n.dir != null && !isNaN(n.dir) ? ' (' + num(n.dir, 0) + '°)' : ''}</div>
      <div class="line">${Ma} legnagyobb lökés: <b>${wtext(t.gustMax)}</b> ${t.gustMaxT ? F.time.format(t.gustMaxT) : ''}</div>
    </div>

    <div class="card">
      <h3>Légnyomás</h3>
      <div class="big">${num(pconv(n.pres), pdec(1))}<small>${plabel()}</small></div>
      <div class="line"><b>${arrow} ${psigned(p3)}</b> / 3 óra – ${trend}</div>
      <div class="line">${Ma}: <b>${num(pconv(t.presMin), pdec(1))} – ${num(pconv(t.presMax), pdec(1))}</b> ${plabel()}</div>
    </div>

    <div class="card">
      <h3>Páratartalom</h3>
      <div class="big">${num(n.hum, 0)}<small>%</small></div>
      <div class="line">Harmatpont <b>${ttext(n.dew)}</b></div>
      <div class="line">${Ma}: <b>${num(t.humMin, 0)} – ${num(t.humMax, 0)} %</b></div>
    </div>

    <div class="card">
      <h3>Csapadék</h3>
      <div class="big">${num(rconv(t.rain), rdec(1))}<small>${rlabel()} ${isToday ? 'ma' : dayName}</small></div>
      <div class="line">${isToday ? 'Most' : 'Utoljára'}: <b>${rrtext(n.rainRate, 1)}</b> · ${ma} max <b>${rrtext(t.rainRateMax, 1)}</b></div>
      <div class="line">Idén: <b>${rtext(y.rain, 0)}</b>, ${y.rainDays} esős nap</div>
    </div>

    <div class="card">
      <h3>Napsugárzás</h3>
      <div class="big">${num(n.solar, 0)}<small>W/m²</small></div>
      <div class="line">UV-index most <b>${num(n.uv, 0)}</b> · ${ma} max <b>${num(t.uvMax, 0)}</b></div>
      <div class="line">${Ma} napenergia: <b>${num(t.energy, 2)} kWh/m²</b></div>
    </div>

    <div class="card wide">
      <h3>${isToday ? 'A mai nap az idei évben' : 'Az utolsó adatnap (' + dayName + ') az idei évben'} <span style="text-transform:none;letter-spacing:0;font-weight:500">${isToday ? '(a nap még tart)' : ''}</span></h3>
      <ul class="year-list">${year.map(x => '<li>' + x + '</li>').join('')}</ul>
    </div>`;
  if (typeof renderInsights === 'function') renderInsights();
}

// ------------------------------------------------------------------ idővonal
const TOP0 = 92, STEP = 182, GH = 132;
const PANELS = [
  { title: 'Hőmérséklet és harmatpont', unit: '°C' },
  { title: 'Páratartalom', unit: '%' },
  { title: 'Légnyomás', unit: 'hPa' },
  { title: 'Szél és széllökés', unit: 'km/h' },
  { title: 'Csapadék', unit: 'mm/h · mm' },
  { title: 'Napsugárzás és UV-index', unit: 'W/m² · UV' }
];
const SERIES_INFO = {}; // id -> { unit, dec }

function axisFmt(v) {
  if (span > 120 * D) return F.month.format(v);
  const hm = F.hour.format(v);
  if (hm === '00:00') return F.day.format(v);
  return span > 12 * D ? F.day.format(v) : hm;
}

function xAxes() {
  const min = META.first, max = META.last;
  return PANELS.map((p, i) => ({
    type: 'time', gridIndex: i, min, max,
    axisLabel: { formatter: axisFmt, color: cssVar('--muted'), fontSize: fs(11), hideOverlap: true },
    axisLine: { lineStyle: { color: cssVar('--line') } },
    axisTick: { show: false },
    splitLine: { show: true, lineStyle: { color: cssVar('--line'), opacity: .6 } },
    axisPointer: { label: { show: i === 5, formatter: o => F.full.format(o.value) } }
  }));
}

function buildChart() {
  const el = $('chart');
  chart = echarts.init(el, null, { renderer: 'canvas' });
  const ink = cssVar('--ink'), muted = cssVar('--muted'), line = cssVar('--line');
  const yBase = (gi, name, extra) => Object.assign({
    type: 'value', gridIndex: gi, name, nameTextStyle: { color: muted, fontSize: fs(11), align: 'left' },
    axisLabel: { color: muted, fontSize: fs(11), formatter: v => num(v, Math.abs(v) < 10 && v % 1 ? (Math.abs(v) < 1 && v % 0.1 ? 2 : 1) : 0) },
    splitLine: { lineStyle: { color: line, opacity: .6 } }, scale: true
  }, extra || {});

  const C = {
    temp: cssVar('--c-temp'), dew: cssVar('--c-dew'), hum: cssVar('--c-hum'), pres: cssVar('--c-pres'),
    wind: cssVar('--c-wind'), gust: cssVar('--c-gust'), rain: cssVar('--c-rain'), sun: cssVar('--c-sun'), uv: cssVar('--c-uv'), flag: cssVar('--c-flag')
  };
  const ln = (id, name, xi, yi, color, unit, dec, extra) => {
    SERIES_INFO[id] = { unit, dec };
    return Object.assign({
      id, name, type: 'line', xAxisIndex: xi, yAxisIndex: yi, showSymbol: false, symbolSize: 4,
      connectNulls: false, lineStyle: { width: 1.6, color }, itemStyle: { color }, data: [], animation: false,
      emphasis: { disabled: true }
    }, extra || {});
  };
  const flags = (id, xi, yi) => ({
    id, name: 'Gyanús mérés', type: 'scatter', xAxisIndex: xi, yAxisIndex: yi, symbolSize: 7, z: 5,
    itemStyle: { color: C.flag, borderColor: '#fff', borderWidth: 1 }, data: [], animation: false
  });

  const option = {
    animation: false,
    textStyle: { fontFamily: '"Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif' },
    title: PANELS.map((p, i) => ({
      text: p.title, top: TOP0 + i * STEP - 30, left: 6,
      textStyle: { fontSize: fs(13.5), fontWeight: 600, color: ink }
    })),
    grid: PANELS.map((p, i) => ({ left: 58, right: 58, top: TOP0 + i * STEP, height: GH })),
    xAxis: xAxes(),
    yAxis: [
      yBase(0, tlabel()), yBase(1, '%', { min: 0, max: 100, scale: false }), yBase(2, plabel()),
      yBase(3, wlabel(), { min: 0, scale: false }), yBase(4, rrlabel(), { min: 0, scale: false }),
      yBase(4, rlabel(), { min: 0, scale: false, position: 'right', splitLine: { show: false } }),
      yBase(5, 'W/m²', { min: 0, scale: false }),
      // az UV-skála legfeljebb 15 – egy lehetetlen tüske (pl. 130) ne lapítsa el a valódi görbét
      yBase(5, 'UV', { min: 0, max: v => Math.max(1, Math.min(Math.ceil(v.max), 15)), scale: false, position: 'right', splitLine: { show: false } })
    ],
    axisPointer: { link: [{ xAxisIndex: 'all' }], lineStyle: { color: muted, type: 'dashed' } },
    tooltip: {
      trigger: 'axis', confine: true, formatter: tooltipFmt, transitionDuration: 0,
      backgroundColor: cssVar('--card'), borderColor: line, textStyle: { color: ink, fontSize: fs(12.5) }
    },
    dataZoom: [
      { type: 'inside', xAxisIndex: [0, 1, 2, 3, 4, 5], filterMode: 'weakFilter', zoomOnMouseWheel: 'ctrl', moveOnMouseWheel: false, moveOnMouseMove: true, minValueSpan: 2 * H },
      {
        type: 'slider', xAxisIndex: [0, 1, 2, 3, 4, 5], filterMode: 'weakFilter', top: 8, height: 34, left: 58, right: 58,
        minValueSpan: 2 * H, labelFormatter: v => F.dayY.format(v), textStyle: { color: muted },
        borderColor: line, fillerColor: 'rgba(37,99,235,.12)', handleStyle: { color: cssVar('--accent') },
        dataBackground: { lineStyle: { color: C.temp, opacity: .5 }, areaStyle: { color: C.temp, opacity: .08 } }
      }
    ],
    series: [
      ln('tLo', 'Minimum', 0, 0, 'transparent', tlabel, 1, { stack: 'band', lineStyle: { width: 0 }, tooltip: { show: false } }),
      ln('tBand', 'Min–max', 0, 0, C.temp, tlabel, 1, { stack: 'band', lineStyle: { width: 0 }, areaStyle: { color: C.temp, opacity: .16 } }),
      ln('tAvg', 'Hőmérséklet', 0, 0, C.temp, tlabel, 1, { lineStyle: { width: 2, color: C.temp }, markArea: gapAreas() }),
      ln('tDew', 'Harmatpont', 0, 0, C.dew, tlabel, 1, { lineStyle: { width: 1.4, color: C.dew } }),
      flags('fTemp', 0, 0),
      ln('hum', 'Páratartalom', 1, 1, C.hum, '%', 0, { areaStyle: { color: C.hum, opacity: .08 }, markArea: gapAreas() }),
      flags('fHum', 1, 1),
      ln('pres', 'Légnyomás', 2, 2, C.pres, plabel, () => pdec(1), { lineStyle: { width: 2, color: C.pres }, markArea: gapAreas() }),
      flags('fPres', 2, 2),
      ln('wind', 'Átlagszél', 3, 3, C.wind, 'km/h', 1, { areaStyle: { color: C.wind, opacity: .14 }, markArea: gapAreas() }),
      ln('gust', 'Széllökés', 3, 3, C.wind, 'km/h', 1, { lineStyle: { width: 1, color: C.wind, opacity: .55, type: 'solid' } }),
      flags('fWind', 3, 3),
      ln('rate', 'Csapadékintenzitás', 4, 4, C.rain, rrlabel, () => rdec(1), { areaStyle: { color: C.rain, opacity: .2 }, markArea: gapAreas() }),
      { id: 'rainDay', name: 'Napi csapadék', type: 'bar', xAxisIndex: 4, yAxisIndex: 5, barMaxWidth: 16, itemStyle: { color: C.rain, opacity: .55, borderRadius: [3, 3, 0, 0] }, data: [], animation: false },
      ln('rainTot', 'Napi halmozott', 4, 5, C.rain, rlabel, () => rdec(1), { lineStyle: { width: 1.6, color: C.rain, type: 'dashed' } }),
      flags('fRain', 4, 4),
      ln('sol', 'Napsugárzás', 5, 6, C.sun, 'W/m²', 0, { areaStyle: { color: C.sun, opacity: .18 }, markArea: gapAreas() }),
      ln('uv', 'UV-index', 5, 7, C.uv, '', 0, { lineStyle: { width: 1.3, color: C.uv } }),
      flags('fSun', 5, 6),
      flags('fUv', 5, 7)
    ]
  };
  SERIES_INFO.rainDay = { unit: 'mm', dec: 1 };
  chart.setOption(option);

  let zt = 0;
  chart.on('datazoom', () => { clearTimeout(zt); zt = setTimeout(onZoom, 220); updateSpan(); });
  window.addEventListener('resize', () => chart && chart.resize());
  // ha a fül/ablak rejtve volt építéskor, a méret később megjön
  if (window.ResizeObserver) new ResizeObserver(() => chart && chart.getWidth() !== el.clientWidth && chart.resize()).observe(el);
}

function gapAreas() {
  if (!META) return {};
  const data = META.gaps.filter(g => g[1] - g[0] >= H).map(g => [{ xAxis: g[0] }, { xAxis: g[1] }]);
  return { silent: true, itemStyle: { color: cssVar('--c-gap') }, data };
}

function tooltipFmt(params) {
  if (!params || !params.length) return '';
  let html = '<div style="font-weight:600;margin-bottom:4px">' + F.full.format(params[0].axisValue) + '</div>';
  for (const p of params) {
    const d = p.data;
    if (!d || p.seriesId === 'tLo') continue;
    if (p.seriesId === 'tBand') {
      if (d[2] == null || d[3] == null) continue;
      html += p.marker + ' Min–max: <b>' + num(d[2]) + ' – ' + num(d[3]) + ' ' + tlabel() + '</b><br>';
      continue;
    }
    if (p.seriesType === 'scatter') {
      html += '<span style="color:' + cssVar('--c-flag') + '">⚠ ' + esc(flagText(d[2])) + '</span> (' + num(d[1]) + ')<br>';
      continue;
    }
    if (d[1] == null) continue;
    const info = SERIES_INFO[p.seriesId] || { unit: '', dec: 1 };
    const u = typeof info.unit === 'function' ? info.unit() : info.unit, dec = typeof info.dec === 'function' ? info.dec() : info.dec;
    let extra = '';
    if (p.seriesId === 'wind' || p.seriesId === 'gust') {
      html += p.marker + ' ' + p.seriesName + ': <b>' + wtext(d[3] != null ? d[3] : d[1]) + '</b>' + (windUnit() !== 'kmh' ? ' (' + num(d[3] != null ? d[3] : d[1], 1) + ' km/h)' : '') + (windUnit() !== 'bft' ? ' · ' + num(beaufortF(d[3] != null ? d[3] : d[1]), 1) + ' Bft' : '') + (p.seriesId === 'wind' && d[2] != null ? ' · ' + dirText(d[2]) + ' felől' : '') + '<br>';
      continue;
    }
    if (p.seriesId === 'rainDay') html += p.marker + ' ' + p.seriesName + ' (' + F.dayY.format(d[0]) + '): <b>' + num(d[1], rdec(1)) + ' ' + rlabel() + '</b><br>';
    else html += p.marker + ' ' + p.seriesName + ': <b>' + num(d[1], dec) + (u ? ' ' + u : '') + '</b>' + extra + '<br>';
  }
  return html;
}

function flagText(bits) {
  const t = META.flagTexts, out = [];
  for (const k in t) if (bits & Number(k)) out.push(t[k]);
  return out.join('; ') || 'gyanús érték';
}

// az alap (teljes időszak, ritkított) és a részletes (látható szakasz) adat összefésülése
function mergeGroup(b, d) {
  if (!d || !d.t.length) return b;
  const t0 = d.t[0], t1 = d.t[d.t.length - 1];
  const keys = Object.keys(b);
  const out = {}; keys.forEach(k => out[k] = []);
  const push = (g, i) => keys.forEach(k => out[k].push(g[k][i]));
  for (let i = 0; i < b.t.length && b.t[i] < t0; i++) push(b, i);
  for (let i = 0; i < d.t.length; i++) push(d, i);
  for (let i = 0; i < b.t.length; i++) if (b.t[i] > t1) push(b, i);
  return out;
}
function mergeFlags(b, d, from, to) {
  if (!d) return b;
  return b.filter(p => p[0] < from).concat(d, b.filter(p => p[0] > to));
}

function applyData() {
  if (!chart || !BASE) return;
  const g = {}, f = {};
  for (const k of Object.keys(BASE.groups)) g[k] = mergeGroup(BASE.groups[k], DETAIL && DETAIL.groups[k]);
  for (const k of Object.keys(BASE.flags)) f[k] = hideSuspect ? [] : mergeFlags(BASE.flags[k], DETAIL && DETAIL.flags[k], DETAIL ? DETAIL.from : 0, DETAIL ? DETAIL.to : 0);
  const pts = (gr, key, cv) => gr.t.map((t, i) => [t, cv ? cv(gr[key][i]) : gr[key][i]]);
  const fp = (arr, cv) => (arr || []).map(p => [p[0], cv(p[1]), p[2]]);
  const T = g.temp, W = g.wind;
  const band = T.t.map((t, i) => {
    const lo = tconv(T.lo[i]), hi = tconv(T.hi[i]);
    return [t, lo == null || hi == null ? null : +(hi - lo).toFixed(2), lo, hi];
  });
  const daily = span > 3 * D;
  chart.setOption({
    series: [
      { id: 'tLo', data: pts(T, 'lo', tconv) },
      { id: 'tBand', data: band },
      { id: 'tAvg', data: pts(T, 'v', tconv) },
      { id: 'tDew', data: pts(T, 'dew', tconv) },
      { id: 'fTemp', data: fp(f.temp, tconv) },
      { id: 'hum', data: pts(g.hum, 'v') },
      { id: 'fHum', data: f.hum || [] },
      { id: 'pres', data: pts(g.pres, 'v', pconv) },
      { id: 'fPres', data: fp(f.pres, pconv) },
      { id: 'wind', data: W.t.map((t, i) => [t, wconv(W.v[i]), W.dir[i], W.v[i]]) },
      { id: 'gust', data: W.t.map((t, i) => [t, wconv(W.gust[i]), null, W.gust[i]]) },
      { id: 'fWind', data: (f.wind || []).map(p => [p[0], wconv(p[1]), p[2]]) },
      { id: 'rate', data: pts(g.rain, 'v', rconv) },
      { id: 'rainDay', data: daily ? BASE.daily.t.map((t, i) => [t, rconv(BASE.daily.rain[i])]) : [] },
      { id: 'rainTot', data: daily ? [] : pts(g.rain, 'tot', rconv) },
      { id: 'fRain', data: fp(f.rain, rconv) },
      { id: 'sol', data: pts(g.sun, 'v') },
      { id: 'uv', data: pts(g.sun, 'uv') },
      { id: 'fSun', data: f.sun || [] },
      { id: 'fUv', data: f.uv || [] }
    ],
    yAxis: [{ name: tlabel() }, {}, { name: plabel() }, { name: wlabel() }, { name: rrlabel() }, { name: rlabel() }]
  });
  if (typeof decorateTimeline === 'function') decorateTimeline();
}

function visibleRange() {
  const dz = chart.getOption().dataZoom[0];
  let from = dz.startValue, to = dz.endValue;
  if (from == null || to == null) {
    const a = META.first, b = META.last;
    from = a + (b - a) * (dz.start || 0) / 100;
    to = a + (b - a) * (dz.end == null ? 100 : dz.end) / 100;
  }
  return [Math.round(from), Math.round(to)];
}

function updateSpan() {
  const [a, b] = visibleRange();
  const old = span > 3 * D;
  span = b - a;
  if ((span > 3 * D) !== old) applyData();
}

async function onZoom() {
  updateSpan();
  const [a, b] = visibleRange();
  // a gombok jelölése, ha a felhasználó maga zoomolt
  const match = Object.entries(rangeOf).find(([k, f]) => { const r = f(); return Math.abs(r[0] - a) < 60e3 && Math.abs(r[1] - b) < 60e3; });
  currentRange = match ? match[0] : '';
  document.querySelectorAll('#ranges button').forEach(x => x.classList.toggle('on', x.dataset.r === currentRange));
  await refreshDetail();
}

async function refreshDetail() {
  const [a, b] = visibleRange();
  const token = ++detailToken;
  const n = Math.max(600, Math.round(chart.getWidth() * 1.3));
  const res = await api('/api/series?from=' + a + '&to=' + b + '&n=' + n + '&clean=' + (hideSuspect ? 1 : 0));
  if (token !== detailToken) return; // közben újabb zoom jött
  DETAIL = res;
  applyData();
}

const rangeOf = {
  '1d': () => [META.last - D, META.last],
  '3d': () => [META.last - 3 * D, META.last],
  '7d': () => [META.last - 7 * D, META.last],
  '30d': () => [META.last - 30 * D, META.last],
  'year': () => {
    const y = Number(F.ymd.format(META.last).slice(0, 4));
    return [Math.max(META.first, Date.UTC(y, 0, 1) - H), META.last];
  },
  'all': () => [META.first, META.last]
};

function setRange(r) {
  currentRange = r;
  const [a, b] = rangeOf[r]();
  span = b - a;
  document.querySelectorAll('#ranges button').forEach(x => x.classList.toggle('on', x.dataset.r === r));
  DETAIL = null;
  applyData();
  chart.dispatchAction({ type: 'dataZoom', startValue: Math.max(META.first, a), endValue: b });
}

// ------------------------------------------------------------------ események
document.querySelectorAll('#ranges button').forEach(b => b.addEventListener('click', () => setRange(b.dataset.r)));
$('btnRefresh').addEventListener('click', () => reload(STATUS ? STATUS.hasDownloader : true));
$('hideSuspect').addEventListener('change', async e => {
  hideSuspect = e.target.checked;
  BASE = await api('/api/series?n=2400&clean=' + (hideSuspect ? 1 : 0));
  DETAIL = null;
  if (!chart) return;
  applyData();
  refreshDetail();
});

// ------------------------------------------------------------------ fülek
let currentTab = 'attekintes';
function showTab(t, save) {
  if (!document.querySelector('.page[data-page="' + t + '"]')) t = 'attekintes';
  currentTab = t;
  document.querySelectorAll('#tabs button').forEach(b => b.classList.toggle('on', b.dataset.tab === t));
  document.querySelectorAll('.page').forEach(p => p.classList.toggle('hidden', p.dataset.page !== t));
  $('staleBanner').style.display = t === 'beallitasok' ? 'none' : '';
  if (t === 'idovonal') { if (chart) chart.resize(); else ensureChart(); }
  if (t === 'beallitasok') renderSettings();
  if (t === 'naptar' && typeof renderCalendar === 'function') renderCalendar();
  if (t === 'szarazsag' && typeof renderDrought === 'function') renderDrought();
  if (t === 'eso') renderRain();
  if (t === 'szel') renderWind();
  if (t === 'stat') renderStats();
  if (t === 'futas') renderRun();
  if (t === 'jelentes') renderReport();
  if (t === 'idovonal' && typeof renderFormulaPanel === 'function') renderFormulaPanel();
  if (t === 'beallitasok' && typeof renderAlerts === 'function') renderAlerts();
  if (save) { UI.tab = t; saveUi(); }
  window.scrollTo(0, 0);
}
document.querySelectorAll('#tabs button').forEach(b => b.addEventListener('click', () => showTab(b.dataset.tab, true)));

// ------------------------------------------------------------------ téma
const darkMq = window.matchMedia('(prefers-color-scheme: dark)');
function applyTheme() {
  const mode = UI.theme || 'auto';
  const dark = mode === 'dark' || (mode === 'auto' && darkMq.matches);
  const want = dark ? 'dark' : 'light';
  document.querySelectorAll('#themeSeg button').forEach(b => b.classList.toggle('on', b.dataset.v === mode));
  if (document.documentElement.dataset.theme === want) return;
  document.documentElement.dataset.theme = want;
  if (typeof onThemeChanged === 'function') onThemeChanged();
  if (chart) {
    // a grafikon színei a témából jönnek: újraépítés ugyanarra a nézetre
    const r = visibleRange();
    chart.dispose(); chart = null;
    buildChart();
    applyData();
    chart.dispatchAction({ type: 'dataZoom', startValue: r[0], endValue: r[1] });
  }
}
darkMq.addEventListener('change', applyTheme);
$('btnTheme').addEventListener('click', () => {
  const dark = document.documentElement.dataset.theme === 'dark';
  UI.theme = dark ? 'light' : 'dark';
  applyTheme(); saveUi();
});
document.querySelectorAll('#themeSeg button').forEach(b => b.addEventListener('click', () => { const old = UI.theme || 'auto'; UI.theme = b.dataset.v; applyTheme(); saveUi(); if (typeof logUi === 'function') logUi('theme', old, UI.theme); }));

// ------------------------------------------------------------------ beállítások oldal
async function renderSettings() {
  if (META) {
    const m = META;
    $('folders').innerHTML =
      '<dt>Program</dt><dd><code>' + esc(m.baseDir || '') + '</code></dd>' +
      '<dt>Adatok</dt><dd><code>' + esc(m.dir) + '</code></dd>' +
      '<dt>CSV fájlok</dt><dd>' + m.files.map(esc).join(', ') + '</dd>';
    $('about').innerHTML =
      '<dt>Verzió</dt><dd>' + esc(m.version) + '</dd>' +
      '<dt>Állomás</dt><dd>' + esc(m.station) + ' · ' + esc(placeName()) + ' (' + num(m.lat, 4) + ', ' + num(m.lon, 4) + (m.latSource === 'beállítás' ? ' – az adatokban nincs koordináta, a Beállítások › Helyszín tartalékértéke' : '') + ')</dd>' +
      '<dt>Mérések</dt><dd>' + num(m.n, 0) + ' db, ' + F.dayY.format(m.first) + ' – ' + F.dayY.format(m.last) + '</dd>' +
      '<dt>Gyanús mérések</dt><dd>' + num(m.flaggedRows, 0) + ' jelölve (semmi nincs törölve)</dd>' +
      '<dt>Adathiányok</dt><dd>' + m.gaps.length + ' db (> ' + m.settings.gapMinutes + ' perc)</dd>' +
      '<dt>Betöltés</dt><dd>' + (m.fromCache ? 'cache-ből' : 'CSV-ből') + ', ' + num(m.loadMs / 1000, 1) + ' mp (' + esc(m.loadedAt) + ')</dd>' +
      (m.warnings.length ? '<dt>Figyelmeztetések</dt><dd>' + m.warnings.map(esc).join('<br>') + '</dd>' : '');
  }
  renderSettingsV2();
}

$('btnExport').addEventListener('click', async () => {
  const msg = $('exportMsg');
  msg.className = 'msg'; msg.textContent = '';
  try {
    const r = await post('/api/export');
    if (r.download) { location.href = '/api/export.csv'; msg.className = 'msg ok'; msg.textContent = 'A böngésző letölti a fájlt.'; return; }
    if (r.saved) { msg.className = 'msg ok'; msg.textContent = 'Elmentve (' + r.rows + ' nap): ' + r.path; }
    else if (r.error) { msg.className = 'msg err'; msg.textContent = r.error; }
  } catch (e) { msg.className = 'msg err'; msg.textContent = e.message; }
});

document.querySelectorAll('[data-open]').forEach(b => b.addEventListener('click', () =>
  post('/api/open?what=' + b.dataset.open).then(r => { if (r.error) toast(r.error); }).catch(e => toast(e.message))));

// szél-egység váltó (minden .wunit csoport)
function syncWindUnitButtons() { document.querySelectorAll('.wunit button').forEach(b => b.classList.toggle('on', b.dataset.w === windUnit())); }
document.addEventListener('click', e => {
  const b = e.target.closest && e.target.closest('.wunit button');
  if (!b) return;
  const old = windUnit();
  if (old === b.dataset.w) return;
  UI.units = Object.assign({}, UI.units || {}); UI.units.wind = b.dataset.w; delete UI.windUnit;
  saveUi(); syncWindUnitButtons();
  if (typeof logUi === 'function') logUi('units.wind', old, b.dataset.w);
  if (TODAY) renderToday();
  if (chart) applyData();
  if (typeof onWindUnitChanged === 'function') onWindUnitChanged();
});

async function init() {
  try { UI = await api('/api/ui'); } catch (e) { UI = {}; }
  if (typeof UI !== 'object' || UI === null) UI = {};
  if (UI.windUnit && !(UI.units && UI.units.wind)) { UI.units = Object.assign({}, UI.units || {}, { wind: UI.windUnit }); delete UI.windUnit; }
  if (typeof markOpen === 'function') markOpen();
  applyTheme(); applyFont(); applyLang().then(() => { if (lang() !== 'hu' && META) refreshView(); });
  syncWindUnitButtons(); syncUnitButtons();
  showTab(UI.tab || 'attekintes', false);
  start();
  checkUpdate();
}
// minden szkript betöltése után indul (a Beállítások és a többi modul függvényei addigra megvannak)
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else setTimeout(init, 0);

// rekordok (a szerver metrikusan küldi): érték és részletek a választott mértékegységben
function recQ(r) { return r.key === 'range' || r.key === 'tdrop' ? 'tdiff' : /^pdrop/.test(r.key) ? 'presd' : UNIT_Q[r.unit]; }
function recValue(r) { return qtext(recQ(r), r.value, r.unit === 'km/h' ? 1 : r.dec, r.unit); }
function recDetail(r) {
  const d = r.detail || '';
  if (r.key === 'range') { const m = d.match(/(-?[\d,]+) → (-?[\d,]+) °C/); if (m) return ttext(parseFloat(m[1].replace(',', '.'))) .replace(' ' + tlabel(), '') + ' → ' + ttext(parseFloat(m[2].replace(',', '.'))); }
  if (r.key === 'gust') return windUnit() === 'kn' ? num(beaufortF(r.value), 1) + ' Bft' : d;
  return d;
}
