'use strict';
/* Többnyelvűség: a felület magyarul készül, a többi nyelv a kész szövegeket fordítja egy kifejezés-szótárral.
   Minden nyelv egy külön fájl: web/lang/<kód>.json  ({"_meta": {...}, "magyar kifejezés": "fordítás", ...}).
   ÚJ NYELV: másold a web/lang/_sablon.json-t <kód>.json néven, töltsd ki a _meta részt és a fordításokat,
   majd futtasd a build.bat-ot – a nyelv magától megjelenik a Beállítások › Nyelv listában (lásd: docs/FORDITAS.md).
   A fordítás a szöveg-csomópontokon, a title / placeholder attribútumokon, a grafikonok feliratain és a buboréksúgókon fut;
   a számok és a mértékegységek a helyükön maradnak. */

const I18N_LANGS = { hu: { code: 'hu', name: 'Magyar', locale: 'hu-HU' } };   // a /api/langs tölti fel
const I18N_DICTS = {};                                                     // betöltött szótárak: kód → {_meta, ...}
let I18N = null;
const I18N_ORIG = new WeakMap(), I18N_DONE = new WeakMap();
const escRe = s => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

// az elérhető nyelvek listája (a program a web/lang mappában lévő fájlokból adja)
async function i18nLoadList() {
  try { (await api('/api/langs')).forEach(l => { I18N_LANGS[l.code] = l; }); } catch (e) { }
  const sel = typeof $ === 'function' && $('langSel');
  if (sel) {
    const cur = lang();
    sel.innerHTML = Object.values(I18N_LANGS).sort((a, b) => a.code === 'hu' ? -1 : b.code === 'hu' ? 1 : a.name.localeCompare(b.name)).map(l => '<option value="' + l.code + '">' + esc(l.name) + '</option>').join('');
    sel.value = cur;
  }
}
async function i18nLoad(code) {
  if (code === 'hu' || I18N_DICTS[code]) return I18N_DICTS[code];
  try {
    const r = await fetch('/lang/' + encodeURIComponent(code) + '.json', { cache: 'no-cache' });
    if (r.ok) I18N_DICTS[code] = await r.json();
  } catch (e) { console.error('A nyelvi fájl nem tölthető be: ' + code, e); }
  return I18N_DICTS[code];
}
function langMeta(code) { const d = I18N_DICTS[code]; return Object.assign({}, I18N_LANGS[code] || {}, d && d._meta || {}); }

// a nyelvfüggő tömbök (hónapok, napok, égtájak) eredeti, magyar tartalma
const I18N_ARR = {};
function i18nArrays(L) {
  const meta = langMeta(L), loc = meta.locale || 'hu-HU';
  const keep = (name, arr) => { if (!I18N_ARR[name]) I18N_ARR[name] = arr.slice(); return I18N_ARR[name]; };
  const set = (arr, vals) => { arr.splice(0, arr.length, ...vals); };
  const fmt = (o, d) => new Intl.DateTimeFormat(loc, Object.assign({ timeZone: 'UTC' }, o)).format(d);
  if (typeof MONTHS_HU !== 'undefined') { const hu = keep('MONTHS_HU', MONTHS_HU); set(MONTHS_HU, L === 'hu' ? hu : [...Array(12).keys()].map(m => fmt({ month: 'short' }, Date.UTC(2026, m, 15)))); }
  if (typeof MONTHS_FULL !== 'undefined') { const hu = keep('MONTHS_FULL', MONTHS_FULL); set(MONTHS_FULL, L === 'hu' ? hu : [...Array(12).keys()].map(m => fmt({ month: 'long' }, Date.UTC(2026, m, 15)))); }
  if (typeof DAY_NAMES !== 'undefined') { const hu = keep('DAY_NAMES', DAY_NAMES); set(DAY_NAMES, L === 'hu' ? hu : [...Array(7).keys()].map(d => fmt({ weekday: 'short' }, Date.UTC(2026, 9, 18 + d)))); }
  if (typeof DIRS !== 'undefined') { const hu = keep('DIRS', DIRS); set(DIRS, L !== 'hu' && Array.isArray(meta.dirs) && meta.dirs.length === 16 ? meta.dirs : hu); }
  if (typeof MONTH_ADJ !== 'undefined') { const hu = keep('MONTH_ADJ', MONTH_ADJ); set(MONTH_ADJ, L === 'hu' ? hu : MONTHS_FULL.slice()); }
  if (typeof DOW !== 'undefined') { const hu = keep('DOW', DOW); set(DOW, L === 'hu' ? hu : [...Array(7).keys()].map(d => fmt({ weekday: 'short' }, Date.UTC(2026, 9, 19 + d)))); }
}

