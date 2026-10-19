'use strict';
/* Figyelő (fagy, hőség, nyomás, szél, eső, komfort, szárazság, Zambretti), égbolt (nap, hold), „Ezen a napon”, jelölők az idővonalon */

// ------------------------------------------------------------------ időszak megnyitása az idővonalon
function gotoRange(a, b, pad) {
  pad = pad == null ? 2 * H : pad;
  showTab('idovonal', true);
  const go = () => {
    if (!chart) return;
    currentRange = '';
    document.querySelectorAll('#ranges button').forEach(x => x.classList.remove('on'));
    chart.dispatchAction({ type: 'dataZoom', startValue: Math.max(META.first, a - pad), endValue: Math.min(META.last + H, b + pad) });
  };
  go(); setTimeout(go, 60);
}

// ------------------------------------------------------------------ nap és hold (internet nélkül, csillagászati közelítéssel)
const RAD = Math.PI / 180;
function sunTimes(dateStr, lat, lon) {
  const jdNoon = DS.ms(dateStr) / 86400000 + 2440587.5 + 0.5;
  const n = Math.round(jdNoon - 2451545.0 + 0.0008);
  const js = n - lon / 360;
  const M = (357.5291 + 0.98560028 * js) % 360;
  const C = 1.9148 * Math.sin(M * RAD) + 0.02 * Math.sin(2 * M * RAD) + 0.0003 * Math.sin(3 * M * RAD);
  const lam = (M + C + 180 + 102.9372) % 360;
  const jt = 2451545 + js + 0.0053 * Math.sin(M * RAD) - 0.0069 * Math.sin(2 * lam * RAD);
  const dec = Math.asin(Math.sin(lam * RAD) * Math.sin(23.4397 * RAD));
  const cw = (Math.sin(-0.833 * RAD) - Math.sin(lat * RAD) * Math.sin(dec)) / (Math.cos(lat * RAD) * Math.cos(dec));
  const toMs = j => (j - 2440587.5) * 86400000;
  if (cw > 1) return { polarNight: true };
  if (cw < -1) return { polarDay: true };
  const w = Math.acos(cw) / RAD;
  return { rise: toMs(jt - w / 360), set: toMs(jt + w / 360), noon: toMs(jt), len: 2 * w / 15 };
}
function moonPhase(ms) {
  const P = 29.530588853;
  const age = (((ms / 86400000 + 2440587.5) - 2451550.1) % P + P) % P;
  const illum = (1 - Math.cos(2 * Math.PI * age / P)) / 2;
  const k = Math.floor((age / P) * 8 + 0.5) % 8;
  const names = ['újhold', 'növő sarló', 'első negyed', 'növő hold', 'telihold', 'fogyó hold', 'utolsó negyed', 'fogyó sarló'];
  const icons = ['🌑', '🌒', '🌓', '🌔', '🌕', '🌖', '🌗', '🌘'];
  const nextFull = ms + ((P / 2 - age + P) % P) * 86400000;
  const nextNew = ms + ((P - age) % P) * 86400000;
  return { age, illum, name: names[k], icon: icons[k], nextFull, nextNew };
}
function hm(h) { const m = Math.round(h * 60); return Math.floor(m / 60) + ' ó ' + String(m % 60).padStart(2, '0') + ' p'; }

