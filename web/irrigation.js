'use strict';
/* Öntözés – egyszerű változat: mentett programok (zóna, napok, mennyiség) és érvényességi időszakaik.
   Új időszakhoz csak kiválasztod a programot és megadod a tól–ig dátumot. A mérleg minden programozott öntözést beszámít. */

const IRR_UNITS = { gyep: ['perc', 'mm', 'l'], bokor: ['l', 'perc', 'mm'], fa: ['l', 'perc', 'mm'], cserep: ['l', 'mm'], felszin: ['mm'] };
const IRR_UNIT_NAME = { perc: 'perc', mm: 'mm', l: 'liter' };
const DOW = ['H', 'K', 'Sze', 'Cs', 'P', 'Szo', 'V'];

// ugyanaz, mint a szerveren (DroughtStore.ProgramOn)
function programOn(pg, sc, s) {
  let a;
  const md = s.slice(5);
  if (sc.yearly) {
    const inside = sc.from <= sc.to ? md >= sc.from && md <= sc.to : md >= sc.from || md <= sc.to;
    if (!inside) return false;
    a = s.slice(0, 4) + '-' + sc.from;
    if (a > s) a = (+s.slice(0, 4) - 1) + '-' + sc.from;
  } else {
    if (s < sc.from || s > sc.to) return false;
    a = sc.from;
  }
  if (pg.every > 0) return Math.round((DS.ms(s) - DS.ms(a)) / D) % pg.every === 0;
  const dow = (new Date(DS.ms(s) + 12 * H).getUTCDay() + 6) % 7 + 1;
  return String(pg.days || '').split(/[,; ]+/).includes(String(dow));
}
function irrToMm(p, r, amount, unit) {
  const area = Math.max(1e-4, (r && r.area) || 1);
  if (unit === 'mm') return amount;
  if (unit === 'perc') {
    if (p.type === 'gyep') return amount / 60 * p.sprinklerRate;
    if (p.type === 'bokor' || p.type === 'fa') return p.dripCount * p.dripLph * amount / 60 / area;
    return 0;
  }
  return amount / area;
}
function programsFor(zoneId) { return (DR.config.programs || []).filter(pg => pg.zone === zoneId); }
// a következő 7 nap programozott öntözése (mm) egy zónára
function weeklyProgramMm(p, r) {
  const today = DS.today();
  let mm = 0, n = 0;
  (DR.config.schedules || []).forEach(sc => {
    const pg = (DR.config.programs || []).find(x => x.id === sc.program);
    if (!pg || pg.zone !== p.id) return;
    for (let k = 0; k < 7; k++) if (programOn(pg, sc, DS.add(today, k))) { mm += irrToMm(p, r, pg.amount, pg.unit); n++; }
  });
  return { mm, n };
}
// a javaslatba: elég-e a program
function programAdvice(p, r) {
  const w = weeklyProgramMm(p, r);
  if (!w.n) return null;
  const need = Math.max(0, avgLast(r.kc, 7) * avgLast(DR.et0, 7) * 7 - sumLast(DR.rain, 7));
  const ratio = need > 0 ? w.mm / need : 9;
  const amt = p.type === 'gyep' ? rtext(w.mm, 0) : fmtL(w.mm * (r.area || 1)) + (p.type === 'bokor' ? ' bokronként' : '');
  const needTxt = p.type === 'gyep' ? rtext(need, 0) : fmtL(need * (r.area || 1));
  return 'A programok a következő 7 napban <b>' + amt + '</b> vizet adnak (' + w.n + ' alkalom), a szükséglet kb. <b>' + needTxt + '</b> – ' +
    (ratio < 0.85 ? '<b class="neg">kevés</b>.' : ratio > 1.4 ? '<b class="pos">több a szükségesnél</b>, csökkenthető.' : '<b class="pos">elég</b>.');
}

function pgSummary(pg) {
  const p = profById(pg.zone);
  const when = pg.every > 0 ? (pg.every === 1 ? 'naponta' : pg.every + ' naponta') : String(pg.days || '').split(/[,; ]+/).filter(Boolean).map(d => DOW[+d - 1]).join('-');
  return (p ? TYPE_ICON[p.type] + ' ' + ut(p.name) : '<span class="neg">törölt zóna</span>') + ' · ' + when + ', ' + num(pg.amount, pg.amount % 1 ? 1 : 0) + ' ' + IRR_UNIT_NAME[pg.unit];
}
function scText(sc) {
  if (sc.yearly) return 'minden évben ' + sc.from.replace('-', '. ') + '. – ' + sc.to.replace('-', '. ') + '.';
  const f = s => DS.fmt(s, { year: 'numeric', month: 'short', day: 'numeric' });
  return f(sc.from) + ' – ' + f(sc.to);
}