function i18nBuild() {
  const L = typeof lang === 'function' ? lang() : 'hu';
  i18nArrays(L);
  const dict = I18N_DICTS[L];
  if (L === 'hu' || !dict) { I18N = null; return; }
  const map = new Map();
  for (const k of Object.keys(dict)) if (k !== '_meta' && dict[k]) map.set(k, dict[k]);
  // a rövid, egyszavas kifejezések (pl. „és”, „nap”) csak önálló szövegként fordulnak, mondat belsejében nem –
  // így egy hiányzó mondatból nem lesz félig lefordított keverék
  const isShort = k => k.length < 5 && !/\s/.test(k);
  const mk = list => { try { return list.length ? new RegExp('(?<![\\p{L}_])(?:' + list.sort((a, b) => b.length - a.length).map(escRe).join('|') + ')(?![\\p{L}_])', 'gu') : null; } catch (e) { console.error(e); return null; } };
  I18N = { L, map, re: mk([...map.keys()].filter(k => !isShort(k))), reShort: mk([...map.keys()].filter(isShort)) };
}

// egy szöveg fordítása: előbb a teljes (levágott) szöveg, aztán a benne lévő ismert kifejezések
function tr(s) {
  if (!I18N || s == null) return s;
  s = String(s).replace(/\s*\n\s*|[ \u00a0]{2,}/g, ' ');   // a HTML-ben tördelt bekezdés is egyezzen
  const t = s.trim();
  if (!t || !/[A-Za-zÀ-ž]/.test(t)) return s;
  const whole = I18N.map.get(t) || I18N.map.get(t.replace(/[.:!?]+$/, ''));
  if (whole) {
    const core = t.replace(/[.:!?]+$/, '');
    // a kulcsból levágott záró pont a fordításban is ott van: ne legyen belőle „..” (pl. „Click for details..”)
    return s.replace(core, whole.endsWith('.') && t.slice(core.length) === '.' ? whole.slice(0, -1) : whole);
  }
  // a gyűjtő formájában (szélső írásjelek nélkül) tárolt kulcs is egészben egyezzen, pl. „… (0,8 ill. 0,6)”
  const edge = trEdge(t);
  if (edge) return s.replace(t, edge);
  let out = I18N.re ? s.replace(I18N.re, trDot) : s;
  // a rövid szavak (pl. „perc”, „most”) csak rövid feliratokban (legfeljebb 4 szó), hogy ne keletkezzen félig fordított mondat
  if (I18N.reShort && (out.match(/\p{L}{2,}/gu) || []).length <= 4) out = out.replace(I18N.reShort, trDot);
  return out;
}
// ponttal végződő fordítás (pl. „kb” → „approx.”) után az eredeti pontja ne duplázódjon („approx..”); a „...” megmarad
function trDot(m, off, str) {
  const v = I18N.map.get(m) || m;
  return v !== m && v.endsWith('.') && str[off + m.length] === '.' && str[off + m.length + 1] !== '.' ? v.slice(0, -1) : v;
}

