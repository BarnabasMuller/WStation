'use strict';
/* Részletes magyarázatok: kanadai FWI lépésenként + „Mai számítás” táblázat; kiszáradás % (vödör-hasonlat) */

// ------------------------------------------------------------------ FWI – a számítás lépései JS-ben, a köztes értékekkel (Van Wagner 1987)
const FWI_LE = [6.5, 7.5, 9.0, 12.8, 13.9, 13.9, 12.4, 10.9, 9.4, 8.0, 7.0, 6.0];
const FWI_LF = [-1.6, -1.6, -1.6, 0.9, 3.8, 5.8, 6.4, 5.0, 2.4, 0.4, -1.6, -1.6];
function fwiSteps(T, H, W, ro, Fo, Po, Do, month) {
  const s = {};
  // FFMC
  let mo = 147.2 * (101 - Fo) / (59.5 + Fo); s.mo = mo;
  if (ro > 0.5) {
    const rf = ro - 0.5; s.rf = rf;
    let add = 42.5 * rf * Math.exp(-100 / (251 - mo)) * (1 - Math.exp(-6.93 / rf));
    if (mo > 150) add += 0.0015 * Math.pow(mo - 150, 2) * Math.sqrt(rf);
    mo = Math.min(250, mo + add); s.mr = mo;
  }
  const ed = 0.942 * Math.pow(H, 0.679) + 11 * Math.exp((H - 100) / 10) + 0.18 * (21.1 - T) * (1 - Math.exp(-0.115 * H)); s.ed = ed;
  let m;
  if (mo > ed) {
    const ko = 0.424 * (1 - Math.pow(H / 100, 1.7)) + 0.0694 * Math.sqrt(W) * (1 - Math.pow(H / 100, 8));
    s.k = ko * 0.581 * Math.exp(0.0365 * T); s.dir = 'szárad';
    m = ed + (mo - ed) * Math.pow(10, -s.k);
  } else {
    const ew = 0.618 * Math.pow(H, 0.753) + 10 * Math.exp((H - 100) / 10) + 0.18 * (21.1 - T) * (1 - Math.exp(-0.115 * H)); s.ew = ew;
    if (mo < ew) {
      const k1 = 0.424 * (1 - Math.pow((100 - H) / 100, 1.7)) + 0.0694 * Math.sqrt(W) * (1 - Math.pow((100 - H) / 100, 8));
      s.k = k1 * 0.581 * Math.exp(0.0365 * T); s.dir = 'nedvesedik';
      m = ew - (ew - mo) * Math.pow(10, -s.k);
    } else { m = mo; s.dir = 'egyensúlyban'; }
  }
  s.m = m; s.F = Math.max(0, Math.min(101, 59.5 * (250 - m) / (147.2 + m)));
  // DMC
  const Td = Math.max(T, -1.1);
  s.Le = FWI_LE[month - 1];
  s.K = 1.894 * (Td + 1.1) * (100 - H) * s.Le * 1e-4;
  let pr = Po;
  if (ro > 1.5) {
    s.re = 0.92 * ro - 1.27;
    const moD = 20 + Math.exp(5.6348 - Po / 43.43);
    const b = Po <= 33 ? 100 / (0.5 + 0.3 * Po) : Po <= 65 ? 14 - 1.3 * Math.log(Po) : 6.2 * Math.log(Po) - 17.2;
    const mr = moD + 1000 * s.re / (48.77 + b * s.re);
    pr = Math.max(0, 244.72 - 43.43 * Math.log(mr - 20)); s.Pr = pr;
  }
  s.P = Math.max(0, pr + Math.max(0, s.K));
  // DC
  const Tc = Math.max(T, -2.8);
  s.Lf = FWI_LF[month - 1];
  s.V = Math.max(0, (0.36 * (Tc + 2.8) + s.Lf) / 2);
  let dr = Do;
  if (ro > 2.8) {
    s.rd = 0.83 * ro - 1.27;
    const qo = 800 * Math.exp(-Do / 400);
    s.Q = qo + 3.937 * s.rd;
    dr = Math.max(0, 400 * Math.log(800 / s.Q)); s.Dr = dr;
  }
  s.D = dr + s.V;
  // ISI, BUI, FWI
  const fm = 147.2 * (101 - s.F) / (59.5 + s.F);
  s.fW = Math.exp(0.05039 * W);
  s.fF = 91.9 * Math.exp(-0.1386 * fm) * (1 + Math.pow(fm, 5.31) / 4.93e7);
  s.ISI = 0.208 * s.fW * s.fF;
  let u = s.P <= 0.4 * s.D ? 0.8 * s.P * s.D / (s.P + 0.4 * s.D) : s.P - (1 - 0.8 * s.D / (s.P + 0.4 * s.D)) * (0.92 + Math.pow(0.0114 * s.P, 1.7));
  s.BUI = isNaN(u) || u < 0 ? 0 : u;
  s.fD = s.BUI <= 80 ? 0.626 * Math.pow(s.BUI, 0.809) + 2 : 1000 / (25 + 108.64 * Math.exp(-0.023 * s.BUI));
  s.B = 0.1 * s.ISI * s.fD;
  s.FWI = s.B > 1 ? Math.exp(2.72 * Math.pow(0.434 * Math.log(s.B), 0.647)) : s.B;
  return s;
}