// ------------------------------------------------------------------ Zambretti (Negretti & Zambra), magyarul
const ZAMB = {
  A: 'Tartósan szép idő', B: 'Szép idő', C: 'Javuló, szépülő idő', D: 'Szép, de később kevésbé állandó', E: 'Szép, záporok lehetségesek',
  F: 'Elég szép, javuló', G: 'Elég szép, eleinte zápor lehet', H: 'Elég szép, később zápor', I: 'Eleinte záporos, majd javuló', J: 'Változékony, javuló',
  K: 'Elég szép, zápor valószínű', L: 'Inkább változékony, később kiderül', M: 'Változékony, valószínűleg javul', N: 'Záporos, napos időszakokkal',
  O: 'Záporos, egyre változékonyabb', P: 'Változékony, kevés eső', Q: 'Változékony, rövid szép időszakokkal', R: 'Változékony, később eső',
  S: 'Változékony, időnként eső', T: 'Nagyon változékony', U: 'Időnként eső, romló idő', V: 'Időnként eső, nagyon változékony',
  W: 'Gyakori eső', X: 'Esős, nagyon változékony', Y: 'Viharos, később javulhat', Z: 'Viharos, sok eső'
};
function zambretti(p, trend3h, dir, month) {
  if (p == null || isNaN(p)) return null;
  let kind = trend3h == null || isNaN(trend3h) || Math.abs(trend3h) < 1.6 ? 'steady' : trend3h > 0 ? 'rising' : 'falling';
  const groups = {
    falling: { z: 127 - 0.12 * p, lo: 1, letters: 'ABDHORUXZ' },
    steady: { z: 144 - 0.13 * p, lo: 10, letters: 'ABEKNPSWXZ' },
    rising: { z: 185 - 0.16 * p, lo: 20, letters: 'ABCFGIJLMQTYZ' }
  };
  const g = groups[kind];
  let idx = Math.round(g.z) - g.lo;
  // szélirány: déli szél egy fokkal rosszabb, északi egy fokkal jobb (északi féltekén)
  if (dir != null && !isNaN(dir)) { const d = ((dir % 360) + 360) % 360; if (d >= 135 && d <= 225) idx++; else if (d >= 315 || d <= 45) idx--; }
  // évszak: télen az emelkedő nyomás kevésbé jelent javulást, nyáron a csökkenő kevésbé romlást
  if (kind === 'rising' && (month <= 2 || month >= 11)) idx++;
  if (kind === 'falling' && month >= 5 && month <= 8) idx--;
  idx = Math.max(0, Math.min(g.letters.length - 1, idx));
  const L = g.letters[idx];
  return { letter: L, text: ZAMB[L], kind };
}

// ------------------------------------------------------------------ komfort
function comfort(dew) {
  if (dew == null || isNaN(dew)) return null;
  if (dew < 10) return ['száraz, kellemes', 'ok'];
  if (dew < 16) return ['kellemes', 'ok'];
  if (dew < 18) return ['kissé párás', 'mild'];
  if (dew < 21) return ['fülledt', 'strong'];
  return ['nagyon fülledt', 'severe'];
}

