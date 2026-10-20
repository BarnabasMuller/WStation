'use strict';
/* Statisztika fül: rekordok, hónapok összevetése, óra × nap hőtérkép, havi napi menet, két nap / hét egymásra, adatminőség */

let stMonths = null, stHm = null, stCycle = null, stCmp = null;
let ST_LAST = null;   // a legutóbbi érvényes időszak-lista
// kérésszámlálók: gyors gépelésnél egy régebbi (lassabb) válasz ne írja felül az újabbat
let stToken = 0, hmToken = 0, qToken = 0;

function countRows(rec) {
  const c = rec.counts, fr = rec.frost;
  return [
    ['Napok (legalább 80% adattal)', c.days],
    ['Hőségnapok (≥ ' + ttext(c.hotDay, 0) + ')', c.hotDays], ['Trópusi éjszakák (min ≥ ' + ttext(c.tropicalNight, 0) + ')', c.tropicalNights],
    ['Fagyos napok (min < ' + ttext(c.frostDay, 0) + ')', c.frostDays], ['Téli napok (max < ' + ttext(c.iceDay, 0) + ')', c.iceDays],
    ['Esős napok (≥ ' + rthr(1) + ')', c.rainDays], ['Csapadék összesen', rtext(c.rainTotal, 1)],
    ['Utolsó tavaszi fagy', fr.last ? DS.fmt(fr.last, { year: 'numeric', month: 'short', day: 'numeric' }) + ' (' + ttext(fr.lastT) + ')' : '–'],
    ['Első őszi fagy', fr.first ? DS.fmt(fr.first, { year: 'numeric', month: 'short', day: 'numeric' }) + ' (' + ttext(fr.firstT) + ')' : 'nem volt']
  ];
}
function stParam() { return '&ranges=' + encodeURIComponent(rangeParam(ST_LAST || [resolveRow({})])); }

// egy időpont kiemelése az idővonalon (rekordnál)
function highlightTime(t, label) {
  gotoRange(t - 20 * H, t + 20 * H, 4 * H);
  setTimeout(() => {
    if (!chart) return;
    chart.setOption({ series: [{ id: 'tLo', markLine: { symbol: ['none', 'circle'], silent: true, animation: false, data: [{ xAxis: t, lineStyle: { color: '#e11d48', width: 2 }, label: { formatter: '★ ' + (label || ''), color: '#e11d48', position: 'insideEndTop' } }] } }] });
  }, 300);
}

