'use strict';
/* Kerti napló (Naptár fül) és a riasztások beállítása (Beállítások fül) */

const DIARY_TAGS = { metszés: '✂️', trágyázás: '🧪', permetezés: '💨', virágzás: '🌸', termés: '🍑', öntözés: '💧', ültetés: '🌱', egyéb: '📝' };
let DIARY = null;
async function getDiary() { if (!DIARY) { const r = await api('/api/store/naplo'); DIARY = Array.isArray(r) ? r : []; } return DIARY; }
async function saveDiary() {
  await post('/api/store/naplo', DIARY);
  if (typeof invalidateMarks === 'function') invalidateMarks();
}

async function renderDiary() {
  if (currentTab !== 'naptar') return;
  const list = await getDiary();
  const sel = $('dTag');
  if (!sel.options.length) sel.innerHTML = Object.keys(DIARY_TAGS).map(t => '<option value="' + t + '">' + DIARY_TAGS[t] + ' ' + t + '</option>').join('');
  if (!$('dDate').value) $('dDate').value = DS.today();
  // öntözésnél zóna (a szárazság-profilok közül) és mennyiség
  const z = $('dZone');
  if (typeof DR !== 'undefined' && DR && z.options.length !== DR.config.profiles.length) z.innerHTML = DR.config.profiles.filter(p => p.type !== 'felszin').map(p => '<option value="' + p.id + '">' + esc(p.name) + '</option>').join('');
  $('dWater').classList.toggle('hidden', $('dTag').value !== 'öntözés');
  const filterMonth = UI.cal && UI.cal.view === 'month' ? calMonth : null;
  const rows = list.map((x, i) => Object.assign({ i }, x)).filter(x => !filterMonth || (x.date || '').startsWith(filterMonth)).sort((a, b) => (b.date || '').localeCompare(a.date || ''));
  $('dList').innerHTML = rows.length ? rows.map(x => '<div class="drow"><span class="ddate" data-d="' + x.date + '">' + DS.fmt(x.date, { year: 'numeric', month: 'short', day: 'numeric', weekday: 'short' }) + '</span>' +
    '<span class="dtag">' + (DIARY_TAGS[x.tag] || '📝') + ' ' + esc(x.tag || '') + '</span><span class="dtext">' + esc(x.text || '') + (x.amount ? ' <span class="muted">(' + num(x.amount, 1) + ' ' + esc(x.unit || '') + ')</span>' : '') + '</span>' +
    '<a href="#" data-del="' + x.i + '" title="Törlés">✕</a></div>').join('') : '<div class="muted">' + (filterMonth ? 'Ebben a hónapban nincs bejegyzés.' : 'Még nincs bejegyzés. Írd be pl. a metszést, a trágyázást vagy a virágzás napját.') + '</div>';
  $('dList').querySelectorAll('[data-d]').forEach(s => s.addEventListener('click', () => gotoDay(s.dataset.d)));
  $('dList').querySelectorAll('[data-del]').forEach(a => a.addEventListener('click', async e => {
    e.preventDefault();
    if (!confirm('Törlöd a bejegyzést?')) return;
    DIARY.splice(+a.dataset.del, 1); await saveDiary(); renderDiary(); renderCalendar();
  }));
}

$('dTag').addEventListener('change', () => $('dWater').classList.toggle('hidden', $('dTag').value !== 'öntözés'));
$('dAdd').addEventListener('click', async () => {
  const date = $('dDate').value, tag = $('dTag').value, text = $('dText').value.trim();
  if (!date) { toast('Add meg a dátumot.'); return; }
  await getDiary();
  const entry = { date, tag, text };
  if (tag === 'öntözés') {
    const amount = parseFloat(String($('dAmount').value).replace(',', '.'));
    if (amount > 0) {
      entry.amount = amount; entry.unit = $('dUnit').value; entry.zone = $('dZone').value;
      // a locsolás a talajvíz-mérlegbe is bekerül
      if (typeof DR !== 'undefined' && DR) { DR.config.log.push({ date, zone: entry.zone, unit: entry.unit, amount, note: 'kerti napló' }); await saveDroughtConfig(); }
    }
  }
  DIARY.push(entry);
  await saveDiary();
  $('dText').value = ''; $('dAmount').value = '';
  toast('Bejegyzés mentve.');
  renderDiary(); renderCalendar();
});

// a naptár buboréka és a hónapnézet cellája a bejegyzéseket is mutatja
function diaryFor(dateStr) { return (DIARY || []).filter(x => x.date === dateStr); }