// ------------------------------------------------------------------ figyelő és égbolt kártyák az Áttekintésen
async function renderInsights() {
  if (!TODAY || !META) return;
  const n = TODAY.now || {}, t = TODAY.today || {};
  const lat = META.lat || 47.46, lon = META.lon || 18.96;
  const now = Date.now(), lastT = n.t || META.last;
  const hour = +F.hour.format(lastT).slice(0, 2), month = +F.ymd.format(lastT).slice(5, 7);
  const isToday = t.date === DS.today();
  const lines = [];
  const L = (icon, text, cls) => lines.push('<li class="' + (cls || '') + '"><span class="wi">' + icon + '</span><span>' + text + '</span></li>');

  // napsütés aránya ma (a derültség becsléséhez)
  const sun = sunTimes(t.date || DS.today(), lat, lon);
  const d = await getDays(true).catch(() => null);
  const ti = d && t.date ? d.idx[t.date] : undefined;
  const sunH = ti !== undefined ? d.sun[ti] : null;
  const sunFrac = sunH != null && sun.len ? sunH / Math.max(1, sun.len) : null;

  // fagy: szeptember–május, délután 14 órától reggel 8-ig
  if (isToday && (month >= 9 || month <= 5) && (hour >= 14 || hour < 8) && n.temp != null) {
    const clear = sunFrac != null && sunFrac > 0.5, calm = n.wind != null && n.wind < 5;
    const pred = Math.min(n.temp, n.dew != null ? n.dew : n.temp) - (clear && calm ? 2 : clear || calm ? 1 : 0);
    const why = 'harmatpont ' + ttext(n.dew) + (clear ? ', derült' : '') + (calm ? ', szélcsendes' : '');
    if (pred <= 0) L('❄️', '<b>Ma éjjel fagy valószínű</b>: ' + why + '. Védd a fagyérzékeny növényeket!', 'strong');
    else if (pred <= 2) L('🥶', '<b>Ma éjjel fagy közeli</b> lehet: ' + why + '.', 'mild');
  }
  // hőség (a magyar hőségriasztási fokozatok logikája szerint, a napi középhőmérsékletből)
  const means = (TODAY.now.tavgPrev || []).slice().reverse().concat(t.tavg != null ? [t.tavg] : []);
  const last3 = means.slice(-3);
  if (last3.length >= 3 && last3.every(x => x >= 27)) L('🔥', '<b>Hőség</b>: a napi középhőmérséklet 3 napja legalább ' + ttext(27, 0) + ' (a hőségriasztás III. fokozatának megfelelő).', 'severe');
  else if (last3.length >= 3 && last3.every(x => x >= 25)) L('🌡️', '<b>Hőség</b>: a napi középhőmérséklet 3 napja legalább ' + ttext(25, 0) + ' (II. fokozat).', 'strong');
  else if (t.tavg != null && t.tavg >= 25) L('🌡️', '<b>Meleg nap</b>: a napi középhőmérséklet ' + ttext(t.tavg) + ' (I. fokozat).', 'mild');
  // nyomás
  const p3 = n.pres3h;
  if (p3 != null && p3 <= -3) L('📉', '<b>Gyorsan esik a légnyomás</b>: ' + psigned(p3) + ' / 3 óra – front közeledhet.', p3 <= -5 ? 'strong' : 'mild');
  else if (p3 != null && p3 >= 3) L('📈', '<b>Gyorsan emelkedik a légnyomás</b>: ' + psigned(p3) + ' / 3 óra – javuló idő.', 'ok');
  // szél, eső
  if (TODAY.now.gust1h != null && TODAY.now.gust1h >= 50) L('💨', '<b>Erős széllökések</b>: ' + wtext(TODAY.now.gust1h) + ' az utolsó órában.', 'strong');
  if (TODAY.now.rate1h != null && TODAY.now.rate1h >= 20) L('🌧️', '<b>Heves eső</b>: ' + rrtext(TODAY.now.rate1h, 0) + ' az utolsó órában.', 'strong');
  // komfort
  const cf = comfort(n.dew);
  if (cf) L('💧', 'Komfort: <b class="tag ' + cf[1] + '">' + cf[0] + '</b> (harmatpont ' + ttext(n.dew) + (n.hum != null ? ', ' + num(n.hum, 0) + ' %' : '') + ')', '');
  // szárazság (ha a szárazság-modul már betöltött)
  if (typeof DR !== 'undefined' && DR) {
    const lawn = DR.config.profiles.find(p => p.type === 'gyep' && p.enabled);
    const r = lawn && DR.profiles.find(x => x.id === lawn.id);
    if (r) {
      const ks = r.ks[r.ks.length - 1], dr = r.dr[r.dr.length - 1], raw = r.raw[r.raw.length - 1];
      if (ks < 0.7) L('🌱', '<b>Erős szárazság</b>: a gyep vízhiányos (' + rtext(dr, 0) + ' hiányzik) – <a href="#" data-goto="szarazsag">locsolási javaslat</a>.', 'strong');
      else if (dr >= raw) L('🌱', 'A gyep a stresszhatáron van – <a href="#" data-goto="szarazsag">locsolási javaslat</a>.', 'mild');
    }
    const fwiNow = DR.fwi && DR.fwi[DR.fwi.length - 1];
    if (fwiNow != null && fwiNow >= 21.3) L('🔥', '<b>Magas tűzveszély</b>: FWI ' + num(fwiNow, 0) + ' – ne gyújts tüzet a kertben.', fwiNow >= 38 ? 'severe' : 'strong');
  } else if (typeof getDrought === 'function') {
    getDrought().then(() => renderInsights()).catch(() => { });
  }
  // Zambretti
  const z = zambretti(n.pres, p3, n.dir, month);
  if (z) L('🌤️', 'Előrejelzés (Zambretti, a légnyomás alapján): <b>' + z.text + '</b> <span class="muted">– hagyományos becslés a következő 12 órára</span>', '');

  // égbolt
  const ys = sunTimes(DS.add(t.date || DS.today(), -1), lat, lon);
  const mp = moonPhase(now);
  const sky = sun.rise ? 'Napkelte <b>' + F.time.format(sun.rise) + '</b> · napnyugta <b>' + F.time.format(sun.set) + '</b><br>Nappal: <b>' + hm(sun.len) + '</b> (' + (sun.len >= ys.len ? '+' : '−') + Math.round(Math.abs(sun.len - ys.len) * 60) + ' perc tegnaphoz képest)' : '';

  let card = document.getElementById('watchCard');
  if (!card) {
    card = document.createElement('div');
    card.id = 'watchCard';
    card.className = 'card watch';
    // a „Ma” kártya (és a szél) alá, teljes szélességben
    const kids = $('today').children;
    $('today').insertBefore(card, kids[2] || null);
  }
  card.innerHTML = '<h3>Figyelő' + (isToday ? '' : ' <span class="muted" style="text-transform:none;letter-spacing:0">(az utolsó adat alapján)</span>') + '</h3><ul class="watch-list">' + lines.join('') + '</ul>';
  card.querySelectorAll('[data-goto]').forEach(a => a.addEventListener('click', e => { e.preventDefault(); showTab(a.dataset.goto, true); }));

  let skyCard = document.getElementById('skyCard');
  if (!skyCard) { skyCard = document.createElement('div'); skyCard.id = 'skyCard'; skyCard.className = 'card'; $('today').appendChild(skyCard); }
  skyCard.innerHTML = '<h3>Égbolt</h3><div class="moon">' + mp.icon + '</div>' +
    '<div class="line">' + sky + '</div>' +
    '<div class="line">Hold: <b>' + mp.name + '</b>, ' + num(mp.illum * 100, 0) + '% megvilágított</div>' +
    '<div class="line small">Telihold: ' + F.day.format(mp.nextFull) + ' · újhold: ' + F.day.format(mp.nextNew) + '</div>';

  renderOnThisDay(d);
}

