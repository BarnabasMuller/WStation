'use strict';
/* Szárazság › Víz: vízfogyasztás (a rögzített és programozott öntözésből) és vízigény (virtuális öntözéssel a célállapot tartásához), költséggel */

const WTYPES = { gyep: '🌱 gyep', bokor: '🌿 bokrok', fa: '🌳 fák', cserep: '🪴 cserepek' };
const WEFF = { gyep: 0.75, bokor: 0.9, fa: 0.9, cserep: 1 };   // szórófej ~75%, csepegtető ~90%
function wState() {
  const w = UI.water = UI.water || {};
  if (typeof w.detailed !== 'boolean') w.detailed = false;
  if (!(w.price >= 0)) w.price = 360;
  if (!(w.sewer >= 0)) w.sewer = 450;
  if (!(w.simple >= 0)) w.simple = 810;
  if (typeof w.eff !== 'boolean') w.eff = true;
  w.targets = w.targets || {};
  w.types = w.types || { gyep: true, bokor: true, fa: true, cserep: true };
  if (!(w.cmp >= 0)) w.cmp = 20;
  return w;
}
function wPrice() { const w = wState(); return w.detailed ? w.price + w.sewer : w.simple; }
function costText(m3) {
  const w = wState();
  if (!w.detailed) return num(m3 * w.simple, 0) + ' Ft';
  return num(m3 * w.price, 0) + ' Ft + ' + num(m3 * w.sewer, 0) + ' Ft = <b>' + num(m3 * (w.price + w.sewer), 0) + ' Ft</b> <span class="muted">(vízdíj + csatornadíj)</span>';
}
// egy profil teljes területe (m²) a literekhez
function totalArea(p, r) { return ((r && r.area) || 1) * (p.type === 'bokor' ? Math.max(1, p.count || 1) : 1); }
function targetOf(p, r) { const w = wState(); const t = w.targets[p.id]; return t >= 5 && t <= 95 ? t / 100 : baseP(p, r); }
function baseP(p, r) { return p.type === 'felszin' ? last(r.raw) / last(r.taw) : Math.max(0.1, Math.min(0.9, p.p)); }

// a talajvíz-mérleg újrafuttatása virtuális öntözéssel: ha a kiszáradás eléri a célt, visszatölti a vödröt
function simulateDemand(p, r, target) {
  const n = DR.date.length, out = new Array(n).fill(0);
  const rainF = p.type === 'cserep' ? (p.place === 'fedett' ? 0 : p.place === 'felig' ? 0.5 : 1) : 1;
  const etF = p.type === 'cserep' ? (p.place === 'fedett' ? 0.6 : p.place === 'felig' ? 0.8 : 1) : 1;
  let dr = r.dr[0] != null ? Math.max(0, r.dr[0] - (r.irr[0] || 0)) : 0;
  for (let i = 0; i < n; i++) {
    const taw = r.taw[i], raw = r.raw[i], pf = raw / taw, kc = r.kc[i] || 0, et0 = DR.et0[i] || 0;
    const ks = dr > raw ? Math.max(0, (taw - dr) / ((1 - pf) * taw)) : 1;
    const peff = ((DR.rain[i] || 0) - (p.type === 'cserep' ? 0 : (DR.ro[i] || 0))) * rainF;
    dr = Math.min(taw, Math.max(0, dr - peff + ks * kc * et0 * etF));
    if (dr / taw >= target) { out[i] = dr; dr = 0; }
  }
  return out;   // mm (nettó)
}

