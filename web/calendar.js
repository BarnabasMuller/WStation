'use strict';
/* Naptár-hőtérkép (év- és hónapnézet) + közös napi adatok */

// ------------------------------------------------------------------ napi adatok (a naptár és a heti összesítő közös forrása)
let DAYS = {};             // clean (1/0) -> { date:[], start:[], ..., idx: {date -> i} }
async function getDays(clean) {
  const k = clean ? 1 : 0;
  if (DAYS[k]) return DAYS[k];
  const d = await api('/api/days?clean=' + k);
  d.idx = {};
  d.date.forEach((s, i) => d.idx[s] = i);
  DAYS[k] = d;
  return d;
}

// dátum-szöveg (ÉÉÉÉ-HH-NN) segédek, időzóna-függetlenül
const DS = {
  ms: s => typeof s === 'string' ? Date.UTC(+s.slice(0, 4), +s.slice(5, 7) - 1, +s.slice(8, 10)) : NaN,   // hiányzó nap: NaN (2.2.7.2)
  str: ms => new Date(ms).toISOString().slice(0, 10),
  add: (s, n) => DS.str(DS.ms(s) + n * D),
  dow: s => (new Date(DS.ms(s)).getUTCDay() + 6) % 7,          // 0 = hétfő
  today: () => F.ymd.format(Date.now()),
  fmt: (s, o) => { const t = DS.ms(s); return isFinite(t) ? new Date(t + 12 * H).toLocaleDateString(numLocale(), Object.assign({ timeZone: 'UTC' }, o)) : '–'; }
};

// egy nap megnyitása az idővonalon, ±1 nap környezettel
function gotoDay(dateStr) {
  const d = DAYS[1] || DAYS[0];
  const i = d ? d.idx[dateStr] : undefined;
  const start = i !== undefined ? d.start[i] : DS.ms(dateStr) - 2 * H;
  const end = i !== undefined ? d.end[i] : start + D;
  showTab('idovonal', true);
  const go = () => {
    if (!chart) return;
    currentRange = '';
    document.querySelectorAll('#ranges button').forEach(x => x.classList.remove('on'));
    chart.dispatchAction({ type: 'dataZoom', startValue: Math.max(META.first, start - D), endValue: Math.min(META.last + H, end + D) });
  };
  go();
  setTimeout(go, 60); // ha a grafikon most épült, a saját alapnézete után állítsuk be
}

