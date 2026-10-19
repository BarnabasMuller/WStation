'use strict';
/* Időtartomány-választó a Statisztika fülhöz: tetszőleges számú tól–ig sor, gyorsgombok, kedvencek, közös halmaz / egymás mellett */

// egy sor: { ay, am, ad, by, bm, bd } – szövegként, ahogy beírták; az üres „tól” = első mért nap, az üres „ig” = ma
function stRanges() {
  const S = UI.stat = UI.stat || {};
  if (!Array.isArray(S.ranges) || !S.ranges.length) {
    S.ranges = S.year ? [{ ay: String(S.year), am: '', ad: '', by: String(S.year), bm: '', bd: '' }] : [{ ay: '', am: '', ad: '', by: '', bm: '', bd: '' }];
    delete S.year;
  }
  if (S.rangeMode !== 'side') S.rangeMode = 'union';
  if (!Array.isArray(S.favs)) S.favs = [];
  return S.ranges;
}
const pad2 = n => String(n).padStart(2, '0');
function firstDay() { return META ? F.ymd.format(META.first) : '2000-01-01'; }

// egy dátumvég feloldása; side: 'a' (tól) vagy 'b' (ig). Eredmény: { v: 'YYYY-MM-DD' | '', err: { mező: üzenet } }
function resolveEnd(y, m, d, side) {
  const err = {};
  const t = s => String(s == null ? '' : s).trim();
  y = t(y); m = t(m); d = t(d);
  if (!y && !m && !d) return { v: '', err };
  if (!/^\d{4}$/.test(y) || +y < 2000 || +y > 2100) err.y = 'évszám (2000–2100)';
  if (m && (!/^\d{1,2}$/.test(m) || +m < 1 || +m > 12)) err.m = 'hónap: 1–12';
  if (d && !m) err.m = 'a naphoz hónap is kell';
  if (d && /^\d{1,2}$/.test(m) && +m >= 1 && +m <= 12 && /^\d{4}$/.test(y)) {
    const last = new Date(Date.UTC(+y, +m, 0)).getUTCDate();
    if (!/^\d{1,2}$/.test(d) || +d < 1 || +d > last) err.d = 'nap: 1–' + last;
  } else if (d && !/^\d{1,2}$/.test(d)) err.d = 'nap: szám';
  if (Object.keys(err).length) return { v: null, err };
  const mm = m ? +m : (side === 'a' ? 1 : 12);
  const dd = d ? +d : (side === 'a' ? 1 : new Date(Date.UTC(+y, mm, 0)).getUTCDate());
  return { v: y + '-' + pad2(mm) + '-' + pad2(dd), err };
}
// a sor feloldva: { a, b, ok, err }
function resolveRow(r) {
  const A = resolveEnd(r.ay, r.am, r.ad, 'a'), B = resolveEnd(r.by, r.bm, r.bd, 'b');
  const err = {};
  Object.keys(A.err).forEach(k => err['a' + k] = A.err[k]);
  Object.keys(B.err).forEach(k => err['b' + k] = B.err[k]);
  const a = A.v === '' ? firstDay() : A.v, b = B.v === '' ? DS.today() : B.v;
  if (A.v != null && B.v != null && a > b) err.order = 'a „tól” későbbi, mint az „ig”';
  return { a, b, ok: !Object.keys(err).length, err, full: A.v === '' && B.v === '' };
}
function rangeLabel(x) {
  if (x.full) return 'teljes időszak';
  const f = s => DS.fmt(s, { year: 'numeric', month: 'short', day: 'numeric' });
  if (x.a.slice(5) === '01-01' && x.b.slice(5) === '12-31' && x.a.slice(0, 4) === x.b.slice(0, 4)) return x.a.slice(0, 4);
  return f(x.a) + ' – ' + f(x.b);
}
// az érvényes sorok; ha egy sor hibás, null (ilyenkor nem kérdezünk le semmit)
function validRanges() {
  const rows = stRanges().map(resolveRow);
  return rows.every(r => r.ok) ? rows : null;
}
function rangeParam(list) { return list.map(x => x.full ? '_' : x.a + '_' + x.b).join(','); }
// napi szűrő (YYYY-MM-DD) a kliens oldali számításokhoz
function rangePred(list) { return s => list.some(x => s >= x.a && s <= x.b); }

