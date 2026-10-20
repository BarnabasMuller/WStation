'use strict';
/* Jelentés fül: havi időjárás-jelentés és éves időjárás-évkönyv, nyomtatható / PDF-be menthető */

let repCharts = [];
function repState() {
  UI.rep = UI.rep || {};
  if (!UI.rep.mode) UI.rep.mode = 'month';
  return UI.rep;
}

async function renderReport() {
  if (currentTab !== 'jelentes' || !META) return;
  const st = repState();
  const d = await getDays(true);
  const months = [...new Set(d.date.map(s => s.slice(0, 7)))];
  const years = [...new Set(d.date.map(s => s.slice(0, 4)))];
  document.querySelectorAll('#repMode button').forEach(b => b.classList.toggle('on', b.dataset.v === st.mode));
  const sel = $('repPick');
  const opts = st.mode === 'month' ? months.slice().reverse().map(m => [m, m.slice(0, 4) + '. ' + MONTHS_FULL[+m.slice(5) - 1]]) : years.slice().reverse().map(y => [y, y + '. év']);
  sel.innerHTML = opts.map(o => '<option value="' + o[0] + '">' + o[1] + '</option>').join('');
  const want = st.mode === 'month' ? st.month : st.year;
  sel.value = opts.some(o => o[0] === want) ? want : opts[0][0];
  repCharts.forEach(c => c.dispose()); repCharts = [];
  if (st.mode === 'month') await monthReport(d, sel.value); else await yearReport(d, sel.value);
}

// a jelentés fehér lapon van (képernyőn és PDF-ben is): a grafikonfeliratok fix sötétszürkék, a témától függetlenül (16.5)
const REP_MUTED = '#4b5563';
function repTable(rows) { return '<table class="rtable">' + rows.map(r => '<tr><td>' + r[0] + '</td><td><b>' + r[1] + '</b></td></tr>').join('') + '</table>'; }

function monthStats(d, m) {
  const ix = d.date.map((s, i) => i).filter(i => d.date[i].startsWith(m));
  const good = ix.filter(i => d.cov[i] >= COV_FULL);   // közös lefedettségi küszöb (8.6)
  const avg = k => { const v = good.map(i => d[k][i]).filter(x => x != null); return v.length ? v.reduce((a, b) => a + b, 0) / v.length : null; };
  const sum = k => ix.reduce((a, i) => a + (d[k][i] || 0), 0);
  const ext = (k, f) => good.reduce((b, i) => d[k][i] != null && (b == null || f(d[k][i], d[k][b])) ? i : b, null);
  return { ix, good, avg, sum, ext, tavg: avg('tavg'), rain: sum('rain'), sun: sum('sun'), energy: sum('energy'),
    imax: ext('tmax', (a, b) => a > b), imin: ext('tmin', (a, b) => a < b), igust: ext('gust', (a, b) => a > b), iwet: ext('rain', (a, b) => a > b),
    hot: good.filter(i => d.tmax[i] >= META.settings.hotDay).length, trop: good.filter(i => d.tmin[i] >= META.settings.tropicalNight).length,
    frost: good.filter(i => d.tmin[i] < META.settings.frostDay).length, rainy: ix.filter(i => d.rain[i] >= 1).length };
}