// ------------------------------------------------------------------ mennyiségek és színskálák
const CAL_METRICS = [
  { k: 'tmax', q: 'temp', name: 'Max. hőmérséklet', unit: '°C', dec: 1, scale: 'temp' },
  { k: 'tmin', q: 'temp', name: 'Min. hőmérséklet', unit: '°C', dec: 1, scale: 'temp' },
  { k: 'tavg', q: 'temp', name: 'Átlaghőmérséklet', unit: '°C', dec: 1, scale: 'temp' },
  { k: 'range', q: 'tdiff', name: 'Napi hőingás', unit: '°C', dec: 1, scale: 'orange' },
  { k: 'dew', q: 'temp', name: 'Átl. harmatpont', unit: '°C', dec: 1, scale: 'temp' },
  { k: 'rain', q: 'rain', name: 'Csapadék', unit: 'mm', dec: 1, scale: 'rain' },
  { k: 'rate', q: 'rate', name: 'Max. csapadékintenzitás', unit: 'mm/h', dec: 0, scale: 'rain' },
  { k: 'sun', name: 'Napsütéses órák', unit: 'h', dec: 1, scale: 'sun' },
  { k: 'energy', name: 'Napenergia', unit: 'kWh/m²', dec: 2, scale: 'sun' },
  { k: 'gust', q: 'wind', name: 'Max. széllökés', unit: 'km/h', dec: 0, scale: 'wind' },
  { k: 'wind', q: 'wind', name: 'Átlagszél', unit: 'km/h', dec: 1, scale: 'wind' },
  { k: 'hum', name: 'Átl. páratartalom', unit: '%', dec: 0, scale: 'hum' },
  { k: 'pres', q: 'pres', name: 'Átl. légnyomás', unit: 'hPa', dec: 1, scale: 'pres' },
  { k: 'presChg', q: 'presd', name: 'Légnyomás-változás a nap során', unit: 'hPa', dec: 1, scale: 'presChg' },
  { k: 'uv', name: 'Max. UV-index', unit: '', dec: 0, scale: 'uv' },
  { k: 'vpd', name: 'Max. páranyomás-hiány (VPD)', unit: 'kPa', dec: 2, scale: 'orange' }
];
const TEMP_COLORS = ['#053061', '#2166ac', '#4393c3', '#92c5de', '#d1e5f0', '#f7f7f7', '#fddbc7', '#f4a582', '#d6604d', '#b2182b', '#67001f'];
const SEQ = {
  orange: ['#fff5eb', '#fdd0a2', '#fd8d3c', '#d94801', '#7f2704'],
  sun: ['#fffbea', '#ffe082', '#ffc107', '#ff8f00', '#e65100'],
  wind: ['#f7fcf5', '#a1d99b', '#41ab5d', '#6a51a3', '#3f007d'],
  hum: ['#fff7ec', '#c7e9b4', '#41b6c4', '#225ea8', '#081d58'],
  uv: ['#f7fcf5', '#fee391', '#fe9929', '#cc4c02', '#8c2d04']
};
const RAIN_PIECES = [
  { value: 0, label: '0', color: null },          // halványszürke, a témából
  { gt: 0, lt: 1, label: '< 1', color: '#deebf7' },
  { gte: 1, lt: 2.5, label: '1–2,5', color: '#c6dbef' },
  { gte: 2.5, lt: 5, label: '2,5–5', color: '#9ecae1' },
  { gte: 5, lt: 10, label: '5–10', color: '#6baed6' },
  { gte: 10, lt: 20, label: '10–20', color: '#3182bd' },
  { gte: 20, lt: 40, label: '20–40', color: '#08519c' },
  { gte: 40, label: '≥ 40', color: '#08306b' }
];
const RATE_PIECES = [
  { value: 0, label: '0', color: null },
  { gt: 0, lt: 2, label: '< 2', color: '#deebf7' },
  { gte: 2, lt: 10, label: '2–10', color: '#9ecae1' },
  { gte: 10, lt: 30, label: '10–30', color: '#4292c6' },
  { gte: 30, lt: 60, label: '30–60', color: '#08519c' },
  { gte: 60, label: '≥ 60', color: '#08306b' }
];