function renderRangePicker() {
  const rows = stRanges(), S = UI.stat;
  const today = DS.today(), fd = firstDay();
  const inp = (i, k, w, ph, max) => '<input class="inp rnum" data-i="' + i + '" data-k="' + k + '" inputmode="numeric" maxlength="' + max + '" style="width:' + w + 'px" placeholder="' + ph + '" value="' + esc(rows[i][k] || '') + '">';
  $('stRanges').innerHTML = rows.map((r, i) =>
    '<div class="rrow" data-i="' + i + '">' +
    inp(i, 'ay', 62, fd.slice(0, 4), 4) + '. ' + inp(i, 'am', 40, fd.slice(5, 7), 2) + '. ' + inp(i, 'ad', 40, fd.slice(8, 10), 2) + '. ' +
    '<span class="rdash">–</span> ' +
    inp(i, 'by', 62, today.slice(0, 4), 4) + '. ' + inp(i, 'bm', 40, today.slice(5, 7), 2) + '. ' + inp(i, 'bd', 40, today.slice(8, 10), 2) + '. ' +
    (rows.length > 1 ? '<button class="btn small" data-del="' + i + '" title="Sor törlése">×</button>' : '') +
    (i === rows.length - 1 ? ' <button class="btn small" id="stAdd" title="Új időszak">+</button>' : '') +
    ' <span class="rlabel muted"></span> <span class="perr"></span></div>').join('');
  $('stRanges').querySelectorAll('.rnum').forEach(el => {
    el.addEventListener('input', () => { el.value = el.value.replace(/\D/g, ''); rows[+el.dataset.i][el.dataset.k] = el.value; checkRows(); });
    el.addEventListener('change', () => { if (checkRows()) { saveUi(); renderStats(); } });
    el.addEventListener('keydown', e => { if (e.key === 'Enter') el.dispatchEvent(new Event('change')); });
  });
  $('stRanges').querySelectorAll('[data-del]').forEach(b => b.addEventListener('click', () => { rows.splice(+b.dataset.del, 1); saveUi(); renderRangePicker(); renderStats(); }));
  $('stAdd').addEventListener('click', () => { rows.push({ ay: '', am: '', ad: '', by: '', bm: '', bd: '' }); saveUi(); renderRangePicker(); const l = $('stRanges').querySelectorAll('.rrow'); l[l.length - 1].querySelector('.rnum').focus(); });
  document.querySelectorAll('#stRangeMode button').forEach(b => b.classList.toggle('on', b.dataset.v === S.rangeMode));

  // gyorsgombok: minden mért év, előző 365 nap, teljes időszak (Ctrl + kattintás: hozzáadás)
  const y0 = +fd.slice(0, 4), y1 = +today.slice(0, 4);
  const qb = [];
  for (let y = y0; y <= y1; y++) qb.push('<button class="chip" data-q="y' + y + '">' + y + '</button>');
  qb.push('<button class="chip" data-q="365">előző 365 nap</button>', '<button class="chip" data-q="all">teljes időszak</button>');
  $('stQuick').innerHTML = '<span class="muted">Gyorsan:</span> ' + qb.join('') + ' <span class="muted small">(Ctrl + kattintás: hozzáadás a meglévőkhöz)</span>';
  $('stQuick').querySelectorAll('[data-q]').forEach(b => b.addEventListener('click', e => {
    const q = b.dataset.q;
    let r;
    if (q === 'all') r = { ay: '', am: '', ad: '', by: '', bm: '', bd: '' };
    else if (q === '365') { const a = DS.add(today, -364); r = { ay: a.slice(0, 4), am: String(+a.slice(5, 7)), ad: String(+a.slice(8, 10)), by: '', bm: '', bd: '' }; }
    else r = { ay: q.slice(1), am: '', ad: '', by: q.slice(1), bm: '', bd: '' };
    if (e.ctrlKey) rows.push(r); else rows.splice(0, rows.length, r);
    saveUi(); renderRangePicker(); renderStats();
  }));

  // kedvencek
  $('stFavSel').innerHTML = '<option value="">– kedvenc időszakok –</option>' + S.favs.map((f, i) => '<option value="' + i + '">' + esc(f.name) + '</option>').join('');
  checkRows();
}