let wCal = null, wBars = null;
function renderWaterUse() {
  const box = $('waterUse');
  if (!box || !DR) return;
  const w = wState();
  const list = (typeof validRanges === 'function' && (validRanges() || null)) || [{ a: DR.date[0], b: DS.today(), full: true }];
  const inR = typeof rangePred === 'function' ? rangePred(list) : () => true;
  const profs = DR.config.profiles.filter(p => p.enabled && WTYPES[p.type]).map(p => ({ p, r: resById(p.id) })).filter(x => x.r);
  const shown = profs.filter(x => w.types[x.p.type] !== false);
  const n = DR.date.length;
  const dayUse = new Array(n).fill(0), dayNeed = new Array(n).fill(0), dayNeedCmp = new Array(n).fill(0), dayRain = new Array(n).fill(0);
  shown.forEach(({ p, r }) => {
    const A = totalArea(p, r), eff = w.eff ? WEFF[p.type] : 1;
    const t = targetOf(p, r), t2 = Math.min(0.95, t + w.cmp / 100);
    const need = simulateDemand(p, r, t), need2 = simulateDemand(p, r, t2);
    for (let i = 0; i < n; i++) {
      if (!inR(DR.date[i])) continue;
      dayUse[i] += (r.irr[i] || 0) * A / eff;            // liter (bruttó)
      dayNeed[i] += need[i] * A / eff;
      dayNeedCmp[i] += need2[i] * A / eff;
      if (p.type !== 'cserep') dayRain[i] += (DR.rain[i] || 0) * A;
    }
  });
  const sum = a => a.reduce((s, x) => s + x, 0) / 1000;   // m³
  const useM3 = sum(dayUse), needM3 = sum(dayNeed), cmpM3 = sum(dayNeedCmp);
  const months = {};
  DR.date.forEach((s, i) => { if (!inR(s)) return; const m = s.slice(0, 7); const o = months[m] = months[m] || { need: 0, use: 0, rain: 0 }; o.need += dayNeed[i] / 1000; o.use += dayUse[i] / 1000; o.rain += dayRain[i] / 1000; });
  const mk = Object.keys(months).sort();
  const topM = mk.slice().sort((a, b) => months[b].need - months[a].need)[0];
  let peak = -1; dayNeed.forEach((v, i) => { if (v > 0 && (peak < 0 || v > dayNeed[peak])) peak = i; });
  const label = list.length === 1 && list[0].full ? 'teljes időszak' : list.map(x => typeof rangeLabel === 'function' ? rangeLabel(x) : x.a + '–' + x.b).join(' + ');

  box.innerHTML =
    '<div class="row2 w-ctrl"><span class="muted">Időszak:</span> <b>' + esc(label) + '</b> <span class="muted small">(a Statisztika fül időszak-választójával állítható)</span></div>' +
    '<div class="row2 w-ctrl"><span class="muted">Növények:</span> <span class="daypick" id="wTypes">' + Object.entries(WTYPES).map(([k, v]) => '<label><input type="checkbox" value="' + k + '"' + (w.types[k] !== false ? ' checked' : '') + '>' + v + '</label>').join('') + '</span>' +
    '<label class="switch small"><input type="checkbox" id="wEff"' + (w.eff ? ' checked' : '') + '><span class="slider"></span><span>hatékonysági veszteséggel (szórófej ~75%, csepegtető ~90%)</span></label></div>' +
    '<div class="row2 w-ctrl"><span class="muted">Vízdíj:</span> <div class="seg" id="wPriceMode"><button data-v="0">egy ár</button><button data-v="1">vízdíj + csatornadíj</button></div>' +
    (w.detailed ? '<label>vízdíj <input class="inp num" id="wPrice" inputmode="decimal" style="width:70px" value="' + w.price + '"> Ft/m³</label> <label>csatornadíj <input class="inp num" id="wSewer" inputmode="decimal" style="width:70px" value="' + w.sewer + '"> Ft/m³</label> <span class="muted small">(külön locsolómérőnél a csatornadíj 0)</span>'
      : '<label><input class="inp num" id="wSimple" inputmode="decimal" style="width:80px" value="' + w.simple + '"> Ft/m³</label>') + ' <span class="perr" id="wPErr"></span></div>' +
    '<h3 class="sub-h">Célállapot – legfeljebb ennyi kiszáradást engedj</h3><div class="w-targets">' +
    shown.map(({ p, r }) => { const t = Math.round(targetOf(p, r) * 100), raw = Math.round(baseP(p, r) * 100); return '<div class="w-t"><span>' + TYPE_ICON[p.type] + ' ' + esc(p.name) + '</span><input type="range" min="10" max="90" step="5" data-wt="' + p.id + '" value="' + t + '"><b>' + t + '%</b> <span class="muted small">' + (t < raw - 5 ? 'üde zöld' : t > raw + 10 ? 'takarékos, de stresszes' : 'a stresszhatár körül') + ' · stresszhatár ' + raw + '%</span></div>'; }).join('') + '</div>' +
    '<div class="today w-cards">' +
    '<div class="card mini"><h3>Vízigény</h3><div class="big">' + num(needM3, needM3 < 10 ? 2 : 1) + '<small> m³</small></div><div class="line small">' + costText(needM3) + '</div></div>' +
    '<div class="card mini"><h3>Tényleges öntözés</h3><div class="big">' + num(useM3, useM3 < 10 ? 2 : 1) + '<small> m³</small></div><div class="line small">' + costText(useM3) + '</div><div class="line small muted">a rögzített és a programozott locsolásból</div></div>' +
    '<div class="card mini"><h3>Legdrágább hónap</h3><div class="big">' + (topM ? MONTHS_FULL[+topM.slice(5) - 1] : '–') + '</div><div class="line small">' + (topM ? num(months[topM].need, 2) + ' m³ · ' + num(months[topM].need * wPrice(), 0) + ' Ft' : '') + '</div></div>' +
    '<div class="card mini"><h3>Csúcsnap</h3><div class="big">' + (peak >= 0 ? DS.fmt(DR.date[peak], { month: 'short', day: 'numeric' }) : '–') + '</div><div class="line small">' + (peak >= 0 ? fmtL(dayNeed[peak]) + ' egyszerre (a vödör visszatöltése)' : '') + '</div></div></div>' +
    '<p class="w-cmp">Ha mindenhol <select class="sel" id="wCmp">' + [10, 20, 30].map(x => '<option value="' + x + '"' + (x === w.cmp ? ' selected' : '') + '>' + x + '</option>').join('') + '</select> százalékponttal szárazabban tartod a növényeket, ebben az időszakban kb. <b>' + num(Math.max(0, needM3 - cmpM3), 1) + ' m³</b>-rel (≈ ' + num(Math.max(0, needM3 - cmpM3) * wPrice(), 0) + ' Ft) kevesebb víz kellett volna: ' + num(needM3, 1) + ' m³ helyett ' + num(cmpM3, 1) + ' m³.</p>' +
    '<div id="wBars" style="height:300px"></div><div id="wCal"></div>' +
    '<p class="hint">A vízigény: a talajvíz-mérleg a mért időjárással fut, és amikor egy növény kiszáradása eléri a célt, egy virtuális locsolás visszatölti a vödrét. Ezek összege literben (a terület × mm), m³-ben és forintban. A fák területe a lombkorona, a bokroké bokronként × darabszám.</p>';

  // vezérlők
  box.querySelectorAll('#wTypes input').forEach(c => c.addEventListener('change', () => { w.types[c.value] = c.checked; saveUi(); renderWaterUse(); }));
  $('wEff').addEventListener('change', e => { w.eff = e.target.checked; saveUi(); renderWaterUse(); });
  box.querySelectorAll('#wPriceMode button').forEach(b => { b.classList.toggle('on', (b.dataset.v === '1') === w.detailed); b.addEventListener('click', () => { w.detailed = b.dataset.v === '1'; saveUi(); renderWaterUse(); }); });
  const priceIn = (id, key) => { const el = $(id); if (!el) return; el.addEventListener('change', () => {
    const s = String(el.value).trim().replace(',', '.');
    if (!/^\d+(\.\d+)?$/.test(s) || +s > 100000) { el.classList.add('bad'); $('wPErr').textContent = '⚠ 0 és 100 000 Ft/m³ között'; return; }
    w[key] = +s; saveUi(); renderWaterUse();
  }); };
  priceIn('wPrice', 'price'); priceIn('wSewer', 'sewer'); priceIn('wSimple', 'simple');
  box.querySelectorAll('[data-wt]').forEach(s => {
    s.addEventListener('input', () => { s.nextElementSibling.textContent = s.value + '%'; });
    s.addEventListener('change', () => { w.targets[s.dataset.wt] = +s.value; saveUi(); renderWaterUse(); });
  });
  $('wCmp').addEventListener('change', e => { w.cmp = +e.target.value; saveUi(); renderWaterUse(); });

  // havi oszlopok: vízigény, tényleges öntözés, eső (a területre)
  const ink = cssVar('--ink'), muted = cssVar('--muted'), line = cssVar('--line'), card = cssVar('--card');
  if (wBars && !wBars.isDisposed()) wBars.dispose();
  wBars = echarts.init($('wBars'));
  wBars.setOption({
    animation: false, grid: { left: 52, right: 16, top: 34, bottom: 30 }, legend: { top: 0, textStyle: { color: muted } },
    tooltip: { trigger: 'axis', confine: true, backgroundColor: card, borderColor: line, textStyle: { color: ink }, valueFormatter: v => num(v, 2) + ' m³' },
    xAxis: { type: 'category', data: mk.map(m => MONTHS_HU[+m.slice(5) - 1] + ' ' + m.slice(2, 4)), axisLabel: { color: muted } },
    yAxis: { type: 'value', name: 'm³', nameTextStyle: { color: muted }, axisLabel: { color: muted }, splitLine: { lineStyle: { color: line, opacity: .5 } } },
    series: [
      { name: 'vízigény', type: 'bar', data: mk.map(m => +months[m].need.toFixed(3)), itemStyle: { color: '#f59e0b' } },
      { name: 'tényleges öntözés', type: 'bar', data: mk.map(m => +months[m].use.toFixed(3)), itemStyle: { color: '#16a34a' } },
      { name: 'eső (a területre)', type: 'bar', data: mk.map(m => +months[m].rain.toFixed(3)), itemStyle: { color: cssVar('--c-rain'), opacity: .6 } }
    ]
  }, true);

  // csempés naptár: napi vízigény literben
  const years = [...new Set(DR.date.filter(inR).map(s => s.slice(0, 4)))];
  const el = $('wCal'), W = el.clientWidth || 900, cs = Math.max(9, Math.min(20, Math.floor((W - 110) / 54)));
  el.style.height = (50 + years.length * (cs * 7 + 40)) + 'px';
  if (wCal && !wCal.isDisposed()) wCal.dispose();
  wCal = echarts.init(el);
  const data = DR.date.map((s, i) => inR(s) ? [s, Math.round(dayNeed[i])] : null).filter(Boolean);
  const mx = Math.max(10, ...data.map(d => d[1]));
  wCal.setOption({
    animation: false,
    title: { text: 'Napi vízigény (liter)', left: 4, top: 0, textStyle: { color: ink, fontSize: fs(13) } },
    tooltip: { confine: true, backgroundColor: card, borderColor: line, textStyle: { color: ink }, formatter: p => { const i = DR.idx[p.data[0]]; return DS.fmt(p.data[0], { year: 'numeric', month: 'short', day: 'numeric', weekday: 'short' }) + '<br>vízigény: <b>' + fmtL(p.data[1]) + '</b><br>öntözés: ' + fmtL(dayUse[i]) + ' · eső: ' + rtext(DR.rain[i], 1) + ' · ET₀: ' + rtext(DR.et0[i], 1); } },
    visualMap: { min: 0, max: mx, orient: 'horizontal', right: 10, top: 0, itemHeight: 140, textStyle: { color: muted }, inRange: { color: ['#f1f5f9', '#fde68a', '#f59e0b', '#c2410c'] }, calculable: false },
    calendar: years.map((y, k) => ({ range: y, top: 44 + k * (cs * 7 + 40), left: 60, cellSize: [cs, cs], splitLine: { show: false }, itemStyle: { color: cssVar('--empty'), borderColor: card, borderWidth: 2 },
      yearLabel: { position: 'left', color: ink }, dayLabel: { firstDay: 1, nameMap: DAY_NAMES, color: muted, fontSize: fs(9) }, monthLabel: { nameMap: MONTHS_HU, color: muted, fontSize: fs(10) } })),
    series: years.map((y, k) => ({ type: 'heatmap', coordinateSystem: 'calendar', calendarIndex: k, data: data.filter(d => d[0].startsWith(y)) }))
  }, true);
}

// al-fül váltó: Talajvíz | Víz
function applyDrSub() {
  const sub = UI.drSub === 'viz' ? 'viz' : 'merleg';
  document.querySelectorAll('#drSub button').forEach(b => b.classList.toggle('on', b.dataset.v === sub));
  document.querySelectorAll('[data-page="szarazsag"] .dr-merleg').forEach(el => el.classList.toggle('hidden', sub !== 'merleg'));
  $('waterPanel').classList.toggle('hidden', sub !== 'viz');
  if (sub === 'viz') renderWaterUse(); else if (drChart) { drChart.resize(); if (fwiChart) fwiChart.resize(); }
}
document.querySelectorAll('#drSub button').forEach(b => b.addEventListener('click', () => { UI.drSub = b.dataset.v; saveUi(); applyDrSub(); }));
window.addEventListener('resize', () => { if (currentTab === 'szarazsag' && UI.drSub === 'viz') { if (wBars) wBars.resize(); if (wCal) wCal.resize(); } });
