'use strict';
/* ƒx ablakok: minden számolt érték mellett képlet, magyarázat, paraméterek (szimbólummal, szabványos értékkel, visszaállítással, presetekkel).
   A paraméterek a megjelenito.ini-ben vannak (/api/ini), a szárazság-modul sajátjai a meglévő ? ablakokban. */

// [ini-kulcs, felirat, szimbólum, egység, magyarázat a szabványos értékhez]
const FX = {
  feels: {
    title: '🌡️ Hőérzet',
    f: 'T ≤ 10 °C és v &gt; 4,8 km/h: WC = 13,12 + 0,6215·T − 11,37·v<sup>0,16</sup> + 0,3965·T·v<sup>0,16</sup><br>T ≥ 27 °C: hőindex (Rothfusz/NOAA, a hőmérséklet és a páratartalom függvénye)<br>közte: a léghőmérséklet',
    text: 'Hidegben a szél „elviszi” a test melegét (szélhűtés, kanadai–amerikai JAG/TI 2001), melegben a magas páratartalom miatt rosszabbul hűt az izzadás (hőindex). Az értékeket az állomás küldi (WU), a program csak kiválasztja, melyik érvényes.'
  },
  comfort: {
    title: '💧 Komfort (harmatpont)',
    f: 'T<sub>d</sub> &lt; 10 °C száraz, kellemes · 10–16 kellemes · 16–18 kissé párás · 18–21 fülledt · &gt; 21 nagyon fülledt',
    text: 'A harmatpont a levegő tényleges víztartalmát mutatja (a relatív páratartalom a hőmérséklettel együtt változik, ezért kevésbé jó mérce). Futásnál és alvásnál a 18 °C feletti harmatpont már megterhelő.'
  },
  beaufort: {
    title: '🌬️ Beaufort-fokozat (tört érték)',
    f: 'B = (v / 0,836)<sup>2/3</sup>, v m/s-ban (WMO: v = 0,836 · B<sup>3/2</sup>)<br>1 csomó = 1,852 km/h · 1 m/s = 3,6 km/h',
    text: 'A klasszikus Beaufort-skála egész fokozatait a WMO-képlet folytonossá teszi: így látszik, hogy 3,3 vagy 3,9 Bft fújt. A fokozat neve (pl. „gyenge szél”) a kerekített értékből jön. Példa: 18 km/h = 5,0 m/s → 3,3 Bft.'
  },
  sun: {
    title: '☀️ Napsütéses órák és napenergia',
    f: 'napsütéses órák = Σ Δt, ahol a napsugárzás ≥ 120 W/m² (WMO)<br>napenergia (kWh/m²) = Σ (napsugárzás · Δt) / 3 600 000',
    text: 'A WMO szerint „süt a nap”, ha a közvetlen napsugárzás legalább 120 W/m². A mi állomásunk teljes (globális) sugárzást mér, ezért ez jó közelítés. A napenergia a vízszintes felületre jutó energia.'
  },
  zambretti: {
    title: '🌤️ Zambretti-előrejelzés',
    f: 'Z = f(tengerszintre átszámított légnyomás, 3 órás nyomásváltozás, szélirány, évszak)',
    text: 'Az 1915-ös Negretti & Zambra „időjós korong” algoritmusa: a nyomás szintje és változása szerint 26 szöveges előrejelzés közül választ, a következő ~12 órára. Hagyományos, egyszerű becslés – modern előrejelzést nem helyettesít.'
  },
  rainevent: {
    title: '🌧️ Esőesemények és felhőszakadás',
    f: 'egy esemény vége: legalább <i>t</i><sub>szünet</sub> perc eső nélkül<br>összeg = Σ csapadék az eseményben · intenzitás = mm/h · felhőszakadás: max. intenzitás ≥ <i>I</i><sub>fh</sub>',
    text: 'Az egymást követő esős mérésekből eseményeket fűz össze; ha hosszabb szünet jön, új esemény kezdődik.',
    params: [['EsoesemenySzunetPerc', 'Szünet egy esemény végéig', 't_szünet', 'perc', 'tipikus: 30 perc'], ['FelhoszakadasMmH', 'Felhőszakadás határa', 'I_fh', 'mm/h', 'OMSZ-szerű: 30 mm/h']]
  },
  front: {
    title: '⚡ Front- és zivatardetektor',
    f: 'front: ΔT<sub>1 óra</sub> ≤ −ΔT<sub>f</sub> és (lökés ≥ v<sub>f</sub> vagy szélfordulás ≥ φ<sub>f</sub>)<br>zivatar: intenzitás ≥ I<sub>z</sub> és lökés ≥ v<sub>z</sub> és lehűlés ≥ ΔT<sub>z</sub>',
    text: 'A mért idősorban keres jellegzetes mintákat: hidegfrontnál gyors lehűlés, szélfordulás és lökések; zivatarnál heves eső, lökés és lehűlés egyszerre. A jelölők az idővonalon látszanak.',
    params: [['FrontHomersekletEses', 'Front: lehűlés 1 órán belül', 'ΔT_f', '°C', '4 °C'], ['FrontLokes', 'Front: lökés', 'v_f', 'km/h', '40 km/h'], ['FrontSzelfordulas', 'Front: szélfordulás', 'φ_f', '°', '45°'],
      ['ZivatarIntenzitas', 'Zivatar: csapadékintenzitás', 'I_z', 'mm/h', '20 mm/h'], ['ZivatarLokes', 'Zivatar: lökés', 'v_z', 'km/h', '45 km/h'], ['ZivatarLehules', 'Zivatar: lehűlés', 'ΔT_z', '°C', '3 °C']]
  },
  thresholds: {
    title: '📊 Statisztikai küszöbök',
    f: 'hőségnap: T<sub>max</sub> ≥ T<sub>h</sub> · trópusi éjszaka: T<sub>min</sub> ≥ T<sub>tr</sub> · fagyos nap: T<sub>min</sub> &lt; T<sub>f</sub> · téli nap: T<sub>max</sub> &lt; T<sub>t</sub>',
    text: 'A napok számlálása a megadott határok szerint (az OMSZ szokásos küszöbeivel). Csak a legalább 80%-ban lefedett napok számítanak.',
    params: [['HosegNap', 'Hőségnap határa', 'T_h', '°C', 'OMSZ: 30 °C'], ['TropusiEjszaka', 'Trópusi éjszaka határa', 'T_tr', '°C', 'OMSZ: 20 °C'], ['FagyosNap', 'Fagyos nap határa', 'T_f', '°C', '0 °C'], ['TeliNap', 'Téli nap határa', 'T_t', '°C', '0 °C'], ['HianyPerc', 'Adathiány, ha a szünet hosszabb', 't_hiány', 'perc', '15 perc (az állomás 5 percenként küld)']]
  },
  windy: {
    title: '⛵ Szeles délutánok',
    f: 'szeles délután: átlagszél(t<sub>tól</sub> … t<sub>ig</sub>) ≥ v<sub>sz</sub>, és legalább 12 mérés az ablakban',
    text: 'Vitorlázáshoz: azok a napok, amikor délután tartósan fújt. 20 km/h ≈ 4 Bft (3 Bft fölött).',
    params: [['DelutanTol', '„Délután” kezdete', 't_tól', 'óra', '12'], ['DelutanIg', '„Délután” vége', 't_ig', 'óra', '18'], ['SzelesDelutanKmh', 'Átlagszél legalább', 'v_sz', 'km/h', '20 km/h ≈ 4 Bft']]
  },
  projection: {
    title: '📈 Hónap végi becslés',
    f: 'becslés = (eddigi összeg / adatos napok száma) · a hónap napjainak száma',
    text: 'A szaggatott keret azt mutatja, mennyi lenne a hónap végére, ha ugyanilyen ütemben folytatódna. Átlaghőmérsékletnél nincs értelme, ezért ott nem jelenik meg.'
  },
  runmodel: {
    title: '🏃 Futóruházat-tanácsadó',
    f: 'T<sub>hat</sub> = hőérzet − 0,3·max(0, v − 10) − 2·[esik] + 1,5·[napsütés]<br>érzet = β₀ + β₁·T<sub>hat</sub> + β₂·rétegek + β₃·intenzitás (lineáris modell, legkisebb négyzetek)',
    text: 'A futás után megadott érzetből (−3 fázott … +3 melege volt) tanul: kevés adatnál az ökölszabály a súlyosabb („öltözz úgy, mintha 10 °C-kal melegebb lenne”), több futás után egyre inkább a saját adatod. A javasolt rétegszám az, ahol a becsült érzet a legközelebb van a 0-hoz.'
  },
  suspect: { title: '🔎 Gyanús mérések', custom: true }
};