// „Ezen a napon” több év adatánál; egy évnél „A hónap legjei eddig”
function renderOnThisDay(d) {
  if (!d) return;
  let card = document.getElementById('otdCard');
  if (!card) { card = document.createElement('div'); card.id = 'otdCard'; card.className = 'card'; $('today').appendChild(card); }
  const today = DS.today(), md = today.slice(5);
  const years = [...new Set(d.date.map(s => s.slice(0, 4)))];
  const prev = d.date.map((s, i) => [s, i]).filter(([s]) => s.slice(5) === md && s !== today);
  if (years.length >= 2 && prev.length) {
    card.innerHTML = '<h3>Ezen a napon</h3>' + prev.reverse().map(([s, i]) => '<div class="line"><b>' + s.slice(0, 4) + '</b>: ' + num(tconv(d.tmin[i])) + ' / ' + ttext(d.tmax[i]) + ' · ' + rtext(d.rain[i], 1) + ' · lökés ' + wtext(d.gust[i]) + '</div>').join('');
    return;
  }
  const mon = (d.date[d.date.length - 1] || today).slice(0, 7);
  const ix = d.date.map((s, i) => [s, i]).filter(([s]) => s.startsWith(mon)).map(x => x[1]);
  if (!ix.length) { card.innerHTML = ''; return; }
  const pick = (k, f) => ix.reduce((b, i) => d[k][i] != null && (b == null || f(d[k][i], d[k][b])) ? i : b, null);
  const a = pick('tmax', (x, y) => x > y), b = pick('tmin', (x, y) => x < y), c = pick('rain', (x, y) => x > y), g = pick('gust', (x, y) => x > y);
  const day = i => DS.fmt(d.date[i], { month: 'short', day: 'numeric' });
  card.innerHTML = '<h3>A hónap legjei eddig</h3>' +
    (a != null ? '<div class="line">Legmelegebb: <b>' + ttext(d.tmax[a]) + '</b> (' + day(a) + ')</div>' : '') +
    (b != null ? '<div class="line">Leghidegebb: <b>' + ttext(d.tmin[b]) + '</b> (' + day(b) + ')</div>' : '') +
    (c != null && d.rain[c] > 0 ? '<div class="line">Legcsapadékosabb: <b>' + rtext(d.rain[c], 1) + '</b> (' + day(c) + ')</div>' : '<div class="line">Ebben a hónapban még nem esett.</div>') +
    (g != null ? '<div class="line">Legerősebb lökés: <b>' + wtext(d.gust[g]) + '</b> (' + day(g) + ')</div>' : '') +
    '<div class="line small">Több év adata után itt az „Ezen a napon” összevetés jelenik meg.</div>';
}