async function renderStats() {
  if (currentTab !== 'stat' || !META) return;
  UI.stat = UI.stat || {};
  const S = UI.stat;
  if (S.clean === undefined) S.clean = true;
  if (!S.metric) S.metric = 'tavg';
  if (!S.hm) S.hm = 'temp';
  stRanges();
  if (!$('stRanges').children.length) renderRangePicker();
  // hibás sornál a legutóbbi érvényes időszakkal számolunk
  const list = validRanges() || ST_LAST || [resolveRow({})];
  const token = ++stToken;
  ST_LAST = list;
  const side = S.rangeMode === 'side' && list.length > 1;
  const d = await getDays(true);
  if (token !== stToken) return;
  $('stClean').checked = S.clean; $('stMetric').value = S.metric; $('stHmMetric').value = S.hm;

  // --- rekordok és számlálók
  if (!side) {
    const rec = await api('/api/records?clean=' + (S.clean ? 1 : 0) + '&ranges=' + encodeURIComponent(rangeParam(list)));
    if (token !== stToken) return;
    $('stRecords').className = 'rec-grid';
    $('stRecords').innerHTML = rec.records.map(r =>
      '<div class="rec" role="button" tabindex="0" data-t="' + r.t + '" title="Kattints: megnyitás az idővonalon"><div class="rec-t">' + esc(recTitle(r)) + '</div>' +
      '<div class="rec-v">' + recValue(r) + recWarn(r) + '</div>' +
      '<div class="rec-d">' + (r.t ? (r.dateOnly ? F.dayY.format(r.t) : F.full.format(r.t)) : '') + (r.detail ? ' · ' + esc(recDetail(r)) : '') + '</div></div>').join('') || '<span class="muted">Ebben az időszakban nincs adat.</span>';
    $('stRecords').querySelectorAll('.rec').forEach(el => el.addEventListener('click', () => { const r = rec.records.find(x => String(x.t) === el.dataset.t); highlightTime(+el.dataset.t, r ? r.title : ''); }));
    $('stCounts').innerHTML = countRows(rec).map(([k, v]) => '<div class="ws-item"><span>' + k + '</span><b>' + v + '</b></div>').join('');
  } else {
    // egymás mellett: időszakonként egy oszlop
    const recs = await Promise.all(list.map(x => api('/api/records?clean=' + (S.clean ? 1 : 0) + '&ranges=' + encodeURIComponent(rangeParam([x])))));
    if (token !== stToken) return;
    const keys = [];
    recs.forEach(rc => rc.records.forEach(r => { if (!keys.find(k => k.key === r.key)) keys.push({ key: r.key, title: recTitle(r) }); }));
    const head = '<thead><tr><th></th>' + list.map(x => '<th>' + esc(rangeLabel(x)) + '</th>').join('') + '</tr></thead>';
    $('stRecords').className = 'tablewrap';
    $('stRecords').innerHTML = '<table class="dtable rec-cmp">' + head + '<tbody>' + keys.map(k => '<tr><td>' + esc(k.title) + '</td>' + recs.map(rc => {
      const r = rc.records.find(x => x.key === k.key);
      return r ? '<td data-t="' + r.t + '" role="button" tabindex="0" style="cursor:pointer" title="Kattints: megnyitás az idővonalon"><b>' + recValue(r) + '</b>' + recWarn(r) + '<small>' + (r.t ? F.dayY.format(r.t) : '') + '</small></td>' : '<td class="muted">–</td>';
    }).join('') + '</tr>').join('') + '</tbody></table>';
    $('stRecords').querySelectorAll('td[data-t]').forEach(el => el.addEventListener('click', () => highlightTime(+el.dataset.t, '')));
    const cr = recs.map(countRows);
    $('stCounts').innerHTML = '<div class="tablewrap" style="width:100%"><table class="dtable rec-cmp">' + head + '<tbody>' +
      cr[0].map((row, j) => '<tr><td>' + row[0] + '</td>' + cr.map(c => '<td><b>' + c[j][1] + '</b></td>').join('') + '</tr>').join('') + '</tbody></table></div>';
  }

  renderMonthCompare(d);
  renderHourHeat();
  renderCompare();
  renderQuality();
}

