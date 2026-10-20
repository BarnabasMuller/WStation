'use strict';
/* Eső fül: esőesemények listája, havi csapadék, szűrők */

let RAIN = null, rainChart = null, rainSort = { k: 'start', dir: -1 };
async function getRainEvents() { if (!RAIN) RAIN = await api('/api/rainevents'); return RAIN; }

function rainFilterState() {
  UI.rain = UI.rain || {};
  const r = UI.rain;
  if (r.year === undefined) r.year = '';
  if (r.month === undefined) r.month = '';
  if (r.min === undefined) r.min = 0.2;
  return r;
}

async function renderRain() {
  if (currentTab !== 'eso' || !META) return;
  const [ev, d] = await Promise.all([getRainEvents(), getDays(true)]);
  const st = rainFilterState();
  const years = [...new Set(d.date.map(s => s.slice(0, 4)))];
  const ysel = $('rainYear');
  if (ysel.options.length !== years.length + 1) ysel.innerHTML = '<option value="">Minden év</option>' + years.map(y => '<option>' + y + '</option>').join('');
  ysel.value = st.year; $('rainMonth').value = st.month; $('rainMin').value = +(rconv(st.min) || 0).toFixed(rdec(1)); $('rainMinU').textContent = rlabel(); $('rainMin').step = units().rain === 'in' ? 0.01 : 0.1; $('rainBurst').checked = !!st.burst;   // a lépésköz illeszkedjen az alapértékhez (0,2) – 9.8

  const inSel = ms => { const s = F.ymd.format(ms); return (!st.year || s.startsWith(st.year)) && (!st.month || s.slice(5, 7) === st.month); };
  const list = ev.events.filter(e => inSel(e.start) && e.total >= (+st.min || 0) && (!st.burst || e.maxRate >= ev.cloudburst));
  const dIdx = d.date.map((s, i) => i).filter(i => (!st.year || d.date[i].startsWith(st.year)) && (!st.month || d.date[i].slice(5, 7) === st.month));
  const total = dIdx.reduce((a, i) => a + (d.rain[i] || 0), 0), rainy = dIdx.filter(i => d.rain[i] >= 1).length;
  // a gyanús (csapadékugrásos) események a táblázatban látszanak, de rekordnak és felhőszakadásnak nem számítanak
  const okList = list.filter(e => !e.suspect);
  const biggest = okList.slice().sort((a, b) => b.total - a.total)[0], fastest = okList.slice().sort((a, b) => b.maxRate - a.maxRate)[0];
  const dur = e => (e.end - e.start) / 60000;
  const fmtDur = m => fmtDuration(m);
  const nSus = list.length - okList.length;

  $('rainCards').innerHTML = [
    ['Csapadék a kiválasztott időszakban', rtext(total, 1), 'esős nap (legalább ' + rthr(1) + '): ' + rainy],
    ['Esőesemények', list.length + ' db', 'két eső között legalább ' + ev.gapMin + ' perc szünet' + (nSus ? ' · ' + nSus + ' gyanús' : '')],
    ['Legnagyobb zápor', biggest ? rtext(biggest.total, 1) : '–', biggest ? F.full.format(biggest.start) + ', ' + fmtDur(dur(biggest)) : ''],
    ['Legnagyobb intenzitás', fastest ? rrtext(fastest.maxRate, 0) : '–', fastest ? F.full.format(fastest.maxT || fastest.start) : ''],
    ['Felhőszakadás (≥ ' + rrtext(ev.cloudburst, 0) + ')', okList.filter(e => e.maxRate >= ev.cloudburst).length + ' db', '']
  ].map(([a, b, c]) => '<div class="card mini"><h3>' + a + '</h3><div class="big">' + b + '</div><div class="line small">' + esc(c) + '</div></div>').join('');

  // havi csapadék + események
  const months = {};
  dIdx.forEach(i => { const m = d.date[i].slice(0, 7); months[m] = (months[m] || 0) + (d.rain[i] || 0); });
  const mk = Object.keys(months).sort();
  const ink = cssVar('--ink'), muted = cssVar('--muted'), line = cssVar('--line'), card = cssVar('--card');
  if (!rainChart || rainChart.isDisposed()) rainChart = echarts.init($('rainChart'));
  rainChart.setOption({
    animation: false, grid: [{ left: 52, right: 20, top: 34, height: 150 }, { left: 52, right: 20, top: 240, bottom: 30 }],
    title: [{ text: 'Havi csapadék', left: 4, top: 4, textStyle: { color: ink, fontSize: fs(13) } }, { text: 'Esőesemények (pont mérete: max. intenzitás)', left: 4, top: 210, textStyle: { color: ink, fontSize: fs(13) } }],
    tooltip: { confine: true, backgroundColor: card, borderColor: line, textStyle: { color: ink } },
    xAxis: [{ type: 'category', gridIndex: 0, data: mk.map(m => MONTHS_HU[+m.slice(5) - 1] + (st.year ? '' : ' ' + m.slice(2, 4))), axisLabel: { color: muted } },
            { type: 'time', gridIndex: 1, axisLabel: { color: muted, formatter: v => F.day.format(v) } }],
    yAxis: [{ type: 'value', gridIndex: 0, name: rlabel(), nameTextStyle: { color: muted }, axisLabel: { color: muted }, splitLine: { lineStyle: { color: line, opacity: .5 } } },
            { type: 'value', gridIndex: 1, name: rlabel(), nameTextStyle: { color: muted }, axisLabel: { color: muted }, splitLine: { lineStyle: { color: line, opacity: .5 } } }],
    series: [
      { type: 'bar', xAxisIndex: 0, yAxisIndex: 0, data: mk.map(m => +rconv(months[m]).toFixed(3)), itemStyle: { color: cssVar('--c-rain'), borderRadius: [4, 4, 0, 0] }, label: { show: true, position: 'top', color: muted, fontSize: fs(12), formatter: p => p.value > 0 ? num(p.value, rdec(0)) : '' },
        tooltip: { formatter: p => p.name + ': <b>' + num(p.value, rdec(1)) + ' ' + rlabel() + '</b>' } },
      { type: 'scatter', xAxisIndex: 1, yAxisIndex: 1, data: list.map(e => ({ value: [e.start, +rconv(e.total).toFixed(3)], e })),
        symbolSize: p => 6 + Math.sqrt(0), itemStyle: { color: p => p.data.e.maxRate >= ev.cloudburst ? '#7c3aed' : cssVar('--c-rain'), opacity: .8 },
        tooltip: { formatter: p => { const e = p.data.e; return F.full.format(e.start) + '<br><b>' + rtext(e.total, 1) + '</b>, ' + fmtDur(dur(e)) + '<br>max ' + rrtext(e.maxRate, 0) + '<br><span style="color:' + muted + '">Kattints: megnyitás az idővonalon</span>'; } } }
    ]
  }, true);
  rainChart.setOption({ series: [{}, { symbolSize: (v, p) => 6 + Math.min(26, Math.sqrt(p.data.e.maxRate) * 2) }] });
  rainChart.off('click');
  rainChart.on('click', p => { if (p.data && p.data.e) gotoRange(p.data.e.start, p.data.e.end, 3 * H); });

  // táblázat
  const sorted = list.slice().sort((a, b) => {
    const va = rainSort.k === 'dur' ? dur(a) : a[rainSort.k], vb = rainSort.k === 'dur' ? dur(b) : b[rainSort.k];
    return (va - vb) * rainSort.dir;
  });
  const th = (k, l) => '<th data-k="' + k + '"' + (rainSort.k === k ? ' class="sorted"' : '') + '>' + l + (rainSort.k === k ? (rainSort.dir < 0 ? ' ▼' : ' ▲') : '') + '</th>';
  $('rainTable').innerHTML = '<table class="dtable"><thead><tr>' + th('start', 'Kezdete') + '<th>Vége</th>' + th('dur', 'Hossz') + th('total', 'Összeg') + th('maxRate', 'Max. intenzitás') + th('avgRate', 'Átl. intenzitás') + '</tr></thead><tbody>' +
    sorted.map(e => '<tr data-a="' + e.start + '" data-b="' + e.end + '"' + (e.maxRate >= ev.cloudburst ? ' class="burst"' : '') + '><td>' + F.full.format(e.start) + '</td><td>' + F.time.format(e.end) + '</td><td>' + fmtDur(dur(e)) + '</td><td><b>' + rtext(e.total, 1) + '</b></td><td>' + rrtext(e.maxRate, 0) + (e.suspect ? ' <span class="neg" title="Csapadékugrás: valószínűleg egyszerre elolvadt hó vagy mérési hiba – rekordnak nem számít">⚠ gyanús</span>' : e.maxRate >= ev.cloudburst ? ' <span title="felhőszakadás">⛈</span>' : '') + '</td><td>' + rrtext(e.avgRate, 1) + '</td></tr>').join('') +
    '</tbody></table>' + (sorted.length ? '' : '<div class="muted" style="padding:12px">Nincs a szűrésnek megfelelő esőesemény.</div>');
  $('rainTable').querySelectorAll('th[data-k]').forEach(h => h.addEventListener('click', () => { rainSort = { k: h.dataset.k, dir: rainSort.k === h.dataset.k ? -rainSort.dir : -1 }; renderRain(); }));
  $('rainTable').querySelectorAll('tr[data-a]').forEach(r => r.addEventListener('click', () => gotoRange(+r.dataset.a, +r.dataset.b, 3 * H)));
}

['rainYear', 'rainMonth'].forEach(id => $(id).addEventListener('change', e => { rainFilterState()[id === 'rainYear' ? 'year' : 'month'] = e.target.value; saveUi(); renderRain(); }));
$('rainMin').addEventListener('change', e => { rainFilterState().min = Math.max(0, qback('rain', parseFloat(String(e.target.value).replace(',', '.')) || 0)); saveUi(); renderRain(); });
$('rainBurst').addEventListener('change', e => { rainFilterState().burst = e.target.checked; saveUi(); renderRain(); });
window.addEventListener('resize', () => { if (rainChart && currentTab === 'eso') rainChart.resize(); });
