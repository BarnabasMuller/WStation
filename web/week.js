'use strict';
/* Heti összesítő az Áttekintés fülön */

const WEEK_ROWS = [
  { k: 'temp', name: 'Hőmérséklet (min / max / átlag)' },
  { k: 'rain', name: 'Csapadék és max. intenzitás' },
  { k: 'sun', name: 'Napsütéses órák (becslés)' },
  { k: 'cloud', name: 'Felhőzet (becslés, nappal)' },
  { k: 'energy', name: 'Napenergia (kWh/m²)' },
  { k: 'wind', name: 'Átlagszél és max. lökés' },
  { k: 'dir', name: 'Uralkodó szélirány' },
  { k: 'hum', name: 'Páratartalom' },
  { k: 'pres', name: 'Légnyomás és változása' },
  { k: 'uv', name: 'Max. UV-index' }
];
const WEEK_MODES = { last7: 'Utolsó 7 nap', prev: 'Előző hét (H–V)', cur: 'Aktuális hét' };   // az utolsó 7 nap az alap (2.2.8)
let weekOffset = 0;   // hány héttel lapoztunk vissza (nem mentődik)

function weekState() {
  UI.week = UI.week || {};
  const w = UI.week;
  if (!WEEK_MODES[w.mode]) w.mode = 'last7';
  // 2.2.8: az új alapérték egyszer a meglévő beállításra is (a korábbi automatikus „előző hét” helyett; utána szabadon váltható)
  if (!w.last7Default) { w.last7Default = true; if (w.mode === 'prev') w.mode = 'last7'; }
  if (!Array.isArray(w.rows)) w.rows = ['temp', 'rain', 'sun', 'cloud', 'wind', 'dir'];
  // 2.2: a felhőzet sora egyszer bekerül a meglévő beállításba is (utána ki-be kapcsolható)
  if (!w.cloudAdded) { w.cloudAdded = true; if (!w.rows.includes('cloud')) w.rows.splice(Math.max(0, w.rows.indexOf('sun') + 1), 0, 'cloud'); }
  if (!['kmh', 'kn', 'bft'].includes(w.wind)) w.wind = (META && META.settings.windUnit === 'csomo') ? 'kn' : (META && META.settings.windUnit === 'beaufort') ? 'bft' : 'kmh';
  return w;
}

function weekDates() {
  const w = weekState();
  const today = DS.today();
  let first;
  // az utolsó 7 nap az utolsó adatos napig tart, ha az régebbi a mainál (pl. pár napig nem futott a Letöltő) –
  // különben a hét nagy része üres kártya lenne (2.2.8)
  if (w.mode === 'last7') { const ld = META && META.last ? F.ymd.format(META.last) : today; first = DS.add(ld < today ? ld : today, -6); }
  else {
    const monday = DS.add(today, -DS.dow(today));
    first = w.mode === 'prev' ? DS.add(monday, -7) : monday;
  }
  first = DS.add(first, weekOffset * 7);
  return Array.from({ length: 7 }, (_, i) => DS.add(first, i));
}

// a közös szél-mértékegységgel (Beállítások); km/h-ban a lökés egész, a csomó és a Bft egy tizedes
function windText(kmh, unit, dec) {
  if (kmh == null || isNaN(kmh)) return '–';
  const u = windUnit();
  return wtext(kmh, u === 'kmh' || u === 'mph' ? (dec == null ? 0 : dec) : 1);
}

// nappalhossz órában (csillagászati közelítés)
function dayLength(dateStr, latDeg) {
  const n = Math.round((DS.ms(dateStr) - Date.UTC(+dateStr.slice(0, 4), 0, 0)) / D);
  const decl = 23.44 * Math.sin(2 * Math.PI * (284 + n) / 365) * Math.PI / 180;
  const lat = (latDeg != null ? latDeg : (META && META.lat) || 47.46) * Math.PI / 180;
  const x = -Math.tan(lat) * Math.tan(decl);
  return x <= -1 ? 24 : x >= 1 ? 0 : 2 * Math.acos(x) * 12 / Math.PI;
}