function summaryText(d, m, s) {
  // a hónap a többi (adatos) hónaphoz képest
  const all = [...new Set(d.date.map(x => x.slice(0, 7)))].map(mm => ({ mm, s: monthStats(d, mm) })).filter(o => o.s.good.length >= 20);
  const others = all.filter(o => o.mm !== m);
  let temp = '', wet = '';
  const days = s.ix.length;
  const dim = new Date(Date.UTC(+m.slice(0, 4), +m.slice(5, 7), 0)).getUTCDate();
  const partial = days < dim;
  if (others.length >= 2 && s.tavg != null) {
    // a nevezőben a vizsgált hónap is benne van (akkor is, ha részleges, és ezért az `all`-ból kimaradt)
    const n = others.length + 1, rankWarm = 1 + others.filter(o => o.s.tavg > s.tavg).length;
    temp = rankWarm === 1 ? 'az eddigi legmelegebb hónap' : rankWarm === n ? 'az eddigi leghidegebb hónap' : 'a ' + n + ' hónapból a ' + rankWarm + '. legmelegebb';
    if (partial) temp += ' – részleges hónap: ' + days + ' / ' + dim + ' nap adata';
  } else if (partial) temp = 'részleges hónap: ' + days + ' / ' + dim + ' nap adata';
  // a hónap saját sokéves átlagához mérve (9.6: júniusban kb. kétszer annyi eső az átlag, mint januárban); részleges hónapnál arányosan
  const norm = (META.settings.rainNormal || [])[+m.slice(5, 7) - 1], exp = norm != null ? norm * days / dim : null;
  const ratio = exp ? s.rain / exp : null;
  wet = ratio == null ? '' : ratio < 0.4 ? 'nagyon száraz' : ratio < 0.75 ? 'száraz' : ratio <= 1.3 ? 'átlagos csapadékú' : ratio <= 2 ? 'csapadékos' : 'nagyon csapadékos';
  const wetPct = ratio == null ? '' : ' (csapadék: a sokéves havi átlag ' + num(ratio * 100, 0) + '%-a)';
  const warm = s.tavg == null ? '' : s.tavg >= 22 ? 'Forró' : s.tavg >= 17 ? 'Meleg' : s.tavg >= 10 ? 'Enyhe' : s.tavg >= 3 ? 'Hűvös' : 'Hideg';
  return warm + (wet ? ', ' + wet : '') + ' hónap' + wetPct + ': átlag <b>' + ttext(s.tavg) + '</b>' + (temp ? ' (' + temp + ')' : '') + ', <b>' + rtext(s.rain, 1) + '</b> csapadék ' + s.rainy + ' esős nappal, <b>' + num(s.sun, 0) + ' óra</b> napsütés.' +
    (s.hot ? ' ' + s.hot + ' hőségnap' + (s.trop ? ' és ' + s.trop + ' trópusi éjszaka' : '') + '.' : '') + (s.frost ? ' ' + s.frost + ' fagyos nap.' : '');
}