function fwiExplainer() {
  return F_FWI + `
<details open class="explain"><summary><b>Honnan jönnek a betűk? – lépésenként</b></summary>
<ol class="steps">
<li><b>Bemenet minden nap délben</b> (12:00 téli idő): hőmérséklet <b>T</b> (°C), relatív páratartalom <b>RH</b> (%), szélsebesség <b>W</b> (km/h, 10 m-en), és az előző 24 óra csapadéka <b>r₀</b> (mm, déltől délig).</li>
<li><b>FFMC – a fű és az avar szárazsága</b> (órák alatt reagál). A tegnapi FFMC-ből nedvességtartalom lesz: <b class="fm">m₀ = 147,2·(101 − FFMC₀)/(59,5 + FFMC₀)</b>. Ha esett (r₀ &gt; 0,5 mm), m₀ nő. Ezután a levegővel való egyensúly felé mozdul: az <b>Eᵈ</b> (száradási egyensúly) és <b>Eʷ</b> (nedvesedési egyensúly) a T-ből és RH-ból jön; a <b>k</b> sebesség a széltől és a melegtől nő. A végén: <b class="fm">FFMC = 59,5·(250 − m)/(147,2 + m)</b>. 0–101, a 85 felett már könnyen gyullad.</li>
<li><b>DMC – a felső, laza talajréteg</b> (kb. 7 cm, napok–hetek). Naponta <b class="fm">K = 1,894·(T + 1,1)·(100 − RH)·Lₑ·10⁻⁴</b>-gyel nő, ahol <b>Lₑ</b> a hónap nappalhossz-szorzója. 1,5 mm feletti eső (<b class="fm">rₑ = 0,92·r₀ − 1,27</b>) visszanedvesíti.</li>
<li><b>DC – a mély, tömör réteg és a fák alatti talaj</b> (hónapok). Naponta <b class="fm">V = (0,36·(T + 2,8) + L<sub>f</sub>)/2</b>-vel nő; 2,8 mm feletti eső (<b class="fm">r<sub>d</sub> = 0,83·r₀ − 1,27</b>) csökkenti. A nyári aszály itt látszik.</li>
<li><b>ISI – terjedési sebesség</b> = 0,208 · f(W) · f(FFMC): a szél és a fű szárazsága. 10 km/h több szél ≈ 65%-kal nagyobb ISI.</li>
<li><b>BUI – éghető anyag mennyisége</b> = a DMC és a DC kombinációja: mennyi anyag égne el, ha meggyullad.</li>
<li><b>FWI</b> = f(ISI, BUI): a tűz várható intenzitása egy számban.</li>
</ol>
<p><b>A beállítható számok hatása:</b> a <b>kezdőértékek</b> (FFMC₀, DMC₀, DC₀) csak a szezon első heteit befolyásolják – az FFMC pár nap, a DMC kb. 2–3 hét, a DC 1–2 hónap alatt „elfelejti” őket. A <b>szezonkezdet</b> napján a három kód újraindul. A <b>szél-szorzó</b> a 10 m-es szabványhoz igazít: 1,1 kb. 5%-kal emeli az ISI-t és az FWI-t. A <b>déli időpont</b> eltolása a T és RH felhasználását tolja el (délután melegebb, szárazabb → nagyobb érték).</p>
</details>
<details open class="explain"><summary><b>Mai számítás</b> – <select class="sel" id="fwiDay"></select></summary><div id="fwiCalc"></div></details>`;
}

