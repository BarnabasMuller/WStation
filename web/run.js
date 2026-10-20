'use strict';
/* Futás fül: komfortindex és tanuló futóruházat-tanácsadó */

let RUNS = null;
async function getRuns() { if (!RUNS) { const r = await api('/api/store/futas'); RUNS = Array.isArray(r) ? r : []; } return RUNS; }
async function saveRuns() { await post('/api/store/futas', RUNS); }

// hatásos hőmérséklet: hőérzet + napsütés − eső − erős szél
function teff(w) {
  if (!w || w.feels == null) return null;
  let t = w.feels;
  if (w.solar != null) t += w.solar > 500 ? 2 : w.solar > 200 ? 1 : 0;
  if (w.rainRate != null && w.rainRate > 0.5) t -= 2;
  if (w.wind != null && w.wind > 20) t -= 1;
  return t;
}

// ruházati javaslat a rétegszámból és a hatásos hőmérsékletből
function clothing(layers, te) {
  const parts = [];
  if (layers <= 1) parts.push('póló vagy trikó');
  else if (layers === 2) parts.push('hosszú ujjú felső', te < 8 ? 'vékony széldzseki' : 'vagy póló + vékony mellény');
  else if (layers === 3) parts.push('aláöltözet', 'hosszú ujjú felső', 'szél- vagy esőkabát');
  else parts.push('meleg aláöltözet', 'polár / thermo felső', 'szél- és vízálló kabát');
  parts.push(te < 5 ? 'hosszú (téli) nadrág' : te < 12 ? 'térdnadrág vagy vékony hosszú nadrág' : 'rövidnadrág');
  if (te < 5) parts.push('sapka vagy fejpánt');
  if (te < 2) parts.push('kesztyű');
  if (te >= 22) parts.push('napsapka, sok folyadék');
  return parts.join(', ');
}

// futónkénti tanuló modell: érzet = b0 + b1·Teff + b2·rétegek + b3·intenzitás (ridge-regresszió egy általános ökölszabály felé húzva)
const PRIOR = [-6.25, 0.25, 1.0, 0.3];   // 15 °C-on, 1 réteg, közepes (5) intenzitás = 0 („tökéletes”); „öltözz úgy, mintha 10 °C-kal melegebb lenne”
function fitModel(runs) {
  const X = runs.map(r => [1, r.teff, r.layers, r.intensity]), y = runs.map(r => r.feel);
  const lam = 3;
  const A = [0, 1, 2, 3].map(i => [0, 1, 2, 3].map(j => X.reduce((s, x) => s + x[i] * x[j], 0) + (i === j ? lam : 0)));
  const b = [0, 1, 2, 3].map(i => X.reduce((s, x, k) => s + x[i] * y[k], 0) + lam * PRIOR[i]);
  // Gauss-elimináció
  for (let i = 0; i < 4; i++) {
    let p = i; for (let k = i + 1; k < 4; k++) if (Math.abs(A[k][i]) > Math.abs(A[p][i])) p = k;
    [A[i], A[p]] = [A[p], A[i]]; [b[i], b[p]] = [b[p], b[i]];
    for (let k = i + 1; k < 4; k++) { const f = A[k][i] / A[i][i]; for (let j = i; j < 4; j++) A[k][j] -= f * A[i][j]; b[k] -= f * b[i]; }
  }
  const beta = [0, 0, 0, 0];
  for (let i = 3; i >= 0; i--) { let s = b[i]; for (let j = i + 1; j < 4; j++) s -= A[i][j] * beta[j]; beta[i] = s / A[i][i]; }
  if (beta[2] < 0.3) beta[2] = 0.3;   // egy réteg mindig melegít
  const rmse = runs.length ? Math.sqrt(runs.reduce((s, r) => s + Math.pow(r.feel - (beta[0] + beta[1] * r.teff + beta[2] * r.layers + beta[3] * r.intensity), 2), 0) / runs.length) : null;
  return { beta, rmse, n: runs.length };
}
function recommend(model, te, intensity) {
  const b = model.beta;
  const L = (0 - b[0] - b[1] * te - b[3] * intensity) / b[2];
  return Math.max(1, Math.min(6, Math.round(L)));
}