function renderIrrigation() {
  const box = $('irrBox');
  if (!box || !DR) return;
  const c = DR.config;
  c.programs = c.programs || []; c.schedules = c.schedules || [];
  const today = DS.today();
  const zones = c.profiles.filter(p => p.type !== 'felszin');
  box.innerHTML =
    '<h3 class="sub-h">Mentett programok</h3>' +
    (c.programs.length ? '<table class="dtable"><tbody>' + c.programs.map((pg, i) => '<tr><td><b>' + ut(pg.name) + '</b></td><td>' + pgSummary(pg) + '</td><td>' +
      c.schedules.filter(s => s.program === pg.id).length + ' időszak</td><td><button class="btn small" data-pedit="' + i + '">szerkeszt</button> <button class="btn small" data-pdel="' + i + '">×</button></td></tr>').join('') + '</tbody></table>'
      : '<p class="muted">Még nincs mentett program. Pl. „Nyári gyep: H-Sze-P, 12 perc” vagy „Bokrok csepegtető: naponta 6 l”.</p>') +
    '<div class="irr-form" id="pgForm"></div>' +
    '<div class="row2"><button class="btn" id="pgNew">+ Új program</button></div>' +
    '<h3 class="sub-h">Időszakok – mikor fut a program</h3>' +
    (c.schedules.length ? '<table class="dtable"><thead><tr><th>Program</th><th>Érvényes</th><th>Most</th><th></th></tr></thead><tbody>' + c.schedules.map((sc, i) => {
      const pg = c.programs.find(x => x.id === sc.program);
      const active = pg && (sc.yearly ? true : today >= sc.from && today <= sc.to) && (sc.yearly ? (sc.from <= sc.to ? today.slice(5) >= sc.from && today.slice(5) <= sc.to : today.slice(5) >= sc.from || today.slice(5) <= sc.to) : true);
      return '<tr><td>' + (pg ? ut(pg.name) : '–') + '</td><td>' + scText(sc) + '</td><td>' + (active ? '<span class="tag ok">aktív</span>' : '<span class="muted">–</span>') + '</td><td><button class="btn small" data-sdel="' + i + '">×</button></td></tr>';
    }).join('') + '</tbody></table>' : '<p class="muted">Nincs időszak – a programok addig nem számítanak bele a mérlegbe.</p>') +
    (c.programs.length ? '<div class="row2 irr-sched"><select class="sel" id="scProg">' + c.programs.map(pg => '<option class="ut" value="' + pg.id + '">' + esc(pg.name) + '</option>').join('') + '</select>' +
      '<label class="switch small"><input type="checkbox" id="scYearly"><span class="slider"></span><span>minden évben</span></label>' +
      '<span id="scDates"><input type="date" class="inp" id="scFrom" value="' + today + '"> – <input type="date" class="inp" id="scTo" value="' + DS.add(today, 30) + '"></span>' +
      '<span id="scMd" class="hidden"><input class="inp" id="scFromMd" placeholder="HH-NN" maxlength="5" style="width:70px" value="05-01"> – <input class="inp" id="scToMd" placeholder="HH-NN" maxlength="5" style="width:70px" value="09-30"></span>' +
      '<button class="btn primary" id="scAdd">+ Időszak hozzáadása</button><span class="perr" id="scErr"></span></div>' : '');

  box.querySelectorAll('[data-pdel]').forEach(b => b.addEventListener('click', () => {
    const pg = c.programs[+b.dataset.pdel];
    const n = c.schedules.filter(s => s.program === pg.id).length;
    if (!confirm('Törlöd a(z) „' + pg.name + '” programot' + (n ? ' és ' + n + ' időszakát' : '') + '?')) return;
    c.schedules = c.schedules.filter(s => s.program !== pg.id);
    c.programs.splice(+b.dataset.pdel, 1);
    saveDroughtConfig('Program törölve.');
  }));
  box.querySelectorAll('[data-pedit]').forEach(b => b.addEventListener('click', () => programForm(c.programs[+b.dataset.pedit])));
  box.querySelectorAll('[data-sdel]').forEach(b => b.addEventListener('click', () => { c.schedules.splice(+b.dataset.sdel, 1); saveDroughtConfig('Időszak törölve.'); }));
  $('pgNew').addEventListener('click', () => programForm(null));
  if ($('scYearly')) {
    $('scYearly').addEventListener('change', e => { $('scDates').classList.toggle('hidden', e.target.checked); $('scMd').classList.toggle('hidden', !e.target.checked); });
    $('scAdd').addEventListener('click', () => {
      const yearly = $('scYearly').checked;
      const from = yearly ? $('scFromMd').value.trim() : $('scFrom').value, to = yearly ? $('scToMd').value.trim() : $('scTo').value;
      let err = '';
      if (yearly ? !(validMD(from) && validMD(to)) : !(/^\d{4}-\d{2}-\d{2}$/.test(from) && /^\d{4}-\d{2}-\d{2}$/.test(to))) err = yearly ? 'HH-NN formában (pl. 05-01)' : 'adj meg két dátumot';
      else if (!yearly && from > to) err = 'a „tól” későbbi, mint az „ig”';
      $('scErr').textContent = err ? '⚠ ' + err : '';
      if (err) return;
      c.schedules.push({ id: 'sc' + Date.now().toString(36), program: $('scProg').value, from, to, yearly });
      saveDroughtConfig('Időszak hozzáadva – a mérleg beszámítja.');
    });
  }
}

