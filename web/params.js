'use strict';
/* Közös paraméterkezelés a ? és ƒx ablakokhoz: táblázat, szimbólumok, ellenőrzés, presetek */

// ------------------------------------------------------------------ ablak
// valódi párbeszédablak (7.5): a fókusz bekerül és bent marad, a háttér nem érhető el, bezáráskor visszatér a megnyitóra
let MODAL_OPENER = null, MODAL_DIRTY = false, MODAL_VALIDATE = null;
function setBackgroundInert(on) { document.querySelectorAll('body > header, body > main, body > footer').forEach(el => { if (on) el.setAttribute('inert', ''); else el.removeAttribute('inert'); }); }
function openModal(html) {
  if ($('modal').classList.contains('hidden')) MODAL_OPENER = document.activeElement;
  $('modalBody').innerHTML = html;
  MODAL_DIRTY = false; MODAL_VALIDATE = null;
  const h = $('modalBody').querySelector('h2, h3');
  if (h) { h.id = 'modalTitle'; h.tabIndex = -1; $('modal').setAttribute('aria-labelledby', 'modalTitle'); } else $('modal').removeAttribute('aria-labelledby');
  $('modal').classList.remove('hidden');
  document.body.style.overflow = 'hidden';
  setBackgroundInert(true);
  $('modal').scrollTop = 0;
  (h || $('modalClose')).focus();
}
function closeModal() {
  $('modal').classList.add('hidden'); document.body.style.overflow = '';
  setBackgroundInert(false);
  MODAL_DIRTY = false; MODAL_VALIDATE = null;
  if (MODAL_OPENER && document.contains(MODAL_OPENER)) MODAL_OPENER.focus();
  MODAL_OPENER = null;
}
// a ✕, az Esc és a háttérre kattintás: ha a táblában átírtál valamit, előbb rákérdez (7.19)
async function requestCloseModal() {
  if (MODAL_DIRTY && !(await askConfirm('Elveted a változásokat?', { sub: 'Az ablakban átírt értékek nincsenek elmentve.', ok: 'Elvetem', cancel: 'Folytatom a szerkesztést' }))) return;
  closeModal();
}
$('modalClose').addEventListener('click', requestCloseModal);
$('modal').addEventListener('click', e => { if (e.target.id === 'modal') requestCloseModal(); });
// egyetlen figyelő a modálon (11.11: korábban minden megnyitás újabbat tett fel)
['input', 'change'].forEach(ev => $('modalBody').addEventListener(ev, e => {
  if (e.target.matches('input, select, textarea') && !e.target.closest('.preset-bar')) MODAL_DIRTY = true;
  if (MODAL_VALIDATE) MODAL_VALIDATE();
}));
document.addEventListener('keydown', e => {
  if (!$('cdlg').classList.contains('hidden')) { if (e.key === 'Escape') { e.preventDefault(); $('cdlgNo').click(); } else if (e.key === 'Tab') trapTab(e, $('cdlg')); return; }
  if ($('modal').classList.contains('hidden')) return;
  if (e.key === 'Escape') { e.preventDefault(); requestCloseModal(); }
  else if (e.key === 'Tab') trapTab(e, $('modal'));
});
// Tab / Shift+Tab körbe a párbeszédablakon belül
function trapTab(e, box) {
  const f = [...box.querySelectorAll('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])')].filter(el => !el.disabled && el.offsetParent !== null);
  if (!f.length) return;
  const first = f[0], last = f[f.length - 1];
  if (e.shiftKey && (document.activeElement === first || !box.contains(document.activeElement))) { e.preventDefault(); last.focus(); }
  else if (!e.shiftKey && (document.activeElement === last || !box.contains(document.activeElement))) { e.preventDefault(); first.focus(); }
}

// saját megerősítő ablak a böngésző „OK / Mégse” helyett (7.30): cselekvő gomb és a következmény kiírva
function askConfirm(msg, o) {
  o = o || {};
  return new Promise(resolve => {
    const box = $('cdlg'), opener = document.activeElement, wasInert = $('modal').classList.contains('hidden');
    $('cdlgMsg').textContent = msg;
    $('cdlgSub').textContent = o.sub || ''; $('cdlgSub').classList.toggle('hidden', !o.sub);
    $('cdlgOk').textContent = o.ok || 'Igen'; $('cdlgOk').className = 'btn ' + (o.danger ? 'danger' : 'primary');
    $('cdlgNo').textContent = o.cancel || 'Mégse';
    box.classList.remove('hidden');
    if (wasInert) setBackgroundInert(true); else $('modal').setAttribute('inert', '');
    const done = v => {
      box.classList.add('hidden'); $('cdlgOk').onclick = $('cdlgNo').onclick = null;
      if (wasInert) setBackgroundInert(false); else $('modal').removeAttribute('inert');
      if (opener && document.contains(opener)) opener.focus();
      resolve(v);
    };
    $('cdlgOk').onclick = () => done(true); $('cdlgNo').onclick = () => done(false);
    box.onclick = e => { if (e.target === box) done(false); };
    $('cdlgNo').focus();
  });
}