async function weatherAt(ms) {
  if (ms > META.last + 2 * H) return Object.assign({ future: true }, await api('/api/at?t=' + META.last));
  return api('/api/at?t=' + ms);
}

function localInput(ms) { const d = new Date(ms - new Date(ms).getTimezoneOffset() * 60000); return d.toISOString().slice(0, 16); }

async function renderRun() {
  if (currentTab !== 'futas' || !META) return;
  const runs = await getRuns();
  const names = [...new Set(runs.map(r => r.who).filter(Boolean))];
  // valódi legördülő: a ▾ gombra mindig az összes eddigi név
  const allNames = () => [...new Set((RUNS || []).map(r => r.who).filter(Boolean))].sort((a, b) => a.localeCompare(b, 'hu'));
  makeCombo($('advWho'), allNames);
  makeCombo($('logWho'), allNames);
  if (!$('advWhen').value) $('advWhen').value = localInput(Date.now());
  if (!$('logWhen').value) $('logWhen').value = localInput(Date.now());
  if (!$('advWho').value && names.length) $('advWho').value = UI.runWho || names[0];

  // komfort: ugyanabból a mérésből, mint a „Mit vegyek fel” (7.21), és ha nem friss, kiírja, mikori (6.25)
  const n = await weatherAt(Date.now()).catch(() => ({}));
  const cf = comfort(n.dew), fresh = n.t && Date.now() - n.t < 90 * 60000;
  $('runComfort').innerHTML = '<h3>' + (fresh ? 'Komfort most' : 'Komfort') + '</h3><div class="big">' + (cf ? cf[0] : '–') + '</div>' +
    (n.t && !fresh ? '<div class="line small warn">' + tf('a legutóbbi mérés alapján ({when})', { when: F.full.format(n.t) }) + '</div>' : '') +
    '<div class="line">Harmatpont <b>' + ttext(n.dew) + '</b> · hőérzet <b>' + ttext(n.feels) + '</b> · szél <b>' + wtext(n.wind) + '</b></div>' +
    '<div class="line small">Harmatpont-sávok: ' + dewBands() + '. Fülledt időben lassabb tempó és több folyadék javasolt.</div>';
  await advise();

  // napló
  const rows = runs.slice().sort((a, b) => b.t - a.t);
  $('runTable').innerHTML = rows.length ? '<table class="dtable"><thead><tr><th>Mikor</th><th>Ki</th><th>Intenzitás</th><th>Rétegek</th><th>Érzet</th><th>Hatásos hőm.</th><th></th></tr></thead><tbody>' +
    rows.map(r => '<tr><td>' + F.full.format(r.t) + '</td><td>' + ut(r.who) + '</td><td>' + r.intensity + '</td><td>' + r.layers + '</td><td>' + (r.feel > 0 ? '+' : '') + r.feel + '</td><td>' + ttext(r.teff, 1) + '</td><td><a href="#" data-del="' + r.id + '" title="Törlés">✕</a></td></tr>').join('') + '</tbody></table>' : '<div class="muted">Még nincs rögzített futás. Futás után írd be az 5 adatot – minél több futás van, annál pontosabb a javaslat.</div>';
  $('runTable').querySelectorAll('[data-del]').forEach(a => a.addEventListener('click', async e => {
    e.preventDefault();
    const r0 = RUNS.find(r => String(r.id) === a.dataset.del);
    if (!r0 || !(await askConfirm(tf('Törlöd a(z) {when} futást ({who})?', { when: F.full.format(r0.t), who: r0.who }), { sub: 'Ez nem vonható vissza.', ok: 'Futás törlése', cancel: 'Megtartom', danger: true }))) return;
    RUNS = RUNS.filter(r => String(r.id) !== a.dataset.del); await saveRuns(); renderRun();
  }));
}