// ------------------------------------------------------------------ hónapok összevetése
function renderMonthCompare(d) {
  const S = UI.stat, k = S.metric;
  const today = DS.today(), curM = d.date[d.date.length - 1].slice(0, 7);
  const months = {};
  const inR = rangePred(ST_LAST || [resolveRow({})]);
  d.date.forEach((s, i) => {
    if (!inR(s)) return;
    const m = s.slice(0, 7);
    const o = months[m] = months[m] || { t: [], rain: 0, sun: 0, n: 0, nAll: 0, days: new Date(Date.UTC(+m.slice(0, 4), +m.slice(5), 0)).getUTCDate() };
    if (d.cov[i] >= COV_FULL) { if (d.tavg[i] != null) o.t.push(d.tavg[i]); o.n++; }
    // az összeg és a vetítés ugyanazokból a napokból (11.5): a csapadék / napsütés minden adatos napból
    o.rain += d.rain[i] || 0; o.sun += d.sun[i] || 0; o.nAll += d.cov[i] || 0;
  });
  const keys = Object.keys(months).sort();
  const mq = k === 'tavg' ? 'temp' : k === 'rain' ? 'rain' : null;
  const val0 = m => { const o = months[m]; return k === 'tavg' ? (o.t.length ? o.t.reduce((a, b) => a + b, 0) / o.t.length : null) : k === 'rain' ? o.rain : o.sun; };
  const val = m => qv(mq, val0(m));
  const proj = m => { const o = months[m]; if (m !== curM || k === 'tavg' || !(o.nAll > 0.5)) return null; return val(m) / o.nAll * o.days; };
  const ink = cssVar('--ink'), muted = cssVar('--muted'), line = cssVar('--line'), card = cssVar('--card');
  const col = k === 'tavg' ? cssVar('--c-temp') : k === 'rain' ? cssVar('--c-rain') : cssVar('--c-sun');
  const unit = qu(mq, 'h');
  if (!stMonths || stMonths.isDisposed()) stMonths = echarts.init($('stMonths'));
  stMonths.setOption({
    animation: false, grid: { left: 50, right: 20, top: 20, bottom: 30 },
    tooltip: { trigger: 'axis', confine: true, backgroundColor: card, borderColor: line, textStyle: { color: ink },
      formatter: ps => ps.filter(p => p.value != null).map(p => p.name + ' – ' + p.seriesName + ': <b>' + num(p.value, qd(mq, 1)) + ' ' + unit + '</b>').join('<br>') },
    xAxis: { type: 'category', data: keys.map(m => MONTHS_HU[+m.slice(5) - 1] + ' ' + m.slice(2, 4)), axisLabel: { color: muted } },
    yAxis: { type: 'value', axisLabel: { color: muted }, splitLine: { lineStyle: { color: line, opacity: .5 } }, scale: k === 'tavg' },
    series: [
      { name: k === 'tavg' ? 'havi átlag' : 'eddig', type: 'bar', data: keys.map(m => ({ value: val(m) == null ? null : +val(m).toFixed(3), itemStyle: { color: col, opacity: m === curM ? 1 : .55, borderColor: m === curM ? ink : 'transparent', borderWidth: m === curM ? 2 : 0 } })),
        label: { show: true, position: 'top', color: muted, fontSize: fs(12), formatter: p => p.value == null ? '' : num(p.value, qd(mq, k === 'tavg' ? 1 : 0)) } },
      { name: 'ilyen ütemben a hónap végére', type: 'bar', barGap: '-100%', data: keys.map(m => proj(m) == null ? null : +proj(m).toFixed(3)), itemStyle: { color: 'transparent', borderColor: col, borderType: 'dashed', borderWidth: 1.5 }, z: 1 }
    ]
  }, true);
  const cur = months[curM];
  $('stMonthNote').textContent = cur ? 'A kiemelt oszlop a mostani hónap (' + cur.n + ' nap adat). ' + (k !== 'tavg' ? 'A szaggatott keret: ilyen ütemben a hónap végére várható érték.' : '') : '';
}