// ------------------------------------------------------------------ szabályok (megengedett tartomány) és képletbeli szimbólumok
const MD = { md: true };
const RULES = {
  // profilok
  name: { req: true, maxLen: 60 }, kc: [0.1, 2], kcIni: [0, 2], kcMid: [0, 2], kcEnd: [0, 2], kcWinter: [0, 2],
  zr: [0.05, 5], p: [0.05, 0.95], sprinklerRate: [1, 100], maxSession: [1, 100], lawnArea: [0, 100000],
  area: [0.01, 1000], count: [1, 10000, 'int'], dripCount: [0, 100, 'int'], dripLph: [0.1, 100], potLiters: [0.1, 2000], potDiam: [5, 300],
  availFrac: [0.05, 0.8], canopyD: [0.2, 40], height: [0.2, 60], trunkCm: [1, 1000], age: [0, 500, 'int'],
  formFactor: [0.1, 1], sapFrac: [0.05, 1], waterFrac: [0.01, 0.6], autoMinutes: [0, 600],
  autoDays: { pattern: /^\s*[1-7](\s*,\s*[1-7])*\s*$/, msg: '1–7 közötti számok vesszővel (pl. 1,4)' },
  autoFrom: MD, autoTo: MD,
  // közös
  solarFactor: [0.5, 2], windHeight: [1, 100], roofFactor: [0.3, 2], albedo: [0.05, 0.5], elevation: [-500, 9000],
  springStart: MD, fullLeaf: MD, fallStart: MD, dormant: MD,
  // talaj, lefolyás
  topDepth: [0.01, 1], topFC: [0.02, 0.6], topWP: [0.005, 0.5], subFC: [0.02, 0.6], subWP: [0.005, 0.5], rew: [1, 30], cn: [30, 99],
  ia: [0, 0.5], dry: [0, 300], wet: [0, 300], dryGrow: [0, 300], wetGrow: [0, 300],
  // FWI, VPD, GDD
  ffmc0: [0, 101], dmc0: [0, 500], dc0: [0, 1500], seasonStart: MD, windFactor: [0.3, 3], noonHourUtc: [0, 23, 'int'],
  low: [0, 10], idealLo: [0, 10], idealHi: [0, 10], high: [0, 10], severe: [0, 10],
  baseT: [-10, 30], cap: [10, 50], start: MD, phenoBase: [-10, 30], springGdd: [0, 5000], fullLeafGdd: [0, 10000]
};
// a képletben használt szimbólum (a ƒx és ? ablakokban a paraméter neve mellett)
const SYM = {
  kc: 'Kc', kcIni: 'Kc,ini', kcMid: 'Kc,mid', kcEnd: 'Kc,end', kcWinter: 'Kc,win', zr: 'Zr', p: 'p', availFrac: 'f', potLiters: 'V',
  canopyD: 'd_k', height: 'h', trunkCm: 'K', formFactor: 'f_a', sapFrac: 's', waterFrac: 'w',
  solarFactor: 'k_Rs', windHeight: 'z', roofFactor: 'k_roof', albedo: 'α', elevation: 'z_asl',
  topFC: 'θFC', topWP: 'θWP', subFC: 'θFC,sub', subWP: 'θWP,sub', topDepth: 'Z_top', rew: 'REW', cn: 'CN', ia: 'Ia/S',
  ffmc0: 'FFMC₀', dmc0: 'DMC₀', dc0: 'DC₀', windFactor: 'k_W', baseT: 'T_base', cap: 'T_max', phenoBase: 'T_base,phen',
  sprinklerRate: 'I', dripLph: 'q', dripCount: 'n'
};
// mezők közötti összefüggések
function crossCheck(o) {
  const e = {};
  if (o.topFC != null && o.topWP != null && !(o.topWP < o.topFC)) e.topWP = 'kisebb legyen a szántóföldi vízkapacitásnál (' + num(o.topFC, 3) + ')';
  if (o.subFC != null && o.subWP != null && !(o.subWP < o.subFC)) e.subWP = 'kisebb legyen az alsó réteg vízkapacitásánál (' + num(o.subFC, 3) + ')';
  if (o.low != null && o.idealLo != null && o.idealHi != null && o.high != null && o.severe != null) {
    const seq = ['low', 'idealLo', 'idealHi', 'high', 'severe'];
    for (let i = 1; i < seq.length; i++) if (!(o[seq[i]] > o[seq[i - 1]])) e[seq[i]] = 'nagyobb legyen az előzőnél';
  }
  if (o.springGdd != null && o.fullLeafGdd != null && !(o.fullLeafGdd > o.springGdd)) e.fullLeafGdd = 'nagyobb legyen a rügyfakadás értékénél';
  if (o.baseT != null && o.cap != null && !(o.cap > o.baseT)) e.cap = 'nagyobb legyen a bázishőmérsékletnél';
  if (o.dry != null && o.wet != null && !(o.wet > o.dry)) e.wet = 'nagyobb legyen az AMC I határnál';
  if (o.dryGrow != null && o.wetGrow != null && !(o.wetGrow > o.dryGrow)) e.wetGrow = 'nagyobb legyen az AMC I határnál';
  return e;
}