function visualMapFor(m, values, ink, muted) {
  const v = values.filter(x => x != null);
  const lo = v.length ? Math.min(...v) : 0, hi = v.length ? Math.max(...v) : 1;
  const base = {
    orient: 'horizontal', right: 16, top: 8, itemWidth: 12, itemHeight: 170, textGap: 6,
    textStyle: { color: muted, fontSize: fs(11) }, calculable: false
  };
  if (m.scale === 'rain' || (m.k === 'rate')) {
    const pieces = (m.k === 'rate' ? RATE_PIECES : RAIN_PIECES).map(p => Object.assign({}, p, { color: p.color || cssVar('--zero'), label: pieceLabel(p, m.q) }));
    return Object.assign(base, { type: 'piecewise', pieces, itemWidth: 16, itemHeight: 12, itemGap: 6, textGap: 4 });
  }
  if (m.scale === 'fwi')
    return Object.assign(base, { type: 'continuous', min: 0, max: Math.max(1, hi), inRange: { color: ['#2e7d32', '#7cb342', '#fdd835', '#fb8c00', '#e53935', '#6a1b9a'] }, text: [num(hi, 0), '0'] });
  if (m.scale === 'drought')
    return Object.assign(base, { type: 'continuous', min: 0, max: 100, inRange: { color: ['#1a9850', '#91cf60', '#d9ef8b', '#fee08b', '#fc8d59', '#d73027', '#a50026'] }, text: ['100%', '0%'] });
  if (m.scale === 'temp' || m.scale === 'pres' || m.scale === 'presChg') {
    // divergáló: a középpont fehér (hőmérséklet 15 °C, nyomás 1013 hPa, változás 0)
    const c = m.scale === 'temp' ? (UI.calTempCenter != null ? UI.calTempCenter : 15) : m.scale === 'pres' ? 1013 : 0;
    const r = Math.max(Math.abs(lo - c), Math.abs(hi - c), m.scale === 'temp' ? 5 : 3);
    const colors = m.scale === 'temp' ? TEMP_COLORS : ['#762a83', '#af8dc3', '#e7d4e8', '#f7f7f7', '#d9f0d3', '#7fbf7b', '#1b7837'];
    const mn = +(c - r).toFixed(1), mx = +(c + r).toFixed(1);
    return Object.assign(base, { type: 'continuous', min: mn, max: mx, inRange: { color: colors }, text: [num(qv(m.q, mx), qd(m.q, 1)), num(qv(m.q, mn), qd(m.q, 1))] });
  }
  const mn = Math.min(0, lo), mx = hi > 0 ? hi : 1;
  return Object.assign(base, { type: 'continuous', min: mn, max: mx, inRange: { color: SEQ[m.scale] }, text: [num(qv(m.q, mx), qd(m.q, m.dec > 1 ? 1 : 0)), num(qv(m.q, mn), 0)] });
}
// a csapadék-sávok felirata a választott mértékegységben (a sávhatárok mm-ben maradnak)
function pieceLabel(p, q) {
  if (units().rain === 'mm' || p.value === 0) return p.label;
  const f = x => num(qv(q, x), x < 2.5 ? 2 : 1);
  if (p.gte !== undefined && p.lt !== undefined) return f(p.gte) + '–' + f(p.lt);
  if (p.lt !== undefined) return '< ' + f(p.lt);
  if (p.gte !== undefined) return '≥ ' + f(p.gte);
  return p.label;
}

// ------------------------------------------------------------------ rajzolás
let calChart = null, calMonth = null;
const MONTHS_HU = ['jan.', 'febr.', 'márc.', 'ápr.', 'máj.', 'jún.', 'júl.', 'aug.', 'szept.', 'okt.', 'nov.', 'dec.'];
const MONTHS_FULL = ['január', 'február', 'március', 'április', 'május', 'június', 'július', 'augusztus', 'szeptember', 'október', 'november', 'december'];
const DAY_NAMES = ['V', 'H', 'K', 'Sze', 'Cs', 'P', 'Szo'];   // az ECharts vasárnappal kezd

// a szárazság-modul mutatói (párolgás, kiszáradás profilonként) is választhatók
function allMetrics() {
  const extra = [];
  if (typeof DR !== 'undefined' && DR) {
    extra.push({ k: 'et0', q: 'rain', name: 'Referencia-párolgás (ET₀)', unit: 'mm', dec: 1, scale: 'sun', src: 'dr', drKey: 'et0' });
    extra.push({ k: 'fwi', name: 'Tűzveszély (FWI)', unit: '', dec: 0, scale: 'fwi', src: 'dr', drKey: 'fwi' });
    extra.push({ k: 'ffmc', name: 'Fű, avar szárazsága (FFMC)', unit: '', dec: 0, scale: 'fwi', src: 'dr', drKey: 'ffmc' });
    extra.push({ k: 'dmc', name: 'Felső talaj szárazsága (DMC)', unit: '', dec: 0, scale: 'fwi', src: 'dr', drKey: 'dmc' });
    extra.push({ k: 'dc', name: 'Mély talaj szárazsága (DC)', unit: '', dec: 0, scale: 'fwi', src: 'dr', drKey: 'dc' });
    extra.push({ k: 'gddCum', q: 'gdd', name: 'Hőösszeg (GDD, halmozott)', unit: '°C·nap', dec: 0, scale: 'orange', src: 'dr', drKey: 'gddCum' });
    DR.config.profiles.filter(p => p.enabled).forEach(p => extra.push({ k: 'dr:' + p.id, name: 'Kiszáradás – ' + p.name, unit: '%', dec: 0, scale: 'drought', src: 'dr' }));
  }
  return CAL_METRICS.concat(extra);
}
function fillMetricSelect() {
  const sel = $('calMetric');
  sel.innerHTML = allMetrics().map(m => '<option value="' + m.k + '">' + esc(m.name) + '</option>').join('');
}
function refreshCalendarMetrics() {
  const cur = $('calMetric').value;
  fillMetricSelect();
  const ok = allMetrics().some(m => m.k === cur);
  $('calMetric').value = ok ? cur : 'tmax';
  if (currentTab === 'naptar') renderCalendar();
}