// ------------------------------------------------------------------ óra × nap hőtérkép és havi napi menet
const HM_INFO = { temp: ['Hőmérséklet', '°C', TEMP_COLORS], hum: ['Páratartalom', '%', SEQ.hum], wind: ['Szél', 'km/h', SEQ.wind], sun: ['Napsugárzás', 'W/m²', SEQ.sun], pres: ['Légnyomás', 'hPa', ['#762a83', '#c2a5cf', '#f7f7f7', '#a6dba0', '#1b7837']], dew: ['Harmatpont', '°C', TEMP_COLORS], vpd: ['VPD', 'kPa', SEQ.orange] };
async function renderHourHeat() {
  const k = UI.stat.hm, info = HM_INFO[k];
  const token = ++hmToken;
  const h = await api('/api/hourly?metric=' + k + stParam());
  if (token !== hmToken) return;
  const hq = { temp: 'temp', dew: 'temp', wind: 'wind', pres: 'pres' }[k];
  const conv = v => qv(hq, v);
  const unit = qu(hq, info[1]);
  const data = [];
  h.values.forEach((row, x) => row.forEach((v, y) => { if (v != null) data.push([x, y, conv(v)]); }));
  const vals = data.map(p => p[2]);
  // üres időszaknál üres-állapot szöveg (15.4: a Math.min(...[]) Infinity-t adott, hibás színskálával)
  let empty = $('stHmEmpty');
  if (!empty) { empty = document.createElement('div'); empty.id = 'stHmEmpty'; empty.className = 'muted'; empty.style.padding = '12px'; $('stHm').after(empty); }
  empty.textContent = vals.length ? '' : 'Ebben az időszakban nincs adat.';
  $('stHm').classList.toggle('hidden', !vals.length);
  if (!vals.length) return;
  const ink = cssVar('--ink'), muted = cssVar('--muted'), line = cssVar('--line'), card = cssVar('--card');
  if (!stHm || stHm.isDisposed()) stHm = echarts.init($('stHm'));
  stHm.setOption({
    animation: false, grid: { left: 46, right: 80, top: 24, bottom: 56 },
    tooltip: { confine: true, backgroundColor: card, borderColor: line, textStyle: { color: ink }, formatter: p => DS.fmt(h.days[p.data[0]], { year: 'numeric', month: 'short', day: 'numeric', weekday: 'short' }) + ', ' + p.data[1] + ' óra: <b>' + num(p.data[2], 1) + ' ' + unit + '</b>' },
    // helyi dátum és óra (6.24, 6.21): „jan. 1.”, „6:00”
    xAxis: { type: 'category', data: h.days.map(s => DS.fmt(s, { month: 'short', day: 'numeric' })), axisLabel: { color: muted, hideOverlap: true } },
    yAxis: { type: 'category', data: [...Array(24).keys()].map(x => x + ':00'), axisLabel: { color: muted } },
    dataZoom: [{ type: 'slider', xAxisIndex: 0, bottom: 6, height: 18, textStyle: { color: muted } }, { type: 'inside', xAxisIndex: 0 }],
    visualMap: { min: Math.min(...vals), max: Math.max(...vals), calculable: true, right: 6, top: 24, textStyle: { color: muted }, inRange: { color: info[2] } },
    series: [{ type: 'heatmap', data, progressive: 5000 }]
  }, true);
  stHm.off('click'); stHm.on('click', p => gotoDay(h.days[p.data[0]]));

  // átlagos nap hónaponként: hogyan alakul a mennyiség a nap 24 órájában
  $('stCycleTitle').textContent = 'Átlagos nap hónaponként – hogyan alakul a ' + info[0].toLowerCase() + ' a nap 24 órájában';
  const colors = ['#313695', '#4575b4', '#74add1', '#abd9e9', '#a6d96a', '#66bd63', '#fdae61', '#f46d43', '#d73027', '#a50026', '#8073ac', '#542788'];
  if (!stCycle || stCycle.isDisposed()) stCycle = echarts.init($('stCycle'));
  const list = ST_LAST || [resolveRow({})];
  if (UI.stat.rangeMode === 'side' && list.length > 1) {
    // egymás mellett: időszakonként egy átlagos nap
    $('stCycleTitle').textContent = 'Átlagos nap időszakonként – hogyan alakul a ' + info[0].toLowerCase() + ' a nap 24 órájában';
    const hs = await Promise.all(list.map(x => api('/api/hourly?metric=' + k + '&ranges=' + encodeURIComponent(rangeParam([x])))));
    const pal = ['#e4572e', '#3d7ff5', '#16a34a', '#a855f7', '#f59e0b', '#0ea5e9', '#db2777', '#64748b'];
    stCycle.setOption({
      animation: false, grid: { left: 50, right: 20, top: 30, bottom: 30 }, legend: { top: 0, type: 'scroll', textStyle: { color: muted } },
      tooltip: { trigger: 'axis', confine: true, backgroundColor: card, borderColor: line, textStyle: { color: ink }, valueFormatter: v => v == null ? '–' : num(v, qd(hq, 1)) + ' ' + unit },
      xAxis: { type: 'category', data: [...Array(24).keys()].map(x => x + 'h'), axisLabel: { color: muted } },
      yAxis: { type: 'value', name: unit, scale: true, nameTextStyle: { color: muted }, axisLabel: { color: muted }, splitLine: { lineStyle: { color: line, opacity: .5 } } },
      series: hs.map((hh, j) => {
        const avg = [...Array(24).keys()].map(hr => { const v = hh.values.map(r => r[hr]).filter(x => x != null); return v.length ? conv(v.reduce((a, b) => a + b, 0) / v.length) : null; });
        return { name: rangeLabel(list[j]), type: 'line', smooth: true, showSymbol: false, data: avg.map(v => v == null ? null : +v.toFixed(2)), lineStyle: { color: pal[j % pal.length], width: 2 }, itemStyle: { color: pal[j % pal.length] } };
      })
    }, true);
    return;
  }
  stCycle.setOption({
    animation: false, grid: { left: 50, right: 20, top: 30, bottom: 30 }, legend: { top: 0, type: 'scroll', textStyle: { color: muted, fontSize: fs(11) } },
    tooltip: { trigger: 'axis', confine: true, backgroundColor: card, borderColor: line, textStyle: { color: ink } },
    xAxis: { type: 'category', data: [...Array(24).keys()].map(x => x + 'h'), axisLabel: { color: muted } },
    yAxis: { type: 'value', name: unit, scale: true, nameTextStyle: { color: muted }, axisLabel: { color: muted }, splitLine: { lineStyle: { color: line, opacity: .5 } } },
    series: h.cycle.map((row, m) => row.some(v => v != null) ? { name: MONTHS_FULL[m], type: 'line', smooth: true, showSymbol: false, data: row.map(v => v == null ? null : +conv(v).toFixed(2)), lineStyle: { color: colors[m], width: 2 }, itemStyle: { color: colors[m] } } : null).filter(Boolean)
  }, true);
}

