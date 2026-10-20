'use strict';
/* Szárazság és locsolás fül: talajvíz-mérleg profilonként, ? ablakok képletekkel és állítható arányszámokkal, presetek, locsolási javaslat */

let DR = null, DR_PREV = null, drChart = null, drRange = '90';
async function getDrought(force) {
  if (DR && !force) return DR;
  DR = await api('/api/drought');
  DR.idx = {};
  DR.date.forEach((s, i) => DR.idx[s] = i);
  DR_SNAP = JSON.parse(JSON.stringify(DR.config));
  return DR;
}
let DR_SNAP = null;   // a legutóbb mentett beállítás (a változásnaplóhoz)

// ------------------------------------------------------------------ paraméterek leírása (szabványos értékek szövegesen)
const TYPE_NAME = { felszin: 'Talajfelszín', gyep: 'Gyep', bokor: 'Bokrok', cserep: 'Cserép', fa: 'Fa' };
const TYPE_ICON = { felszin: '🟫', gyep: '🌱', bokor: '🌿', cserep: '🪴', fa: '🌳' };
const FAJTA = {
  barack: { name: 'Barackfa (csonthéjas)', kcIni: 0.55, kcMid: 0.90, kcEnd: 0.65, std: 'FAO-56, 12. táblázat – csonthéjasok (barack, kajszi, szilva), fagyos téllel: <b>talajtakarás nélkül 0,55 / 0,90 / 0,65</b>; aktív füves talajtakarással 0,85 / 1,20 / 0,85.' },
  meggy: { name: 'Meggyfa / cseresznye', kcIni: 0.45, kcMid: 0.95, kcEnd: 0.70, std: 'FAO-56, 12. táblázat – alma, cseresznye, meggy, körte, fagyos téllel: <b>talajtakarás nélkül 0,45 / 0,95 / 0,70</b>; aktív füves talajtakarással 0,50 / 1,20 / 0,95.' },
  altalanos: { name: 'Általános lombos fa', kcIni: 0.50, kcMid: 0.95, kcEnd: 0.70, std: 'Lombos fákra becsült érték a FAO-56 gyümölcsfa-adatai alapján: <b>0,50 / 0,95 / 0,70</b>.' },
  egyedi: { name: 'Egyedi fajta', std: 'Saját értékek (pl. szakirodalomból).' }
};
const P = (label, unit, std, extra) => Object.assign({ label, unit, std }, extra || {});
const PROFILE_PARAMS = {
  name: P('Név', '', 'szabadon választható'),
  kc: P('Növényi szorzó (Kc)', '', 'felszín: FAO-56 – nedves csupasz talaj párolgása Ke ≈ 1,0–1,05; konténeres növények: 1,0 (napos helyen 1,1–1,2)', { step: 0.05 }),
  kcIni: P('Kc – kezdeti (rügyfakadás)', '', 'lásd a fajtánál', { step: 0.05 }),
  kcMid: P('Kc – közép (teljes lomb / aktív)', '', 'gyep: FAO-56 – hidegtűrő pázsit 0,90–0,95, melegtűrő 0,80–0,85; díszcserjék 0,5–0,9; fáknál lásd a fajtánál', { step: 0.05 }),
  kcEnd: P('Kc – vég (lombhullás előtt)', '', 'lásd a fajtánál', { step: 0.05 }),
  kcWinter: P('Kc – nyugalmi időszak', '', 'fa lomb nélkül: csupasz talajon kb. 0,15, füves talajon 0,4–0,5 (a fű párologtat); gyep télen: becslés 0,5–0,6; cserje: 0,3', { step: 0.05 }),
  zr: P('Gyökérzóna mélysége (Zr)', 'm', 'FAO-56: gyep 0,5–1,0 m (a gyökerek zöme a felső 20–30 cm-ben, ezért 0,3); cserjék 0,5–0,8; csonthéjasok 1,0–2,0 m (agyagos altalajon inkább 1,0–1,5); talajfelszín: Ze = 0,10–0,15 m', { step: 0.05 }),
  p: P('Könnyen felvehető víz aránya (p)', '', 'FAO-56, 22. táblázat: gyep 0,40–0,50; gyümölcsfák 0,50; konténeres növények 0,30–0,40. Ha a víz ennél jobban fogy, a növény stresszbe kerül.', { step: 0.05 }),
  sprinklerRate: P('Szórófej kijuttatási intenzitása', 'mm/h', 'spray (porlasztó) fejek: 35–40 mm/h; rotoros fejek: 8–15 mm/h', { step: 1 }),
  maxSession: P('Egy menetben kijuttatható víz', 'mm', 'agyagos talaj beszivárgása 1–5 mm/h: egy menetben 6–12 mm, utána 30–60 perc szünet (cycle & soak)', { step: 1 }),
  lawnArea: P('Gyepterület', 'm²', 'csak a literben rögzített locsoláshoz kell', { step: 5 }),
  area: P('Bokronkénti lombkorona-terület', 'm²', 'a csepegtetett, átnedvesített terület bokronként', { step: 0.1 }),
  count: P('Bokrok száma', 'db', '', { step: 1 }),
  dripCount: P('Csepegtetők száma (bokronként / fánként)', 'db', '', { step: 1 }),
  dripLph: P('Csepegtető teljesítménye', 'l/h', 'tipikus csepegtetők: 2, 4 vagy 8 l/h', { step: 0.5 }),
  potLiters: P('Cserép térfogata', 'l', 'kicsi 3 l (Ø 18 cm), közepes 10 l (Ø 28 cm), nagy 25 l (Ø 38 cm), dézsa 60 l (Ø 50 cm)', { step: 1 }),
  potDiam: P('Cserép átmérője (felül)', 'cm', '', { step: 1 }),
  availFrac: P('A virágföld hasznos víztartalma', 'm³/m³', 'tőzeg alapú virágföld: kb. 0,3–0,4 a cserép térfogatára', { step: 0.05 }),
  place: P('Elhelyezés', '', 'szabad ég: az esőt teljesen megkapja; félig fedett: kb. fele; fedett: semennyit (a párolgás is kisebb: 0,8 ill. 0,6)', { options: { szabad: 'szabad ég', felig: 'félig fedett', fedett: 'fedett' } }),
  fajta: P('Fajta', '', 'a Kc-értékek a fajta szerint (lásd lent)', { options: { barack: 'Barackfa', meggy: 'Meggyfa / cseresznye', altalanos: 'Általános lombos fa', egyedi: 'Egyedi' } }),
  cover: P('Talajtakarás a fa alatt', '', 'füves talajtakarásnál a FAO-56 magasabb Kc-t ad (lásd a fajta szövegét); a Kc-értékeket kézzel kell átírni', { options: { csupasz: 'csupasz föld', fu: 'fű', mulcs: 'mulcs' } }),
  canopyD: P('Lombkorona átmérője', 'm', 'a napi vízigény literben = párolgás (mm) × lombkorona-terület (m²)', { step: 0.5 }),
  height: P('Famagasság', 'm', '', { step: 0.5 }),
  trunkCm: P('Törzskerület 1,3 m magasan', 'cm', 'a fa belső víztartalékának becsléséhez', { step: 5 }),
  age: P('Életkor', 'év', '5 év alatt a gyökérzóna arányosan kisebb', { step: 1 }),
  formFactor: P('Törzs alaktényező', '', 'kb. 0,4–0,5', { step: 0.05 }),
  sapFrac: P('A szijács aránya a törzsben', '', 'fiatal gyümölcsfánál kb. 0,6–0,8', { step: 0.05 }),
  waterFrac: P('A szijácsból kivonható víz', 'm³/m³', 'kb. 0,2–0,3 (tájékoztató, a szakirodalomban nagy a szórás)', { step: 0.05 }),
  autoIrr: P('Automata öntözés be van állítva', '', 'ha igen, a mérleg a programot is beszámítja', { bool: true }),
  autoDays: P('Öntözési napok', '', '1 = hétfő … 7 = vasárnap, vesszővel elválasztva (pl. 1,4)', { text: true }),
  autoMinutes: P('Öntözés hossza', 'perc', 'gyepnél a szórófej intenzitásával, bokornál/fánál a csepegtetőkkel számol', { step: 1 }),
  autoFrom: P('Öntözési idény kezdete', 'HH-NN', 'pl. 05-01', { text: true }),
  autoTo: P('Öntözési idény vége', 'HH-NN', 'pl. 09-30', { text: true })
};
const TYPE_FIELDS = {
  felszin: ['name', 'kc', 'zr'],
  gyep: ['name', 'kcMid', 'kcWinter', 'zr', 'p', 'sprinklerRate', 'maxSession', 'lawnArea'],
  bokor: ['name', 'kcMid', 'kcWinter', 'zr', 'p', 'area', 'count', 'dripCount', 'dripLph'],
  cserep: ['name', 'potLiters', 'potDiam', 'place', 'kc', 'p', 'availFrac'],
  fa: ['name', 'fajta', 'cover', 'kcIni', 'kcMid', 'kcEnd', 'kcWinter', 'zr', 'p', 'canopyD', 'height', 'trunkCm', 'age', 'dripCount', 'dripLph', 'formFactor', 'sapFrac', 'waterFrac']
};
const GLOBAL_PARAMS = {
  solarFactor: P('Napsugárzás-korrekciós szorzó', '', 'kalibrált műszernél 1,00. A te érzékelőd a legtisztább nyári napokon az elméleti érték kb. 85–87%-át méri, ezért a javaslat ~1,10–1,15 (lásd a táblázatot lent). Minden kiszáradási mutatóra érvényes.', { step: 0.01 }),
  autoCal: P('Havi automatikus kalibrálás', '', 'a hónap 3 legtisztább napja alapján havonta más szorzó. Figyelem: télen az alacsony nap miatti árnyékolást is „kijavítja”, ami a kertet is érintheti.', { bool: true }),
  windHeight: P('A szélmérő magassága', 'm', 'FAO-56 referencia: 2 m. A te szélmérőd a tetőn van (kb. 6 m; két emelet + tetőtér gyakran 9–10 m, érdemes lemérni).', { step: 0.5 }),
  roofFactor: P('Tetőhelyzet-szorzó a szélre', '', '1,0 = nincs korrekció. A tető fölött a szél turbulens és gyorsabb lehet; ha a 2 m-re átszámolt szél túl nagynak tűnik, csökkentsd (pl. 0,8).', { step: 0.05 }),
  albedo: P('Albedó (visszaverődés)', '', 'FAO-56 referencia-gyep: 0,23', { step: 0.01 }),
  elevation: P('Tengerszint feletti magasság', 'm', 'az állomás: kb. 164 m (a légnyomás és a γ számításához)', { step: 1 }),
  startFull: P('Az első napon telített talaj', '', 'téli feltöltődés feltételezése (január 1-jén indulva szokásos)', { bool: true }),
  pAdjust: P('p korrekciója a párolgás szerint', '', 'FAO-56: p = p_táblázat + 0,04 · (5 − ETc); nagy melegben a növény hamarabb stresszbe kerül', { bool: true }),
  springStart: P('Vegetáció kezdete (rügyfakadás)', 'HH-NN', 'Budaörsön tipikusan március vége – április eleje', { text: true }),
  fullLeaf: P('Teljes lombozat', 'HH-NN', 'tipikusan május vége – június eleje', { text: true }),
  fallStart: P('Lombhullás kezdete', 'HH-NN', 'tipikusan szeptember közepe', { text: true }),
  dormant: P('Nyugalmi időszak kezdete', 'HH-NN', 'tipikusan október vége – november eleje', { text: true })
};
const SOIL_PARAMS = {
  name: P('Név', '', ''),
  topDepth: P('A felső (termő)réteg vastagsága', 'm', 'a te kertedben kb. 0,10–0,15 m jó termőföld', { step: 0.01 }),
  topFC: P('Felső réteg: szántóföldi vízkapacitás (θFC)', 'm³/m³', 'FAO-56, 19. táblázat: homok 0,07–0,17; vályog 0,20–0,30; agyag 0,32–0,40', { step: 0.01 }),
  topWP: P('Felső réteg: hervadáspont (θWP)', 'm³/m³', 'homok 0,02–0,07; vályog 0,07–0,17; agyag 0,20–0,24', { step: 0.005 }),
  subFC: P('Alsó réteg: szántóföldi vízkapacitás', 'm³/m³', 'lásd fent', { step: 0.01 }),
  subWP: P('Alsó réteg: hervadáspont', 'm³/m³', 'lásd fent', { step: 0.005 }),
  rew: P('Könnyen párolgó víz a felszínen (REW)', 'mm', 'FAO-56, 19. táblázat: homok 2–7; vályog 8–10; agyag 8–12 mm', { step: 1 }),
  cn: P('SCS görbeszám (CN, normál nedvesség)', '', 'USDA NRCS, jó állapotú gyep: A (homok) 39 · B 61 · C (vályog/agyagos vályog) 74 · D (agyag) 80. Termőföld agyagon: kb. 74–80.', { step: 1 })
};
const RUNOFF_PARAMS = {
  enabled: P('Lefolyás számítása', '', 'kikapcsolva minden eső beszivárog', { bool: true }),
  method: P('Előzetes nedvesség módszere', '', 'a talajvíz-mérlegből: a felső réteg telítettsége fokozatmentesen tolja a CN-t (száraz → AMC I, telített → AMC III); 5 napos csapadék: a szabványos SCS-határok szerint', { options: { merleg: 'a talajvíz-mérlegből', '5nap': '5 napos előzetes csapadék' } }),
  ia: P('Kezdeti veszteség aránya (Ia/S)', '', 'SCS szabvány: 0,2 (újabb vizsgálatok szerint 0,05 is lehet)', { step: 0.01 }),
  dry: P('AMC I határ, nyugalmi időszak', 'mm/5 nap', 'SCS: 13 mm', { step: 1 }),
  wet: P('AMC III határ, nyugalmi időszak', 'mm/5 nap', 'SCS: 28 mm', { step: 1 }),
  dryGrow: P('AMC I határ, vegetációs idő', 'mm/5 nap', 'SCS: 36 mm', { step: 1 }),
  wetGrow: P('AMC III határ, vegetációs idő', 'mm/5 nap', 'SCS: 53 mm', { step: 1 })
};
const SOILS = {
  homokos: { name: 'Homokos', topDepth: 0.15, topFC: 0.12, topWP: 0.045, subFC: 0.12, subWP: 0.045, rew: 5, cn: 49 },
  valyogos: { name: 'Vályogos', topDepth: 0.15, topFC: 0.27, topWP: 0.115, subFC: 0.27, subWP: 0.115, rew: 9, cn: 69 },
  agyagos: { name: 'Agyagos', topDepth: 0.15, topFC: 0.36, topWP: 0.22, subFC: 0.36, subWP: 0.22, rew: 10, cn: 80 },
  sajat: { name: 'Saját kert: 15 cm termőföld agyagon', topDepth: 0.15, topFC: 0.27, topWP: 0.115, subFC: 0.36, subWP: 0.22, rew: 9, cn: 77 }
};

