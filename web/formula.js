'use strict';
/* Saját képlet sáv az idővonalon, pl.  eső(t) / (napsütés(t-1nap) * légnyomás(t-3nap) * 0.98 - 1) */

// ------------------------------------------------------------------ változók és függvények
const norm = s => s.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
const VAR_ALIASES = {
  hom: ['hom', 'homerseklet', 'temp', 'ho'], hom_max: ['hom_max', 'homax', 'tmax', 'max_hom'], hom_min: ['hom_min', 'homin', 'tmin', 'min_hom'],
  harmat: ['harmat', 'harmatpont', 'dew'], para: ['para', 'paratartalom', 'hum', 'rh'], legnyomas: ['legnyomas', 'nyomas', 'pres', 'p'],
  szel: ['szel', 'wind'], lokes: ['lokes', 'szellokes', 'gust'], szelirany: ['szelirany', 'irany', 'dir'],
  eso: ['eso', 'esointenzitas', 'intenzitas', 'rate'], eso_osszeg: ['eso_osszeg', 'esoosszeg', 'csapadek', 'rain'],
  napsutes: ['napsutes', 'napsugarzas', 'sol', 'solar'], uv: ['uv'], vpd: ['vpd'], et0: ['et0', 'parolgas'], fwi: ['fwi', 'tuzveszely']
};
const VAR_DOC = [['hom', 'hőmérséklet (°C)'], ['hom_max / hom_min', 'a sáv max./min. hőmérséklete'], ['harmatpont', '°C'], ['para', 'páratartalom (%)'], ['légnyomás', 'hPa'],
  ['szél / lökés', 'km/h'], ['szélirány', '°'], ['eső', 'csapadékintenzitás (mm/h)'], ['eső_összeg', 'csapadék a sávban (mm)'], ['napsütés', 'W/m²'], ['uv', ''], ['vpd', 'kPa'],
  ['et0', 'párolgás (mm/nap)'], ['fwi', 'tűzveszély'], ['szaraz_gyep, szaraz_barack, …', 'kiszáradás % profilonként']];
const VAR_LOOKUP = {};
Object.entries(VAR_ALIASES).forEach(([k, al]) => al.forEach(a => VAR_LOOKUP[a] = k));
const FUNCS = ['osszeg', 'sum', 'atlag', 'avg', 'min', 'max', 'abs', 'sqrt', 'gyok', 'ln', 'log', 'exp', 'ha', 'if', 'kerekit', 'round'];
const UNIT_SEC = { perc: 60, p: 60, min: 60, ora: 3600, o: 3600, h: 3600, nap: 86400, n: 86400, d: 86400, het: 604800, w: 604800 };

// ------------------------------------------------------------------ elemző
function tokenize(src) {
  const out = []; let i = 0;
  while (i < src.length) {
    const c = src[i];
    if (/\s/.test(c)) { i++; continue; }
    if (/[0-9]/.test(c) || (c === '.' && /[0-9]/.test(src[i + 1] || ''))) {
      let j = i; while (j < src.length && /[0-9.,]/.test(src[j]) && !(src[j] === ',' && !/[0-9]/.test(src[j + 1] || ''))) j++;
      out.push({ t: 'num', v: parseFloat(src.slice(i, j).replace(',', '.')), p: i }); i = j; continue;
    }
    if (/[A-Za-zÀ-ž_]/.test(c)) { let j = i; while (j < src.length && /[A-Za-zÀ-ž_0-9]/.test(src[j])) j++; out.push({ t: 'id', v: src.slice(i, j), p: i }); i = j; continue; }
    const two = src.slice(i, i + 2);
    if (['<=', '>=', '==', '!=', '<>'].includes(two)) { out.push({ t: 'op', v: two === '<>' ? '!=' : two, p: i }); i += 2; continue; }
    if ('+-*/^()<>;'.includes(c)) { out.push({ t: 'op', v: c, p: i }); i++; continue; }
    throw { msg: 'Ismeretlen karakter: „' + c + '”', pos: i };
  }
  out.push({ t: 'end', p: src.length });
  return out;
}