// ------------------------------------------------------------------ ablak
let INI = null;
async function getIni(force) { if (!INI || force) INI = await api('/api/ini'); return INI; }

function fxDefs(params, ini) {
  const defs = {};
  params.forEach(([k, label, sym, unit, std]) => {
    const r = ini.ranges[k] || [];
    defs[k] = { label, unit, std, rule: r[3] ? null : [r[0], r[1]].concat(r[2] ? ['int'] : []), bool: !!r[3] };
    SYM[k] = sym.replace(/_/g, ' ');
  });
  return defs;
}
const iniNum = v => { const x = parseFloat(String(v).replace(',', '.')); return isNaN(x) ? v : x; };
const iniVal = (ini, k, which) => { const v = ini[which || 'values'][k]; return ini.ranges[k] && ini.ranges[k][3] ? String(v).toLowerCase() === 'true' : iniNum(v); };

async function openFx(key) {
  if (key === 'et0') return openHelp('et0');
  if (['fwi', 'vpd', 'gdd'].includes(key)) return openHelp2(key);
  const fx = FX[key];
  if (!fx) return;
  if (fx.custom) return openSuspect();
  let html = '<h2>' + fx.title + '</h2><div class="formula">' + fx.f + '</div><p>' + fx.text + '</p>';
  if (!fx.params) { openModal(html); return; }
  const ini = await getIni(true);
  await getPresets();
  const defs = fxDefs(fx.params, ini);
  const obj = {}, std = {};
  Object.keys(defs).forEach(k => { obj[k] = iniVal(ini, k); std[k] = iniVal(ini, k, 'defaults'); });
  html += '<h3>Paraméterek</h3><div id="fxTable">' + paramTable(defs, obj, std) + '</div>' +
    presetBar('fx_' + key) +
    '<div class="row2"><button class="btn primary" id="mapply">Alkalmaz</button><button class="btn" id="fxReset">Visszaállítás a szabványos értékekre</button><span class="pmsg" id="fxMsg"></span></div>' +
    '<p class="hint">Az Alkalmaz után a program újraszámolja a statisztikákat (pár másodperc). A változás a Beállítások › Változásnapló-ban visszaállítható.</p>';
  openModal(html);
  const root = () => $('fxTable');
  liveValidate(() => [[root(), defs]]);
  const setData = d => { $('fxTable').innerHTML = paramTable(defs, Object.assign({}, obj, d), std); validateTables([[root(), defs]]); };
  wirePresets('fx_' + key, () => { const o = {}; return readTable(root(), defs, o) ? o : false; }, setData);
  $('fxReset').addEventListener('click', () => { setData(std); $('fxMsg').textContent = 'A szabványos értékek betöltve – az Alkalmaz gombbal mentheted.'; });
  $('mapply').addEventListener('click', async () => {
    const o = {};
    if (!readTable(root(), defs, o)) { $('fxMsg').textContent = '⚠ Javítsd a pirossal jelölt mezőket.'; return; }
    if (key === 'windy' && !(o.DelutanIg > o.DelutanTol)) { $('fxMsg').textContent = '⚠ A „délután” vége legyen későbbi a kezdeténél.'; return; }
    try {
      INI = await post('/api/ini', o);
      $('fxMsg').textContent = '✔ Mentve, újraszámolás…';
      await waitReload();
      $('fxMsg').textContent = '✔ Mentve és újraszámolva.';
    } catch (e) { $('fxMsg').textContent = '⚠ ' + e.message; }
  });
}