// ------------------------------------------------------------------ képletek a ? ablakokhoz
const F_BALANCE = `<div class="formula">Dr<sub>i</sub> = Dr<sub>i−1</sub> − (P − RO)<sub>i</sub> − I<sub>i</sub> + ETc<sub>i</sub>  (0 ≤ Dr ≤ TAW)<br>
ETc = Ks · Kc · ET₀<br>
TAW = 1000 · (θFC − θWP) · Zr   (két rétegnél rétegenként összegezve)<br>
RAW = p · TAW<br>
Ks = (TAW − Dr) / ((1 − p) · TAW), ha Dr &gt; RAW; különben 1</div>
<p><b>Dr</b>: a gyökérzóna vízhiánya (mm) · <b>P</b>: csapadék · <b>RO</b>: lefolyás · <b>I</b>: öntözés · <b>TAW</b>: összes felvehető víz · <b>RAW</b>: könnyen felvehető víz · <b>Ks</b>: vízstressz-tényező (1 = nincs stressz). <b>Kiszáradás</b> = Dr / TAW. Forrás: FAO-56, 85. egyenlet.</p>`;
const F_SURFACE = `<div class="formula">TEW = 1000 · (θFC − 0,5 · θWP) · Ze<br>Kr = (TEW − De) / (TEW − REW), ha De &gt; REW; különben 1<br>E = Kr · Kc · ET₀</div>
<p>A felső 10 cm párolgása (FAO-56, 7. fejezet): amíg a könnyen párolgó víz (REW) tart, teljes ütemben párolog, utána lassul. Napos léptékű: egy eső után gyorsan nedves, pár nap alatt kiszárad.</p>`;
const F_POT = `<div class="formula">TAW<sub>cserép</sub> (mm) = V (l) · f / A (m²)<br>P<sub>hatásos</sub> = P · eső-arány (szabad 1 · félig 0,5 · fedett 0)<br>ETc = Ks · Kc · ET₀ · párolgás-arány (1 · 0,8 · 0,6)</div>
<p>A cserépben csak a földje tárol vizet: V a térfogat, f a hasznos víztartalom, A a felszín. 1 mm a cserép felszínén = A liter.</p>`;
const F_TREE = `<div class="formula">napi vízigény (l) = ETc (mm) · lombkorona-terület (m²)<br>
belső tartalék (l) = π · (d/2)² · h · alaktényező · szijács-arány · 1000 · kivonható víz   (d = törzskerület / π)<br>
tartós vízhiány (30 / 90 nap) = átlag(1 − Ks)</div>
<p>A fák Kc-je szezonálisan változik: rügyfakadástól a teljes lombig a kezdeti értékről a középsőre nő, lombhullástól a vég-értékre csökken, télen a nyugalmi érték. A mély gyökérzóna miatt a fa hónapokra átviszi a tartalékot (egy csapadékos május után júliusban is jobban áll). A belső tartalék tájékoztató becslés.</p>`;
const F_ET0 = `<div class="formula">ET₀ = [0,408·Δ·(Rn − G) + γ·(900/(T+273))·u₂·(es − ea)] / [Δ + γ·(1 + 0,34·u₂)]<br>
u₂ = u<sub>z</sub> · 4,87 / ln(67,8·z − 5,42) · tetőhelyzet-szorzó<br>
Rs = napenergia (kWh/m²) · 3,6 · korrekciós szorzó   (MJ/m²/nap)<br>
Rn = (1 − α)·Rs − σ·[(Tmax+273,16)⁴ + (Tmin+273,16)⁴]/2 · (0,34 − 0,14·√ea) · (1,35·Rs/Rso − 0,35)<br>
Rso = (0,75 + 2·10⁻⁵·z<sub>tszf</sub>) · Ra</div>
<p>FAO-56 Penman–Monteith napi referencia-párolgás (mm/nap): ennyi vizet párologtatna egy jól ellátott, 12 cm-es gyep. <b>Δ</b>: a telítési gőznyomás-görbe meredeksége · <b>γ</b>: pszichrometrikus állandó (a légnyomásból, a magasság alapján) · <b>es, ea</b>: telítési és tényleges gőznyomás (Tmax, Tmin, RHmax, RHmin) · <b>G</b> = 0 napi lépésnél · <b>Ra</b>: légkörön kívüli sugárzás (a szélességből és a napból). Hiányos napon (pl. adatkiesés) a Hargreaves-képlettel becsül.</p>`;
const F_RUNOFF = `<div class="formula">RO = (P − Ia)² / (P − Ia + S), ha P &gt; Ia<br>S = 25400 / CN − 254 (mm) · Ia = 0,2 · S<br>CN<sub>I</sub> = CN / (2,281 − 0,01281·CN) · CN<sub>III</sub> = CN / (0,427 + 0,00573·CN)</div>
<p>SCS görbeszám-módszer (USDA NRCS). Száraz talajon (AMC I) a CN kisebb, alig folyik el víz; telített talajon (AMC III) nagyobb. Ezért ritka az elfolyás, de egy hosszú esős időszak utáni zápornál jelentős lehet.</p>`;