const ICONS = {
  sun: '<circle cx="24" cy="24" r="9" fill="#ffb300"/><g stroke="#ffb300" stroke-width="3" stroke-linecap="round"><path d="M24 5v5M24 38v5M5 24h5M38 24h5M10.6 10.6l3.5 3.5M33.9 33.9l3.5 3.5M10.6 37.4l3.5-3.5M33.9 14.1l3.5-3.5"/></g>',
  partly: '<circle cx="18" cy="17" r="8" fill="#ffb300"/><g stroke="#ffb300" stroke-width="2.6" stroke-linecap="round"><path d="M18 3v4M4 17h4M8 7l2.8 2.8M28 7l-2.8 2.8"/></g><path d="M15 39h21a8 8 0 0 0 0-16 11 11 0 0 0-20.7 3.6A6.3 6.3 0 0 0 15 39z" fill="#cfd8e3"/>',
  cloud: '<path d="M12 37h25a9 9 0 0 0 0-18 12.5 12.5 0 0 0-23.6 4A7 7 0 0 0 12 37z" fill="#aab6c5"/>',
  rain: '<path d="M12 30h25a9 9 0 0 0 0-18 12.5 12.5 0 0 0-23.6 4A7 7 0 0 0 12 30z" fill="#9aa8b9"/><g stroke="#2f80ed" stroke-width="3" stroke-linecap="round"><path d="M17 35l-2 6M25 35l-2 6M33 35l-2 6"/></g>',
  shower: '<circle cx="31" cy="14" r="7" fill="#ffb300"/><path d="M11 31h22a8 8 0 0 0 0-16 11 11 0 0 0-20.7 3.6A6.3 6.3 0 0 0 11 31z" fill="#b8c4d2"/><g stroke="#2f80ed" stroke-width="3" stroke-linecap="round"><path d="M17 36l-2 5M26 36l-2 5"/></g>',
  storm: '<path d="M12 28h25a9 9 0 0 0 0-18 12.5 12.5 0 0 0-23.6 4A7 7 0 0 0 12 28z" fill="#7d8a9c"/><path d="M26 28l-6 9h5l-3 8 9-11h-5l3-6z" fill="#ffb300"/><g stroke="#2f80ed" stroke-width="3" stroke-linecap="round"><path d="M14 33l-2 5M36 33l-2 5"/></g>',
  snow: '<path d="M12 30h25a9 9 0 0 0 0-18 12.5 12.5 0 0 0-23.6 4A7 7 0 0 0 12 30z" fill="#b8c4d2"/><g fill="#5fa8ff"><circle cx="16" cy="38" r="2.4"/><circle cx="25" cy="41" r="2.4"/><circle cx="34" cy="38" r="2.4"/></g>',
  none: '<path d="M14 34h20" stroke="#aab6c5" stroke-width="3" stroke-linecap="round" stroke-dasharray="2 5"/>'
};

function dayIcon(d, i, dateStr) {
  if (i === undefined || d.n[i] === 0) return ['none', 'nincs adat'];
  const rain = d.rain[i] || 0, sun = d.sun[i] || 0, rate = d.rate[i] || 0;
  const frac = sun / Math.max(1, dayLength(dateStr, META.lat));
  if (rain >= 1 && d.tavg[i] != null && d.tavg[i] < 1) return ['snow', 'csapadék fagypont körül'];
  if (rain >= 5 && (rate >= 30 || (d.gust[i] || 0) >= 50)) return ['storm', 'zivataros, heves eső'];
  if (rain >= 5) return ['rain', 'esős'];
  if (rain >= 1) return frac > 0.4 ? ['shower', 'napos, záporral'] : ['rain', 'esős'];
  if (frac > 0.6) return ['sun', 'napos'];
  if (frac > 0.3) return ['partly', 'részben felhős'];
  return ['cloud', 'felhős'];
}

function dirArrow(deg, size) {
  if (deg == null || isNaN(deg)) return '';
  return '<svg class="dir" viewBox="0 0 24 24" style="width:' + size + 'px;height:' + size + 'px;transform:rotate(' + (deg + 180) + 'deg)"><path d="M12 3l5 15-5-3.5L7 18z"/></svg>';
}