function programForm(pg) {
  const c = DR.config, zones = c.profiles.filter(p => p.type !== 'felszin');
  if (!zones.length) { toast('Előbb vegyél fel egy növényt.'); return; }
  const isNew = !pg;
  pg = pg ? Object.assign({}, pg) : { id: 'pg' + Date.now().toString(36), name: '', zone: zones[0].id, days: '1,4', every: 0, amount: 10, unit: IRR_UNITS[zones[0].type][0] };
  const f = $('pgForm');
  const unitOpts = z => (IRR_UNITS[(profById(z) || {}).type] || ['mm']).map(u => '<option value="' + u + '"' + (u === pg.unit ? ' selected' : '') + '>' + IRR_UNIT_NAME[u] + '</option>').join('');
  const days = String(pg.days || '').split(/[,; ]+/);
  f.innerHTML = '<div class="row2"><label>Név <input class="inp" id="pgName" maxlength="60" style="width:220px" value="' + esc(pg.name) + '" placeholder="pl. Nyári gyep"></label>' +
    '<label>Zóna <select class="sel" id="pgZone">' + zones.map(p => '<option class="ut" value="' + p.id + '"' + (p.id === pg.zone ? ' selected' : '') + '>' + TYPE_ICON[p.type] + ' ' + esc(p.name) + '</option>').join('') + '</select></label></div>' +
    '<div class="row2"><select class="sel" id="pgMode"><option value="days">a hét napjain</option><option value="every">x naponta</option></select>' +
    '<span class="daypick" id="pgDays">' + DOW.map((d, i) => '<label><input type="checkbox" value="' + (i + 1) + '"' + (days.includes(String(i + 1)) ? ' checked' : '') + '>' + d + '</label>').join('') + '</span>' +
    '<span id="pgEveryW" class="hidden">minden <input class="inp num" id="pgEvery" style="width:50px" value="' + (pg.every || 2) + '"> napon</span></div>' +
    '<div class="row2"><label>Menetenként <input class="inp num" id="pgAmount" inputmode="decimal" style="width:80px" value="' + String(pg.amount).replace('.', decSep()) + '"></label> <select class="sel" id="pgUnit">' + unitOpts(pg.zone) + '</select> <span class="muted" id="pgConv"></span></div>' +
    '<div class="row2"><button class="btn primary" id="pgSave">' + (isNew ? 'Program mentése' : 'Módosítás mentése') + '</button><button class="btn" id="pgCancel">Mégse</button><span class="perr" id="pgErr"></span></div>';
  $('pgMode').value = pg.every > 0 ? 'every' : 'days';
  const sync = () => {
    $('pgDays').classList.toggle('hidden', $('pgMode').value !== 'days'); $('pgEveryW').classList.toggle('hidden', $('pgMode').value !== 'every');
    const p = profById($('pgZone').value), r = resById($('pgZone').value);
    const a = parseFloat(String($('pgAmount').value).replace(',', '.'));
    $('pgConv').textContent = p && !isNaN(a) ? '≈ ' + rtext(irrToMm(p, r, a, $('pgUnit').value), 1) + (p.type !== 'gyep' ? ' = ' + fmtL(irrToMm(p, r, a, $('pgUnit').value) * ((r && r.area) || 1)) + (p.type === 'bokor' ? ' bokronként' : '') : '') + ' menetenként' : '';
  };
  $('pgZone').addEventListener('change', () => { $('pgUnit').innerHTML = unitOpts($('pgZone').value); sync(); });
  f.addEventListener('input', sync); f.addEventListener('change', sync); sync();
  $('pgCancel').addEventListener('click', () => { f.innerHTML = ''; });
  $('pgSave').addEventListener('click', () => {
    const name = $('pgName').value.trim(), mode = $('pgMode').value;
    const amount = parseFloat(String($('pgAmount').value).replace(',', '.'));
    const sel = [...$('pgDays').querySelectorAll('input:checked')].map(x => x.value);
    const every = parseInt($('pgEvery').value, 10);
    let err = '';
    if (!name) err = 'adj nevet a programnak';
    else if (c.programs.some(x => x.name === name && x.id !== pg.id)) err = 'már van ilyen nevű program';
    else if (!/^\d+([.,]\d+)?$/.test(String($('pgAmount').value).trim()) || !(amount > 0) || amount > 100000) err = 'a mennyiség pozitív szám legyen';
    else if (mode === 'days' && !sel.length) err = 'válassz legalább egy napot';
    else if (mode === 'every' && !(every >= 1 && every <= 60 && /^\d+$/.test($('pgEvery').value.trim()))) err = 'x naponta: 1–60 egész';
    $('pgErr').textContent = err ? '⚠ ' + err : '';
    if (err) return;
    const out = { id: pg.id, name, zone: $('pgZone').value, days: mode === 'days' ? sel.join(',') : pg.days || '1', every: mode === 'every' ? every : 0, amount, unit: $('pgUnit').value };
    const i = c.programs.findIndex(x => x.id === pg.id);
    if (i >= 0) c.programs[i] = out; else c.programs.push(out);
    saveDroughtConfig(isNew ? 'Program mentve – adj hozzá időszakot is.' : 'Program módosítva.');
  });
}