// ------------------------------------------------------------------ időszakok egymásra (több időszak, több adattípus)
const CMP_VARS = [['temp', 'hőmérséklet', 'temp', 'v', 'temp'], ['hum', 'páratartalom', 'hum', 'v', '%'], ['pres', 'légnyomás', 'pres', 'v', 'pres'],
  ['wind', 'szél', 'wind', 'v', 'wind'], ['gust', 'széllökés', 'wind', 'gust', 'wind'], ['sun', 'napsugárzás', 'sun', 'v', 'W/m²'], ['rain', 'csapadék (halmozott)', 'rain', 'tot', 'rain']];
const CMP_PAL = ['#e4572e', '#3d7ff5', '#16a34a', '#a855f7', '#f59e0b', '#0ea5e9', '#db2777', '#64748b'];
function cmpDays(S) { return S.cmpMode === 'week' ? 7 : S.cmpMode === 'month' ? 31 : S.cmpMode === 'n' ? Math.max(1, Math.min(92, parseInt(S.cmpN) || 3)) : 1; }
async function renderCompare() {
  const S = UI.stat;
  const d = await getDays(true);
  const lastD = d.date[d.date.length - 1];
  if (!Array.isArray(S.cmpStarts)) S.cmpStarts = [S.cmpA || DS.add(lastD, -1), S.cmpB || (DS.add(lastD, -366) >= d.date[0] ? DS.add(lastD, -365) : d.date[Math.max(0, d.date.length - 31)])];
  if (!Array.isArray(S.cmpVars) || !S.cmpVars.length) S.cmpVars = [S.cmpVar || 'temp'];
  if (!['day', 'week', 'month', 'n'].includes(S.cmpMode)) S.cmpMode = 'day';
  $('cmpMode').value = S.cmpMode; $('cmpNWrap').classList.toggle('hidden', S.cmpMode !== 'n'); $('cmpN').value = S.cmpN || 3;
  $('cmpStarts').innerHTML = S.cmpStarts.map((s, i) => '<span style="border-left:4px solid ' + CMP_PAL[i % 8] + ';padding-left:6px"><input type="date" class="inp" data-ci="' + i + '" value="' + s + '" min="' + d.date[0] + '" max="' + lastD + '">' +
    (S.cmpStarts.length > 1 ? '<button class="btn small" data-cdel="' + i + '">×</button>' : '') + '</span>').join('') + (S.cmpStarts.length < 8 ? ' <button class="btn small" id="cmpAdd">+</button>' : '');
  $('cmpVars').innerHTML = CMP_VARS.map(v => '<label><input type="checkbox" value="' + v[0] + '"' + (S.cmpVars.includes(v[0]) ? ' checked' : '') + '>' + v[1] + '</label>').join('');
  $('cmpStarts').querySelectorAll('[data-ci]').forEach(el => el.addEventListener('change', () => { if (/^\d{4}-\d{2}-\d{2}$/.test(el.value)) { S.cmpStarts[+el.dataset.ci] = el.value; saveUi(); renderCompare(); } }));
  $('cmpStarts').querySelectorAll('[data-cdel]').forEach(b => b.addEventListener('click', () => { S.cmpStarts.splice(+b.dataset.cdel, 1); saveUi(); renderCompare(); }));
  if ($('cmpAdd')) $('cmpAdd').addEventListener('click', () => { const p = DS.add(S.cmpStarts[S.cmpStarts.length - 1], -365); S.cmpStarts.push(p >= d.date[0] ? p : DS.add(lastD, -7)); saveUi(); renderCompare(); });
  $('cmpVars').querySelectorAll('input').forEach(c => c.addEventListener('change', () => { S.cmpVars = [...$('cmpVars').querySelectorAll('input:checked')].map(x => x.value); if (!S.cmpVars.length) S.cmpVars = ['temp']; saveUi(); renderCompare(); }));

  const days = cmpDays(S), len = days * D;
  const startOf = s => { const i = d.idx[s]; return i !== undefined ? d.start[i] : DS.ms(s) - 2 * H; };
  const res = await Promise.all(S.cmpStarts.map(async s => { const a = startOf(s); return { s, a, r: await api('/api/series?from=' + a + '&to=' + (a + len) + '&n=' + Math.min(6000, 1500 * Math.ceil(days / 7) + 1500) + '&clean=1') }; }));
  const vars = CMP_VARS.filter(v => S.cmpVars.includes(v[0]));
  const groups = [];   // mértékegységenként egy kis grafikon
  vars.forEach(v => { let g = groups.find(x => x.u === v[4]); if (!g) groups.push(g = { u: v[4], vars: [] }); g.vars.push(v); });
  const ink = cssVar('--ink'), muted = cssVar('--muted'), line = cssVar('--line'), card = cssVar('--card');
  const gh = 170, top0 = 40;
  $('stCmp').style.height = (top0 + groups.length * (gh + 34) + 20) + 'px';
  if (stCmp && !stCmp.isDisposed()) stCmp.dispose();
  stCmp = echarts.init($('stCmp'));
  const label = s => DS.fmt(s, { year: 'numeric', month: 'short', day: 'numeric' }) + (days > 1 ? ' – ' + DS.fmt(DS.add(s, days - 1), { month: 'short', day: 'numeric' }) : '');
  const grid = [], xAxis = [], yAxis = [], series = [];
  const step = days === 1 ? 3 : days <= 7 ? 24 : days <= 31 ? 24 * 5 : 24 * 14;
  groups.forEach((g, gi) => {
    const q = QTY[g.u] ? g.u : null, unit = qu(q, g.u);
    grid.push({ left: 56, right: 20, top: top0 + gi * (gh + 34), height: gh });
    // halvány rácsvonalak a téma színével (6.23: sötét témán vastag fehér vonalak voltak), az óra „3:00” alakban
    xAxis.push({ type: 'value', gridIndex: gi, min: 0, max: len / H, interval: step, splitLine: { lineStyle: { color: line, opacity: .45 } }, axisLabel: { show: gi === groups.length - 1, color: muted, formatter: v => days === 1 ? v + ':00' : tf('{n}. nap', { n: v / 24 + 1 }) } });
    yAxis.push({ type: 'value', gridIndex: gi, name: unit, scale: true, nameTextStyle: { color: muted }, axisLabel: { color: muted }, splitLine: { lineStyle: { color: line, opacity: .5 } } });
    res.forEach((X, pi) => g.vars.forEach((v, vi) => {
      const gr = X.r.groups[v[2]];
      if (!gr) return;
      let off = 0, prev = null;
      const data = gr.t.map((t, i) => {
        let val = gr[v[3]][i];
        // a napi halmozott csapadék éjfélkor nullázódik: az időszakra összeadjuk
        if (v[0] === 'rain' && val != null) { if (prev != null && val < prev - 0.05) off += prev; prev = val; val += off; }
        return [(t - X.a) / H, qv(q, val)];
      }).filter(p => p[0] >= 0 && p[0] <= len / H);
      series.push({ name: label(X.s) + (g.vars.length > 1 ? ' · ' + v[1] : ''), type: 'line', xAxisIndex: gi, yAxisIndex: gi, showSymbol: false, data,
        lineStyle: { color: CMP_PAL[pi % 8], width: 2, type: vi ? 'dashed' : 'solid' }, itemStyle: { color: CMP_PAL[pi % 8] } });
      series[series.length - 1].meta = { u: unit, q };
    }));
  });
  const meta = series.map(s => s.meta); series.forEach(s => delete s.meta);
  stCmp.setOption({
    animation: false, grid, xAxis, yAxis, series, legend: { top: 0, type: 'scroll', textStyle: { color: muted } },
    axisPointer: { link: [{ xAxisIndex: 'all' }] },
    tooltip: { trigger: 'axis', confine: true, backgroundColor: card, borderColor: line, textStyle: { color: ink },
      formatter: ps => { const x = ps[0].data[0]; return (days === 1 ? Math.floor(x) + ':' + String(Math.round((x % 1) * 60)).padStart(2, '0') : Math.floor(x / 24) + 1 + '. nap, ' + Math.floor(x % 24) + ' óra') + '<br>' +
        ps.filter(p => p.data[1] != null).map(p => { const m = meta[p.seriesIndex]; return p.marker + ' ' + esc(p.seriesName) + ': <b>' + num(p.data[1], qd(m.q, 1)) + ' ' + m.u + '</b>'; }).join('<br>'); } }
  }, true);
}
$('cmpMode').addEventListener('change', e => { UI.stat.cmpMode = e.target.value; saveUi(); renderCompare(); });
$('cmpN').addEventListener('change', e => {
  const v = String(e.target.value).trim();
  if (!/^\d+$/.test(v) || +v < 1 || +v > 92) { $('cmpNErr').textContent = '⚠ 1–92 nap'; e.target.classList.add('bad'); return; }
  $('cmpNErr').textContent = ''; e.target.classList.remove('bad'); UI.stat.cmpN = +v; saveUi(); renderCompare();
});