// a háttérben induló újraolvasás megvárása, utána a nézet frissítése
async function waitReload() {
  await sleep(400);
  for (let i = 0; i < 120; i++) {
    const s = await api('/api/status').catch(() => null);
    if (s && s.status === 'ready') break;
    await sleep(500);
  }
  if (typeof start === 'function') await start();
}

// ------------------------------------------------------------------ gyanús mérések (Beállítások kártya és ƒx ablak)
const SUSPECT_RULES = [
  ['JelolWuBizonytalan', 'WU', 'qcStatus = 0 – a WU minőségellenőrzése bizonytalannak jelölte', ['qc0']],
  ['JelolWuHibas', 'WU', 'qcStatus = −1 – a WU minőségellenőrzése hibásnak jelölte', ['qc-1']],
  ['JelolLehetetlen', 'program', 'Lehetetlen érték (a határokon kívül: UV, hőmérséklet, páratartalom, légnyomás, csapadék, szél, napsugárzás)', ['uv', 'temp', 'hum', 'pres', 'rain', 'wind', 'solar'],
    [['UvMax', 'UV legfeljebb', 'UV_max', ''], ['HomersekletMin', 'Hőmérséklet legalább', 'T_min', '°C'], ['HomersekletMax', 'Hőmérséklet legfeljebb', 'T_max', '°C'], ['LegnyomasMin', 'Légnyomás legalább', 'p_min', 'hPa'], ['LegnyomasMax', 'Légnyomás legfeljebb', 'p_max', 'hPa'],
      ['CsapadekIntenzitasMax', 'Csapadékintenzitás legfeljebb', 'I_max', 'mm/h'], ['SzellokesMax', 'Lökés legfeljebb', 'v_max', 'km/h'], ['NapsugarzasMax', 'Napsugárzás legfeljebb', 'S_max', 'W/m²']]],
  ['JelolUgras', 'program', 'Tüske: a hőmérséklet mindkét szomszédjától ugyanabba az irányba ugrik', ['spike'], [['UgrasFok', 'Ugrás legalább', 'ΔT', '°C']]],
  ['JelolNapsutes', 'program', 'Napsütés miatti túlmelegedés: meleg + erős napsugárzás + szélcsend (a hőmérő árnyékolója felmelegszik)', ['sun'],
    [['NapsutesHomerseklet', 'Hőmérséklet legalább', 'T_nap', '°C'], ['NapsutesSugarzas', 'Napsugárzás legalább', 'S_nap', 'W/m²'], ['NapsutesSzel', 'Szél legfeljebb', 'v_nap', 'km/h']]],
  ['JelolKieses', 'program', 'Kiesés utáni első mérés (az állomás újraindulása után az első érték gyakran pontatlan)', ['restart'], [['KiesesPerc', 'Kiesés, ha a szünet hosszabb', 't_k', 'perc']]]
];
async function renderSuspect(box) {
  const ini = await getIni(true);
  const defs = {};
  SUSPECT_RULES.forEach(r => (r[4] || []).forEach(([k, label, sym, unit]) => { const rg = ini.ranges[k]; defs[k] = { label, unit, rule: [rg[0], rg[1]].concat(rg[2] ? ['int'] : []) }; SYM[k] = sym.replace(/_/g, ' '); }));
  box.innerHTML = '<p>Semmit nem töröl: a jelölt méréseket a napi statisztikák, a rekordok és a számítások kihagyják (ha a „Gyanús adatok kizárása” be van kapcsolva), a grafikonon pontként látszanak.</p>' +
    '<div class="row2"><label class="switch"><input type="checkbox" id="suExcl"' + (iniVal(ini, 'GyanusKizarasa') ? ' checked' : '') + '><span class="slider"></span><span>A rekordoknál alapból zárja ki a gyanús adatokat</span></label></div>' +
    SUSPECT_RULES.map(([k, who, text, codes, ps], j) => '<div class="su-rule"><label class="switch"><input type="checkbox" data-su="' + k + '"' + (iniVal(ini, k) ? ' checked' : '') + '><span class="slider"></span></label>' +
      '<span class="tag ' + (who === 'WU' ? 'mild' : 'ok') + '">' + (who === 'WU' ? 'WU jelöli' : 'a program jelöli') + '</span> ' + esc(text) + ' <b class="su-n" data-codes="' + codes.join(',') + '"></b>' +
      (ps ? '<div class="su-params" data-j="' + j + '">' + paramTable(Object.fromEntries(ps.map(p => [p[0], defs[p[0]]])), Object.fromEntries(ps.map(p => [p[0], iniVal(ini, p[0])])), Object.fromEntries(ps.map(p => [p[0], iniVal(ini, p[0], 'defaults')]))) + '</div>' : '') + '</div>').join('') +
    '<div class="su-sum" id="suSum"></div>' +
    '<div class="row2"><button class="btn primary" id="suSave">Alkalmaz</button><button class="btn" id="suReset">Szabványos értékek</button><span class="pmsg" id="suMsg"></span></div>';
  const collect = () => {
    const o = {};
    box.querySelectorAll('[data-su]').forEach(c => o[c.dataset.su] = c.checked);
    o.GyanusKizarasa = $('suExcl').checked;
    const pairs = [...box.querySelectorAll('.su-params')].map(el => [el, defs]);
    if (!validateTables(pairs)) return null;
    pairs.forEach(([el]) => Object.assign(o, tableValues(el, defs)));
    if (o.HomersekletMin != null && o.HomersekletMax != null && !(o.HomersekletMax > o.HomersekletMin)) return null;
    if (o.LegnyomasMin != null && o.LegnyomasMax != null && !(o.LegnyomasMax > o.LegnyomasMin)) return null;
    return o;
  };
  const preview = async () => {
    const o = collect();
    if (!o) { $('suSum').textContent = '⚠ Javítsd a pirossal jelölt mezőket.'; $('suSave').disabled = true; return; }
    $('suSave').disabled = false;
    try {
      const p = await post('/api/ini/preview', o);
      const byCode = {}; p.counts.forEach(c => byCode[c.code] = c.count);
      box.querySelectorAll('.su-n').forEach(el => { const n = el.dataset.codes.split(',').reduce((s, c) => s + (byCode[c] || 0), 0); el.textContent = '– ' + num(n, 0) + ' jelölés'; });
      $('suSum').innerHTML = 'Így <b>' + num(p.flagged, 0) + '</b> mérés lenne gyanús a ' + num(p.n, 0) + '-ból (' + num(p.flagged / Math.max(1, p.n) * 100, 2) + '%).';
    } catch (e) { $('suSum').textContent = '⚠ ' + e.message; }
  };
  box.addEventListener('change', () => { clearTimeout(box._t); box._t = setTimeout(preview, 250); });
  box.addEventListener('input', e => { if (e.target.classList.contains('inp')) { clearTimeout(box._t); box._t = setTimeout(preview, 500); } });
  $('suReset').addEventListener('click', () => {
    box.querySelectorAll('[data-su]').forEach(c => c.checked = iniVal(ini, c.dataset.su, 'defaults'));
    box.querySelectorAll('.su-params [data-k]').forEach(el => el.value = String(iniVal(ini, el.dataset.k, 'defaults')).replace('.', decSep()));
    preview();
  });
  $('suSave').addEventListener('click', async () => {
    const o = collect();
    if (!o) return;
    try { INI = await post('/api/ini', o); $('suMsg').textContent = '✔ Mentve, újraszámolás…'; await waitReload(); $('suMsg').textContent = '✔ Mentve és újraszámolva.'; }
    catch (e) { $('suMsg').textContent = '⚠ ' + e.message; }
  });
  preview();
}
function openSuspect() {
  openModal('<h2>🔎 Gyanús mérések</h2><div id="suBox"></div>');
  renderSuspect($('suBox'));
}