// ------------------------------------------------------------------ segédek
function drColor(depl) {
  const stops = ['#1a9850', '#91cf60', '#d9ef8b', '#fee08b', '#fc8d59', '#d73027', '#a50026'];
  const t = Math.max(0, Math.min(1, depl)) * (stops.length - 1), k = Math.min(stops.length - 2, Math.floor(t));
  const a = hexRgb(stops[k]), b = hexRgb(stops[k + 1]), f = t - k;
  return 'rgb(' + a.map((x, j) => Math.round(x + (b[j] - x) * f)).join(',') + ')';
}
function stressText(ks, depl, rawFrac) {
  if (depl <= rawFrac) return ['nincs vízhiány', 'ok'];
  if (ks >= 0.7) return ['enyhe vízhiány', 'mild'];
  if (ks >= 0.35) return ['erős vízhiány', 'strong'];
  return ['súlyos vízhiány', 'severe'];
}
const last = a => a[a.length - 1];
const sumLast = (a, n) => a.slice(-n).reduce((s, x) => s + (x || 0), 0);
const avgLast = (a, n) => { const v = a.slice(-n).filter(x => x != null); return v.length ? v.reduce((s, x) => s + x, 0) / v.length : 0; };
function profById(id) { return DR.config.profiles.find(p => p.id === id); }
function resById(id) { return DR.profiles.find(p => p.id === id); }
function fmtL(l) { return l == null || isNaN(l) ? '–' : l >= 100 ? num(l, 0) + ' l' : num(l, 1) + ' l'; }

// ------------------------------------------------------------------ rajzolás
async function renderDrought() {
  if (currentTab !== 'szarazsag' || !META) return;
  try { await getDrought(); } catch (e) { $('drCards').innerHTML = '<div class="card">Hiba: ' + esc(e.message) + '</div>'; return; }
  if (DR.repairNote && !window._repairShown) { window._repairShown = true; toast('⚠ ' + DR.repairNote, 12000); }
  renderDrChips();
  renderDrCards();
  renderWater();
  if (typeof renderIrrigation === 'function') renderIrrigation();
  if (typeof applyDrSub === 'function') applyDrSub();
  renderDrChart();
  renderDroughtExtras().catch(e => console.error(e));
}

function renderDrChips() {
  const c = DR.config;
  $('drChips').innerHTML = c.profiles.map(p =>
    '<label class="chip' + (p.enabled ? ' on' : '') + '" title="Megjelenítés be/ki"><input type="checkbox" data-id="' + p.id + '"' + (p.enabled ? ' checked' : '') + '>' +
    TYPE_ICON[p.type] + ' ' + esc(p.name) + '</label>').join('');
  $('drChips').querySelectorAll('input').forEach(i => i.addEventListener('change', () => {
    profById(i.dataset.id).enabled = i.checked; saveDroughtConfig();
  }));
  $('drSoilName').textContent = c.soil.name;
}

function renderDrCards() {
  const n = DR.n, h = [];
  // ET0 kártya
  const et7 = sumLast(DR.et0, 7), rain7 = sumLast(DR.rain, 7);
  h.push('<div class="card dr-card"><button class="qbtn" data-q="et0" title="Képlet és beállítások">ƒx</button><h3>☀️ Párolgás (ET₀)</h3>' +
    '<div class="big">' + num(rconv(last(DR.et0)), rdec(1)) + '<small>' + rlabel() + '/nap</small></div>' +
    '<div class="line">Utolsó 7 nap: <b>' + rtext(et7, 1) + '</b> párolgás · <b>' + rtext(rain7, 1) + '</b> eső</div>' +
    '<div class="line">Vízmérleg (7 nap): <b class="' + (rain7 - et7 < 0 ? 'neg' : 'pos') + '">' + signed(rconv(rain7 - et7), rdec(1)) + ' ' + rlabel() + '</b></div>' +
    '<div class="line small">Napsugárzás-szorzó: ' + num(DR.config.global.solarFactor, 2) + (DR.config.global.autoCal ? ' (havi kalibrálás)' : '') + (last(DR.est) ? ' · ⚠ a mai érték becsült' : '') + '</div></div>');
  for (const p of DR.config.profiles) {
    if (!p.enabled) continue;
    const r = resById(p.id);
    if (!r) continue;
    const depl = last(r.depl), dr = last(r.dr), taw = last(r.taw), raw = last(r.raw), ks = last(r.ks);
    const [st, cls] = stressText(ks, depl, raw / taw);
    let extra = '';
    if (p.type === 'fa' || p.type === 'bokor' || p.type === 'cserep') extra += '<div class="line">Hiányzik: <b>' + rtext(dr, 0) + ' ≈ ' + fmtL(dr * r.area * (p.type === 'bokor' ? 1 : 1)) + '</b>' + (p.type === 'bokor' ? ' bokronként' : '') + '</div>';
    else extra += '<div class="line">Hiányzik: <b>' + rtext(dr, 0) + '</b> (összesen ' + rtext(taw, 0) + ' fér el)</div>';
    if (p.type === 'fa') {
      const trend = r.drMonthAgo != null ? dr - r.drMonthAgo : null;
      extra += '<div class="line">Napi vízigény most: <b>' + fmtL(r.litersToday) + '</b> (' + num(last(r.kc), 2) + ' Kc)</div>' +
        '<div class="line">Tartós vízhiány: 30 nap <b>' + num(r.stress30 * 100, 0) + '%</b> · 90 nap <b>' + num(r.stress90 * 100, 0) + '%</b></div>' +
        (trend != null ? '<div class="line">Talajtartalék 30 nap alatt: <b class="' + (trend > 0 ? 'neg' : 'pos') + '">' + (trend > 0 ? 'fogyott ' : 'nőtt ') + rtext(Math.abs(trend), 0) + '</b></div>' : '') +
        (r.reserveL ? '<div class="line small">Belső víztartalék ≈ ' + fmtL(r.reserveL) + (r.reserveDays ? ' (~' + num(r.reserveDays, 1) + ' napi szükséglet)' : '') + '</div>' : '');
    }
    if (p.type === 'felszin') extra += '<div class="line small">A felső 10 cm: egy eső után gyorsan nedves, napok alatt kiszárad.</div>';
    h.push('<div class="card dr-card"><button class="qbtn" data-q="p:' + p.id + '" title="Képlet és beállítások">ƒx</button>' +
      '<h3>' + TYPE_ICON[p.type] + ' ' + esc(p.name) + '</h3>' +
      '<div class="big" style="color:' + drColor(depl) + '">' + num(depl * 100, 0) + '<small>% kiszáradás</small> <button class="qmini" data-depl="' + p.id + '" title="0% = a gyökérzóna „vödre” tele, 100% = üres (hervadáspont). A jelölt vonal a stresszhatár. Kattints a részletekért.">?</button></div>' +
      '<div class="bar"><span style="width:' + Math.min(100, depl * 100) + '%;background:' + drColor(depl) + '"></span><i style="left:' + (raw / taw * 100) + '%" title="stresszhatár (RAW)"></i></div>' +
      '<div class="line"><span class="tag ' + cls + '">' + st + '</span> Ks = ' + num(ks, 2) + '</div>' + extra + '</div>');
  }
  $('drCards').innerHTML = h.join('');
  $('drCards').querySelectorAll('.qbtn').forEach(b => b.addEventListener('click', () => openHelp(b.dataset.q)));
  $('drCards').querySelectorAll('[data-depl]').forEach(b => b.addEventListener('click', () => openDeplHelp(b.dataset.depl)));
}