function parseFormula(src) {
  const tk = tokenize(src); let k = 0;
  const peek = () => tk[k], next = () => tk[k++];
  const expect = v => { const t = next(); if (t.v !== v) throw { msg: 'Itt „' + v + '” kellene', pos: t.p }; };
  function expr() { let a = add(); while (peek().t === 'op' && ['<', '>', '<=', '>=', '==', '!='].includes(peek().v)) { const o = next().v; a = { k: 'bin', o, a, b: add() }; } return a; }
  function add() { let a = mul(); while (peek().t === 'op' && (peek().v === '+' || peek().v === '-')) { const o = next().v; a = { k: 'bin', o, a, b: mul() }; } return a; }
  function mul() { let a = pow(); while (peek().t === 'op' && (peek().v === '*' || peek().v === '/')) { const o = next().v; a = { k: 'bin', o, a, b: pow() }; } return a; }
  function pow() { let a = unary(); if (peek().t === 'op' && peek().v === '^') { next(); a = { k: 'bin', o: '^', a, b: unary() }; } return a; }
  function unary() { if (peek().t === 'op' && peek().v === '-') { next(); return { k: 'neg', a: unary() }; } if (peek().t === 'op' && peek().v === '+') { next(); return unary(); } return primary(); }
  function duration() {
    const n = next(); if (n.t !== 'num') throw { msg: 'Itt egy időtartam kellene (pl. 3nap, 24óra)', pos: n.p };
    const u = peek().t === 'id' ? norm(next().v) : 'ora';
    if (!UNIT_SEC[u]) throw { msg: 'Ismeretlen időegység: „' + u + '” (perc, óra, nap, hét)', pos: n.p };
    return n.v * UNIT_SEC[u];
  }
  function primary() {
    const t = next();
    if (t.t === 'num') return { k: 'num', v: t.v };
    if (t.t === 'op' && t.v === '(') { const e = expr(); expect(')'); return e; }
    if (t.t === 'id') {
      const id = norm(t.v);
      const varKey = VAR_LOOKUP[id] || (id.startsWith('szaraz_') || id.startsWith('szarazsag_') ? 'szaraz_' + id.replace(/^szaraz(sag)?_/, '') : null);
      if (varKey) {
        let shift = 0;
        if (peek().t === 'op' && peek().v === '(') {
          next();
          const tt = next(); if (tt.t !== 'id' || norm(tt.v) !== 't') throw { msg: 'Időeltolásnál ez a forma kell: ' + t.v + '(t-1nap)', pos: tt.p };
          if (peek().t === 'op' && (peek().v === '-' || peek().v === '+')) { const s = next().v; shift = duration() * (s === '-' ? -1 : 1); }
          expect(')');
        }
        return { k: 'var', key: varKey, shift, name: t.v };
      }
      if (FUNCS.includes(id)) {
        expect('(');
        const args = [];
        if (!(peek().t === 'op' && peek().v === ')')) {
          for (;;) {
            // ablakos függvények második argumentuma időtartam lehet
            if (args.length === 1 && ['osszeg', 'sum', 'atlag', 'avg', 'min', 'max'].includes(id) && peek().t === 'num' && tk[k + 1] && tk[k + 1].t === 'id' && UNIT_SEC[norm(tk[k + 1].v)]) args.push({ k: 'dur', v: duration() });
            else args.push(expr());
            if (peek().t === 'op' && peek().v === ';') { next(); continue; }
            break;
          }
        }
        expect(')');
        return { k: 'fn', f: id, args, p: t.p };
      }
      throw { msg: 'Ismeretlen név: „' + t.v + '”', pos: t.p };
    }
    throw { msg: 'Váratlan: „' + (t.v || 'vége') + '”', pos: t.p };
  }
  const ast = expr();
  if (peek().t !== 'end') throw { msg: 'Fölösleges rész: „' + (peek().v) + '”', pos: peek().p };
  return ast;
}

