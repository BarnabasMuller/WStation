'use strict';
/* „Mi változott” sáv az Áttekintés tetején: a legutóbbi megnyitás óta (24 órán belül: „Tegnap óta”) történt fontos dolgok */

let PREV_OPEN = null, CHG_CLOSED = false, CHG_DONE = false;
// az indításkor egyszer: az előző megnyitás ideje, és az új mentése (a Frissítés gomb nem számít megnyitásnak)
function markOpen() {
  PREV_OPEN = +UI.lastOpen || null;
  UI.lastOpen = Date.now();
  saveUi();
}

async function renderChanges() {
  const box = $('changeBar');
  if (!box || CHG_CLOSED || !META || !PREV_OPEN) { if (box) box.classList.add('hidden'); return; }
  const now = Date.now(), from = PREV_OPEN;
  const within24 = now - from < 24 * H;
  const title = within24 ? 'Tegnap óta' : 'Az előző megnyitás óta (' + DS.fmt(F.ymd.format(from), { year: 'numeric', month: '2-digit', day: '2-digit' }) + ')';
  // ha az utolsó mérés régebbi, mint az előző megnyitás, nincs új adat
  if (META.last <= from) {
    box.innerHTML = '<b>' + title + ':</b> nem érkezett új mérés. <button class="chg-x" title="Bezárás">×</button>';
    box.classList.remove('hidden'); wireClose(box); return;
  }
  const [d, ev, rain, recNow, recBefore] = await Promise.all([
    getDays(true), api('/api/events').catch(() => ({ events: [] })), getRainEvents().catch(() => ({ events: [] })),
    api('/api/records?clean=1').catch(() => null), api('/api/records?clean=1&ranges=' + encodeURIComponent('_' + DS.add(F.ymd.format(from), -1))).catch(() => null)
  ]);
  if (typeof getDrought === 'function') { try { await getDrought(); } catch (e) { } }
  const fromDay = F.ymd.format(from), items = [];   // [fontosság, html, kattintás]
  const dayIdx = d.date.map((s, i) => i).filter(i => d.date[i] >= fromDay);
  // eső
  const evs = (rain.events || []).filter(e => e.end >= from);
  const rainSum = evs.reduce((s, e) => s + e.total, 0);
  if (rainSum >= 0.2) {
    const big = evs.slice().sort((a, b) => b.total - a.total)[0];
    items.push([rainSum >= 10 ? 9 : 6, '🌧️ <b>+' + rtext(rainSum, 1) + ' eső</b>' + (big ? ' (a legnagyobb ' + F.full.format(big.start) + ', ' + rtext(big.total, 1) + ')' : ''), big ? { a: big.start, b: big.end } : null]);
  } else if (dayIdx.length >= 2) items.push([2, '☀️ nem esett', null]);
  // front, zivatar
  (ev.events || []).filter(e => e.t >= from).forEach(e => items.push([8, (e.type === 'zivatar' ? '⚡ ' : '❄ ') + '<b>' + esc(e.title) + '</b> ' + F.full.format(e.t) + ' (' + esc(e.detail) + ')', { t: e.t }]));
  // fagy, legerősebb lökés, hőmérséklet-változás
  let gi = -1, fi = -1;
  dayIdx.forEach(i => { if (d.gust[i] != null && (gi < 0 || d.gust[i] > d.gust[gi])) gi = i; if (d.tmin[i] != null && d.tmin[i] < 0 && (fi < 0 || d.tmin[i] < d.tmin[fi])) fi = i; });
  if (fi >= 0) items.push([8, '🥶 <b>fagy</b>: ' + ttext(d.tmin[fi]) + ' (' + DS.fmt(d.date[fi], { month: 'short', day: 'numeric' }) + ')', { day: d.date[fi] }]);
  if (gi >= 0 && d.gust[gi] >= 40) items.push([7, '💨 erős lökés: <b>' + wtext(d.gust[gi]) + '</b> (' + DS.fmt(d.date[gi], { month: 'short', day: 'numeric' }) + ')', { day: d.date[gi] }]);
  const li = d.date.length - 1, pi = d.idx[DS.add(d.date[li], -1)];
  if (pi !== undefined && d.tmax[li] != null && d.tmax[pi] != null && Math.abs(d.tmax[li] - d.tmax[pi]) >= 4)
    items.push([5, '🌡️ a legmagasabb hőmérséklet ' + (d.tmax[li] > d.tmax[pi] ? 'nőtt' : 'csökkent') + ': ' + ttext(d.tmax[pi], 0) + ' → <b>' + ttext(d.tmax[li], 0) + '</b>', { day: d.date[li] }]);
  // kiszáradás alakulása (gyep, és ami stresszben van)
  if (typeof DR !== 'undefined' && DR) {
    const j0 = DR.idx[fromDay] != null ? Math.max(0, DR.idx[fromDay] - 1) : null, j1 = DR.date.length - 1;
    if (j0 != null) DR.config.profiles.filter(p => p.enabled && p.type !== 'felszin').forEach(p => {
      const r = resById(p.id); if (!r) return;
      const a = r.depl[j0], b = r.depl[j1], raw = r.raw[j1] / r.taw[j1];
      if (a == null || b == null || (Math.abs(b - a) < 0.1 && b < raw)) return;
      const act = b >= raw ? ' – <b class="neg">locsolni kell</b>' : b >= raw * 0.75 ? ' – hamarosan locsolni kell' : '';
      items.push([b >= raw ? 8 : 4, TYPE_ICON[p.type] + ' ' + esc(p.name) + ' kiszáradása ' + num(a * 100, 0) + '% → <b>' + num(b * 100, 0) + '%</b>' + act, { tab: 'szarazsag' }]);
    });
  }
  // új rekordok
  if (recNow && recBefore) recNow.records.forEach(r => {
    const o = recBefore.records.find(x => x.key === r.key);
    if (r.t >= from && (!o || o.value !== r.value)) items.push([7, '🏆 új rekord: <b>' + esc(recTitle(r).toLowerCase()) + '</b> ' + recValue(r), { t: r.t }]);
  });
  // adathiány
  const gaps = (META.gaps || []).filter(g => g.to >= from && g.to - g.from >= 3 * H);
  if (gaps.length) items.push([3, '⚠ ' + gaps.length + ' hosszabb adathiány (összesen ' + num(gaps.reduce((s, g) => s + g.to - g.from, 0) / H, 0) + ' óra)', { a: gaps[0].from, b: gaps[0].to }]);

  const top = items.filter(x => x[0] >= 3).sort((a, b) => b[0] - a[0]);
  let html = '<b>' + title + ':</b> ' + (top.length ? top.slice(0, within24 ? 8 : 6).map((x, k) => '<span class="chg-i' + (x[2] ? ' link' : '') + '" data-k="' + items.indexOf(x) + '">' + x[1] + '</span>').join(' · ') : 'nyugodt időszak, nincs teendő.');
  // hosszabb kimaradásnál napokra bontva (lenyitható)
  if (!within24 && dayIdx.length > 1) {
    html += '<details class="chg-days"><summary>napokra bontva (' + dayIdx.length + ' nap)</summary><table class="dtable"><tbody>' + dayIdx.slice().reverse().map(i => {
      const evd = (ev.events || []).filter(e => F.ymd.format(e.t) === d.date[i]);
      return '<tr class="link" data-day="' + d.date[i] + '"><td>' + DS.fmt(d.date[i], { month: 'short', day: 'numeric', weekday: 'short' }) + '</td><td>' + num(tconv(d.tmin[i]), 0) + ' / ' + ttext(d.tmax[i], 0) + '</td><td>' + (d.rain[i] > 0 ? rtext(d.rain[i], 1) : '–') + '</td><td>lökés ' + wtext(d.gust[i]) + '</td><td>' + evd.map(e => esc(e.title)).join(', ') + '</td></tr>';
    }).join('') + '</tbody></table></details>';
  }
  box.innerHTML = html + ' <button class="chg-x" title="Bezárás (a következő megnyitásig)">×</button>';
  box.classList.remove('hidden');
  box.querySelectorAll('.chg-i.link').forEach(el => el.addEventListener('click', () => {
    const g = items[+el.dataset.k][2];
    if (g.tab) showTab(g.tab, true);
    else if (g.t) highlightTime(g.t, '');
    else if (g.day) gotoDay(g.day);
    else if (g.a) gotoRange(g.a, g.b, 3 * H);
  }));
  box.querySelectorAll('tr[data-day]').forEach(tr => tr.addEventListener('click', () => gotoDay(tr.dataset.day)));
  wireClose(box);
}
function wireClose(box) { box.querySelector('.chg-x').addEventListener('click', () => { CHG_CLOSED = true; box.classList.add('hidden'); }); }