function renderDrChart() {
  const el = $('drChart');
  const ink = cssVar('--ink'), muted = cssVar('--muted'), line = cssVar('--line'), card = cssVar('--card');
  const x = DR.date.map(s => DS.ms(s) + 12 * H);
  const series = [];
  const palette = ['#8c6d31', '#2ca25f', '#41ab5d', '#e6550d', '#d6604d', '#b2182b', '#6a51a3', '#3182bd', '#de2d26', '#756bb1'];
  DR.config.profiles.forEach((p, k) => {
    if (!p.enabled) return;
    const r = resById(p.id);
    const col = palette[k % palette.length];
    series.push({ name: p.name, type: 'line', showSymbol: false, data: r.depl.map((v, i) => [x[i], v == null ? null : +(v * 100).toFixed(1)]), lineStyle: { width: 2, color: col }, itemStyle: { color: col }, yAxisIndex: 0 });
    const pr = DR_PREV && DR_PREV.profiles.find(q => q.id === p.id);
    if (pr && JSON.stringify(pr.depl) !== JSON.stringify(r.depl))
      series.push({ name: p.name + ' (előző beállítás)', type: 'line', showSymbol: false, data: pr.depl.map((v, i) => [x[i], v == null ? null : +(v * 100).toFixed(1)]), lineStyle: { width: 1.5, type: 'dashed', color: col, opacity: .7 }, itemStyle: { color: col }, yAxisIndex: 0 });
  });
  series.push({ name: 'Csapadék', type: 'bar', data: DR.rain.map((v, i) => [x[i], v ? rconv(v) : null]), itemStyle: { color: cssVar('--c-rain'), opacity: .55 }, yAxisIndex: 1, barMaxWidth: 6 });
  series.push({ name: 'Párolgás (ET₀)', type: 'line', showSymbol: false, data: DR.et0.map((v, i) => [x[i], rconv(v)]), lineStyle: { width: 1, color: cssVar('--c-sun') }, itemStyle: { color: cssVar('--c-sun') }, yAxisIndex: 1 });
  const irr = DR.date.map((s, i) => { let m = 0; DR.profiles.forEach(r => { if (profById(r.id).enabled) m = Math.max(m, r.irr[i] || 0); }); return [x[i], m ? rconv(m) : null]; });
  if (irr.some(v => v[1])) series.push({ name: 'Öntözés', type: 'bar', data: irr, itemStyle: { color: '#16a34a', opacity: .7 }, yAxisIndex: 1, barMaxWidth: 6 });

  const end = last(x), start = drRange === 'all' ? x[0] : drRange === 'year' ? Math.max(x[0], Date.UTC(new Date(end).getUTCFullYear(), 0, 1)) : end - (+drRange) * D;
  if (!drChart || drChart.isDisposed()) drChart = echarts.init(el);
  drChart.setOption({
    animation: false,
    textStyle: { fontFamily: '"Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif' },
    legend: { top: 0, type: 'scroll', textStyle: { color: muted, fontSize: fs(12) } },
    grid: { left: 52, right: 52, top: 40, bottom: 56 },
    tooltip: { trigger: 'axis', confine: true, backgroundColor: card, borderColor: line, textStyle: { color: ink, fontSize: fs(12.5) },
      formatter: ps => '<b>' + DS.fmt(DS.str(ps[0].axisValue), { year: 'numeric', month: 'short', day: 'numeric', weekday: 'short' }) + '</b><br>' +
        ps.filter(p => p.data[1] != null).map(p => p.marker + ' ' + esc(p.seriesName) + ': <b>' + (series[p.seriesIndex] && series[p.seriesIndex].yAxisIndex === 0 ? num(p.data[1], 1) + ' %' : num(p.data[1], rdec(1)) + ' ' + rlabel()) + '</b>').join('<br>') },
    xAxis: { type: 'time', min: start, max: end + D / 2, axisLabel: { color: muted, formatter: v => F.day.format(v) }, axisLine: { lineStyle: { color: line } } },
    yAxis: [
      { type: 'value', min: 0, max: 100, name: 'kiszáradás %', nameTextStyle: { color: muted }, axisLabel: { color: muted }, splitLine: { lineStyle: { color: line, opacity: .6 } } },
      { type: 'value', min: 0, name: rlabel(), nameTextStyle: { color: muted }, axisLabel: { color: muted }, splitLine: { show: false } }
    ],
    dataZoom: [{ type: 'inside', filterMode: 'none' }, { type: 'slider', height: 22, bottom: 8, filterMode: 'none', textStyle: { color: muted }, labelFormatter: v => F.dayY.format(v) }],
    series
  }, true);
}

// ------------------------------------------------------------------ locsolási javaslat
function lawnAdvice(p, r) {
  const dr = last(r.dr), raw = last(r.raw), taw = last(r.taw);
  const etcAvg = avgLast(r.etc, 7) || 0.1;
  const rain2 = sumLast(DR.rain, 2), tmax = last(DR.tmax), tmin = last(DR.tmin);
  const out = [];
  let status, cls;
  if (tmax != null && tmax < 5) { status = 'Most nem kell locsolni (hideg van).'; cls = 'ok'; }
  else if (rain2 >= 5) { status = 'Az elmúlt 2 napban ' + rtext(rain2, 1) + ' eső esett – várj vele.'; cls = 'ok'; }
  else if (dr < raw * 0.5) { status = 'Nem kell locsolni.'; cls = 'ok'; out.push('Még kb. <b>' + Math.max(1, Math.round((raw - dr) / etcAvg)) + ' nap</b> elég a talajban lévő víz (eső nélkül).'); }
  else if (dr < raw) { status = 'Hamarosan locsolni kell.'; cls = 'mild'; out.push('Kb. <b>' + Math.max(1, Math.round((raw - dr) / etcAvg)) + ' nap</b> múlva éri el a stresszhatárt.'); }
  else { status = 'Locsolni kell.'; cls = 'strong'; }
  if (cls !== 'ok' || dr >= raw * 0.5) {
    const amount = Math.max(0, dr);
    const ses = Math.max(1, Math.ceil(amount / Math.max(1, p.maxSession)));
    const per = amount / ses, minutes = per / Math.max(1, p.sprinklerRate) * 60;
    if (amount >= 2) out.push('A feltöltéshez kb. <b>' + rtext(amount, 0) + '</b> kell: <b>' + ses + ' × ' + rtext(per, 0) + '</b> (zónánként kb. <b>' + num(minutes, 0) + ' perc</b>' + (ses > 1 ? ', a menetek között 30–60 perc szünettel – agyagos talajon így nem folyik el' : '') + '), kora reggel.');
  }
  out.push('A gyep most kb. <b>' + rtext(etcAvg, 1) + '/nap</b> vizet párologtat (heti ' + rtext(etcAvg * 7, 0) + ').');
  if (tmin != null && tmin <= 1) out.push('⚠ Fagy közeli éjszaka volt: csak délelőtt locsolj.');
  const pa = typeof programAdvice === 'function' ? programAdvice(p, r) : null;
  if (pa) out.push(pa);
  return { status, cls, lines: out };
}

// csepegtetési idő: 90 perc alatt percben, 48 óráig órában (pl. „7,6 óra”), fölötte napban is
function durText(min) { return min < 90 ? num(min, 0) + ' perc' : min < 48 * 60 ? num(min / 60, 1) + ' óra' : num(min / 1440, 1) + ' nap (' + num(min / 60, 0) + ' óra)'; }

function shrubAdvice(p, r) {
  const dr = last(r.dr), raw = last(r.raw);
  const etcAvg = avgLast(r.etc, 7) || 0.1;
  const lph = Math.max(0.1, p.dripCount * p.dripLph);
  const out = [];
  let status, cls;
  const daily = etcAvg * p.area;
  if (sumLast(DR.rain, 2) >= 8) { status = 'Az elmúlt napok esője elég.'; cls = 'ok'; }
  else if (dr < raw * 0.5) { status = 'Nem kell locsolni.'; cls = 'ok'; }
  else if (dr < raw) { status = 'Hamarosan locsolni kell.'; cls = 'mild'; }
  else { status = 'Locsolni kell.'; cls = 'strong'; }
  const fill = dr * p.area;
  if (fill >= 0.5 && cls !== 'ok') out.push('Feltöltéshez bokronként kb. <b>' + fmtL(fill) + '</b> → <b>' + durText(fill / lph * 60) + '</b> (' + num(p.dripCount, 0) + ' × ' + num(p.dripLph, 1) + ' l/h csepegtető).');
  out.push('Napi fenntartás: bokronként kb. <b>' + fmtL(daily) + '</b> (≈ <b>' + durText(daily / lph * 60) + '</b> csepegtetés naponta' + (p.count > 1 ? '; ' + num(p.count, 0) + ' bokorra összesen ' + fmtL(daily * p.count) : '') + ').');
  const pa = typeof programAdvice === 'function' ? programAdvice(p, r) : null;
  if (pa) out.push(pa);
  return { status, cls, lines: out };
}

function potAdvice(pots) {
  const out = [];
  let worst = 'ok';
  const order = { ok: 0, mild: 1, strong: 2 };
  for (const p of pots) {
    const r = resById(p.id);
    const A = r.area, tawL = p.potLiters * p.availFrac;
    const etF = p.place === 'fedett' ? 0.6 : p.place === 'felig' ? 0.8 : 1;
    const need = Math.max(0.01, (last(r.kc) || 1) * avgLast(DR.et0, 3) * etF * A);   // liter/nap
    const rainL = (DR.rain[DR.n - 1] || 0) * A * (p.place === 'fedett' ? 0 : p.place === 'felig' ? 0.5 : 1);
    const every = Math.max(0.5, tawL * p.p / need);
    let txt = '<b>' + esc(p.name) + '</b> (' + num(p.potLiters, 0) + ' l, ' + PROFILE_PARAMS.place.options[p.place] + '): ma kb. <b>' + fmtL(need) + '</b> vizet párologtat. ';
    if (rainL >= need) txt += 'A mai eső elég volt.';
    else if (every < 1) { txt += '<b class="neg">Naponta kétszer</b> locsold, alkalmanként kb. ' + fmtL(need / 2 + 0.05) + '.'; worst = 'strong'; }
    else { txt += 'Kb. <b>' + (every < 1.5 ? 'naponta' : Math.round(every) + ' naponta') + '</b> locsold, alkalmanként kb. <b>' + fmtL(Math.min(tawL, need * Math.max(1, Math.round(every)))) + '</b>.'; if (every < 2 && order[worst] < 1) worst = 'mild'; }
    out.push(txt);
  }
  const status = worst === 'strong' ? 'Kánikula: a cserepeknek sok víz kell.' : worst === 'mild' ? 'Naponta érdemes ránézni.' : 'Ritkán kell locsolni.';
  return { status, cls: worst, lines: out };
}