// ------------------------------------------------------------------ ƒx ikonok elhelyezése (a címek szövege alapján)
const FX_PLACES = [
  ['attekintes', 'h3', /^(Most|Utolsó mérés) · /, 'feels'], ['attekintes', 'h3', /^Szél$/, 'beaufort'], ['attekintes', 'h3', /^Légnyomás$/, 'zambretti'],
  ['attekintes', 'h3', /^Napsugárzás$/, 'sun'], ['attekintes', 'h3', /^Páratartalom$/, 'comfort'], ['attekintes', 'h3', /^Figyelő/, 'front'],
  ['futas', 'h3', /^Komfort most$/, 'comfort'], ['futas', 'h3', /^Mit vegyek fel/, 'runmodel'],
  ['eso', 'h2', /^Eső$/, 'rainevent'], ['szel', 'h2', /^Szél$/, 'windy'], ['szel', 'h3', /^Átlagszél$/i, 'beaufort'],
  ['stat', 'h2', /^Rekordok$/, 'thresholds'], ['stat', 'h2', /^Ez a hónap/, 'projection'], ['stat', 'h2', /^Adatminőség$/, 'suspect'],
  ['idovonal', 'h2', /^Idővonal$/, 'front']
];
function decorateFx() {
  if (typeof decorateCalib === 'function') decorateCalib();
  FX_PLACES.forEach(([page, tag, re, key]) => {
    const pg = document.querySelector('[data-page="' + page + '"]');
    if (!pg) return;
    pg.querySelectorAll(tag).forEach(h => {
      if (h.querySelector('.fx')) return;
      const t = (h.firstChild && h.firstChild.nodeType === 3 ? h.firstChild.textContent : h.textContent).trim();
      if (re.test(t)) h.insertAdjacentHTML('beforeend', ' <button class="fx" data-fx="' + key + '" title="Képlet, magyarázat és beállítások">ƒx</button>');
    });
  });
}
document.addEventListener('click', e => {
  const b = e.target.closest && e.target.closest('.fx[data-fx]');
  if (!b) return;
  e.preventDefault(); e.stopPropagation();
  openFx(b.dataset.fx);
}, true);
new MutationObserver(() => { clearTimeout(window._fxT); window._fxT = setTimeout(decorateFx, 120); }).observe(document.querySelector('main') || document.body, { childList: true, subtree: true });