function validMD(s) {
  const m = /^(\d{2})-(\d{2})$/.exec(String(s).trim());
  if (!m) return false;
  const mo = +m[1], d = +m[2];
  return mo >= 1 && mo <= 12 && d >= 1 && d <= [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][mo - 1];
}

// egy mező ellenőrzése; hibaüzenet vagy null
function checkField(k, d, raw) {
  const r = (d && d.rule) || RULES[k];
  if (!d || d.bool || d.options) return null;
  if (d.text || (r && (r.md || r.pattern || r.req))) {
    const s = String(raw == null ? '' : raw);
    if (r && r.req && !s.trim()) return 'nem lehet üres';
    if (r && r.maxLen && s.length > r.maxLen) return 'legfeljebb ' + r.maxLen + ' karakter';
    if (r && r.md && !validMD(s)) return 'HH-NN formában (pl. 04-01)';
    if (r && r.pattern && !r.pattern.test(s)) return r.msg || 'érvénytelen';
    return null;
  }
  const s = String(raw == null ? '' : raw).trim().replace(',', '.');
  if (s === '') return 'adj meg egy számot';
  if (!/^-?\d+(\.\d+)?$/.test(s)) return 'csak szám (pl. ' + num(1.5, 1) + ')';
  const v = parseFloat(s);
  if (Array.isArray(r)) {
    if (r[2] === 'int' && !Number.isInteger(v)) return 'egész szám kell';
    if (v < r[0] || v > r[1]) return num(r[0], r[0] % 1 ? 2 : 0) + ' és ' + num(r[1], r[1] % 1 ? 2 : 0) + ' között';
  }
  return null;
}