function renderWater() {
  const c = DR.config, cards = [];
  const mk = (title, icon, ids, adv, zoneOpts) => {
    const logs = c.log.map((l, i) => Object.assign({ i }, l)).filter(l => ids.includes(l.zone)).slice(-4).reverse();
    return '<div class="card water-card"><h3>' + icon + ' ' + title + '</h3>' +
      '<div class="w-status ' + adv.cls + '">' + adv.status + '</div>' +
      '<ul class="w-lines">' + adv.lines.map(x => '<li>' + x + '</li>').join('') + '</ul>' +
      '<details class="w-log"><summary>Locsoltam…</summary>' +
      '<div class="w-form">' + (zoneOpts.length > 1 ? '<select class="sel w-zone">' + zoneOpts.map(o => '<option value="' + o[0] + '">' + esc(o[1]) + '</option>').join('') + '</select>' : '<input type="hidden" class="w-zone" value="' + zoneOpts[0][0] + '">') +
      '<input type="date" class="w-date" value="' + DS.today() + '">' +
      '<input type="number" class="w-amount" min="0" step="0.5" placeholder="mennyiség">' +
      '<select class="sel w-unit">' + zoneOpts[0][2].map(u => '<option value="' + u + '">' + ({ mm: 'mm', perc: 'perc', l: 'liter' })[u] + '</option>').join('') + '</select>' +
      '<button class="btn primary w-add">Rögzítés</button></div>' +
      (logs.length ? '<div class="w-hist">' + logs.map(l => '<span>' + esc(l.date) + ': ' + num(l.amount, 1) + ' ' + ({ mm: 'mm', perc: 'perc', l: 'l' })[l.unit] + (ids.length > 1 ? ' (' + esc((profById(l.zone) || {}).name || '') + ')' : '') + ' <a href="#" data-del="' + l.i + '" title="Törlés">✕</a></span>').join('') + '</div>' : '') +
      (logs.some(l => l.date > last(DR.date)) ? '<div class="w-note">⚠ Az adatok ' + esc(last(DR.date)) + '-ig tartanak: a későbbi locsolás a következő adatfrissítés után számít bele.</div>' : '') +
      '<div class="w-note">A rögzített locsolást a talajvíz-mérleg beszámítja.</div></details></div>';
  };
  const lawn = c.profiles.filter(p => p.type === 'gyep' && p.enabled);
  lawn.forEach(p => cards.push(mk(esc(p.name), '🌱', [p.id], lawnAdvice(p, resById(p.id)), [[p.id, p.name, ['perc', 'mm', 'l']]])));
  const shrubs = c.profiles.filter(p => p.type === 'bokor' && p.enabled);
  shrubs.forEach(p => cards.push(mk(esc(p.name), '🌿', [p.id], shrubAdvice(p, resById(p.id)), [[p.id, p.name, ['perc', 'l', 'mm']]])));
  const pots = c.profiles.filter(p => p.type === 'cserep' && p.enabled);
  if (pots.length) cards.push(mk('Cserepes virágok', '🪴', pots.map(p => p.id), potAdvice(pots), pots.map(p => [p.id, p.name, ['l', 'mm']])));
  const trees = c.profiles.filter(p => p.type === 'fa' && p.enabled);
  if (trees.length) {
    const lines = trees.map(p => {
      const r = resById(p.id), dr = last(r.dr), raw = last(r.raw), lph = p.dripCount * p.dripLph;
      const fill = Math.max(0, dr - raw * 0.5) * r.area;
      return '<b>' + esc(p.name) + '</b>: ' + (dr <= raw ? 'nincs szüksége öntözésre.' : 'mélyen kiszáradt (' + num(last(r.depl) * 100, 0) + '%). Egy alapos, ritka beöntözés segít: kb. <b>' + fmtL(fill) + '</b> a lombkorona alá, több részletben' + (lph > 0 ? ' (≈ ' + durText(fill / lph * 60) + ' csepegtetés)' : '') + '.');
    });
    cards.push(mk('Fák', '🌳', trees.map(p => p.id), { status: 'A fák mélyen gyökereznek: ritkán, de sokat.', cls: trees.some(p => last(resById(p.id).dr) > last(resById(p.id).raw)) ? 'mild' : 'ok', lines }, trees.map(p => [p.id, p.name, ['l', 'perc', 'mm']])));
  }
  $('waterCards').innerHTML = cards.join('') || '<div class="muted">Nincs bekapcsolt gyep, bokor, cserép vagy fa profil.</div>';
  $('waterCards').querySelectorAll('.water-card').forEach(card => {
    const zoneEl = card.querySelector('.w-zone');
    const unitSel = card.querySelector('.w-unit');
    if (zoneEl && zoneEl.tagName === 'SELECT') zoneEl.addEventListener('change', () => {
      const p = profById(zoneEl.value);
      const units = p.type === 'gyep' ? ['perc', 'mm', 'l'] : p.type === 'cserep' ? ['l', 'mm'] : ['l', 'perc', 'mm'];
      unitSel.innerHTML = units.map(u => '<option value="' + u + '">' + ({ mm: 'mm', perc: 'perc', l: 'liter' })[u] + '</option>').join('');
    });
    const add = card.querySelector('.w-add');
    if (add) add.addEventListener('click', () => {
      const amount = parseFloat(String(card.querySelector('.w-amount').value).replace(',', '.'));
      if (!(amount > 0)) { toast('Adj meg egy mennyiséget.'); return; }
      DR.config.log.push({ date: card.querySelector('.w-date').value, zone: zoneEl.value, unit: unitSel.value, amount, note: '' });
      saveDroughtConfig('Locsolás rögzítve.');
    });
    card.querySelectorAll('[data-del]').forEach(a => a.addEventListener('click', e => {
      e.preventDefault();
      const l = DR.config.log[+a.dataset.del];
      if (!l || !confirm('Törlöd a(z) ' + l.date + '-i locsolást?' + (l.diaryId ? ' A kerti naplóból is kikerül.' : ''))) return;
      DR.config.log.splice(+a.dataset.del, 1); saveDroughtConfig('Bejegyzés törölve.');
      // a kerti naplóból rögzített locsolás bejegyzése is törlődik
      if (l.diaryId && typeof getDiary === 'function') getDiary().then(list => { const i = list.findIndex(x => x.id === l.diaryId); if (i >= 0) { list.splice(i, 1); saveDiary(); } });
    }));
  });
}

// ------------------------------------------------------------------ mentés
async function saveDroughtConfig(msg) {
  try {
    DR_PREV = DR;
    const res = await post('/api/drought/config', DR.config);
    if (typeof logDroughtDiff === 'function' && DR_SNAP) logDroughtDiff(DR_SNAP, res.config);
    DR = res; DR.idx = {}; DR.date.forEach((s, i) => DR.idx[s] = i);
    DR_SNAP = JSON.parse(JSON.stringify(DR.config));
    if (msg) toast(msg);
    renderDrought();
    if (typeof refreshCalendarMetrics === 'function') refreshCalendarMetrics();
  } catch (e) {
    // a sikertelen mentés ne hagyja félkész állapotban a memóriát (különben minden további mentés is elbukna)
    if (DR_SNAP) { DR.config = JSON.parse(JSON.stringify(DR_SNAP)); renderDrought(); }
    toast('Nem menthető: ' + e.message + ' – a legutóbbi mentett állapot maradt érvényben.', 9000);
  }
}

// a ? ablakok közös részei (openModal, paramTable, readTable, presetek) a params.js-ben

