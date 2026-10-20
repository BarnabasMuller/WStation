'use strict';
/* Szél fül: óra × hónap hőtérkép, szélrózsa csomókategóriákkal, szeles délutánok */

let windHm = null, windRose = null;

function windPeriods(d) {
  const last = d.date[d.date.length - 1];
  const opts = [['all', 'Minden adat'], ['30', 'Utolsó 30 nap'], ['year', 'Idén']];
  const seasons = [['tavasz', 'Tavasz (márc–máj)', [3, 5]], ['nyar', 'Nyár (jún–aug)', [6, 8]], ['osz', 'Ősz (szept–nov)', [9, 11]], ['tel', 'Tél (dec–febr)', [12, 2]]];
  seasons.forEach(s => opts.push([s[0], s[1]]));
  [...new Set(d.date.map(s => s.slice(0, 7)))].reverse().forEach(m => opts.push([m, m.slice(0, 4) + '. ' + MONTHS_FULL[+m.slice(5) - 1]]));
  return opts;
}

function periodRange(p, d) {
  const first = META.first, last = META.last;
  const y = +d.date[d.date.length - 1].slice(0, 4);
  if (p === '30') return [last - 30 * D, last];
  if (p === 'year') return [Math.max(first, Date.UTC(y, 0, 1) - H), last];
  const S = { tavasz: [3, 5], nyar: [6, 8], osz: [9, 11] };
  if (S[p]) return [Date.UTC(y, S[p][0] - 1, 1) - 2 * H, Date.UTC(y, S[p][1], 1) - 2 * H];
  if (p === 'tel') return [Date.UTC(y - 1, 11, 1) - H, Date.UTC(y, 2, 1) - H];
  if (/^\d{4}-\d{2}$/.test(p)) { const yy = +p.slice(0, 4), mm = +p.slice(5); return [Date.UTC(yy, mm - 1, 1) - 2 * H, Date.UTC(yy, mm, 1) - 2 * H]; }
  return [first, last];
}