// ------------------------------------------------------------------ táblázat
function paramTable(defs, obj, std) {
  const u = units();
  const metricNote = (u.temp !== 'C' || u.rain !== 'mm' || u.wind !== 'kmh') && Object.values(defs).some(d => /°C|mm|km\/h/.test(d.unit || ''))
    ? '<div class="hint">A modell-paraméterek a szakirodalommal egyezően metrikusan (°C, mm, km/h) adhatók meg; az eredmények a választott mértékegységben jelennek meg.</div>' : '';
  return metricNote + '<table class="ptable"><thead><tr><th>Paraméter</th><th>Érték</th><th>Szabványos / tipikus érték</th></tr></thead><tbody>' +
    Object.keys(defs).map(k => {
      const d = defs[k], v = obj[k], r = d.rule || RULES[k];
      let input;
      if (d.bool) input = '<label class="switch small"><input type="checkbox" data-k="' + k + '"' + (v ? ' checked' : '') + '><span class="slider"></span></label>';
      else if (d.options) input = '<select class="sel" data-k="' + k + '">' + Object.entries(d.options).map(([ov, ol]) => '<option value="' + ov + '"' + (ov === v ? ' selected' : '') + '>' + esc(ol) + '</option>').join('') + '</select>';
      else if (d.text || typeof v === 'string' || (r && (r.md || r.pattern || r.req)))
        input = '<input class="inp" data-k="' + k + '" value="' + esc(v == null ? '' : v) + '"' + (r && r.md ? ' placeholder="HH-NN" maxlength="5"' : '') + '>';
      else {
        const rng = Array.isArray(r) ? ' min="' + r[0] + '" max="' + r[1] + '"' : '';
        input = '<input class="inp num" inputmode="decimal" data-k="' + k + '"' + rng + ' value="' + (v == null ? '' : String(v).replace('.', decSep())) + '">' + (d.unit ? '<span class="unit">' + esc(d.unit) + '</span>' : '');
      }
      const sval = std ? std[k] : undefined;
      const shown = sval === undefined || typeof sval === 'object' || k === 'name' || sval === '' ? null
        : typeof sval === 'boolean' ? (sval ? 'igen' : 'nem')
        : d.options ? d.options[sval] || sval
        : typeof sval === 'number' ? num(sval, sval % 1 ? Math.min(3, String(sval).split('.')[1].length) : 0) : String(sval);
      const sv = shown != null ? '<span class="stdv">alapérték: ' + esc(shown) + '</span> ' : '';
      const sym = SYM[k] ? ' <span class="sym">' + esc(SYM[k]) + '</span>' : '';
      const rangeTxt = Array.isArray(r) ? '<div class="prange">megengedett: ' + num(r[0], r[0] % 1 ? 2 : 0) + ' … ' + num(r[1], r[1] % 1 ? 2 : 0) + (d.unit ? ' ' + esc(d.unit) : '') + '</div>' : '';
      // a szöveges tipp (pl. „0,75”) előbb fordul, aztán a tizedesjele a felület nyelvét követi
      const stdTxt = d.std ? (decSep() === ',' ? tr(d.std) : tr(d.std).replace(/(\d),(\d)/g, '$1.$2')) : '';
      return '<tr><td>' + esc(d.label) + sym + '</td><td class="pin">' + input + '<div class="perr" data-err="' + k + '"></div>' + rangeTxt + '</td><td class="pstd">' + sv + stdTxt + '</td></tr>';
    }).join('') + '</tbody></table>';
}
function decSep() { return (1.5).toLocaleString(numLocale ? numLocale() : 'hu-HU').charAt(1); }

// a táblázat aktuális értékei (ellenőrzés nélkül)
function tableValues(root, defs) {
  const o = {};
  root.querySelectorAll('[data-k]').forEach(el => {
    const k = el.dataset.k, d = defs[k];
    if (!d) return;
    if (d.bool) o[k] = el.checked;
    else if (d.options || d.text || el.classList.contains('num') === false) o[k] = el.value;
    else { const v = parseFloat(String(el.value).replace(',', '.')); o[k] = isNaN(v) ? null : v; }
  });
  return o;
}

// ellenőrzés: jelöli a hibás mezőket; true, ha minden rendben
function validateTables(pairs) {
  let ok = true;
  for (const [root, defs] of pairs) {
    if (!root) continue;
    const vals = tableValues(root, defs);
    const cross = crossCheck(vals);
    root.querySelectorAll('[data-k]').forEach(el => {
      const k = el.dataset.k;
      const msg = checkField(k, defs[k], el.type === 'checkbox' ? el.checked : el.value) || cross[k] || null;
      el.classList.toggle('bad', !!msg);
      const box = root.querySelector('[data-err="' + k + '"]');
      if (box) box.textContent = msg ? '⚠ ' + msg : '';
      if (msg) ok = false;
    });
  }
  return ok;
}

// a táblázat értékei az objektumba – csak ha minden mező érvényes
function readTable(root, defs, obj) {
  if (!validateTables([[root, defs]])) return false;
  Object.assign(obj, tableValues(root, defs));
  return true;
}

// élő ellenőrzés: hibás mezőnél az Alkalmaz és a Mentés presetként gomb tiltva
function liveValidate(pairsFn) {
  const run = () => {
    const ok = validateTables(pairsFn());
    ['mapply', 'psave'].forEach(id => { const b = $(id); if (b) { b.disabled = !ok; b.title = ok ? '' : 'Előbb javítsd a pirossal jelölt mezőket.'; } });
    return ok;
  };
  MODAL_VALIDATE = run;   // a modál közös figyelője hívja (11.11)
  run();
  return run;
}

// ------------------------------------------------------------------ presetek (a modal nyitva marad)
let PRESETS = null;
async function getPresets(force) { if (!PRESETS || force) PRESETS = await api('/api/presets'); return PRESETS; }