async function openHelp(q) {
  const c = DR.config;
  await getPresets();
  const actions = (extra) => '<div class="modal-actions">' + (extra || '') + '<button class="btn" id="mreset">Szabványra visszaállít</button><button class="btn primary" id="mapply">Alkalmaz</button></div>';
  if (q === 'et0') {
    const cal = DR.cal.filter(m => m.ratio != null).map(m => '<tr><td>' + MONTHS_FULL[m.month - 1] + '</td><td>' + num(m.ratio, 2) + '</td><td>' + num(m.factor, 2) + '</td></tr>').join('');
    openModal('<h2>Referencia-párolgás (ET₀) és közös beállítások</h2>' + F_ET0 +
      '<h3>Beállítások</h3><div id="ptab">' + paramTable(GLOBAL_PARAMS, c.global, DR.defaults.global) + '</div>' +
      '<h3>Napsugárzás-kalibráció a mért adatból</h3><p>A hónap 3 legtisztább napján mért napi sugárzás az elméleti tiszta égbolti érték (Rso) hányada. Egy jó műszer ilyenkor kb. 0,95–1,0-et mér. <b>Javasolt szorzó a nyári hónapok alapján: ' + num(DR.suggestedFactor, 2) + '</b>. Télen a nagyobb eltérés valószínűleg árnyékolás (alacsony nap).</p>' +
      '<table class="ptable small"><thead><tr><th>Hónap</th><th>mért / elméleti</th><th>javasolt szorzó</th></tr></thead><tbody>' + cal + '</tbody></table>' +
      presetBar('globalis') + actions());
    const pairs = () => [[$('ptab'), GLOBAL_PARAMS]];
    const check = liveValidate(pairs);
    $('mapply').addEventListener('click', () => { if (readTable($('ptab'), GLOBAL_PARAMS, c.global)) { closeModal(); saveDroughtConfig('Beállítás mentve, újraszámolva.'); } });
    $('mreset').addEventListener('click', () => { $('ptab').innerHTML = paramTable(GLOBAL_PARAMS, DR.defaults.global, DR.defaults.global); check(); });
    wirePresets('globalis', () => { const o = Object.assign({}, c.global); return readTable($('ptab'), GLOBAL_PARAMS, o) ? o : false; },
      d => { $('ptab').innerHTML = paramTable(GLOBAL_PARAMS, Object.assign({}, DR.defaults.global, d), DR.defaults.global); check(); });
    return;
  }
  if (q === 'soil') {
    openModal('<h2>Talaj és lefolyás</h2>' +
      '<div class="preset-bar"><b>Talajtípus:</b>' + Object.entries(SOILS).map(([k, s]) => '<button class="btn soilbtn" data-s="' + k + '">' + esc(s.name.split(':')[0]) + '</button>').join('') + '</div>' +
      '<h3>Talaj</h3><div id="ptab">' + paramTable(SOIL_PARAMS, c.soil, DR.defaults.soil) + '</div>' +
      '<h3>Lefolyás</h3>' + F_RUNOFF + '<div id="ptab2">' + paramTable(RUNOFF_PARAMS, c.runoff, DR.defaults.runoff) + '</div>' +
      presetBar('talaj') + actions());
    const check = liveValidate(() => [[$('ptab'), SOIL_PARAMS], [$('ptab2'), RUNOFF_PARAMS]]);
    document.querySelectorAll('.soilbtn').forEach(b => b.addEventListener('click', () => { $('ptab').innerHTML = paramTable(SOIL_PARAMS, Object.assign({}, SOILS[b.dataset.s]), DR.defaults.soil); check(); }));
    $('mapply').addEventListener('click', () => {
      if (!validateTables([[$('ptab'), SOIL_PARAMS], [$('ptab2'), RUNOFF_PARAMS]])) return;
      readTable($('ptab'), SOIL_PARAMS, c.soil); readTable($('ptab2'), RUNOFF_PARAMS, c.runoff);
      closeModal(); saveDroughtConfig('Talaj mentve, újraszámolva.');
    });
    $('mreset').addEventListener('click', () => { $('ptab').innerHTML = paramTable(SOIL_PARAMS, DR.defaults.soil, DR.defaults.soil); $('ptab2').innerHTML = paramTable(RUNOFF_PARAMS, DR.defaults.runoff, DR.defaults.runoff); check(); });
    wirePresets('talaj', () => { const o = Object.assign({}, c.soil); return readTable($('ptab'), SOIL_PARAMS, o) ? o : false; },
      d => { $('ptab').innerHTML = paramTable(SOIL_PARAMS, Object.assign({}, DR.defaults.soil, d), DR.defaults.soil); check(); });
    return;
  }
  // profil
  const id = q.slice(2), p = profById(id);
  if (!p) return;
  const defs = {};
  TYPE_FIELDS[p.type].forEach(k => defs[k] = PROFILE_PARAMS[k]);
  const std = (DR.defaults.profiles.find(d => d.id === p.id) || DR.defaults.profiles.find(d => d.type === p.type && (p.type !== 'fa' || d.fajta === p.fajta)) || DR.defaults.profiles.find(d => d.type === p.type));
  const formula = p.type === 'felszin' ? F_SURFACE : F_BALANCE + (p.type === 'cserep' ? F_POT : '') + (p.type === 'fa' ? F_TREE : '');
  const fajtaText = p.type === 'fa' ? '<div class="kc-note" id="kcnote">' + (FAJTA[p.fajta] || FAJTA.egyedi).std + '<br>Talajtakarás most: <b>' + PROFILE_PARAMS.cover.options[p.cover] + '</b>. A Kc-értékeket a fenti szöveg alapján kézzel írd át, ha kell.</div>' : '';
  openModal('<h2>' + TYPE_ICON[p.type] + ' ' + esc(p.name) + ' <span class="muted">(' + TYPE_NAME[p.type] + ')</span></h2>' + formula + fajtaText +
    '<h3>Arányszámok</h3><div id="ptab">' + paramTable(defs, p, std) + '</div>' +
    presetBar('profil:' + p.type) + actions('<button class="btn danger" id="mdelete">Profil törlése</button>'));
  const check = liveValidate(() => [[$('ptab'), defs]]);
  const wireFajta = () => {
    const fsel = document.querySelector('#ptab [data-k="fajta"]');
    if (fsel) fsel.addEventListener('change', () => {
      const f = FAJTA[fsel.value];
      $('kcnote').innerHTML = f.std;
      if (f.kcIni != null) ['kcIni', 'kcMid', 'kcEnd'].forEach(k => { const el = document.querySelector('#ptab [data-k="' + k + '"]'); if (el) el.value = String(f[k]).replace('.', decSep()); });
      check();
    });
  };
  wireFajta();
  $('mapply').addEventListener('click', () => { if (readTable($('ptab'), defs, p)) { closeModal(); saveDroughtConfig('Mentve, újraszámolva.'); } });
  $('mreset').addEventListener('click', () => { const o = Object.assign({}, p, std, { id: p.id, name: p.name, enabled: p.enabled }); $('ptab').innerHTML = paramTable(defs, o, std); wireFajta(); check(); });
  $('mdelete').addEventListener('click', () => {
    // a zóna öntözési programjai és azok időszakai is törlődnek (különben a szerver elutasítja a mentést: „ismeretlen zóna”)
    const pgs = (DR.config.programs || []).filter(pg => pg.zone === p.id), pgIds = pgs.map(pg => pg.id);
    const nLog = DR.config.log.filter(l => l.zone === p.id).length;
    const extra = [nLog ? nLog + ' rögzített locsolása' : '', pgs.length ? pgs.length + ' öntözési programja (' + pgs.map(pg => '„' + pg.name + '”').join(', ') + ')' : ''].filter(Boolean);
    if (!confirm('Törlöd a(z) „' + p.name + '” profilt?' + (extra.length ? ' Vele együtt törlődik: ' + extra.join(' és ') + '.' : '') + ' Ez nem vonható vissza.')) return;
    DR.config.profiles = DR.config.profiles.filter(x => x.id !== p.id);
    DR.config.log = DR.config.log.filter(l => l.zone !== p.id);
    DR.config.programs = (DR.config.programs || []).filter(pg => pg.zone !== p.id);
    DR.config.schedules = (DR.config.schedules || []).filter(sc => !pgIds.includes(sc.program));
    closeModal(); saveDroughtConfig('Profil törölve.');
  });
  wirePresets('profil:' + p.type, () => { const o = Object.assign({}, p); if (!readTable($('ptab'), defs, o)) return false; delete o.id; delete o.enabled; return o; },
    d => { $('ptab').innerHTML = paramTable(defs, Object.assign({}, p, d, { id: p.id }), std); wireFajta(); check(); });
}

function openAdd() {
  openModal('<h2>Új növény / cserép</h2><p>Válaszd ki a típust. Utána a ? gombbal minden adata beállítható.</p>' +
    '<div class="add-grid">' +
    [['gyep', 'Gyep (újabb zóna)'], ['bokor', 'Bokor / cserjesor'], ['cserep', 'Cserép'], ['fa:barack', 'Barackfa'], ['fa:meggy', 'Meggyfa / cseresznye'], ['fa:altalanos', 'Egyéb lombos fa'], ['fa:egyedi', 'Egyedi fafajta']]
      .map(([k, l]) => '<button class="btn add-btn" data-k="' + k + '">' + TYPE_ICON[k.split(':')[0]] + ' ' + l + '</button>').join('') +
    '</div><div class="preset-bar"><b>Cserép tipikus méretei:</b> kicsi 3 l / Ø18 · közepes 10 l / Ø28 · nagy 25 l / Ø38 · dézsa 60 l / Ø50</div>' +
    '<p><label>Név: <input class="inp" id="addName" placeholder="pl. Nagy barack a kerítésnél"></label></p>');
  document.querySelectorAll('.add-btn').forEach(b => b.addEventListener('click', () => {
    const [type, fajta] = b.dataset.k.split(':');
    const base = DR.defaults.profiles.find(d => d.type === type && (!fajta || d.fajta === fajta)) || DR.defaults.profiles.find(d => d.type === type) || {};
    const p = Object.assign({}, base, { id: type + '_' + Date.now().toString(36), enabled: true, fajta: fajta || base.fajta || '' });
    p.name = $('addName').value.trim() || (fajta ? FAJTA[fajta].name : TYPE_NAME[type]);
    if (fajta && FAJTA[fajta].kcIni != null) Object.assign(p, { kcIni: FAJTA[fajta].kcIni, kcMid: FAJTA[fajta].kcMid, kcEnd: FAJTA[fajta].kcEnd });
    DR.config.profiles.push(p);
    closeModal();
    saveDroughtConfig('Hozzáadva: ' + p.name).then(() => openHelp('p:' + p.id));
  }));
}