async function renderWind() {
  if (currentTab !== 'szel' || !META) return;
  const d = await getDays(true);
  UI.windPeriod = UI.windPeriod || 'all';
  const sel = $('windPeriod');
  const opts = windPeriods(d);
  if (sel.options.length !== opts.length) sel.innerHTML = opts.map(o => '<option value="' + o[0] + '">' + esc(o[1]) + '</option>').join('');
  if (!opts.some(o => o[0] === UI.windPeriod)) UI.windPeriod = 'all';
  sel.value = UI.windPeriod;
  const [a, b] = periodRange(UI.windPeriod, d);
  const w = await api('/api/wind?from=' + a + '&to=' + b);
  const ink = cssVar('--ink'), muted = cssVar('--muted'), line = cssVar('--line'), card = cssVar('--card');

  // összefoglaló kártyák
  const di = d.date.map((s, i) => i).filter(i => d.start[i] >= a - H && d.start[i] < b);   // (11.4: nem egy nappal korábbról)
  const gi = di.reduce((m, i) => d.gust[i] != null && (m == null || d.gust[i] > d.gust[m]) ? i : m, null);
  let vx = 0, vy = 0; di.forEach(i => { if (d.dir[i] != null && d.wind[i] != null) { vx += d.wind[i] * Math.sin(d.dir[i] * RAD); vy += d.wind[i] * Math.cos(d.dir[i] * RAD); } });
  const dom = vx || vy ? (Math.atan2(vx, vy) / RAD + 360) % 360 : null;
  const avgWind = di.map(i => d.wind[i]).filter(x => x != null);
  // a Szél fülön a Beaufort mindenhol egy, az átlagszélnél két tizedes; a csomó egy tizedes
  const awk = avgWind.length ? avgWind.reduce((x, y) => x + y, 0) / avgWind.length : null;
  // a második egységként a Beaufort egész fokozat + név (7.32: a tört Bft laikusnak furcsa; tört érték csak Bft módban)
  const both = (kmh, bd) => kmh == null ? '' : [windUnit() !== 'bft' ? bftText(kmh) : null, windUnit() !== 'kn' ? num(kmh / 1.852, 1) + ' csomó' : null].filter(Boolean).join(' · ');
  $('windCards').innerHTML = [
    ['Átlagszél', wtext(awk, windUnit() === 'bft' ? 2 : null), both(awk, 2)],
    ['Legerősebb lökés', gi != null ? wtext(d.gust[gi]) : '–', gi != null ? DS.fmt(d.date[gi], { month: 'short', day: 'numeric' }) + ' · ' + both(d.gust[gi], 1) : ''],
    ['Uralkodó irány', dom == null ? '–' : dirText(dom), dom == null ? '' : num(dom, 0) + '° felől'],
    // a definíciók a fül egységében (7.32)
    ['Szélcsend (' + (windUnit() === 'bft' ? '0 Bft' : '< ' + wtext(CALM_KMH, 1)) + ')', num(w.calmPct, 0) + '%', 'a mérések arányában'],
    ['Szeles délutánok', w.windyDays + ' nap', tf('átlag ≥ {v} {a}–{b} óra között, {n} napból · leghosszabb sorozat: {s} nap', { v: wtext(w.windyKmh, windUnit() === 'bft' ? 2 : null), a: w.afternoonFrom, b: w.afternoonTo, n: w.afternoonDays, s: w.windyStreak })]
  ].map(([x, y, z]) => '<div class="card mini"><h3>' + x + '</h3><div class="big">' + y + '</div><div class="line small">' + esc(z) + '</div></div>').join('');

  // óra × hónap hőtérkép
  const data = [];
  w.hourMonth.forEach((row, m) => row.forEach((v, h) => { if (v != null) data.push([h, m, wconv(v)]); }));
  const vals = data.map(x => x[2]);
  const monthsWith = w.hourMonth.map((r, m) => r.some(v => v != null) ? m : -1).filter(m => m >= 0);
  if (!windHm || windHm.isDisposed()) windHm = echarts.init($('windHm'));
  windHm.setOption({
    animation: false, grid: { left: 86, right: 90, top: 30, bottom: 40 },
    title: { text: 'Átlagszél napszak és hónap szerint (' + wlabel() + ')', left: 4, top: 0, textStyle: { color: ink, fontSize: fs(13) } },
    tooltip: { confine: true, backgroundColor: card, borderColor: line, textStyle: { color: ink }, formatter: p => MONTHS_FULL[p.data[1]] + ', ' + p.data[0] + '–' + (p.data[0] + 1) + ' óra: <b>' + num(p.data[2], windUnit() === 'bft' ? 2 : 1) + ' ' + wlabel() + '</b>' },
    xAxis: { type: 'category', data: [...Array(24).keys()].map(h => h + ':00'), axisLabel: { color: muted, hideOverlap: true }, splitArea: { show: false } },
    yAxis: { type: 'category', data: MONTHS_FULL, axisLabel: { color: muted }, min: monthsWith.length ? monthsWith[0] : 0, max: monthsWith.length ? monthsWith[monthsWith.length - 1] : 11 },
    visualMap: { min: 0, max: Math.max(1, ...vals), calculable: true, orient: 'vertical', right: 10, top: 40, textStyle: { color: muted }, inRange: { color: SEQ.wind } },
    // a cellaszám csak akkor, ha olvasható méretben elfér (6.21); különben a buboréksúgóban
    series: [{ type: 'heatmap', data, label: { show: ($('windHm').clientWidth - 176) / 24 >= fs(11) * 2.6, fontSize: fs(11), color: '#222', formatter: p => num(p.data[2], ({ bft: 2, kn: 1, ms: 1 })[windUnit()] || 0) } }]
  }, true);

  // szélrózsa: 16 irány × csomókategória
  // a kategóriák csomóban vannak; a határok a választott mértékegységben jelennek meg
  const bins = w.knBins;
  const binName = i => {
    const lo = bins[i], hi = bins[i + 1], u = windUnit();
    const f = x => u === 'kn' ? num(x, 0) : num(wconv(x * 1.852), ({ bft: 1, ms: 1 })[u] || 0);
    return hi ? f(lo) + '–' + f(hi) + ' ' + wlabel() : '≥ ' + f(lo) + ' ' + wlabel();
  };
  const roseCols = ['#c7e9c0', '#74c476', '#31a354', '#3182bd', '#756bb1', '#de2d26'];
  if (!windRose || windRose.isDisposed()) windRose = echarts.init($('windRose'));
  windRose.setOption({
    animation: false,
    title: { text: 'Szélrózsa (a mérések %-a; a körök 2%-onként)', left: 4, top: 0, textStyle: { color: ink, fontSize: fs(13) } },
    legend: { bottom: 0, textStyle: { color: muted, fontSize: fs(11) } },
    tooltip: { confine: true, backgroundColor: card, borderColor: line, textStyle: { color: ink }, formatter: p => DIRS[p.dataIndex] + ' felől, ' + p.seriesName + ': <b>' + num(p.value, 1) + '%</b>' },
    angleAxis: { type: 'category', data: DIRS, startAngle: 90 + 11.25, axisLabel: { color: muted }, axisLine: { lineStyle: { color: line } } },
    radiusAxis: { axisLabel: { show: false }, axisLine: { show: false }, axisTick: { show: false }, splitLine: { lineStyle: { color: line } } },
    polar: { radius: ['6%', '68%'], center: ['50%', '50%'] },
    series: bins.map((b, i) => ({ type: 'bar', coordinateSystem: 'polar', stack: 'r', name: binName(i), data: w.rose.map(r => +r[i].toFixed(2)), itemStyle: { color: roseCols[i] } }))
  }, true);

  // szeles délutánok listája
  $('windDays').innerHTML = w.windyList.length ? w.windyList.map(s => '<button class="chip" data-d="' + s + '">' + DS.fmt(s, { month: 'short', day: 'numeric', weekday: 'short' }) + '</button>').join('') : '<span class="muted">Ebben az időszakban nem volt szeles délután.</span>';
  $('windDays').querySelectorAll('[data-d]').forEach(c => c.addEventListener('click', () => gotoDay(c.dataset.d)));
}

$('windPeriod').addEventListener('change', e => { UI.windPeriod = e.target.value; saveUi(); renderWind(); });
function onWindUnitChanged() {
  if (currentTab === 'szel') renderWind();
  if (currentTab === 'attekintes' && typeof renderWeek === 'function') renderWeek();
}
window.addEventListener('resize', () => { if (currentTab === 'szel') { if (windHm) windHm.resize(); if (windRose) windRose.resize(); } });