// ------------------------------------------------------------------ riasztások (a Letöltő beállításai)
// [kapcsoló, érték, felirat, egység utáni szöveg, mennyiség, érvényes tartomány (metrikusan)]
const ALERT_UI = [
  ['RiasztasFagy', 'FagyHatar', 'Fagyveszély', 'alatt (vagy egyenlő)', 'temp', [-30, 10]],
  ['RiasztasNyomas', 'NyomasEses3h', 'Gyors nyomásesés', '/ 3 óra', 'presd', [0.5, 20]],
  ['RiasztasSzel', 'LokesHatar', 'Erős szél (lökés)', 'felett', 'wind', [10, 200]],
  ['RiasztasEso', 'EsoIntenzitas', 'Felhőszakadás', 'felett', 'rate', [1, 300]],
  ['RiasztasHoseg', 'HosegHatar', 'Hőség', 'felett', 'temp', [20, 45]]
];
async function renderAlerts() {
  let a;
  try { a = await api('/api/alerts'); } catch (e) { return; }
  $('alertBox').innerHTML = (a.hasIni ? '' : '<p class="msg err">Még nincs beállításfájl: előbb indítsd el egyszer a Letöltőt.</p>') +
    '<table class="ptable small"><tbody>' + ALERT_UI.map(([on, val, label, unit, q]) => {
      const v = qv(q, parseFloat(String(a[val]).replace(',', '.')));
      return '<tr><td><label class="switch small"><input type="checkbox" data-k="' + on + '"' + (String(a[on]).toLowerCase() === 'true' ? ' checked' : '') + '><span class="slider"></span></label></td><td>' + label + '</td><td><input class="inp num" inputmode="decimal" data-k="' + val + '" data-q="' + q + '" value="' + (v == null ? '' : String(+v.toFixed(qd(q, 1) + 1))) + '"> <span class="unit">' + qu(q) + ' ' + unit + '</span> <span class="perr"></span></td></tr>';
    }).join('') +
    '<tr><td></td><td>Ismétlés legfeljebb</td><td><input class="inp num" type="number" step="1" data-k="RiasztasIsmetlesOra" value="' + esc(a.RiasztasIsmetlesOra) + '"> <span class="unit">óránként</span></td></tr>' +
    '<tr><td></td><td>Csendes órák</td><td><input class="inp num" type="number" step="1" min="0" max="24" data-k="CsendesTol" value="' + esc(a.CsendesTol) + '"> – <input class="inp num" type="number" step="1" min="0" max="24" data-k="CsendesIg" value="' + esc(a.CsendesIg) + '"></td></tr>' +
    '</tbody></table>';
}
$('alertSave').addEventListener('click', async () => {
  const body = {};
  let bad = false;
  $('alertBox').querySelectorAll('[data-k]').forEach(el => {
    if (el.type === 'checkbox') { body[el.dataset.k] = el.checked; return; }
    const err = el.parentNode.querySelector('.perr');
    const s = String(el.value).trim().replace(',', '.');
    let x = /^-?\d+(\.\d+)?$/.test(s) ? parseFloat(s) : NaN;
    const row = ALERT_UI.find(r => r[1] === el.dataset.k);
    let msg = '';
    if (isNaN(x)) msg = '⚠ számot adj meg';
    else if (el.dataset.q) {
      x = qback(el.dataset.q, x);
      const [lo, hi] = row[5];
      if (x < lo - 1e-9 || x > hi + 1e-9) msg = '⚠ ' + num(qv(row[4], lo), qd(row[4], 0)) + ' és ' + num(qv(row[4], hi), qd(row[4], 0)) + ' ' + qu(row[4]) + ' között';
      x = +x.toFixed(2);
    } else if (el.dataset.k === 'RiasztasIsmetlesOra' && (x < 1 || x > 168 || x % 1)) msg = '⚠ 1–168 egész óra';
    else if ((el.dataset.k === 'CsendesTol' || el.dataset.k === 'CsendesIg') && (x < 0 || x > 24 || x % 1)) msg = '⚠ 0–24 egész';
    if (err) err.textContent = msg; else if (msg) el.title = msg;
    el.classList.toggle('bad', !!msg);
    if (msg) bad = true;
    body[el.dataset.k] = String(x);
  });
  if (bad) { $('alertMsg').className = 'msg err'; $('alertMsg').textContent = 'Javítsd a pirossal jelölt értékeket – nem mentettem.'; return; }
  try { const r = await post('/api/alerts', body); $('alertMsg').className = 'msg ' + (r.error ? 'err' : 'ok'); $('alertMsg').textContent = r.error || 'Mentve. A következő letöltéstől érvényes.'; renderAlerts(); }
  catch (e) { $('alertMsg').className = 'msg err'; $('alertMsg').textContent = e.message; }
});
$('alertTest').addEventListener('click', async () => {
  const r = await post('/api/alerts/test');
  $('alertMsg').className = 'msg ' + (r.error ? 'err' : 'ok');
  $('alertMsg').textContent = r.error || 'Próba-értesítés elküldve (jobb alsó sarok / értesítési központ).';
});