// ------------------------------------------------------------------ adatminőség
async function renderQuality() {
  const token = ++qToken;
  const q = await api('/api/quality?x=1' + stParam());
  if (token !== qToken) return;
  const fmtMin = m => fmtDuration(m);   // egységes időtartam-formátum (6.22)
  $('stGaps').innerHTML = '<table class="dtable"><thead><tr><th>Utolsó mérés előtte</th><th>Első mérés utána</th><th>Hossz</th></tr></thead><tbody>' +
    q.gaps.slice().sort((a, b) => b.minutes - a.minutes).map(g => '<tr data-a="' + g.from + '" data-b="' + g.to + '"><td>' + F.full.format(g.from) + '</td><td>' + F.full.format(g.to) + '</td><td><b>' + fmtMin(g.minutes) + '</b></td></tr>').join('') + '</tbody></table>';
  $('stGaps').querySelectorAll('tr[data-a]').forEach(r => r.addEventListener('click', () => gotoRange(+r.dataset.a, +r.dataset.b, 6 * H)));
  $('stFlags').innerHTML = q.flags.filter(f => f.count > 0).map(f => '<details class="qflag"><summary><b>' + esc(f.text) + '</b> – ' + num(f.count, 0) + ' mérés</summary><div class="qlist">' +
    f.samples.map(s => '<span data-t="' + s.t + '">' + F.full.format(s.t) + ': ' + esc(s.value) + '</span>').join('') + (f.count > f.samples.length ? '<span class="muted">… és még ' + (f.count - f.samples.length) + '</span>' : '') + '</div></details>').join('') ||
    '<span class="muted">Nincs gyanús mérés.</span>';
  $('stFlags').querySelectorAll('[data-t]').forEach(s => s.addEventListener('click', () => highlightTime(+s.dataset.t, 'gyanús')));
  $('stQualitySum').textContent = num(q.n, 0) + ' mérés, ebből ' + num(q.flagged, 0) + ' jelölt (' + num(q.flagged / q.n * 100, 2) + '%) – semmi nincs törölve, a jelölt értékek a napi statisztikákból és a rekordokból kizárhatók.';
}

$('stClean').addEventListener('change', e => { UI.stat.clean = e.target.checked; saveUi(); renderStats(); });
$('stMetric').addEventListener('change', e => { UI.stat.metric = e.target.value; saveUi(); getDays(true).then(renderMonthCompare); });
$('stHmMetric').addEventListener('change', e => { UI.stat.hm = e.target.value; saveUi(); renderHourHeat(); });
window.addEventListener('resize', () => { if (currentTab === 'stat') [stMonths, stHm, stCycle, stCmp].forEach(c => c && c.resize()); });