function calState() {
  UI.cal = UI.cal || {};
  const c = UI.cal;
  if (!c.view) c.view = 'year';
  if (!allMetrics().find(m => m.k === c.metric)) c.metric = 'tmax';
  if (c.clean === undefined) c.clean = META ? META.settings.excludeSuspect : true;
  return c;
}

function initCalendarControls() {
  const sel = $('calMetric');
  sel.innerHTML = CAL_METRICS.map(m => '<option value="' + m.k + '">' + esc(m.name) + '</option>').join('');
  sel.addEventListener('change', () => { calState().metric = sel.value; saveUi(); renderCalendar(); });
  document.querySelectorAll('#calView button').forEach(b => b.addEventListener('click', () => { calState().view = b.dataset.v; saveUi(); renderCalendar(); }));
  $('calPrev').addEventListener('click', () => shiftMonth(-1));
  $('calNext').addEventListener('click', () => shiftMonth(1));
  $('calClean').addEventListener('change', e => { calState().clean = e.target.checked; saveUi(); renderCalendar(); });
  $('calFixed').addEventListener('change', e => { calState().fixedScale = e.target.checked; saveUi(); renderCalendar(); });
  document.addEventListener('keydown', e => {
    if (currentTab !== 'naptar' || calState().view !== 'month' || e.target.tagName === 'SELECT' || e.target.tagName === 'INPUT') return;
    if (e.key === 'ArrowLeft') shiftMonth(-1);
    if (e.key === 'ArrowRight') shiftMonth(1);
  });
}

function shiftMonth(n) {
  const d = DAYS[calState().clean ? 1 : 0];
  if (!d || !calMonth) return;
  let [y, m] = calMonth.split('-').map(Number);
  m += n; if (m < 1) { m = 12; y--; } if (m > 12) { m = 1; y++; }
  const next = y + '-' + String(m).padStart(2, '0');
  if (next < d.date[0].slice(0, 7) || next > d.date[d.date.length - 1].slice(0, 7)) return;
  calMonth = next;
  calState().month = next; saveUi();
  renderCalendar();
}