// ------------------------------------------------------------------ kiértékelés a változórácson
function evalFormula(ast, V) {
  const t = V.t, n = t.length;
  const step = V.res === 'day' ? 86400e3 : V.res === 'hour' ? 3600e3 : 300e3;
  const find = target => { let lo = 0, hi = n - 1; while (lo < hi) { const m = (lo + hi) >> 1; if (t[m] < target) lo = m + 1; else hi = m; } let b = lo; if (b > 0 && Math.abs(t[b - 1] - target) < Math.abs(t[b] - target)) b--; return Math.abs(t[b] - target) <= step / 2 + 1000 ? b : -1; };
  const arr = key => { const a = V[key]; if (!a) throw { msg: 'Ehhez a felbontáshoz nincs „' + key + '” adat' + (key === 'et0' || key === 'fwi' || key.startsWith('szaraz_') ? ' (csak óra/nap felbontásnál)' : ''), pos: 0 }; return a; };
  function ev(node, i) {
    switch (node.k) {
      case 'num': return node.v;
      case 'neg': { const a = ev(node.a, i); return a == null ? null : -a; }
      case 'var': {
        const a = arr(node.key);
        if (!node.shift) return a[i];
        const j = find(t[i] + node.shift * 1000);
        return j < 0 ? null : a[j];
      }
      case 'bin': {
        const a = ev(node.a, i), b = ev(node.b, i);
        if (a == null || b == null) return null;
        switch (node.o) {
          case '+': return a + b; case '-': return a - b; case '*': return a * b;
          case '/': return b === 0 ? null : a / b; case '^': return Math.pow(a, b);
          case '<': return a < b ? 1 : 0; case '>': return a > b ? 1 : 0; case '<=': return a <= b ? 1 : 0; case '>=': return a >= b ? 1 : 0;
          case '==': return a === b ? 1 : 0; case '!=': return a !== b ? 1 : 0;
        }
        return null;
      }
      case 'fn': {
        const f = node.f, A = node.args;
        if (['osszeg', 'sum', 'atlag', 'avg', 'min', 'max'].includes(f) && A.length === 2 && A[1].k === 'dur') {
          const from = t[i] - A[1].v * 1000; let s = 0, c = 0, mn = Infinity, mx = -Infinity;
          for (let j = i; j >= 0 && t[j] > from; j--) { const v = ev(A[0], j); if (v == null) continue; s += v; c++; if (v < mn) mn = v; if (v > mx) mx = v; }
          if (!c) return null;
          return f === 'osszeg' || f === 'sum' ? s : f === 'atlag' || f === 'avg' ? s / c : f === 'min' ? mn : mx;
        }
        const v = A.map(a => ev(a, i));
        if (f === 'ha' || f === 'if') { if (v[0] == null) return null; return v[0] ? v[1] : v[2]; }
        if (v.some(x => x == null)) return null;
        switch (f) {
          case 'min': return Math.min(...v); case 'max': return Math.max(...v); case 'abs': return Math.abs(v[0]);
          case 'sqrt': case 'gyok': return v[0] < 0 ? null : Math.sqrt(v[0]);
          case 'ln': return v[0] <= 0 ? null : Math.log(v[0]); case 'log': return v[0] <= 0 ? null : Math.log10(v[0]);
          case 'exp': return Math.exp(v[0]); case 'kerekit': case 'round': return Math.round(v[0] * Math.pow(10, v[1] || 0)) / Math.pow(10, v[1] || 0);
          case 'osszeg': case 'sum': return v.reduce((a, b) => a + b, 0); case 'atlag': case 'avg': return v.reduce((a, b) => a + b, 0) / v.length;
        }
        throw { msg: 'Ismeretlen függvény: ' + f, pos: node.p };
      }
    }
    return null;
  }
  const out = new Array(n);
  for (let i = 0; i < n; i++) { const v = ev(ast, i); out[i] = v == null || !isFinite(v) ? null : v; }
  return out;
}

// ------------------------------------------------------------------ felület
let fChart = null, fEdit = -1;
function formulas() { UI.formulas = UI.formulas || []; return UI.formulas; }

function renderFormulaPanel() {
  const L = formulas();
  $('fList').innerHTML = L.length ? L.map((f, i) => '<label class="chip' + (f.on ? ' on' : '') + '" style="' + (f.on ? 'background:' + f.color + ';border-color:' + f.color : '') + '"><input type="checkbox" data-i="' + i + '"' + (f.on ? ' checked' : '') + '>' + esc(f.name) + '</label><a href="#" class="fedit" data-i="' + i + '" title="Szerkesztés">✎</a>').join('') : '<span class="muted">Még nincs saját képlet.</span>';
  $('fList').querySelectorAll('input').forEach(c => c.addEventListener('change', () => { L[+c.dataset.i].on = c.checked; saveUi(); renderFormulaPanel(); drawFormulas(); }));
  $('fList').querySelectorAll('.fedit').forEach(a => a.addEventListener('click', e => { e.preventDefault(); openFormulaEditor(+a.dataset.i); }));
  $('fVars').innerHTML = VAR_DOC.map(([a, b]) => '<code>' + esc(a) + '</code>' + (b ? ' ' + esc(b) : '')).join(' · ');
  drawFormulas();
}

function openFormulaEditor(i) {
  fEdit = i;
  const f = i >= 0 ? formulas()[i] : { name: 'Saját mutató', expr: 'eső(t) / (napsütés(t-1nap) * légnyomás(t-3nap) * 0.98 - 1)', res: 'hour', unit: '', color: '#e11d48' };
  $('fName').value = f.name; $('fExpr').value = f.expr; $('fRes').value = f.res; $('fUnit').value = f.unit || ''; $('fColor').value = f.color || '#e11d48';
  $('fDelete').classList.toggle('hidden', i < 0);
  $('fEditor').classList.remove('hidden');
  checkFormula();
}