// ------------------------------------------------------------------ jelölők az idővonalon (frontok, zivatarok, nagy esők, kerti napló)
let MARKS = null;
async function loadMarks() {
  if (MARKS) return MARKS;
  const [ev, rain, diary] = await Promise.all([api('/api/events'), api('/api/rainevents'), api('/api/store/naplo').catch(() => [])]);
  MARKS = { ev: ev.events || [], rain: (rain.events || []).filter(e => e.total >= 5 || e.maxRate >= rain.cloudburst), cloudburst: rain.cloudburst, diary: Array.isArray(diary) ? diary : [] };
  return MARKS;
}
async function decorateTimeline() {
  if (!chart || chart.__decorated) return;
  chart.__decorated = true;
  let m;
  try { m = await loadMarks(); } catch (e) { return; }
  if (!chart) return;
  const lineEv = m.ev.map(e => ({ xAxis: e.t, name: e.title + ': ' + e.detail, lineStyle: { color: e.type === 'zivatar' ? '#a855f7' : '#0ea5e9', type: 'dashed', width: 1.5 }, label: { formatter: e.type === 'zivatar' ? '⚡' : '❄', position: 'end', fontSize: fs(13) } }));
  const lineDiary = m.diary.filter(x => x.date).map(x => ({ xAxis: DS.ms(x.date) + 10 * H, name: 'Kerti napló: ' + (x.tag || '') + (x.text ? ' – ' + x.text : ''), lineStyle: { color: '#16a34a', type: 'dotted', width: 1.5 }, label: { formatter: '📝', position: 'end', fontSize: fs(12) } }));
  const areaRain = m.rain.map(e => [{ xAxis: e.start, name: rtext(e.total, 1) + ', max ' + rrtext(e.maxRate, 0), itemStyle: { color: e.maxRate >= m.cloudburst ? 'rgba(124,58,237,.18)' : 'rgba(31,120,209,.14)' } }, { xAxis: e.end }]);
  chart.setOption({
    series: [
      { id: 'tDew', markLine: { symbol: 'none', silent: false, animation: false, data: lineEv.concat(lineDiary), emphasis: { label: { show: true, formatter: p => p.name, backgroundColor: cssVar('--card'), color: cssVar('--ink'), padding: 6, borderRadius: 6, position: 'insideEndTop' } } } },
      { id: 'rainTot', markArea: { silent: false, data: areaRain, label: { show: false }, emphasis: { label: { show: true, formatter: p => p.name, color: cssVar('--ink') } } } }
    ]
  });
}
function invalidateMarks() { MARKS = null; if (chart) { chart.__decorated = false; decorateTimeline(); } }