async function renderWeek() {
  if (!META || !$('week')) return;
  const w = weekState();
  const d = await getDays(true);
  const dates = weekDates();
  const cd = w.rows.includes('cloud') && typeof getCloudDays === 'function' ? await getCloudDays().catch(() => null) : null;
  const today = DS.today();
  document.querySelectorAll('#weekMode button').forEach(b => b.classList.toggle('on', b.dataset.v === w.mode));
  $('weekTitle').textContent = DS.fmt(dates[0], { month: 'short', day: 'numeric' }) + ' – ' + DS.fmt(dates[6], { year: 'numeric', month: 'short', day: 'numeric' });
  $('weekNext').disabled = weekOffset >= 0;
  $('weekPrev').disabled = DS.ms(dates[0]) <= DS.ms(d.date[0]);
  const rows = new Set(w.rows);

  const cards = dates.map(s => {
    const i = d.idx[s];
    const future = s > today;
    const [ico, icoText] = future ? ['none', 'még nem volt'] : dayIcon(d, i, s);
    const isToday = s === today;
    let body = '';
    if (i === undefined || future) {
      body = '<div class="wk-empty">' + (future ? 'még nem volt' : 'nincs adat') + '</div>';
    } else {
      const R = (label, val) => '<div class="wk-row"><span>' + label + '</span><b>' + val + '</b></div>';
      if (rows.has('temp')) body += '<div class="wk-temp"><span class="hi">' + num(tconv(d.tmax[i]), 1) + '°</span><span class="lo">' + num(tconv(d.tmin[i]), 1) + '°</span></div>' +
        '<div class="wk-sub">átlag ' + ttext(d.tavg[i], 1) + '</div>';
      if (rows.has('rain')) body += R('Csapadék', (d.rain[i] > 0 ? rtext(d.rain[i], 1) : '–') + (d.rate[i] > 0 ? '<small> · max ' + rrtext(d.rate[i], 0) + '</small>' : ''));
      if (rows.has('sun')) body += R('Napsütés', num(d.sun[i], 1) + ' h');
      if (rows.has('cloud')) { const ci = cd && cd.idx[s]; body += R('Felhőzet', ci != null && cd.pct[ci] != null ? num(cd.pct[ci], 0) + ' %' : '–'); }
      if (rows.has('energy')) body += R('Napenergia', num(d.energy[i], 2) + ' kWh/m²');
      if (rows.has('wind')) body += R('Szél', windText(d.wind[i], w.wind, w.wind === 'kmh' ? 1 : 0) + '<small> · lökés ' + windText(d.gust[i], w.wind) + '</small>');
      if (rows.has('dir')) body += R('Irány', '<span title="' + esc(dirText(d.dir[i]) + ' (' + num(d.dir[i], 0) + '°)') + '">' + dirArrow(d.dir[i], 16) + ' ' + dirText8(d.dir[i]) + '</span>');
      if (rows.has('hum')) body += R('Pára', num(d.humMin[i], 0) + '–' + num(d.humMax[i], 0) + ' %');
      if (rows.has('pres')) body += R('Nyomás', ptext(d.pres[i], 0) + '<small> · ' + signed(pconv(d.presChg[i]), pdec(1)) + '</small>');
      if (rows.has('uv')) body += R('UV', num(d.uv[i], 0));
      if (d.cov[i] < COV_FULL) body += '<div class="wk-warn">⚠ hiányos adat (' + num(d.cov[i] * 100, 0) + '%)</div>';
    }
    return '<div class="wk-card' + (isToday ? ' today-card' : '') + (i === undefined || future ? ' empty' : '') + '" data-date="' + s + '" title="' + esc(icoText) + (i !== undefined && !future ? ' – kattints: megnyitás az idővonalon' : '') + '">' +
      '<div class="wk-head"><div><div class="wk-day">' + DS.fmt(s, { weekday: 'long' }) + (isToday ? ' · ma' : '') + '</div><div class="wk-date">' + DS.fmt(s, { month: 'short', day: 'numeric' }) + '</div></div>' +
      '<svg class="wk-ico" viewBox="0 0 48 48">' + ICONS[ico] + '</svg></div>' + body + '</div>';
  });
  $('weekCards').innerHTML = cards.join('');
  $('weekCards').querySelectorAll('.wk-card:not(.empty)').forEach(c => c.addEventListener('click', () => gotoDay(c.dataset.date)));
  $('weekSum').innerHTML = weekSummary(d, dates.filter(s => s <= today), w);
}