async function monthReport(d, m) {
  repState().month = m; saveUi();
  const s = monthStats(d, m);
  const ev = await getRainEvents();
  const evs = ev.events.filter(e => F.ymd.format(e.start).startsWith(m)).sort((a, b) => b.total - a.total);
  const cdays = typeof getCloudDays === 'function' ? await getCloudDays().catch(() => null) : null;
  const cvals = cdays && cdays.date ? cdays.date.map((x, i) => x.startsWith(m) ? cdays.pct[i] : null).filter(x => x != null) : [];
  const cloudAvg = cvals.length ? cvals.reduce((a, b) => a + b, 0) / cvals.length : null;
  const dr = typeof getDrought === 'function' ? await getDrought().catch(() => null) : null;
  const title = m.slice(0, 4) + '. ' + MONTHS_FULL[+m.slice(5) - 1];
  const day = i => i == null ? '' : ' (' + DS.fmt(d.date[i], { month: 'short', day: 'numeric' }) + ')';
  let drHtml = '';
  if (dr) {
    const lastIdx = dr.date.map((x, i) => i).filter(i => dr.date[i].startsWith(m)).pop();
    const firstIdx = dr.date.findIndex(x => x.startsWith(m));
    if (lastIdx != null && firstIdx >= 0) drHtml = '<h3>Szárazság a hónap végén</h3>' + repTable(dr.config.profiles.filter(p => p.enabled).map(p => {
      const r = dr.profiles.find(x => x.id === p.id); return [ut(p.name), num(r.depl[lastIdx] * 100, 0) + '% (hónap elején ' + num(r.depl[firstIdx] * 100, 0) + '%)'];
    }).concat([['Hőösszeg (' + ttext(dr.config.gdd.baseT, 0) + ' felett), hónap végéig', num(tdconv(dr.gddCum[lastIdx]), 0) + ' ' + tlabel() + '·nap'], ['Legnagyobb tűzveszély (FWI)', num(Math.max(...dr.fwi.slice(firstIdx, lastIdx + 1)), 0)]]));
  }
  $('repBody').innerHTML = '<div class="rep-head"><div><div class="rep-kicker">Időjárás-jelentés · ' + esc(META.station) + ' · ' + esc(placeName()) + '</div><h1>' + title + '</h1></div><div class="rep-date">Készült: ' + F.dayY.format(Date.now()) + '</div></div>' +
    '<p class="rep-sum">' + summaryText(d, m, s) + '</p>' +
    '<div class="rep-grid"><div>' + '<h3>Hőmérséklet</h3>' + repTable([
      ['Átlag', ttext(s.tavg)], ['Legmagasabb', (s.imax != null ? ttext(d.tmax[s.imax]) + day(s.imax) : '–')], ['Legalacsonyabb', (s.imin != null ? ttext(d.tmin[s.imin]) + day(s.imin) : '–')],
      ['Hőségnapok / trópusi éjszakák', s.hot + ' / ' + s.trop], ['Fagyos napok', s.frost]]) +
    '<h3>Szél és napsütés</h3>' + repTable([['Legerősebb lökés', s.igust != null ? wtext(d.gust[s.igust]) + day(s.igust) : '–'], ['Napsütés', num(s.sun, 0) + ' óra'], ['Napenergia', num(s.energy, 0) + ' kWh/m²']].concat(cloudAvg != null ? [['Felhőzet (becslés, nappal)', num(cloudAvg, 0) + ' %']] : [])) +
    '</div><div><h3>Csapadék</h3>' + repTable([['Összesen', rtext(s.rain, 1)], ['Esős napok (≥ ' + rthr(1) + ')', s.rainy], ['Legcsapadékosabb nap', s.iwet != null && d.rain[s.iwet] > 0 ? rtext(d.rain[s.iwet], 1) + day(s.iwet) : '–']]) +
    '<h3>Legnagyobb esők</h3>' + (evs.length ? repTable(evs.slice(0, 6).map(e => [F.full.format(e.start), rtext(e.total, 1) + ', max ' + rrtext(e.maxRate, 0)])) : '<p class="muted">Nem volt esőesemény.</p>') +
    drHtml + '</div></div>' +
    '<h3>Napi menet</h3><div class="rep-chart" id="repC1"></div><h3>Naptár (napi maximum)</h3><div class="rep-chart small" id="repC2"></div>';
  const x = s.ix.map(i => DS.ms(d.date[i]) + 12 * H);
  const c1 = echarts.init($('repC1'));
  c1.setOption({
    animation: false, grid: { left: 44, right: 44, top: 26, bottom: 26 }, legend: { top: 0, textStyle: { color: REP_MUTED } },
    xAxis: { type: 'time', axisLabel: { formatter: v => F.day.format(v), color: REP_MUTED } },
    yAxis: [{ type: 'value', name: tlabel(), scale: true, axisLabel: { color: REP_MUTED } }, { type: 'value', name: rlabel(), min: 0, splitLine: { show: false }, axisLabel: { color: REP_MUTED } }],
    series: [
      { name: 'Max.', type: 'line', data: s.ix.map((i, k) => [x[k], tconv(d.tmax[i])]), lineStyle: { color: '#e4572e' }, itemStyle: { color: '#e4572e' }, symbolSize: 4 },
      { name: 'Min.', type: 'line', data: s.ix.map((i, k) => [x[k], tconv(d.tmin[i])]), lineStyle: { color: '#3d7ff5' }, itemStyle: { color: '#3d7ff5' }, symbolSize: 4 },
      { name: 'Csapadék', type: 'bar', yAxisIndex: 1, data: s.ix.map((i, k) => [x[k], rconv(d.rain[i])]), itemStyle: { color: '#1f78d1', opacity: .6 }, barMaxWidth: 12 }
    ]
  });
  const c2 = echarts.init($('repC2'));
  const vals = s.ix.map(i => d.tmax[i]).filter(v => v != null);
  const c = 15, r = Math.max(5, ...vals.map(v => Math.abs(v - c)));
  c2.setOption({
    animation: false, visualMap: { show: false, min: c - r, max: c + r, inRange: { color: TEMP_COLORS } },
    calendar: { range: m, orient: 'horizontal', cellSize: ['auto', 26], left: 50, right: 20, top: 26, bottom: 6, dayLabel: { firstDay: 1, nameMap: DAY_NAMES }, monthLabel: { show: false }, yearLabel: { show: false }, itemStyle: { borderColor: '#fff', borderWidth: 2 } },
    series: [{ type: 'heatmap', coordinateSystem: 'calendar', data: s.ix.map(i => [d.date[i], d.tmax[i]]), label: { show: true, fontSize: fs(10), formatter: p => num(tconv(p.data[1]), 0) } }]
  });
  repCharts.push(c1, c2);
}