// mondatsablon: egész mondat helyőrzőkkel, a nyelvi fájlban is egészben (pl. "{n} m³-rel kevesebb víz" → "{n} m³ less water").
// Ott kell, ahol a szórend nyelvenként más, vagy a magyar rag a számhoz tapad (6.33, 6.35, 6.37). A behelyettesített értékek nem fordulnak.
// a kulcsgyűjtő a szélső írásjeleket levágja (pl. „…({avg}).” → „…({avg}”, „Gyep (szórófejes)” → „Gyep (szórófejes”):
// a szótárban így szereplő kulcsot keressük meg, és az írásjeleket visszarakjuk
const I18N_EDGE = /^([\s·:,;()–\-—→.!?+=\/*•⚠✔"']*)([\s\S]*?)([\s·:,;()–\-—→.!?+=\/*•⚠✔"']*)$/;
function trEdge(s) {
  const m = s.match(I18N_EDGE), w = m && m[2] && I18N.map.get(m[2]);
  return w ? m[1] + (w.endsWith('.') && /^\.(?!\.)/.test(m[3]) ? w.slice(0, -1) : w) + m[3] : null;
}
function tf(tpl, vars) {
  let t = I18N && (I18N.map.get(tpl) || trEdge(tpl));
  t = t || tpl;
  return t.replace(/\{(\w+)\}/g, (m, k) => vars && vars[k] !== undefined ? vars[k] : m);
}
function trWhole(s) {
  if (!I18N || s == null) return s;
  const t = String(s).trim(), w = t && (I18N.map.get(t) || trEdge(t));
  return w ? String(s).replace(t, w) : s;
}

// ------------------------------------------------------------------ a DOM fordítása
const I18N_SKIP = new Set(['SCRIPT', 'STYLE', 'TEXTAREA', 'CODE']);
// egy címsor eredeti (magyar) szövege: a magyar mintákra illesztő kód (ƒx-gombok, kalibrációs jelvény)
// így más nyelven is megtalálja a címsort (eddig angolul, németül, spanyolul nem jelentek meg az ƒx-gombok)
function origText(n) {
  if (n.nodeType === 3) return I18N_ORIG.get(n) || n.nodeValue;
  let s = '';
  for (let c = n.firstChild; c; c = c.nextSibling) if (c.nodeType === 3 || (c.nodeType === 1 && !I18N_SKIP.has(c.nodeName))) s += origText(c);
  return s;
}
function headText(h) { return origText(h.firstChild && h.firstChild.nodeType === 3 ? h.firstChild : h).trim(); }
function trNode(n) {
  if (n.nodeType === 3) {
    const p = n.parentNode;
    if (!p || I18N_SKIP.has(p.nodeName) || (p.closest && p.closest('.notr'))) return;
    const cur = n.nodeValue, done = I18N_DONE.get(n), L = I18N ? I18N.L : 'hu';
    if (done && done.out === cur && done.L === L) return;   // ezen a nyelven már lefordított
    // ha a szöveg azóta nem változott, az eredetiből fordítunk újra (nyelvváltás); különben az új szöveg az eredeti
    const orig = done && done.out === cur ? I18N_ORIG.get(n) : cur;
    I18N_ORIG.set(n, orig);
    // a felhasználó saját szövege (.ut): csak egészében fordul (pl. az alap „Gyep” profilnév), darabokban soha (6.7)
    const user = p.closest && p.closest('.ut');
    const out = !I18N ? orig : user ? trWhole(orig) : tr(orig);
    I18N_DONE.set(n, { out, L });
    if (out !== cur) n.nodeValue = out;
    return;
  }
  if (n.nodeType !== 1) return;
  // a szövegmező tartalma a felhasználóé, de a helyőrzője és a súgója fordul (pl. a képletszerkesztő példája)
  if (I18N_SKIP.has(n.nodeName)) { if (n.nodeName === 'TEXTAREA') trAttrs(n); return; }
  if (n.classList && n.classList.contains('notr')) return;
  trAttrs(n);
  if (n.nodeName === 'INPUT') return;
  for (let c = n.firstChild; c; c = c.nextSibling) trNode(c);
}
function trAttrs(el) {
  for (const a of ['title', 'placeholder', 'aria-label']) {
    if (!el.hasAttribute || !el.hasAttribute(a)) continue;
    // a dataset kulcsban nem lehet kötőjel: aria-label → i18nAriaLabel
    const key = 'i18n' + a.replace(/(^|-)(\w)/g, (m, d, c) => c.toUpperCase()), cur = el.getAttribute(a);
    let orig = el.dataset[key];
    if (orig === undefined || el.dataset[key + 'Out'] !== cur) { orig = cur; el.dataset[key] = orig; }
    else if (el.dataset[key + 'L'] === (I18N ? I18N.L : 'hu')) continue;
    el.dataset[key + 'L'] = I18N ? I18N.L : 'hu';
    const out = I18N ? tr(orig) : orig;
    el.dataset[key + 'Out'] = out;
    if (out !== cur) el.setAttribute(a, out);
  }
}
async function applyI18n() {
  await i18nLoad(lang());
  i18nBuild();
  if (typeof buildFormatters === 'function') buildFormatters();
  document.title = 'WStation';   // márkanév: nem fordul
  trNode(document.body);
}
let I18N_MO = null;
function i18nWatch() {
  if (I18N_MO) return;
  I18N_MO = new MutationObserver(muts => {
    if (!I18N) return;
    for (const m of muts) {
      if (m.type === 'characterData') trNode(m.target);
      else if (m.type === 'attributes') trAttrs(m.target);
      else m.addedNodes.forEach(trNode);
    }
  });
  I18N_MO.observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['title', 'placeholder', 'aria-label'] });   // a később hozzáadott aria-label is forduljon (12.8)
}

// ------------------------------------------------------------------ grafikonok: feliratok és buboréksúgók
const I18N_OPT_KEYS = new Set(['name', 'text', 'subtext']);
function trOption(o, depth) {
  if (!I18N || !o || depth > 8) return o;
  if (Array.isArray(o)) { o.forEach((x, i) => { if (typeof x === 'string') o[i] = tr(x); else trOption(x, depth + 1); }); return o; }
  if (typeof o !== 'object') return o;
  for (const k of Object.keys(o)) {
    const v = o[k];
    if (typeof v === 'string' && I18N_OPT_KEYS.has(k)) o[k] = tr(v);
    else if (typeof v === 'function' && (k === 'formatter' || k === 'valueFormatter' || k === 'labelFormatter')) {
      if (!v._i18n) { const f = v; o[k] = function () { const r = f.apply(this, arguments); return typeof r === 'string' ? tr(r) : r; }; o[k]._i18n = true; }
    } else if (typeof v === 'string' && k === 'formatter') o[k] = tr(v);
    else if (k === 'data' && Array.isArray(v) && v.length && typeof v[0] === 'string') o[k] = v.map(tr);
    else if (k === 'nameMap' && Array.isArray(v)) o[k] = v.map(tr);
    else if (v && typeof v === 'object' && k !== 'data') trOption(v, depth + 1);
    else if (k === 'data' && Array.isArray(v)) v.forEach(x => { if (x && typeof x === 'object' && !Array.isArray(x) && typeof x.name === 'string') x.name = tr(x.name); });
  }
  return o;
}

// párbeszédablakok
(function () {
  const c0 = window.confirm.bind(window), a0 = window.alert.bind(window), p0 = window.prompt.bind(window);
  window.confirm = m => c0(tr(m));
  window.alert = m => a0(tr(m));
  window.prompt = (m, d) => p0(tr(m), d);
})();