function checkFormula() {
  const src = $('fExpr').value;
  try { parseFormula(src); $('fMsg').className = 'msg ok'; $('fMsg').textContent = '✔ A képlet rendben van.'; return true; }
  catch (e) {
    $('fMsg').className = 'msg err';
    const pos = e.pos == null ? 0 : e.pos;
    $('fMsg').innerHTML = '✖ ' + esc(e.msg || String(e)) + '<br><code>' + esc(src.slice(0, pos)) + '<span class="errpos">' + esc(src.slice(pos, pos + 1) || ' ') + '</span>' + esc(src.slice(pos + 1)) + '</code>';
    return false;
  }
}

const VARS_CACHE = {};
async function varsFor(res) {
  if (res === '5perc') {
    const [a, b] = chart ? visibleRange() : [META.last - 7 * D, META.last];
    const key = '5perc:' + Math.round(a / 600e3) + ':' + Math.round(b / 600e3);
    if (!VARS_CACHE[key]) VARS_CACHE[key] = await api('/api/vars?res=5perc&from=' + a + '&to=' + b);
    return VARS_CACHE[key];
  }
  if (!VARS_CACHE[res]) VARS_CACHE[res] = await api('/api/vars?res=' + res);
  return VARS_CACHE[res];
}

async function drawFormulas() {
  const on = formulas().filter(f => f.on);
  $('fChartWrap').classList.toggle('hidden', !on.length);
  if (!on.length || currentTab !== 'idovonal' || !META) return;
  const series = [];
  for (const f of on) {
    try {
      const V = await varsFor(f.res);
      const ys = evalFormula(parseFormula(f.expr), V);
      series.push({ name: f.name + (f.unit ? ' (' + f.unit + ')' : ''), type: 'line', showSymbol: false, connectNulls: false, data: V.t.map((t, i) => [t, ys[i] == null ? null : +ys[i].toFixed(4)]), lineStyle: { color: f.color, width: 1.8 }, itemStyle: { color: f.color } });
    } catch (e) { toast('„' + f.name + '”: ' + (e.msg || e)); }
  }
  const ink = cssVar('--ink'), muted = cssVar('--muted'), line = cssVar('--line'), card = cssVar('--card');
  if (!fChart || fChart.isDisposed()) { fChart = echarts.init($('fChart')); fChart.group = 'idovonal'; }
  const [a, b] = chart ? visibleRange() : [META.first, META.last];
  fChart.setOption({
    animation: false, grid: { left: 58, right: 58, top: 30, bottom: 26 }, legend: { top: 0, textStyle: { color: muted } },
    tooltip: { trigger: 'axis', confine: true, backgroundColor: card, borderColor: line, textStyle: { color: ink }, formatter: ps => F.full.format(ps[0].axisValue) + '<br>' + ps.filter(p => p.data[1] != null).map(p => p.marker + ' ' + esc(p.seriesName) + ': <b>' + num(p.data[1], 3) + '</b>').join('<br>') },
    xAxis: { type: 'time', min: META.first, max: META.last, axisLabel: { color: muted, formatter: axisFmt } },
    yAxis: { type: 'value', scale: true, axisLabel: { color: muted }, splitLine: { lineStyle: { color: line, opacity: .5 } } },
    dataZoom: [{ type: 'inside', filterMode: 'none', zoomOnMouseWheel: 'ctrl', moveOnMouseWheel: false, startValue: a, endValue: b }],
    series
  }, true);
  if (chart) { chart.group = 'idovonal'; echarts.connect('idovonal'); }
}

$('fAdd').addEventListener('click', () => openFormulaEditor(-1));
$('fExpr').addEventListener('input', checkFormula);
$('fCancel').addEventListener('click', () => $('fEditor').classList.add('hidden'));
$('fSave').addEventListener('click', () => {
  if (!checkFormula()) return;
  const f = { name: $('fName').value.trim() || 'Saját mutató', expr: $('fExpr').value, res: $('fRes').value, unit: $('fUnit').value.trim(), color: $('fColor').value, on: true };
  if (fEdit >= 0) formulas()[fEdit] = f; else formulas().push(f);
  saveUi(); $('fEditor').classList.add('hidden'); renderFormulaPanel();
});
$('fDelete').addEventListener('click', () => { if (fEdit >= 0 && confirm('Törlöd a képletet?')) { formulas().splice(fEdit, 1); saveUi(); $('fEditor').classList.add('hidden'); renderFormulaPanel(); } });
window.addEventListener('resize', () => { if (fChart && currentTab === 'idovonal') fChart.resize(); });