async function advise() {
  const who = $('advWho').value.trim();
  const ms = new Date($('advWhen').value).getTime() || Date.now();
  const I = +$('advInt').value;
  $('advIntVal').textContent = I;
  const w = await weatherAt(ms);
  const te = teff(w);
  const runs = (RUNS || []).filter(r => !who || String(r.who).toLowerCase() === who.toLowerCase());   // „Peti” = „peti” (10.5)
  const model = fitModel(runs);
  if (te == null) { $('advOut').innerHTML = '<span class="muted">Ehhez az időponthoz nincs mérés.</span>'; return; }
  const L = recommend(model, te, I);
  const rel = model.n === 0 ? 'általános ökölszabály (még nincs saját futás)' : model.n < 5 ? 'kevés adat (' + model.n + ' futás) – még főleg az ökölszabályra épül' : model.n + ' futásból tanulva' + (model.rmse != null ? ', átlagos eltérés ±' + num(model.rmse, 1) + ' érzetpont' : '');
  const perLayer = model.beta[2] / Math.max(0.05, model.beta[1]);
  $('advOut').innerHTML = '<div class="adv-big">' + L + ' réteg</div>' +
    '<div class="line"><b>' + clothing(L, te) + '</b></div>' +
    '<div class="line">Időjárás' + (w.future ? ' (a legutóbbi mérés alapján, ' + F.full.format(w.t) + ')' : ' (' + F.full.format(w.t) + ')') + ': ' + ttext(w.temp) + ', hőérzet ' + ttext(w.feels) + ', szél ' + wtext(w.wind) + ', pára ' + num(w.hum, 0) + ' %' + (w.rainRate > 0.5 ? ', eső' : '') + ' → hatásos hőmérséklet <b>' + ttext(te, 1) + '</b></div>' +
    '<div class="line small">Megbízhatóság: ' + rel + (who ? ' – ' + esc(who) : '') + '. A modell szerint egy réteg kb. ' + tdtext(perLayer, 0) + '-ot ér, egy intenzitáspont kb. ' + tdtext(model.beta[3] / Math.max(0.05, model.beta[1]), 1) + '-ot.</div>';
}

['advWho', 'advWhen', 'advInt'].forEach(id => $(id).addEventListener('input', () => { if (id === 'advWho') { UI.runWho = $('advWho').value; saveUi(); } advise(); }));
$('advNow').addEventListener('click', () => { $('advWhen').value = localInput(Date.now()); advise(); });
$('logInt').addEventListener('input', () => $('logIntVal').textContent = $('logInt').value);
$('logFeel').addEventListener('input', () => { const v = +$('logFeel').value; $('logFeelVal').textContent = (v > 0 ? '+' : '') + v + (v === 0 ? ' (tökéletes)' : v < 0 ? ' (hideg)' : ' (meleg)'); });
$('logSave').addEventListener('click', async () => {
  let who = $('logWho').value.trim();
  if (!who) { toast('Írd be, ki futott.'); return; }
  // egy már ismert név más kis-/nagybetűvel ugyanaz a futó (10.5)
  const known = (RUNS || []).map(r => r.who).find(x => x && x.toLowerCase() === who.toLowerCase()); if (known) who = known;
  const t = new Date($('logWhen').value).getTime();
  if (!t) { toast('Add meg, mikor volt a futás.'); return; }
  const w = await api('/api/at?t=' + t);
  if (!w.found) { toast('Ehhez az időponthoz nincs mérés (frissítsd az adatokat, vagy válassz korábbi időpontot).'); return; }
  const te = teff(w);
  if (te == null) { toast('Ehhez az időponthoz nincs hőmérséklet-adat (hőérzet), így a futás nem rögzíthető. Válassz egy közeli másik időpontot.', 7000); return; }
  await getRuns();
  RUNS.push({ id: Date.now(), t, who, intensity: +$('logInt').value, layers: +$('logLayers').value, feel: +$('logFeel').value, teff: +te.toFixed(1), temp: w.temp, feels: w.feels, wind: w.wind, hum: w.hum, solar: w.solar });
  await saveRuns();
  toast('Futás rögzítve – köszönöm, ebből tanul a tanácsadó.');
  $('advWho').value = who;
  renderRun();
});

// a harmatpont-sávok a választott hőmérséklet-egységben
function dewBands() {
  const t = x => num(tconv(x), 0);
  return '&lt; ' + t(10) + ' ' + tlabel() + ' száraz, kellemes · ' + t(10) + '–' + t(16) + ' kellemes · ' + t(16) + '–' + t(18) + ' kissé párás · ' + t(18) + '–' + t(21) + ' fülledt · &gt; ' + t(21) + ' nagyon fülledt';
}