function wireFwiExplainer() {
  const sel = $('fwiDay');
  if (!sel || !DR || !DR.fwiT || !DR.fwiT.length) { if ($('fwiCalc')) $('fwiCalc').innerHTML = '<p class="muted">Nincs adat.</p>'; return; }
  const n = DR.date.length;
  sel.innerHTML = DR.date.map((s, i) => '<option value="' + i + '">' + DS.fmt(s, { year: 'numeric', month: 'short', day: 'numeric', weekday: 'short' }) + '</option>').reverse().slice(0, 400).join('');
  sel.value = String(n - 1);
  const show = () => {
    const i = +sel.value, c = DR.config.fwi;
    const prev = j => j >= 0 ? [DR.ffmc[j], DR.dmc[j], DR.dc[j]] : [c.ffmc0, c.dmc0, c.dc0];
    const md = DR.date[i].slice(5);
    const restart = md === c.seasonStart;
    const [Fo, Po, Do] = restart ? [c.ffmc0, c.dmc0, c.dc0] : prev(i - 1);
    const T = DR.fwiT[i], H = DR.fwiH[i], W = DR.fwiW[i], ro = DR.fwiRo[i];
    const s = fwiSteps(T, H, W, ro, Fo, Po, Do, +DR.date[i].slice(5, 7));
    const n1 = (v, d) => v == null || isNaN(v) ? '–' : num(v, d == null ? 1 : d);
    const row = (sym, name, val, note) => '<tr><td><b>' + sym + '</b></td><td>' + name + '</td><td class="num"><b>' + val + '</b></td><td class="muted">' + (note || '') + '</td></tr>';
    const sect = t => '<tr><th colspan="4" style="text-align:left;padding-top:10px">' + t + '</th></tr>';
    $('fwiCalc').innerHTML = '<table class="dtable fwi-calc"><tbody>' +
      sect('Bemenet (déli észlelés)') +
      row('T', 'hőmérséklet', ttext(T, 1)) + row('RH', 'relatív páratartalom', n1(H, 0) + ' %') +
      row('W', 'szél (a szél-szorzóval)', wtext(W, 1)) + row('r₀', 'csapadék tegnap déltől ma délig', rtext(ro, 1)) +
      row('FFMC₀ · DMC₀ · DC₀', restart ? 'kezdőértékek (szezonkezdet)' : 'a tegnapi kódok', n1(Fo) + ' · ' + n1(Po) + ' · ' + n1(Do)) +
      sect('FFMC – fű, avar') +
      row('m₀', 'nedvességtartalom a tegnapi FFMC-ből', n1(s.mo, 1) + ' %') +
      (s.mr != null ? row('mᵣ', 'eső után', n1(s.mr, 1) + ' %', 'r₀ − 0,5 = ' + n1(s.rf, 1) + ' mm hatásos') : row('mᵣ', 'eső után', '–', 'nem esett 0,5 mm-nél több')) +
      row('Eᵈ', 'száradási egyensúlyi nedvesség', n1(s.ed, 1) + ' %') +
      (s.ew != null ? row('Eʷ', 'nedvesedési egyensúly', n1(s.ew, 1) + ' %') : '') +
      row('k', 'közelítés sebessége (' + s.dir + ')', n1(s.k, 3)) +
      row('m', 'mai nedvesség', n1(s.m, 1) + ' %') + row('FFMC', '', n1(s.F), 'program: ' + n1(DR.ffmc[i])) +
      sect('DMC – felső talajréteg') +
      row('Lₑ', 'nappalhossz-szorzó (' + MONTHS_FULL[+DR.date[i].slice(5, 7) - 1] + ')', n1(s.Le, 1)) +
      (s.re != null ? row('rₑ', 'hatásos eső', n1(s.re, 1) + ' mm', 'visszanedvesítés után: ' + n1(s.Pr)) : row('rₑ', 'hatásos eső', '–', '1,5 mm alatt nem számít')) +
      row('K', 'napi száradás', '+' + n1(s.K, 2)) + row('DMC', '', n1(s.P), 'program: ' + n1(DR.dmc[i])) +
      sect('DC – mély talaj') +
      row('L<sub>f</sub>', 'nappalhossz-korrekció', n1(s.Lf, 1)) +
      (s.rd != null ? row('r<sub>d</sub>', 'hatásos eső', n1(s.rd, 1) + ' mm', 'visszanedvesítés után: ' + n1(s.Dr)) : row('r<sub>d</sub>', 'hatásos eső', '–', '2,8 mm alatt nem számít')) +
      row('V', 'napi száradás', '+' + n1(s.V, 2)) + row('DC', '', n1(s.D), 'program: ' + n1(DR.dc[i])) +
      sect('Eredmény') +
      row('f(W) · f(F)', 'szél- és fű-tényező', n1(s.fW, 2) + ' · ' + n1(s.fF, 2)) +
      row('ISI', 'terjedési sebesség', n1(s.ISI), 'program: ' + n1(DR.isi[i])) +
      row('BUI', 'éghető anyag', n1(s.BUI), 'program: ' + n1(DR.bui[i])) +
      row('FWI', 'tűzveszély', n1(s.FWI) + ' – ' + fwiClass(s.FWI)[0], 'program: ' + n1(DR.fwi[i])) +
      '</tbody></table><p class="hint">A „program” oszlop a tárolt (egy tizedesre kerekített) értékekből indul, ezért a tizedesekben lehet apró eltérés.</p>';
  };
  sel.addEventListener('change', show);
  show();
}