async function renderCalendar() {
  if (currentTab !== 'naptar' || !META) return;
  if (!DR) { try { await getDrought(); } catch (e) { } }
  if (typeof getDiary === 'function') { try { await getDiary(); } catch (e) { } }
  if ($('calMetric').options.length !== allMetrics().length) fillMetricSelect();
  const c = calState();
  const d = await getDays(c.clean);
  const m = allMetrics().find(x => x.k === c.metric) || CAL_METRICS[0];
  if (m.src === 'dr') {
    // a szárazság-modul napi értékei a naptár napjaihoz igazítva
    const r = m.drKey ? null : DR.profiles.find(p => 'dr:' + p.id === m.k);
    d[m.k] = d.date.map(s => { const j = DR.idx[s]; if (j === undefined) return null; return r ? (r.depl[j] == null ? null : +(r.depl[j] * 100).toFixed(1)) : DR[m.drKey][j]; });
  }
  $('calMetric').value = m.k;
  $('calClean').checked = c.clean;
  $('calFixed').checked = !!c.fixedScale;
  document.querySelectorAll('#calView button').forEach(b => b.classList.toggle('on', b.dataset.v === c.view));
  $('calNav').classList.toggle('hidden', c.view !== 'month');
  if (!calMonth) calMonth = c.month && d.date.some(s => s.startsWith(c.month)) ? c.month : d.date[d.date.length - 1].slice(0, 7);

  const ink = cssVar('--ink'), muted = cssVar('--muted'), line = cssVar('--line'), card = cssVar('--card');
  const el = $('cal');
  const years = [...new Set(d.date.map(s => +s.slice(0, 4)))];
  const W = (el.parentElement && el.parentElement.clientWidth) || el.clientWidth || 1000;   // a szülő szélessége (a naptár maga szélesebb is lehet)
  let option;

  const item = (i, label) => {
    const v = d[m.k][i];
    const o = { value: [d.date[i], v], i };
    if (d.cov[i] < 0.8) o.itemStyle = { decal: { symbol: 'rect', symbolSize: 1, dashArrayX: [1, 0], dashArrayY: [2, 4], rotation: -Math.PI / 4, color: 'rgba(0,0,0,.28)' } };
    if (label) o.label = label;
    return o;
  };

  if (c.view === 'year') {
    // a cellák és a feliratközök a betűmérettel nőnek (6.32: 18 px-nél egymásra csúsztak)
    const left0 = 40 + fs(14) * 2 + fs(10) * 2;
    const cs = Math.max(11, Math.min(26, Math.floor((W - left0 - 30) / 54)));
    // ha így sem fér ki, a naptár a saját dobozában görgethető (az oldal nem)
    el.style.minWidth = (left0 + cs * 54 + 30) + 'px'; el.parentElement.style.overflowX = 'auto';
    const h = cs * 7, gap = fs(11) * 2 + 26;
    const top0 = 40 + fs(14) + fs(11) * 2;
    el.style.height = (top0 + years.length * (h + gap) + 10) + 'px';
    const calendars = years.map((y, k) => ({
      range: String(y), top: top0 + k * (h + gap), left: left0, cellSize: [cs, cs], orient: 'horizontal',
      splitLine: { show: true, lineStyle: { color: muted, width: 1, opacity: .45 } },
      itemStyle: { color: cssVar('--empty'), borderColor: card, borderWidth: 2 },
      yearLabel: { show: true, position: 'left', margin: fs(10) * 2 + 16, color: ink, fontSize: fs(14), fontWeight: 600 },
      dayLabel: { firstDay: 1, nameMap: DAY_NAMES, color: muted, fontSize: fs(10), margin: 6 },
      monthLabel: { nameMap: MONTHS_HU, color: muted, fontSize: fs(11), margin: 6 }
    }));
    const series = years.map((y, k) => ({
      type: 'heatmap', coordinateSystem: 'calendar', calendarIndex: k,
      data: d.date.map((s, i) => s.startsWith(String(y)) && d[m.k][i] != null ? item(i) : null).filter(Boolean)
    }));
    option = { calendar: calendars, series };
  } else {
    el.style.minWidth = '';
    // hónapnézet: sorok = hetek, oszlopok = a hét napjai; a cellában a nap és az érték
    const cw = Math.max(54, Math.min(150, Math.floor((W - 40) / 7)));
    const chh = Math.max(46, Math.min(84, Math.round(cw * 0.62)));
    // annyi sor, ahány hétre a hónap ténylegesen kiterjed (6.17: alul nem marad üres hely)
    const y0 = +calMonth.slice(0, 4), m0 = +calMonth.slice(5, 7), dim = new Date(Date.UTC(y0, m0, 0)).getUTCDate();
    const off = (new Date(Date.UTC(y0, m0 - 1, 1)).getUTCDay() + 6) % 7, weeks = Math.ceil((off + dim) / 7);
    el.style.height = (70 + weeks * chh + 16) + 'px';
    $('calTitle').textContent = calMonth.slice(0, 4) + '. ' + MONTHS_FULL[+calMonth.slice(5, 7) - 1];
    const label = {
      show: true, formatter: p => {
        const day = +p.data.value[0].slice(8, 10), v = p.data.value[1];
        const note = typeof diaryFor === 'function' && diaryFor(p.data.value[0]).length ? ' 📝' : '';
        return '{d|' + day + note + '}\n{v|' + (v == null ? '' : num(qv(m.q, v), qd(m.q, m.dec)) + (m.q === 'temp' || m.q === 'tdiff' ? '°' : m.unit === '%' ? '%' : m.q === 'rain' ? ' ' + qu(m.q) : m.unit === 'h' ? ' h' : '')) + '}';
      },
      rich: {
        d: { fontSize: fs(11), align: 'center', lineHeight: 16 },
        v: { fontSize: Math.max(11, Math.min(16, Math.round(cw / 7))), fontWeight: 600, lineHeight: 20 }
      }
    };
    const data = d.date.map((s, i) => s.startsWith(calMonth) ? item(i, label) : null).filter(Boolean);
    option = {
      calendar: [{
        range: calMonth, orient: 'vertical', top: 70, left: 'center', cellSize: [cw, chh],
        splitLine: { show: false },
        itemStyle: { color: cssVar('--empty'), borderColor: card, borderWidth: 3 },
        yearLabel: { show: false },
        dayLabel: { firstDay: 1, nameMap: ['Vasárnap', 'Hétfő', 'Kedd', 'Szerda', 'Csütörtök', 'Péntek', 'Szombat'].map(x => cw < 90 ? x.slice(0, 3) : x), color: muted, fontSize: fs(12), margin: 8 },
        monthLabel: { show: false }
      }],
      series: [{ type: 'heatmap', coordinateSystem: 'calendar', data },
        // a mérés nélküli (pl. jövőbeli) napokon is látszik a napszám (6.17)
        { type: 'scatter', coordinateSystem: 'calendar', symbolSize: 0, silent: true, data: [...Array(dim).keys()].map(k => calMonth + '-' + String(k + 1).padStart(2, '0')).filter(s => !data.some(o => o.value[0] === s)).map(s => [s, 0]),
          label: { show: true, formatter: p => String(+p.value[0].slice(8, 10)), color: muted, fontSize: fs(11), position: 'inside' } }]
    };
  }

  // állandó skála: az egész mért időszak értékeiből, így a hónapok összevethetők (6.17)
  const values = d.date.map((s, i) => (c.view === 'year' || c.fixedScale || s.startsWith(calMonth)) ? d[m.k][i] : null);
  option.visualMap = Object.assign(visualMapFor(m, values, ink, muted), { seriesIndex: option.series.map((s, i) => i).filter(i => option.series[i].type === 'heatmap') });
  if (c.view === 'month') {
    // a cella szövegének színe a cella színéhez igazodik (sötét cellán fehér, világoson sötét)
    option.series[0].data.forEach(o => {
      const bg = vmColor(option.visualMap, o.value[1]);
      const dark = bg && luminance(bg) < 0.5;
      const col = o.value[1] == null ? muted : dark ? '#ffffff' : '#1b1f27';
      o.label = Object.assign({}, o.label, { rich: { d: Object.assign({}, o.label.rich.d, { color: col, opacity: .8 }), v: Object.assign({}, o.label.rich.v, { color: col }) } });
    });
  }
  option.title = { text: m.name + (qu(m.q, m.unit) ? ' (' + qu(m.q, m.unit) + ')' : ''), left: 8, top: 8, textStyle: { color: ink, fontSize: fs(14), fontWeight: 600 } };
  option.tooltip = {
    confine: true, backgroundColor: card, borderColor: line, textStyle: { color: ink, fontSize: fs(12.5) },
    formatter: p => calTooltip(d, p.data.i, m)
  };
  option.animation = false;

  if (calChart) calChart.dispose();
  calChart = echarts.init(el);
  calChart.setOption(option);
  calChart.on('click', p => { if (p.data && p.data.value) gotoDay(p.data.value[0]); });
  // a vászon nem érhető el billentyűzettel: a hónap napjai rejtett gombként is (7.3) – Tabbal bejárhatók, a képernyőolvasó felolvassa
  let a11y = $('calA11y');
  if (!a11y) { a11y = document.createElement('div'); a11y.id = 'calA11y'; a11y.className = 'sr-only'; el.after(a11y); }
  a11y.innerHTML = c.view === 'month' ? '<ul>' + d.date.map((s, i) => s.startsWith(calMonth) ? '<li><button type="button" data-day="' + s + '">' + esc(DS.fmt(s, { month: 'long', day: 'numeric', weekday: 'long' }) + ': ' + (d[m.k][i] == null ? 'nincs adat' : num(qv(m.q, d[m.k][i]), qd(m.q, m.dec)) + ' ' + qu(m.q, m.unit))) + '</button></li>' : '').join('') + '</ul>' : '';
  a11y.querySelectorAll('button').forEach(b => b.addEventListener('click', () => gotoDay(b.dataset.day)));
  if (typeof renderDiary === 'function') renderDiary();
}