// a kiválasztott év utolsó adatnapjának indexe a szárazság-modulban (a hőösszeg minden évben nulláról indul)
function gddIdx(dr, y) { let k = null; dr.date.forEach((s, i) => { if (s.startsWith(y)) k = i; }); return k; }

async function yearReport(d, y) {
  repState().year = y; saveUi();
  const months = [...new Set(d.date.filter(s => s.startsWith(y)).map(s => s.slice(0, 7)))];
  const rows = months.map(m => ({ m, s: monthStats(d, m) }));
  const rec = await api('/api/records?clean=1&year=' + y);
  const total = rows.reduce((a, r) => a + r.s.rain, 0), sun = rows.reduce((a, r) => a + r.s.sun, 0);
  const dr = typeof getDrought === 'function' ? await getDrought().catch(() => null) : null;
  $('repBody').innerHTML = '<div class="rep-head"><div><div class="rep-kicker">Időjárás-évkönyv · ' + esc(META.station) + ' · ' + esc(placeName()) + '</div><h1>' + y + '</h1></div><div class="rep-date">Készült: ' + F.dayY.format(Date.now()) + (months.length < 12 ? '<br>(' + months.length + ' hónap adata)' : '') + '</div></div>' +
    '<p class="rep-sum">Az év csapadéka <b>' + rtext(total, 0) + '</b>, napsütése <b>' + num(sun, 0) + ' óra</b>. ' + rec.counts.hotDays + ' hőségnap, ' + rec.counts.tropicalNights + ' trópusi éjszaka, ' + rec.counts.frostDays + ' fagyos nap.' +
    (rec.frost.last ? ' Utolsó tavaszi fagy: ' + DS.fmt(rec.frost.last, { month: 'long', day: 'numeric' }) + '.' : '') + (rec.frost.first ? ' Első őszi fagy: ' + DS.fmt(rec.frost.first, { month: 'long', day: 'numeric' }) + '.' : '') +
    (dr && gddIdx(dr, y) != null ? ' Hőösszeg: ' + num(tdconv(dr.gddCum[gddIdx(dr, y)]), 0) + ' ' + tlabel() + '·nap.' : '') + '</p>' +
    '<h3>Hónapok</h3><table class="dtable rep-months"><thead><tr><th>Hónap</th><th>Átlag</th><th>Max.</th><th>Min.</th><th>Csapadék</th><th>Esős nap</th><th>Napsütés</th><th>Hőségnap</th><th>Fagyos nap</th></tr></thead><tbody>' +
    rows.map(r => '<tr><td>' + MONTHS_FULL[+r.m.slice(5) - 1] + '</td><td>' + ttext(r.s.tavg) + '</td><td>' + (r.s.imax != null ? num(tconv(d.tmax[r.s.imax])) : '–') + '</td><td>' + (r.s.imin != null ? num(tconv(d.tmin[r.s.imin])) : '–') + '</td><td>' + rtext(r.s.rain, 1) + '</td><td>' + r.s.rainy + '</td><td>' + num(r.s.sun, 0) + ' h</td><td>' + r.s.hot + '</td><td>' + r.s.frost + '</td></tr>').join('') +
    '</tbody></table>' +
    '<h3>Rekordok</h3><div class="rep-recs">' + rec.records.map(r => '<div><span>' + esc(recTitle(r)) + '</span><b>' + recValue(r) + recWarn(r) + '</b><small>' + (r.t ? F.dayY.format(r.t) : '') + '</small></div>').join('') + '</div>' +
    '<h3>Napi maximum-hőmérséklet</h3><div class="rep-chart small" id="repY1"></div><h3>Csapadék</h3><div class="rep-chart small" id="repY2"></div><h3>Havi átlaghőmérséklet és csapadék</h3><div class="rep-chart" id="repY3"></div>';
  const yi = d.date.map((s, i) => i).filter(i => d.date[i].startsWith(y));
  const cal = (el, key, vm) => { const c = echarts.init($(el)); c.setOption({ animation: false, visualMap: Object.assign({ show: false }, vm), calendar: { range: y, cellSize: ['auto', 13], left: 40, right: 10, top: 20, bottom: 4, dayLabel: { firstDay: 1, nameMap: DAY_NAMES, fontSize: fs(9) }, monthLabel: { nameMap: MONTHS_HU, fontSize: fs(10) }, yearLabel: { show: false }, itemStyle: { borderColor: '#fff', borderWidth: 1 } }, series: [{ type: 'heatmap', coordinateSystem: 'calendar', data: yi.map(i => [d.date[i], d[key][i]]) }] }); repCharts.push(c); };
  const tv = yi.map(i => d.tmax[i]).filter(v => v != null), r = Math.max(5, ...tv.map(v => Math.abs(v - 15)));
  cal('repY1', 'tmax', { min: 15 - r, max: 15 + r, inRange: { color: TEMP_COLORS } });
  cal('repY2', 'rain', { type: 'piecewise', pieces: RAIN_PIECES.map(p => Object.assign({}, p, { color: p.color || '#eef0f4' })) });
  const c3 = echarts.init($('repY3'));
  c3.setOption({
    animation: false, grid: { left: 44, right: 44, top: 26, bottom: 26 }, legend: { top: 0 },
    xAxis: { type: 'category', data: rows.map(r => MONTHS_HU[+r.m.slice(5) - 1]) },
    yAxis: [{ type: 'value', name: tlabel(), scale: true }, { type: 'value', name: rlabel(), min: 0, splitLine: { show: false } }],
    series: [{ name: 'Átlaghőmérséklet', type: 'line', data: rows.map(r => r.s.tavg == null ? null : +tconv(r.s.tavg).toFixed(1)), lineStyle: { color: '#e4572e' }, itemStyle: { color: '#e4572e' }, label: { show: true, formatter: p => num(p.value, 1) } },
             { name: 'Csapadék', type: 'bar', yAxisIndex: 1, data: rows.map(r => +rconv(r.s.rain).toFixed(3)), itemStyle: { color: '#1f78d1', opacity: .65 } }]
  });
  repCharts.push(c3);
}

document.querySelectorAll('#repMode button').forEach(b => b.addEventListener('click', () => { repState().mode = b.dataset.v; saveUi(); renderReport(); }));
$('repPick').addEventListener('change', e => { const st = repState(); if (st.mode === 'month') st.month = e.target.value; else st.year = e.target.value; saveUi(); renderReport(); });
$('repPrint').addEventListener('click', () => {
  // nyomtatás előtt a grafikonok a papír szélességére igazodnak; a „Mentés PDF-ként” a nyomtatási ablakban választható
  document.body.classList.add('printing');
  repCharts.forEach(c => c.resize());
  setTimeout(() => { window.print(); document.body.classList.remove('printing'); repCharts.forEach(c => c.resize()); }, 300);
});
window.addEventListener('resize', () => { if (currentTab === 'jelentes') repCharts.forEach(c => c.resize()); });