// ------------------------------------------------------------------ vezérlők
$('drAdd').addEventListener('click', openAdd);
$('drSoil').addEventListener('click', () => openHelp('soil'));
$('drEt0').addEventListener('click', () => openHelp('et0'));
document.querySelectorAll('#drRange button').forEach(b => b.addEventListener('click', () => {
  drRange = b.dataset.r;
  document.querySelectorAll('#drRange button').forEach(x => x.classList.toggle('on', x === b));
  renderDrChart();
  renderFwiChart();
}));
window.addEventListener('resize', () => { if (drChart && currentTab === 'szarazsag') { drChart.resize(); if (fwiChart) fwiChart.resize(); } });

// ===================================================================== 4. lépés: tűzveszély (FWI), VPD, hőösszeg (GDD)
const FWI_CLASSES = [[5.2, 'nagyon alacsony', '#2e7d32'], [11.2, 'alacsony', '#7cb342'], [21.3, 'közepes', '#fdd835'], [38, 'magas', '#fb8c00'], [50, 'nagyon magas', '#e53935'], [1e9, 'extrém', '#6a1b9a']];
function fwiClass(v) { if (v == null || isNaN(v)) return ['–', '#999']; const c = FWI_CLASSES.find(c => v < c[0]); return [c[1], c[2]]; }
const FWI_PARAMS = {
  ffmc0: P('FFMC kezdőérték (szezon elején)', '', 'Van Wagner (1987): 85', { step: 1 }),
  dmc0: P('DMC kezdőérték', '', 'Van Wagner (1987): 6', { step: 1 }),
  dc0: P('DC kezdőérték', '', 'Van Wagner (1987): 15 (csapadékos tél után; száraz tél után nagyobb)', { step: 1 }),
  seasonStart: P('A tűzszezon kezdete (újraindulás)', 'HH-NN', 'a hóolvadás / tartós felmelegedés kezdete; nálunk kb. 03-01', { text: true }),
  windFactor: P('Szél-szorzó a 10 m-es szabványhoz', '', 'az FWI 10 m-es szélre készült; a te szélmérőd kb. 6 m-en van → 1,0–1,1', { step: 0.05 }),
  noonHourUtc: P('A „déli” észlelés időpontja (UTC)', 'óra', 'szabvány: 12:00 helyi téli (normál) idő = 11 UTC', { step: 1 })
};
const VPD_PARAMS = {
  low: P('Túl nedves alatta', 'kPa', '0,4 kPa: gombás betegségek kockázata', { step: 0.1 }),
  idealLo: P('Ideális sáv alja', 'kPa', '0,8 kPa', { step: 0.1 }),
  idealHi: P('Ideális sáv teteje', 'kPa', '1,2 kPa', { step: 0.1 }),
  high: P('Stresszhatár', 'kPa', '1,6 kPa felett a növények zárják a gázcserenyílásokat', { step: 0.1 }),
  severe: P('Erős stressz', 'kPa', '2,5 kPa felett erős vízvesztés, lankadás', { step: 0.1 })
};
const GDD_PARAMS = {
  baseT: P('Bázishőmérséklet', '°C', 'általános: 10 °C; gyep: 5 °C; gyümölcsfák: 4,5–5 °C', { step: 0.5 }),
  cap: P('Felső korlát', '°C', '30 °C (efölött a fejlődés nem gyorsul)', { step: 1 }),
  start: P('Az összegzés kezdete évente', 'HH-NN', '01-01 (vagy 03-01)', { text: true }),
  kcByGdd: P('A vegetációs szakaszok a hőösszegből', '', 'ha be van kapcsolva, a rügyfakadás és a teljes lomb napja (a Kc-görbe töréspontjai) nem fix dátum, hanem a hőösszeg alapján jön', { bool: true }),
  phenoBase: P('Fenológiai bázis', '°C', '5 °C (a szakaszokhoz és a mérföldkövekhez)', { step: 0.5 }),
  springGdd: P('Rügyfakadás', '°C·nap', 'kb. 100–150 fokcelsiusnap 5 °C felett (fajtától függ)', { step: 10 }),
  fullLeafGdd: P('Teljes lombozat', '°C·nap', 'kb. 400–500 fokcelsiusnap 5 °C felett', { step: 10 })
};
const F_FWI = `<div class="formula">FFMC (finom holt anyag: fű, avar – órás/napos) · DMC (felső talajréteg, bokrok alja – heti) · DC (mély talaj, fák – hónapos)<br>
ISI = 0,208 · f(W) · f(FFMC) · BUI = f(DMC, DC) · FWI = f(ISI, BUI)</div>
<p>A kanadai erdőtűz-veszélyességi index (Van Wagner 1987) a déli hőmérsékletből, páratartalomból, szélből és a 24 órás csapadékból naponta lépteti a három nedvességkódot. Az FFMC 0–101 (minél nagyobb, annál szárazabb a fű és az avar), a DMC és a DC felülről nyitott skála. Európában az EFFIS ezt használja. Kategóriák (EFFIS): &lt; 5,2 nagyon alacsony · 5,2–11,2 alacsony · 11,2–21,3 közepes · 21,3–38 magas · 38–50 nagyon magas · &gt; 50 extrém.</p>`;
const F_VPD = `<div class="formula">e<sub>s</sub>(T) = 0,6108 · exp(17,27 · T / (T + 237,3))  kPa<br>VPD = e<sub>s</sub>(T) · (1 − RH / 100)</div>
<p>A páranyomás-hiány azt mutatja, mennyire „szívja” a levegő a vizet a levelekből. Napon belül erősen változik: délután a legnagyobb.</p>`;
const F_GDD = `<div class="formula">GDD<sub>nap</sub> = max(0, (min(Tmax, korlát) + max(min(Tmin, korlát), bázis)) / 2 − bázis)<br>GDD = Σ GDD<sub>nap</sub> az év kezdete óta</div>
<p>A hőösszeg (fokcelsiusnap) a vegetáció előrehaladását méri: a virágzás, rügyfakadás évről évre inkább egy hőösszeghez, mint egy dátumhoz köthető. Ha bekapcsolod, a fák és a gyep vízigényének szakaszai (Kc) is ebből lépnek. A kerti naplóba írt virágzásokból a mérföldkövek pontosíthatók.</p>`;

let fwiChart = null;
async function renderDroughtExtras() {
  const d = await getDays(true);
  const last = a => a[a.length - 1];
  const c = DR.config, cards = [];
  // FWI
  const fwi = last(DR.fwi), [fname, fcol] = fwiClass(fwi);
  cards.push('<div class="card dr-card"><button class="qbtn" data-q2="fwi">?</button><h3>🔥 Tűzveszély (kanadai FWI)</h3>' +
    '<div class="big" style="color:' + fcol + '">' + num(fwi, 0) + '<small>' + fname + '</small></div>' +
    '<div class="line">Fű, avar (FFMC): <b>' + num(last(DR.ffmc), 0) + '</b> · felső talaj (DMC): <b>' + num(last(DR.dmc), 0) + '</b> · mély talaj, fák (DC): <b>' + num(last(DR.dc), 0) + '</b></div>' +
    '<div class="line small">Terjedési index (ISI) ' + num(last(DR.isi), 1) + ' · éghető anyag (BUI) ' + num(last(DR.bui), 0) + '</div></div>');
  // VPD
  const v = c.vpd, nowV = TODAY && TODAY.now && TODAY.now.temp != null && TODAY.now.hum != null ? 0.6108 * Math.exp(17.27 * TODAY.now.temp / (TODAY.now.temp + 237.3)) * (1 - TODAY.now.hum / 100) : null;
  const vband = x => x == null ? ['–', ''] : x < v.low ? ['túl nedves', 'mild'] : x < v.idealLo ? ['alacsony', 'ok'] : x <= v.idealHi ? ['ideális', 'ok'] : x <= v.high ? ['emelkedett', 'mild'] : x <= v.severe ? ['stressz', 'strong'] : ['erős stressz', 'severe'];
  const vmax = d.vpd[d.vpd.length - 1], v7 = d.vpd.slice(-7).filter(x => x != null);
  cards.push('<div class="card dr-card"><button class="qbtn" data-q2="vpd">?</button><h3>🍃 Páranyomás-hiány (VPD)</h3>' +
    '<div class="big">' + num(nowV, 2) + '<small>kPa most</small></div>' +
    '<div class="line"><span class="tag ' + vband(nowV)[1] + '">' + vband(nowV)[0] + '</span> napi max: <b>' + num(vmax, 2) + ' kPa</b> (' + vband(vmax)[0] + ')</div>' +
    '<div class="line small">Az utolsó 7 nap napi maximumainak átlaga: ' + num(v7.length ? v7.reduce((a, b) => a + b, 0) / v7.length : null, 2) + ' kPa</div></div>');
  // GDD
  const g = c.gdd, cum = last(DR.gddCum), pcum = last(DR.gddPheno);
  const year = DR.date[DR.date.length - 1].slice(0, 4);
  const ph = DR.pheno && DR.pheno[year];
  const reach = g.milestones.map(m => { const i = DR.gddPheno.findIndex((x, k) => DR.date[k].startsWith(year) && x >= m.gdd); return '<li>' + esc(m.name) + ' (' + num(m.gdd, 0) + '): <b>' + (i >= 0 ? DS.fmt(DR.date[i], { month: 'short', day: 'numeric' }) : 'még nem') + '</b></li>'; }).join('');
  let diaryLine = '';
  try {
    const diary = (MARKS && MARKS.diary) || await api('/api/store/naplo');
    const bloom = (Array.isArray(diary) ? diary : []).filter(x => x.tag === 'virágzás' && x.date && DR.idx[x.date] !== undefined);
    if (bloom.length) diaryLine = '<div class="line small">A naplód szerint: ' + bloom.map(x => esc(x.text || 'virágzás') + ' – ' + num(tdconv(DR.gddPheno[DR.idx[x.date]]), 0) + ' ' + tlabel() + '·nap (' + x.date.slice(5) + ')').join('; ') + '</div>';
  } catch (e) { }
  cards.push('<div class="card dr-card"><button class="qbtn" data-q2="gdd">?</button><h3>🌡️ Hőösszeg (GDD)</h3>' +
    '<div class="big">' + num(tdconv(cum), 0) + '<small>' + tlabel() + '·nap (' + ttext(g.baseT, 0) + ' felett)</small></div>' +
    '<div class="line">' + ttext(5, 0) + ' felett: <b>' + num(tdconv(pcum), 0) + '</b> ' + tlabel() + '·nap' + (ph ? ' · rügyfakadás: <b>' + ph[0].slice(5).replace('-', '.') + '.</b>, teljes lomb: <b>' + ph[1].slice(5).replace('-', '.') + '.</b>' : '') + '</div>' +
    '<ul class="mini-list">' + reach + '</ul>' + diaryLine +
    '<div class="line small">' + (g.kcByGdd ? '✔ A vízigény-szakaszok (Kc) a hőösszegből lépnek.' : 'A vízigény-szakaszok most fix dátumokból lépnek (a ? ablakban átállítható).') + '</div></div>');
  const box = $('drCards');
  const after = box.children[0];
  const tmp = document.createElement('div'); tmp.innerHTML = cards.join('');
  [...tmp.children].reverse().forEach(ch => box.insertBefore(ch, after ? after.nextSibling : null));
  box.querySelectorAll('[data-q2]').forEach(b => b.addEventListener('click', () => openHelp2(b.dataset.q2)));
  renderFwiChart();
}