// ------------------------------------------------------------------ kiszáradás % – vödör-hasonlat
function deplBands(p) {
  // a profil saját p-értékéből (RAW/TAW); Ks lineárisan 1 → 0 a stresszhatártól 100%-ig
  const s = Math.round(p * 100);
  const ksAt = x => x <= p ? 1 : Math.max(0, (1 - x) / (1 - p));
  const a = Math.min(s, 25), b = s, c = Math.max(b + 5, Math.round(100 - 0.6 * (100 - s))), d = Math.max(c + 5, 90);
  return [
    ['0–' + a + '%', 'bőséges víz', num(1, 2), 'nem kell semmi', 'ok'],
    [a + '–' + b + '% (stresszhatár, RAW)', 'fogy, de a növény még gond nélkül felveszi', num(1, 2), 'figyelni: a javaslat mutatja, hány nap múlva kell locsolni', 'ok'],
    [b + '–' + c + '%', 'enyhe vízhiány: lassul a növekedés', num(1, 2) + ' → ' + num(ksAt(c / 100), 2), 'locsolni', 'mild'],
    [c + '–' + d + '%', 'erős vízhiány: a gyep sárgul, a lomb lankad', num(ksAt(c / 100), 2) + ' → ' + num(ksAt(d / 100), 2), 'locsolni, alaposan', 'strong'],
    [d + '–100%', 'súlyos: a hervadáspont közelében', '< ' + num(ksAt(d / 100), 2), 'sürgős; fáknál a következő évre is hatással lehet', 'severe']
  ];
}
function openDeplHelp(id) {
  const p = profById(id), r = resById(id);
  if (!p || !r) return;
  const depl = last(r.depl), dr = last(r.dr), taw = last(r.taw), raw = last(r.raw), ks = last(r.ks);
  const pf = raw / taw;
  const etc = r.etc ? avgLast(r.etc, 7) : null;
  const daysTo = etc > 0.05 && dr < raw ? (raw - dr) / etc : null;
  const lit = r.area ? dr * r.area : null;
  const lawn = DR.profiles.map(x => [profById(x.id), x]).find(([q]) => q && q.type === 'gyep');
  const tree = DR.profiles.map(x => [profById(x.id), x]).find(([q]) => q && q.type === 'fa');
  const pct10 = x => x ? num(10 / last(x.taw) * 100, 0) + '%' : '–';
  openModal('<h2>Mit jelent a kiszáradás %?</h2>' +
    '<div class="bucket"><div class="bk-fill" style="height:' + Math.max(0, 100 - depl * 100) + '%"></div><div class="bk-raw" style="bottom:' + (100 - pf * 100) + '%"><span>stresszhatár</span></div></div>' +
    '<p><b>Képzeld el a gyökérzónát egy vödörként.</b> <b>0%</b> = tele van: a talaj a szántóföldi vízkapacitáson (amennyit a föld egy kiadós eső után megtart). <b>100%</b> = üres: a hervadásponton – a talajban még van víz, de a gyökér már nem tudja kiszívni.</p>' +
    '<p>' + tf('A „{name}” most {pct}-on áll: a felvehető víz ennyi része fogyott el.', { name: ut(p.name), pct: '<b style="color:' + drColor(depl) + '">' + num(depl * 100, 0) + '%</b>' }) + '</p>' +
    '<h3>' + tf('Sávok (ennek a profilnak a p = {p} értékéből)', { p: num(pf, 2) }) + '</h3>' +
    '<table class="dtable"><thead><tr><th>Kiszáradás</th><th>Jelentése</th><th>Ks</th><th>Teendő</th></tr></thead><tbody>' +
    deplBands(pf).map((b, j, all) => '<tr' + (j === all.map(x => parseFloat(x[0]) <= depl * 100).lastIndexOf(true) ? ' class="cur"' : '') + '><td><span class="tag ' + b[4] + '">' + b[0] + '</span></td><td>' + b[1] + '</td><td>' + b[2] + '</td><td>' + b[3] + '</td></tr>').join('') + '</tbody></table>' +
    '<h3>A profil saját számai</h3><dl class="kv">' +
    '<dt>A vödör mérete (TAW)</dt><dd>' + rtext(taw, 0) + ' felvehető víz a gyökérzónában</dd>' +
    '<dt>Stresszhatár (RAW)</dt><dd>' + rtext(raw, 0) + ' = ' + num(pf * 100, 0) + '%</dd>' +
    '<dt>Most hiányzik</dt><dd>' + rtext(dr, 1) + (lit != null ? ' ≈ ' + fmtL(lit) + (p.type === 'bokor' ? ' bokronként' : '') : '') + '</dd>' +
    '<dt>Ks most</dt><dd>' + num(ks, 2) + ' – a növény a szükséges víz ' + num(ks * 100, 0) + '%-át tudja felvenni (1 = mindet)</dd>' +
    '<dt>Eső nélkül a stresszhatárig</dt><dd>' + (dr >= raw ? 'már elérte' : daysTo != null ? tf('kb. {d} (az utolsó 7 nap átlagos párolgásával: {e}/nap)', { d: tf('{d} nap', { d: num(daysTo, daysTo < 10 ? 1 : 0) }), e: rtext(etc, 1) }) : '–') + '</dd></dl>' +
    '<h3>Miért szárad ki gyorsabban a gyep, mint a fa?</h3>' +
    '<p>' + tf('Más a vödör mérete. Ugyanaz a 10 mm eső a gyepnél a vödör {a}-a, a fánál csak {b}.', { a: lawn ? pct10(lawn[1]) : '~20%', b: tree ? pct10(tree[1]) : '~5%' }) +
    ' ' + tf('A gyep sekélyen gyökerezik (kis vödör): gyorsan kiürül, de egy kiadós eső fel is tölti. A fa mélyen gyökerezik (nagy vödör): lassan ürül, és sok víz kell a feltöltéséhez.') + '</p>' +
    '<p class="hint">Ks (vízstressz-tényező): a stresszhatárig 1, onnan a hervadáspontig egyenesen 0-ra csökken (FAO-56, 84. egyenlet).</p>');
}