// a visualMap által adott szín egy értékhez (a cellaszöveg színének kiválasztásához)
function vmColor(vm, v) {
  if (v == null) return null;
  if (vm.type === 'piecewise') {
    const p = vm.pieces.find(p => (p.value !== undefined ? v === p.value : true) && (p.gt === undefined || v > p.gt) && (p.gte === undefined || v >= p.gte) && (p.lt === undefined || v < p.lt));
    return p ? p.color : null;
  }
  const cols = vm.inRange.color;
  const t = Math.max(0, Math.min(1, (v - vm.min) / ((vm.max - vm.min) || 1))) * (cols.length - 1);
  const k = Math.min(cols.length - 2, Math.floor(t)), f = t - k;
  const a = hexRgb(cols[k]), b = hexRgb(cols[k + 1]);
  return 'rgb(' + a.map((x, j) => Math.round(x + (b[j] - x) * f)).join(',') + ')';
}
function hexRgb(c) {
  if (c.startsWith('rgb')) return c.match(/\d+/g).slice(0, 3).map(Number);
  c = c.replace('#', '');
  if (c.length === 3) c = c.split('').map(x => x + x).join('');
  return [0, 2, 4].map(i => parseInt(c.substr(i, 2), 16));
}
function luminance(c) {
  const [r, g, b] = hexRgb(c).map(x => { x /= 255; return x <= 0.03928 ? x / 12.92 : Math.pow((x + 0.055) / 1.055, 2.4); });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b > 0.18 ? 1 : 0;   // 1 = világos háttér
}