// hibák kijelzése soronként; true, ha minden sor érvényes
function checkRows() {
  const rows = stRanges();
  let ok = true;
  $('stRanges').querySelectorAll('.rrow').forEach(el => {
    const i = +el.dataset.i, x = resolveRow(rows[i]);
    el.querySelectorAll('.rnum').forEach(n => n.classList.toggle('bad', !!x.err[n.dataset.k]));
    el.querySelector('.perr').textContent = x.ok ? '' : '⚠ ' + Object.values(x.err).join(' · ');
    el.querySelector('.rlabel').textContent = x.ok ? '= ' + rangeLabel(x) : '';
    if (!x.ok) ok = false;
  });
  const list = ok ? rows.map(resolveRow) : null;
  $('stRangeSum').textContent = !ok ? 'Javítsd a pirossal jelölt mezőket – addig a statisztika a legutóbbi érvényes időszakot mutatja.'
    : UI.stat.rangeMode === 'side' && list.length > 1 ? 'Egymás mellett: a rekordok, a számlálók és az átlagos nap időszakonként külön oszlopban / vonalon.'
      : list.length > 1 ? 'Közös halmaz: a ' + list.length + ' időszak napjait együtt számolja.' : '';
  return ok;
}

document.querySelectorAll('#stRangeMode button').forEach(b => b.addEventListener('click', () => { stRanges(); UI.stat.rangeMode = b.dataset.v; saveUi(); renderRangePicker(); renderStats(); }));
$('stFavSave').addEventListener('click', () => {
  const name = $('stFavName').value.trim();
  if (!name) { $('stFavName').classList.add('bad'); $('stFavName').focus(); return; }
  if (name.length > 40) { toast('A név legfeljebb 40 karakter.'); return; }
  if (!validRanges()) { toast('Előbb javítsd a hibás időszakot.'); return; }
  $('stFavName').classList.remove('bad');
  const S = UI.stat, i = S.favs.findIndex(f => f.name === name);
  if (i >= 0 && !confirm('Már van „' + name + '” nevű kedvenc. Felülírod?')) return;
  const fav = { name, rows: JSON.parse(JSON.stringify(stRanges())), mode: S.rangeMode };
  if (i >= 0) S.favs[i] = fav; else S.favs.push(fav);
  saveUi(); renderRangePicker(); $('stFavSel').value = String(i >= 0 ? i : S.favs.length - 1);
  if (typeof logChange === 'function') logChange('Statisztika', 'kedvenc időszak', null, name);
  toast('Kedvenc mentve: ' + name);
});
$('stFavSel').addEventListener('change', e => {
  if (e.target.value === '') return;
  const f = UI.stat.favs[+e.target.value];
  UI.stat.ranges = JSON.parse(JSON.stringify(f.rows)); if (f.mode) UI.stat.rangeMode = f.mode;
  $('stFavName').value = f.name;
  saveUi(); renderRangePicker(); $('stFavSel').value = e.target.value; renderStats();
});
$('stFavDel').addEventListener('click', () => {
  const v = $('stFavSel').value;
  if (v === '') { toast('Válaszd ki a törlendő kedvencet.'); return; }
  const f = UI.stat.favs[+v];
  if (!confirm('Törlöd a(z) „' + f.name + '” kedvencet?')) return;
  UI.stat.favs.splice(+v, 1); saveUi(); renderRangePicker();
});