function renderFwiChart() {
  const el = $('fwiChart');
  if (!el) return;
  const ink = cssVar('--ink'), muted = cssVar('--muted'), line = cssVar('--line'), card = cssVar('--card');
  const x = DR.date.map(s => DS.ms(s) + 12 * H);
  const end = x[x.length - 1], start = drRange === 'all' ? x[0] : drRange === 'year' ? Math.max(x[0], Date.UTC(new Date(end).getUTCFullYear(), 0, 1)) : end - (+drRange) * D;
  if (!fwiChart || fwiChart.isDisposed()) fwiChart = echarts.init(el);
  fwiChart.setOption({
    animation: false, legend: { top: 0, textStyle: { color: muted } }, grid: { left: 52, right: 112, top: 36, bottom: 30 },
    tooltip: { trigger: 'axis', confine: true, backgroundColor: card, borderColor: line, textStyle: { color: ink },
      formatter: ps => '<b>' + F.dayY.format(ps[0].axisValue) + '</b><br>' + ps.filter(p => p.value && p.value[1] != null).map(p => p.marker + ' ' + p.seriesName + ': <b>' +
        (p.seriesIndex === 2 ? rtext(p.value[2], 1) : p.seriesIndex === 1 ? num(p.value[1], 0) + ' ' + tlabel() + '·nap' : num(p.value[1], 1) + ' (' + fwiClass(p.value[1])[0] + ')') + '</b>').join('<br>') },
    xAxis: { type: 'time', min: start, max: end + D / 2, axisLabel: { color: muted, formatter: v => F.day.format(v) } },
    yAxis: [{ type: 'value', name: 'FWI', min: 0, nameTextStyle: { color: muted }, axisLabel: { color: muted }, splitLine: { lineStyle: { color: line, opacity: .5 } } },
            { type: 'value', name: tlabel() + '·nap', min: 0, nameTextStyle: { color: muted }, axisLabel: { color: muted }, splitLine: { show: false } },
            { type: 'value', name: 'eső, ' + rlabel(), min: 0, position: 'right', offset: 56, nameTextStyle: { color: cssVar('--c-rain') }, axisLabel: { color: cssVar('--c-rain') }, splitLine: { show: false } }],
    series: [
      { name: 'Tűzveszély (FWI)', type: 'bar', barMaxWidth: 6, data: DR.fwi.map((v, i) => ({ value: [x[i], v], itemStyle: { color: fwiClass(v)[1] } })) },
      { name: 'Hőösszeg (' + ttext(DR.config.gdd.baseT, 0) + ' felett)', type: 'line', yAxisIndex: 1, showSymbol: false, data: DR.gddCum.map((v, i) => [x[i], tdconv(v)]), lineStyle: { color: '#e4572e', width: 2 }, itemStyle: { color: '#e4572e' } },
      // esős napok kék pöttyel: magasság = napi csapadék (külön tengely), a méret a mennyiséggel nő
      { name: 'Csapadék', type: 'scatter', yAxisIndex: 2, data: DR.rain.map((v, i) => v >= 0.2 ? [x[i], rconv(v), v] : null).filter(Boolean),
        symbolSize: p => Math.max(5, Math.min(26, 4 + 3.2 * Math.sqrt(p[2]))), itemStyle: { color: cssVar('--c-rain'), opacity: .75, borderColor: '#fff', borderWidth: .5 } }
    ]
  }, true);
}

async function openHelp2(q) {
  const c = DR.config;
  await getPresets();
  const conf = q === 'fwi' ? { title: '🔥 Kanadai erdőtűz-veszélyességi index (FWI)', f: (typeof fwiExplainer === 'function' ? fwiExplainer() : F_FWI), defs: FWI_PARAMS, obj: c.fwi, std: DR.defaults.fwi, kind: 'fwi' }
    : q === 'vpd' ? { title: '🍃 Páranyomás-hiány (VPD)', f: F_VPD, defs: VPD_PARAMS, obj: c.vpd, std: DR.defaults.vpd, kind: 'vpd' }
    : { title: '🌡️ Hőösszeg (GDD) és vegetációs szakaszok', f: F_GDD, defs: GDD_PARAMS, obj: c.gdd, std: DR.defaults.gdd, kind: 'gdd' };
  const ms = q === 'gdd' ? '<h3>Mérföldkövek (5 °C feletti hőösszeg)</h3><p>Soronként: név = fokcelsiusnap. A kerti naplóba írt virágzásokból pontosíthatod őket.</p><textarea class="inp" id="msText" rows="5" style="width:100%">' + esc(c.gdd.milestones.map(m => m.name + ' = ' + m.gdd).join('\n')) + '</textarea><div class="perr" id="msErr"></div>' : '';
  openModal('<h2>' + conf.title + '</h2>' + conf.f + '<h3>Arányszámok</h3><div id="ptab">' + paramTable(conf.defs, conf.obj, conf.std) + '</div>' + ms +
    presetBar(conf.kind) + '<div class="modal-actions"><button class="btn" id="mreset">Szabványra visszaállít</button><button class="btn primary" id="mapply">Alkalmaz</button></div>');
  // mérföldkövek: minden nem üres sor „név = szám” legyen
  const parseMs = () => {
    const rows = $('msText').value.split('\n').map(l => l.trim()).filter(Boolean);
    const out = [], bad = [];
    rows.forEach((l, i) => {
      const p = l.split('=');
      const v = p.length === 2 ? parseFloat(p[1].trim().replace(',', '.')) : NaN;
      if (p.length !== 2 || !p[0].trim() || isNaN(v) || v < 0 || v > 10000 || !/^\s*\d+([.,]\d+)?\s*$/.test(p[1])) bad.push(i + 1);
      else out.push({ name: p[0].trim(), gdd: v });
    });
    $('msErr').textContent = bad.length ? '⚠ Hibás sor: ' + bad.join(', ') + ' – formátum: név = szám (0–10000)' : '';
    $('msText').classList.toggle('bad', bad.length > 0);
    return bad.length ? null : out;
  };
  const check = liveValidate(() => [[$('ptab'), conf.defs]]);
  if (q === 'gdd') {
    const both = () => { const ok = check() && parseMs() !== null; $('mapply').disabled = !ok; };
    $('msText').addEventListener('input', both); both();
  }
  $('mapply').addEventListener('click', () => {
    let msList = null;
    if (q === 'gdd') { msList = parseMs(); if (!msList) return; }
    if (!readTable($('ptab'), conf.defs, conf.obj)) return;
    if (msList) c.gdd.milestones = msList;
    closeModal(); saveDroughtConfig('Mentve, újraszámolva.');
  });
  $('mreset').addEventListener('click', () => { $('ptab').innerHTML = paramTable(conf.defs, conf.std, conf.std); check(); });
  wirePresets(conf.kind, () => { const o = Object.assign({}, conf.obj); return readTable($('ptab'), conf.defs, o) ? o : false; },
    dd => { $('ptab').innerHTML = paramTable(conf.defs, Object.assign({}, conf.std, dd), conf.std); check(); });
  if (q === 'fwi' && typeof wireFwiExplainer === 'function') wireFwiExplainer();
}