function calTooltip(d, i, m) {
  if (i == null) return '';
  const s = d.date[i];
  let h = '<div style="font-weight:600;margin-bottom:4px">' + DS.fmt(s, { year: 'numeric', month: 'long', day: 'numeric', weekday: 'long' }) + '</div>';
  h += m.name + ': <b>' + qtext(m.q, d[m.k][i], m.dec, m.unit) + '</b>';
  if (m.q === 'wind') h += ' (' + [windUnit() !== 'kn' ? num(d[m.k][i] / 1.852, 1) + ' csomó' : null, windUnit() !== 'bft' ? num(beaufortF(d[m.k][i]), 1) + ' Bft' : null].filter(Boolean).join(', ') + ')';
  h += '<br><span style="color:' + cssVar('--muted') + '">' +
    num(tconv(d.tmin[i])) + ' / ' + ttext(d.tmax[i]) + ' · ' + rtext(d.rain[i], 1) + ' · ' + num(d.sun[i], 1) + ' h nap · lökés ' + wtext(d.gust[i]) + '</span>';
  if (d.cov[i] < 0.8) h += '<br><span style="color:' + cssVar('--warn-ink') + '">⚠ hiányos adat: ' + d.n[i] + ' mérés (' + num(d.cov[i] * 100, 0) + '%)</span>';
  if (typeof diaryFor === 'function') diaryFor(s).forEach(x => { h += '<br>' + (DIARY_TAGS[x.tag] || '📝') + ' <b>' + esc(x.tag || '') + '</b>' + (x.text ? ': ' + ut(x.text) : ''); });
  h += '<br><span style="color:' + cssVar('--muted') + ';font-size:11.5px">Kattints: megnyitás az idővonalon</span>';
  return h;
}

window.addEventListener('resize', () => { if (calChart && currentTab === 'naptar') { clearTimeout(window._calRs); window._calRs = setTimeout(renderCalendar, 150); } });
initCalendarControls();