function presetBar(kind) {
  const list = (PRESETS || []).filter(p => p.kind === kind);
  return '<div class="preset-bar"><b>Mentett beállítások:</b>' +
    '<select class="sel" id="psel">' + presetOptions(list) + '</select>' +
    '<button class="btn" id="pload"' + (list.length ? '' : ' disabled') + '>Betöltés</button>' +
    '<button class="btn" id="pdel"' + (list.length ? '' : ' disabled') + '>Törlés</button>' +
    '<input class="inp" id="pname" placeholder="név, pl. Nyári beállítás" aria-label="Az új mentett beállítás neve" maxlength="60">' +
    '<button class="btn" id="psave">Mentés ezen a néven</button><span class="pmsg" id="pmsg" role="status"></span></div>';
}
function presetOptions(list) { return list.length ? list.map(p => '<option class="ut">' + esc(p.name) + '</option>').join('') : '<option value="">(még nincs mentett)</option>'; }

function wirePresets(kind, getData, setData) {
  const list = () => (PRESETS || []).filter(p => p.kind === kind);
  const refresh = (sel) => {
    $('psel').innerHTML = presetOptions(list());
    if (sel) $('psel').value = sel;
    $('pload').disabled = $('pdel').disabled = !list().length;
  };
  // az üzenet a következő műveletig marad (7.11: 3,5 másodperc kevés volt), és a képernyőolvasó is felolvassa (role=status)
  const flash = t => { $('pmsg').textContent = t; };
  $('psave').addEventListener('click', async () => {
    const name = $('pname').value.trim();
    if (!name) { flash('⚠ Adj nevet a mentett beállításnak.'); $('pname').focus(); return; }
    const data = getData();
    if (data === false) { flash('⚠ Előbb javítsd a pirossal jelölt mezőket.'); return; }
    if (list().some(p => p.name === name) && !(await askConfirm('Már van „' + name + '” nevű mentett beállítás. Felülírod?', { ok: 'Felülírom', cancel: 'Mégse' }))) return;
    try {
      await post('/api/drought/preset?kind=' + encodeURIComponent(kind) + '&name=' + encodeURIComponent(name), data);
      await getPresets(true);
      refresh(name); $('pname').value = '';
      flash('✔ Elmentve: ' + name);
      if (typeof logChange === 'function') logChange('preset', kind, 'mentve: ' + name);
    } catch (e) { flash('⚠ ' + e.message); }
  });
  $('pload').addEventListener('click', () => {
    const p = list().find(x => x.name === $('psel').value);
    if (p) { setData(p.data); flash('✔ Betöltve – a Mentés gombbal mentheted.'); }
  });
  $('pdel').addEventListener('click', async () => {
    const name = $('psel').value;
    if (!name || !(await askConfirm('Törlöd a(z) „' + name + '” mentett beállítást?', { sub: 'Ez nem vonható vissza.', ok: 'Törlöm', cancel: 'Megtartom', danger: true }))) return;
    await post('/api/drought/preset-delete?kind=' + encodeURIComponent(kind) + '&name=' + encodeURIComponent(name));
    await getPresets(true);
    refresh(); flash('✔ Törölve.');
  });
}

// ------------------------------------------------------------------ legördülő választó (szabad szöveggel is)
// a ▼ gombra mindig az összes lehetőség megjelenik, gépeléskor szűr
function makeCombo(input, getOptions) {
  if (input.dataset.combo) return;
  input.dataset.combo = '1';
  input.removeAttribute('list');
  const wrap = document.createElement('span'); wrap.className = 'combo';
  input.parentNode.insertBefore(wrap, input); wrap.appendChild(input);
  const btn = document.createElement('button'); btn.type = 'button'; btn.className = 'combo-btn'; btn.textContent = '▾'; btn.title = 'Lista';
  const box = document.createElement('div'); box.className = 'combo-list hidden';
  wrap.appendChild(btn); wrap.appendChild(box);
  const show = all => {
    const q = all ? '' : input.value.trim().toLowerCase();
    const opts = getOptions().filter(o => !q || o.toLowerCase().includes(q));
    box.innerHTML = opts.length ? opts.map(o => '<div class="combo-item">' + esc(o) + '</div>').join('') : '<div class="combo-empty">' + (getOptions().length ? 'nincs egyezés – új név lesz' : 'még nincs név – írj be egyet') + '</div>';
    box.classList.remove('hidden');
    box.querySelectorAll('.combo-item').forEach(it => it.addEventListener('mousedown', e => {
      e.preventDefault(); input.value = it.textContent; box.classList.add('hidden'); input.dispatchEvent(new Event('input', { bubbles: true }));
    }));
  };
  btn.addEventListener('mousedown', e => { e.preventDefault(); if (box.classList.contains('hidden')) { input.focus(); show(true); } else box.classList.add('hidden'); });
  input.addEventListener('input', () => { if (document.activeElement === input) show(false); });
  input.addEventListener('blur', () => setTimeout(() => box.classList.add('hidden'), 120));
}
