'use strict';
/* Csapadékmérő-kalibráció (Beállítások kártya): kézi mérések tól–ig időszakokra → szorzó; hibajelzés, ha a kézi vagy az állomás mérése 0 */

let CAL = null;
async function renderCalib() {
  const box = $('calCard');
  if (!box) return;
  try { CAL = await api('/api/calib'); } catch (e) { box.textContent = 'Nem tölthető be: ' + e.message; return; }
  drawCalib();
}
function calRowHtml(r, i, x) {
  const st = x && x.station != null && !isNaN(x.station) ? rtext(x.station, 1) : '–';
  const ratio = x && x.valid ? num(x.ratio, 2) : '–';
  const msgs = (x && x.error ? '<div class="perr">⚠ ' + esc(x.error) + '</div>' : '') + (x && x.warn ? x.warn.map(w => '<div class="cal-warn">• ' + esc(w) + '</div>').join('') : '');
  const mv = r.manual === '' || r.manual == null ? '' : String(+(rconv(+r.manual)).toFixed(rdec(1))).replace('.', decSep());
  return '<tr data-i="' + i + '"' + (x && x.error ? ' class="cal-bad"' : '') + '><td>' + (i + 1) + '.</td>' +
    '<td><input type="datetime-local" class="inp" data-f="from" value="' + esc(r.from || '') + '"></td>' +
    '<td><input type="datetime-local" class="inp" data-f="to" value="' + esc(r.to || '') + '"></td>' +
    '<td><input class="inp num" data-f="manual" inputmode="decimal" style="width:80px" value="' + mv + '"> <span class="unit">' + rlabel() + '</span></td>' +
    '<td>' + st + '</td><td><b>' + ratio + '</b></td>' +
    '<td><input class="inp" data-f="note" maxlength="200" style="width:140px" value="' + esc(r.note || '') + '"></td>' +
    '<td><button class="btn small" data-del="' + i + '" title="Sor törlése">×</button></td></tr>' +
    (msgs ? '<tr class="cal-msg"><td></td><td colspan="7">' + msgs + '</td></tr>' : '');
}
function drawCalib() {
  const c = CAL, box = $('calCard');
  const comp = c.computed != null && !isNaN(c.computed);
  box.innerHTML = '<p>Tegyél ki egy kézi esővíz-mérőt (henger), és írd be, mennyit mért egy-egy időszakban. A program kiszámolja az állomás mérését ugyanarra az időszakra, és az arányukból egy szorzót: <b>Σ kézi / Σ állomás</b>. A kalibrált csapadék mindenhol érvényes (idővonal, napi összesítő, esőesemények, talajvíz-mérleg, FWI, rekordok, jelentés, export).</p>' +
    '<div class="row2"><label class="switch"><input type="checkbox" id="calOn"' + (c.enabled ? ' checked' : '') + '><span class="slider"></span><span>Kalibrált csapadék használata (kikapcsolva: a nyers állomásérték)</span></label></div>' +
    '<div class="tablewrap"><table class="dtable cal-table"><thead><tr><th></th><th>Tól</th><th>Ig</th><th>Kézi mérés</th><th>Állomás</th><th>Arány</th><th>Megjegyzés</th><th></th></tr></thead><tbody>' +
    c.rows.map((r, i) => calRowHtml(r, i, r)).join('') + '</tbody></table></div>' +
    '<div class="row2"><button class="btn" id="calAdd">+ Új időszak</button></div>' +
    '<div class="cal-sum">' + (comp ? 'Számolt szorzó: <b>' + num(c.computed, 3) + '</b> (Σ kézi ' + rtext(c.sumManual, 1) + ' / Σ állomás ' + rtext(c.sumStation, 1) + ') · soronkénti arányok átlaga ' + num(c.mean, 3) + ', szórása ' + num(c.std, 3) +
      (c.std > 0.15 ? ' – <span class="neg">az arányok eléggé eltérnek, érdemes több időszakot felvenni</span>' : '') : '<span class="muted">Még nincs érvényes sor – a szorzó 1 (nincs kalibrálás).</span>') + '</div>' +
    '<div class="row2"><label>Mód <select class="sel" id="calMode"><option value="global">egy szorzó mindenre</option><option value="period">időszakos (minden nap a hozzá legközelebbi kalibráció arányát kapja)</option></select></label></div>' +
    '<div class="row2"><label>Kézi szorzó <input class="inp num" id="calFactor" inputmode="decimal" style="width:80px" placeholder="' + (comp ? num(c.computed, 3) : '1') + '" value="' + (c.factor > 0 ? String(c.factor).replace('.', decSep()) : '') + '"></label> <button class="btn small" id="calFReset">visszaállítás a számoltra</button> <span class="perr" id="calFErr"></span></div>' +
    '<p class="hint">Most érvényes: <b>' + (c.active ? (c.mode === 'period' && !(c.factor > 0) ? 'időszakos szorzók' : '× ' + num(c.global, 3)) : 'nincs kalibrálás (nyers érték)') + '</b>. Üres kézi szorzó = a számolt érték.</p>' +
    '<div class="row2"><button class="btn primary" id="calSave">Mentés</button><span class="pmsg" id="calMsg"></span></div>';
  $('calMode').value = c.mode;
  box.querySelectorAll('[data-del]').forEach(b => b.addEventListener('click', () => { calCollect(); CAL.rows.splice(+b.dataset.del, 1); drawCalib(); }));
  $('calAdd').addEventListener('click', () => {
    calCollect();
    const lastTo = CAL.rows.length ? CAL.rows[CAL.rows.length - 1].to : '';
    CAL.rows.push({ from: lastTo || DS.add(DS.today(), -7) + 'T07:00', to: DS.today() + 'T07:00', manual: '', note: '' });
    drawCalib();
  });
  $('calFReset').addEventListener('click', () => { $('calFactor').value = ''; });
  $('calSave').addEventListener('click', saveCalib);
}
// a mezők értékei a CAL-ba; hibás mezőnél false
function calCollect() {
  let ok = true;
  $('calCard').querySelectorAll('tr[data-i]').forEach(tr => {
    const r = CAL.rows[+tr.dataset.i];
    tr.querySelectorAll('[data-f]').forEach(el => {
      const f = el.dataset.f;
      if (f === 'manual') {
        const s = String(el.value).trim().replace(',', '.');
        const v = /^\d+(\.\d+)?$/.test(s) ? qback('rain', parseFloat(s)) : NaN;
        el.classList.toggle('bad', isNaN(v) || v > 2000);
        if (isNaN(v) || v > 2000) ok = false; else r.manual = +v.toFixed(2);
        if (s === '') r.manual = '';
      } else {
        r[f] = el.value;
        if (f !== 'note') { const bad = !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(el.value); el.classList.toggle('bad', bad); if (bad) ok = false; }
      }
    });
    const a = tr.querySelector('[data-f="from"]').value, b = tr.querySelector('[data-f="to"]').value;
    if (a && b && a >= b) { tr.querySelector('[data-f="to"]').classList.add('bad'); ok = false; }
  });
  CAL.enabled = $('calOn').checked; CAL.mode = $('calMode').value;
  const fs_ = String($('calFactor').value).trim().replace(',', '.');
  $('calFErr').textContent = '';
  if (fs_ === '') CAL.factor = 0;
  else if (!/^\d+(\.\d+)?$/.test(fs_) || +fs_ < 0.3 || +fs_ > 3) { $('calFErr').textContent = '⚠ 0,3 és 3 között'; ok = false; }
  else CAL.factor = +fs_;
  return ok;
}
async function saveCalib() {
  if (!calCollect()) { $('calMsg').textContent = '⚠ Javítsd a pirossal jelölt mezőket (dátum: tól < ig, kézi mérés: szám).'; return; }
  const body = { enabled: CAL.enabled, mode: CAL.mode, factor: CAL.factor, rows: CAL.rows.map(r => ({ from: r.from, to: r.to, manual: r.manual === '' ? 0 : +r.manual, note: r.note || '' })) };
  try {
    CAL = await post('/api/calib', body);
    drawCalib();
    $('calMsg').textContent = '✔ Mentve, újraszámolás…';
    const ready = typeof waitReload === 'function' ? await waitReload() : true;
    if ($('calMsg')) $('calMsg').textContent = savedMsg(ready);
  } catch (e) { $('calMsg').textContent = '⚠ ' + e.message; }
}
// „kalibrált” jelzés a csapadék mellett
function calibBadge() { return META && META.calib && META.calib.active ? ' <span class="cal-badge" title="Kalibrált csapadék (Beállítások › Csapadékmérő-kalibráció)">kalibrált</span>' : ''; }
function decorateCalib() {
  const on = META && META.calib && META.calib.active;
  [['attekintes', 'h3', /^Csapadék$/], ['eso', 'h2', /^Eső$/]].forEach(([page, tag, re]) => {
    const pg = document.querySelector('[data-page="' + page + '"]');
    if (!pg) return;
    pg.querySelectorAll(tag).forEach(h => {
      const t = headText(h);   // az eredeti magyar szöveg, más nyelven is
      if (!re.test(t)) return;
      const b = h.querySelector('.cal-badge');
      if (on && !b) h.insertAdjacentHTML('beforeend', calibBadge());
      if (!on && b) b.remove();
    });
  });
}