function weekSummary(d, dates, w) {
  const ix = dates.map(s => d.idx[s]).filter(i => i !== undefined);
  if (!ix.length) return '<span class="muted">Erre a hétre nincs adat.</span>';
  const pick = (k, f) => { let best = null; ix.forEach(i => { const v = d[k][i]; if (v != null && (best === null || f(v, d[k][best]))) best = i; }); return best; };
  const iMin = pick('tmin', (a, b) => a < b), iMax = pick('tmax', (a, b) => a > b), iGust = pick('gust', (a, b) => a > b);
  const avg = k => { const v = ix.map(i => d[k][i]).filter(x => x != null); return v.length ? v.reduce((a, b) => a + b, 0) / v.length : null; };
  const sum = k => ix.map(i => d[k][i] || 0).reduce((a, b) => a + b, 0);
  const rainy = ix.filter(i => d.rain[i] >= 1).length;
  let vx = 0, vy = 0;
  ix.forEach(i => { if (d.dir[i] != null && d.wind[i] != null) { vx += d.wind[i] * Math.sin(d.dir[i] * Math.PI / 180); vy += d.wind[i] * Math.cos(d.dir[i] * Math.PI / 180); } });
  const dom = vx || vy ? (Math.atan2(vx, vy) * 180 / Math.PI + 360) % 360 : null;
  const day = i => i == null ? '' : ' <small>(' + DS.fmt(d.date[i], { weekday: 'short' }) + ')</small>';
  const items = [
    ['Legalacsonyabb', iMin != null ? ttext(d.tmin[iMin], 1) + day(iMin) : '–'],
    ['Legmagasabb', iMax != null ? ttext(d.tmax[iMax], 1) + day(iMax) : '–'],
    ['Átlag', ttext(avg('tavg'), 1)],
    ['Csapadék', rtext(sum('rain'), 1) + ' <small>(' + rainy + ' esős nap)</small>'],
    ['Napsütés', num(sum('sun'), 1) + ' h <small>· ' + num(sum('energy'), 1) + ' kWh/m²</small>'],
    ['Max. lökés', iGust != null ? windText(d.gust[iGust], w.wind) + day(iGust) : '–'],
    ['Uralkodó szél', dom == null ? '–' : '<span title="' + esc(dirText(dom)) + '">' + dirArrow(dom, 15) + ' ' + dirText8(dom) + '</span>']
  ];
  return items.map(([k, v]) => '<div class="ws-item"><span>' + k + '</span><b>' + v + '</b></div>').join('');
}

function renderWeekSettings() {
  const w = weekState();
  $('weekRows').innerHTML = WEEK_ROWS.map(r => '<label><input type="checkbox" value="' + r.k + '"' + (w.rows.includes(r.k) ? ' checked' : '') + '> ' + esc(r.name) + '</label>').join('');
  syncWindUnitButtons();
}

function initWeekControls() {
  document.querySelectorAll('#weekMode button').forEach(b => b.addEventListener('click', () => { weekState().mode = b.dataset.v; weekOffset = 0; saveUi(); renderWeek(); }));
  $('weekPrev').addEventListener('click', () => { weekOffset--; renderWeek(); });
  $('weekNext').addEventListener('click', () => { if (weekOffset < 0) { weekOffset++; renderWeek(); } });
  $('weekGear').addEventListener('click', e => {
    e.stopPropagation();
    const p = $('weekPop');
    if (p.classList.contains('hidden')) { renderWeekSettings(); p.classList.remove('hidden'); } else p.classList.add('hidden');
  });
  $('weekPop').addEventListener('click', e => e.stopPropagation());
  document.addEventListener('click', () => $('weekPop').classList.add('hidden'));
  $('weekRows').addEventListener('change', () => {
    // a sorrend a felsorolásé marad
    weekState().rows = [...$('weekRows').querySelectorAll('input:checked')].map(x => x.value);
    saveUi(); renderWeek();
  });
}
initWeekControls();

// új adat betöltésekor (indulás, Frissítés)
function onDataLoaded() {
  DAYS = {};
  if (typeof CLOUD_NOW !== 'undefined') { CLOUD_NOW = null; CLOUD_DAYS = null; CLOUD_SER = {}; }
  if (typeof RAIN !== 'undefined') RAIN = null;
  if (typeof MARKS !== 'undefined') MARKS = null;
  if (typeof renderChanges === 'function') setTimeout(() => renderChanges().catch(e => console.error(e)), 300);
  if (typeof VARS_CACHE !== 'undefined') Object.keys(VARS_CACHE).forEach(k => delete VARS_CACHE[k]);
  ['eso', 'szel', 'stat', 'jelentes', 'futas'].forEach(t => { if (currentTab === t) showTab(t, false); });
  if (typeof DR !== 'undefined') { DR = null; DR_PREV = null; }
  if (currentTab === 'szarazsag') renderDrought();
  calMonth = null;
  renderWeek();
  if (currentTab === 'naptar') renderCalendar();
}
function onThemeChanged() {
  if (currentTab === 'naptar') renderCalendar();
  if (currentTab === 'szarazsag' && DR) { if (drChart) { drChart.dispose(); drChart = null; } renderDrChart(); }
}
