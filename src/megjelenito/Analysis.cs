using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Idojaras
{
    // Elemzések: esőesemények, front-/zivatardetektor, rekordok, szél-statisztika, óránkénti hőtérkép, adatminőség,
    // változórács a saját képletekhez, időjárás egy időpontban, egyszerű JSON-tárak (kerti napló, futónapló), riasztás-beállítások
    static class Analysis
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        static double V(Dataset ds, int col, int i, bool clean, int mask)
        {
            if (clean && (ds.Flags[i] & mask) != 0) return double.NaN;
            return ds.C[col][i];
        }
        static double Pres(Dataset ds, int i, bool clean)
        {
            if (clean && (ds.Flags[i] & Flag.Pres) != 0) return double.NaN;
            return (ds.C[Col.PMAX][i] + ds.C[Col.PMIN][i]) / 2.0;
        }
        static long Ms(long t) { return t * 1000; }

        // csapadék-növekmény az előző mintához képest (a napi halmozott értékből, éjféli és nap közbeni nullázással)
        static double RainInc(Dataset ds, int i, bool clean)
        {
            double tot = V(ds, Col.PTOT, i, clean, Flag.Rain);
            if (double.IsNaN(tot) || i == 0) return 0;
            double prev = V(ds, Col.PTOT, i - 1, clean, Flag.Rain);
            if (ds.Day[i] != ds.Day[i - 1]) return ds.T[i] - ds.T[i - 1] <= 1800 ? Math.Max(0, tot) : 0;
            if (double.IsNaN(prev)) return 0;
            return tot >= prev ? tot - prev : tot;
        }

        // ------------------------------------------------------------------ esőesemények
        public class RainEvent { public long Start, End, MaxT; public double Total, MaxRate, AvgRate; public bool Suspect; }

        public static List<RainEvent> RainEvents(Dataset ds, bool clean, int gapMin)
        {
            var res = new List<RainEvent>();
            RainEvent cur = null;
            long lastWet = 0;
            for (int i = 1; i < ds.N; i++)
            {
                double inc = RainInc(ds, i, clean);
                if (inc > 0.001)
                {
                    if (cur == null || ds.T[i] - lastWet > gapMin * 60L)
                    {
                        if (cur != null) res.Add(cur);
                        cur = new RainEvent { Start = Math.Max(ds.T[i - 1], ds.T[i] - 900) };
                    }
                    cur.Total += inc;
                    cur.End = ds.T[i];
                    lastWet = ds.T[i];
                    if ((ds.Flags[i] & Flag.RAINJUMP) != 0) cur.Suspect = true;
                    // a mérési lépés átlagos intenzitása is (az állomás precipRate-je néha kisebb, mint ami lehullott – 6.2)
                    long dt = ds.T[i] - ds.T[i - 1];
                    if (dt >= 120 && dt <= 900 && (!clean || (ds.Flags[i] & Flag.RainRate) == 0))
                    {
                        double stepRate = inc / (dt / 3600.0);
                        if (stepRate > cur.MaxRate) { cur.MaxRate = stepRate; cur.MaxT = ds.T[i]; }
                    }
                }
                if (cur != null && ds.T[i] >= cur.Start && ds.T[i] <= cur.End + 300)
                {
                    double rate = V(ds, Col.PRATE, i, clean, Flag.RainRate);
                    if (!double.IsNaN(rate) && rate > cur.MaxRate) { cur.MaxRate = rate; cur.MaxT = ds.T[i]; }
                    if ((ds.Flags[i] & Flag.RAINJUMP) != 0) cur.Suspect = true;
                }
            }
            if (cur != null) res.Add(cur);
            res.RemoveAll(e => e.Total < 0.2);
            foreach (var e in res) e.AvgRate = e.Total / Math.Max(5.0 / 60, (e.End - e.Start) / 3600.0);
            return res;
        }

        public static string RainEventsJson(Dataset ds, bool clean, Settings s)
        {
            var ev = RainEvents(ds, clean, s.RainGapMin);
            var w = new JW().Obj().P("gapMin", s.RainGapMin).P("cloudburst", s.CloudburstRate).Key("events").Arr();
            foreach (var e in ev)
                w.Obj().Ms("start", e.Start).Ms("end", e.End).P("total", e.Total, 2).P("maxRate", e.MaxRate, 1).Ms("maxT", e.MaxT).P("avgRate", e.AvgRate, 1).P("suspect", e.Suspect).End();
            return w.EndArr().End().ToString();
        }

        // ------------------------------------------------------------------ front- és zivatardetektor
        public static string EventsJson(Dataset ds, Settings s)
        {
            var list = new List<Dictionary<string, object>>();
            int n = ds.N;
            // 60 perces hőmérséklet-esés: a megelőző 60 perc maximuma mínusz a mostani érték (csúszó maximum)
            var dq = new LinkedList<int>();
            long skipUntil = 0;
            for (int i = 0; i < n; i++)
            {
                double t = V(ds, Col.TA, i, true, Flag.Temp);
                while (dq.Count > 0 && ds.T[dq.First.Value] < ds.T[i] - 3600) dq.RemoveFirst();
                double drop = 0;
                if (dq.Count > 0 && !double.IsNaN(t)) drop = ds.C[Col.TA][dq.First.Value] - t;
                if (!double.IsNaN(t))
                {
                    while (dq.Count > 0 && ds.C[Col.TA][dq.Last.Value] <= t) dq.RemoveLast();
                    dq.AddLast(i);
                }
                if (ds.T[i] < skipUntil || drop < Math.Min(s.FrontTempDrop, s.StormTempDrop)) continue;

                // környezet: 90 perccel előtte – 60 perccel utána
                int a = ds.LowerBound(ds.T[i] - 5400), b = ds.LowerBound(ds.T[i] + 3600);
                double gust = 0, rate = 0, rain = 0;
                double vx1 = 0, vy1 = 0, vx2 = 0, vy2 = 0;
                double pMin = double.NaN, pAfter = double.NaN;
                for (int j = a; j < Math.Min(n, b); j++)
                {
                    double g = V(ds, Col.WGH, j, true, Flag.Wind); if (g > gust) gust = g;
                    double r = V(ds, Col.PRATE, j, true, Flag.RainRate); if (r > rate) rate = r;
                    rain += RainInc(ds, j, true);
                    double ws = V(ds, Col.WSA, j, true, Flag.Wind), dir = ds.C[Col.WDIR][j];
                    if (!double.IsNaN(ws) && !double.IsNaN(dir))
                    {
                        double x = ws * Math.Sin(dir * Math.PI / 180), y = ws * Math.Cos(dir * Math.PI / 180);
                        if (ds.T[j] < ds.T[i]) { vx1 += x; vy1 += y; } else { vx2 += x; vy2 += y; }
                    }
                    double p = Pres(ds, j, true);
                    if (!double.IsNaN(p)) { if (double.IsNaN(pMin) || p < pMin) pMin = p; if (ds.T[j] > ds.T[i]) pAfter = p; }
                }
                double turn = 0;
                if ((vx1 != 0 || vy1 != 0) && (vx2 != 0 || vy2 != 0))
                {
                    double d1 = Math.Atan2(vx1, vy1) * 180 / Math.PI, d2 = Math.Atan2(vx2, vy2) * 180 / Math.PI;
                    turn = Math.Abs(((d2 - d1) % 360 + 540) % 360 - 180);
                }
                // gyenge szélnél az irány véletlenszerű, ezért a fordulás csak érdemi szélnél számít;
                // az esti, szélcsendes kisugárzásos lehűlés így nem minősül frontnak
                double meanWind = Math.Sqrt(Math.Max(vx1 * vx1 + vy1 * vy1, vx2 * vx2 + vy2 * vy2)) / Math.Max(1, (b - a) / 2.0);
                bool realTurn = turn >= s.FrontTurn && meanWind >= 8;
                bool support = gust >= s.FrontGust || rain >= 0.2 || (!double.IsNaN(pMin) && !double.IsNaN(pAfter) && pAfter - pMin >= 0.5);
                string type = null;
                if (rate >= s.StormRate && drop >= s.StormTempDrop && (gust >= s.StormGust || rain >= 5)) type = "zivatar";
                else if (drop >= s.FrontTempDrop && (gust >= s.FrontGust || realTurn) && support) type = "front";
                if (type == null) continue;
                var parts = new List<string> { "−" + drop.ToString("0.0", CultureInfo.GetCultureInfo("hu-HU")) + " °C 1 óra alatt" };
                if (gust > 0) parts.Add("lökés " + gust.ToString("0") + " km/h");
                if (turn >= 30) parts.Add("szélfordulás " + turn.ToString("0") + "°");
                if (rain >= 0.2) parts.Add(rain.ToString("0.0", CultureInfo.GetCultureInfo("hu-HU")) + " mm eső");
                if (!double.IsNaN(pMin) && !double.IsNaN(pAfter) && pAfter - pMin >= 1) parts.Add("utána nyomásemelkedés +" + (pAfter - pMin).ToString("0.0", CultureInfo.GetCultureInfo("hu-HU")) + " hPa");
                list.Add(new Dictionary<string, object>
                {
                    { "t", Ms(ds.T[i]) }, { "type", type },
                    { "title", type == "zivatar" ? "Zivatar" : "Frontátvonulás" },
                    { "detail", string.Join(", ", parts) }
                });
                skipUntil = ds.T[i] + 3 * 3600;
            }
            var w = new JW().Obj();
            w.Key("settings").Obj().P("frontTempDrop", s.FrontTempDrop).P("frontGust", s.FrontGust).P("frontTurn", s.FrontTurn)
                .P("stormRate", s.StormRate).P("stormGust", s.StormGust).P("stormTempDrop", s.StormTempDrop).End();
            w.Key("events").Raw(Js.Serialize(list));
            return w.End().ToString();
        }

        // ------------------------------------------------------------------ rekordok
        static void Rec(JW w, string key, string title, double v, string unit, int dec, long t, string detail)
        {
            if (double.IsNaN(v)) return;
            // napi értékű rekordnál (legcsapadékosabb nap, sorozatok) az időpont nem értelmes: csak dátum (6.5)
            bool dateOnly = key == "rainDay" || key.EndsWith("Streak");
            w.Obj().P("key", key).P("title", title).P("value", v, dec).P("unit", unit).P("dec", dec).Ms("t", t).P("dateOnly", dateOnly).P("detail", detail ?? "").End();
        }

        // ------------------------------------------------------------------ időszak-szűrő
        // ranges = "2025-06-01_2025-08-31,2026-06-01_" (helyi napok, mindkét vég benne van; üres vég = nyitott)
        public static Func<int, bool> DayFilter(string ranges, int year)
        {
            if (!string.IsNullOrEmpty(ranges))
            {
                var list = new List<int[]>();
                foreach (var part in ranges.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var ab = part.Split('_');
                    DateTime a, b;
                    int da = ab.Length > 0 && DateTime.TryParseExact(ab[0], "yyyy-MM-dd", Inv, DateTimeStyles.None, out a) ? Tz.DayNumber(a) : int.MinValue;
                    int db = ab.Length > 1 && DateTime.TryParseExact(ab[1], "yyyy-MM-dd", Inv, DateTimeStyles.None, out b) ? Tz.DayNumber(b) : int.MaxValue;
                    if (da > db) { int x = da; da = db; db = x; }
                    list.Add(new[] { da, db });
                }
                if (list.Count > 0) { var arr = list.ToArray(); return d => { foreach (var r in arr) if (d >= r[0] && d <= r[1]) return true; return false; }; }
            }
            if (year > 0) { int a0 = Tz.DayNumber(new DateTime(year, 1, 1)), a1 = Tz.DayNumber(new DateTime(year, 12, 31)); return d => d >= a0 && d <= a1; }
            return d => true;
        }

        public static string RecordsJson(Dataset ds, Settings s, bool clean, int year, string ranges = null)
        {
            var inDay = DayFilter(ranges, year);
            var days = (clean ? ds.Days : ds.DaysRaw).Where(d => inDay(d.Day)).ToList();
            var good = days.Where(d => d.Coverage >= 0.8).ToList();
            int i0 = 0, i1 = ds.N;
            var w = new JW().Obj().P("year", year).P("clean", clean).Key("records").Arr();
            Func<int, int, int, Func<double, double, bool>, int> best = (col, mask, dummy, better) =>
            {
                int bi = -1;
                for (int i = i0; i < i1; i++) { if (!inDay(ds.Day[i])) continue; double v = V(ds, col, i, clean, mask); if (!double.IsNaN(v) && (bi < 0 || better(v, ds.C[col][bi]))) bi = i; }
                return bi;
            };
            int k;
            k = best(Col.TH, Flag.Temp, 0, (a, b) => a > b); if (k >= 0) Rec(w, "tmax", "Legmagasabb hőmérséklet", ds.C[Col.TH][k], "°C", 1, ds.T[k], null);
            k = best(Col.TL, Flag.Temp, 0, (a, b) => a < b); if (k >= 0) Rec(w, "tmin", "Legalacsonyabb hőmérséklet", ds.C[Col.TL][k], "°C", 1, ds.T[k], null);
            var wn = good.Where(d => !double.IsNaN(d.Tmin)).OrderByDescending(d => d.Tmin).FirstOrDefault();
            if (wn != null) Rec(w, "warmNight", "Legmelegebb éjszaka (napi minimum)", wn.Tmin, "°C", 1, wn.TminT, null);
            var cd = good.Where(d => !double.IsNaN(d.Tmax)).OrderBy(d => d.Tmax).FirstOrDefault();
            if (cd != null) Rec(w, "coldDay", "Leghidegebb nappal (napi maximum)", cd.Tmax, "°C", 1, cd.TmaxT, null);
            var rg = good.Where(d => !double.IsNaN(d.Tmax) && !double.IsNaN(d.Tmin)).OrderByDescending(d => d.Tmax - d.Tmin).FirstOrDefault();
            if (rg != null) Rec(w, "range", "Legnagyobb napi hőingás", rg.Tmax - rg.Tmin, "°C", 1, rg.TmaxT, rg.Tmin.ToString("0.0", Inv).Replace('.', ',') + " → " + rg.Tmax.ToString("0.0", Inv).Replace('.', ',') + " °C");

            // leggyorsabb hőmérséklet-zuhanás 60 percen belül (csúszó maximum)
            {
                var dq = new LinkedList<int>(); double bestDrop = 0; long bt = 0;
                for (int i = i0; i < i1; i++)
                {
                    if (!inDay(ds.Day[i])) { dq.Clear(); continue; }
                    double t = V(ds, Col.TA, i, clean, Flag.Temp);
                    while (dq.Count > 0 && ds.T[dq.First.Value] < ds.T[i] - 3600) dq.RemoveFirst();
                    if (!double.IsNaN(t))
                    {
                        if (dq.Count > 0) { double dr = ds.C[Col.TA][dq.First.Value] - t; if (dr > bestDrop) { bestDrop = dr; bt = ds.T[i]; } }
                        while (dq.Count > 0 && ds.C[Col.TA][dq.Last.Value] <= t) dq.RemoveLast();
                        dq.AddLast(i);
                    }
                }
                if (bt > 0) Rec(w, "tdrop", "Leggyorsabb lehűlés 1 órán belül", bestDrop, "°C", 1, bt, null);
            }
            // legnagyobb nyomásesés 3 és 24 óra alatt
            foreach (var hours in new[] { 3, 24 })
            {
                double bd = 0; long bt = 0;
                for (int i = i0; i < i1; i++)
                {
                    if (!inDay(ds.Day[i])) continue;
                    double p = Pres(ds, i, clean);
                    if (double.IsNaN(p)) continue;
                    int j = ds.LowerBound(ds.T[i] - hours * 3600);
                    if (j >= ds.N || Math.Abs(ds.T[j] - (ds.T[i] - hours * 3600)) > 1200 || !inDay(ds.Day[j])) continue;
                    double pj = Pres(ds, j, clean);
                    if (!double.IsNaN(pj) && pj - p > bd) { bd = pj - p; bt = ds.T[i]; }
                }
                if (bt > 0) Rec(w, "pdrop" + hours, "Legnagyobb nyomásesés " + hours + " óra alatt", bd, "hPa", 1, bt, null);
            }
            k = best(Col.WGH, Flag.Wind, 0, (a, b) => a > b); if (k >= 0) Rec(w, "gust", "Legnagyobb széllökés", ds.C[Col.WGH][k], "km/h", 1, ds.T[k], (ds.C[Col.WGH][k] / 1.852).ToString("0") + " csomó");
            var wet = days.Where(d => !double.IsNaN(d.Rain)).OrderByDescending(d => d.Rain).FirstOrDefault();
            if (wet != null) Rec(w, "rainDay", "Legcsapadékosabb nap", wet.Rain, "mm", 1, (wet.Start + wet.End) / 2, null);
            k = best(Col.PRATE, Flag.RainRate, 0, (a, b) => a > b); if (k >= 0) Rec(w, "rate", "Legnagyobb csapadékintenzitás", ds.C[Col.PRATE][k], "mm/h", 0, ds.T[k], null);
            k = best(Col.SOL, Flag.Solar, 0, (a, b) => a > b); if (k >= 0) Rec(w, "solar", "Legerősebb napsugárzás", ds.C[Col.SOL][k], "W/m²", 0, ds.T[k], null);
            k = best(Col.UV, Flag.Uv, 0, (a, b) => a > b); if (k >= 0) Rec(w, "uv", "Legmagasabb UV-index", ds.C[Col.UV][k], "", 0, ds.T[k], null);
            k = best(Col.DH, Flag.Dew, 0, (a, b) => a > b); if (k >= 0) Rec(w, "dew", "Legmagasabb harmatpont (legfülledtebb)", ds.C[Col.DH][k], "°C", 1, ds.T[k], null);
            k = best(Col.HL, Flag.Hum, 0, (a, b) => a < b); if (k >= 0) Rec(w, "humMin", "Legszárazabb levegő", ds.C[Col.HL][k], "%", 0, ds.T[k], null);
            k = best(Col.PMAX, Flag.Pres, 0, (a, b) => a > b); if (k >= 0) Rec(w, "pmax", "Legmagasabb légnyomás", ds.C[Col.PMAX][k], "hPa", 1, ds.T[k], null);
            k = best(Col.PMIN, Flag.Pres, 0, (a, b) => a < b); if (k >= 0) Rec(w, "pmin", "Legalacsonyabb légnyomás", ds.C[Col.PMIN][k], "hPa", 1, ds.T[k], null);

            // sorozatok
            Func<Func<DayStat, bool>, Tuple<int, DayStat, DayStat>> streak = cond =>
            {
                int bestLen = 0, len = 0; DayStat bs = null, be = null, cs = null; DayStat prev = null;
                foreach (var d in days.Where(x => x.Coverage >= 0.5))
                {
                    bool c = cond(d) && (prev == null || d.Day == prev.Day + 1 || len == 0);
                    if (cond(d)) { if (prev != null && d.Day != prev.Day + 1) len = 0; if (len == 0) cs = d; len++; if (len > bestLen) { bestLen = len; bs = cs; be = d; } }
                    else len = 0;
                    prev = d;
                }
                return Tuple.Create(bestLen, bs, be);
            };
            var dry = streak(d => !(d.Rain >= 0.2));
            if (dry.Item1 > 0) Rec(w, "dryStreak", "Leghosszabb száraz időszak (< 0,2 mm/nap)", dry.Item1, "nap", 0, (dry.Item3.Start + dry.Item3.End) / 2, dry.Item2.Date.ToString("yyyy.MM.dd") + " – " + dry.Item3.Date.ToString("yyyy.MM.dd"));
            var rainy = streak(d => d.Rain >= 1);
            if (rainy.Item1 > 0) Rec(w, "wetStreak", "Leghosszabb esős sorozat (≥ 1 mm/nap)", rainy.Item1, "nap", 0, (rainy.Item3.Start + rainy.Item3.End) / 2, rainy.Item2.Date.ToString("yyyy.MM.dd") + " – " + rainy.Item3.Date.ToString("yyyy.MM.dd"));
            var hot = streak(d => d.Tmax >= s.HotDay);
            if (hot.Item1 > 0) Rec(w, "hotStreak", "Leghosszabb hőhullám (egymást követő hőségnapok)", hot.Item1, "nap", 0, (hot.Item3.Start + hot.Item3.End) / 2, hot.Item2.Date.ToString("yyyy.MM.dd") + " – " + hot.Item3.Date.ToString("yyyy.MM.dd"));
            w.EndArr();

            // számlálók, fagy
            w.Key("counts").Obj()
                .P("days", good.Count)
                .P("hotDays", good.Count(d => d.Tmax >= s.HotDay)).P("tropicalNights", good.Count(d => d.Tmin >= s.TropicalNight))
                .P("frostDays", good.Count(d => d.Tmin < s.FrostDay)).P("iceDays", good.Count(d => d.Tmax < s.IceDay))
                .P("rainDays", days.Count(d => d.Rain >= 1)).P("rainTotal", days.Where(d => !double.IsNaN(d.Rain)).Sum(d => d.Rain), 1)
                .P("hotDay", s.HotDay).P("tropicalNight", s.TropicalNight).P("frostDay", s.FrostDay).P("iceDay", s.IceDay)
                .End();
            var frost = good.Where(d => d.Tmin < 0).ToList();
            // több évnél a legutóbbi év tavasza és ősze (10.3 – különben a két dátum más-más évből jönne)
            int fy = good.Count > 0 ? good.Max(d => d.Date.Year) : 0;
            var spring = frost.Where(d => d.Date.Year == fy && d.Date.Month < 7).OrderBy(d => d.Date).LastOrDefault();
            var autumn = frost.Where(d => d.Date.Year == fy && d.Date.Month >= 7).OrderBy(d => d.Date).FirstOrDefault();
            w.Key("frost").Obj();
            if (spring != null) w.P("last", spring.Date.ToString("yyyy-MM-dd", Inv)).P("lastT", spring.Tmin, 1);
            if (autumn != null) w.P("first", autumn.Date.ToString("yyyy-MM-dd", Inv)).P("firstT", autumn.Tmin, 1);
            w.End();
            w.Key("years").Arr(); foreach (var y in ds.Days.Select(d => d.Date.Year).Distinct()) w.Long(y); w.EndArr();
            return w.End().ToString();
        }

        // ------------------------------------------------------------------ szél-statisztika
        public static readonly double[] KnBins = { 1, 4, 7, 11, 17, 22 };   // csomó: szélcsend < 1; 1–3, 4–6, 7–10, 11–16, 17–21, 22+

        public static string WindJson(Dataset ds, Settings s, long fromMs, long toMs)
        {
            int i0 = ds.LowerBound(fromMs / 1000), i1 = ds.LowerBound(toMs / 1000 + 1);
            var hm = new double[12, 24]; var hmN = new int[12, 24];
            var rose = new int[16, 6]; int calm = 0, total = 0;
            var aft = new Dictionary<int, double[]>();
            for (int i = i0; i < i1; i++)
            {
                double ws = V(ds, Col.WSA, i, true, Flag.Wind);
                if (double.IsNaN(ws)) continue;
                var loc = Tz.Local(ds.T[i]);
                hm[loc.Month - 1, loc.Hour] += ws; hmN[loc.Month - 1, loc.Hour]++;
                double kn = ws / 1.852;
                total++;
                double dir = ds.C[Col.WDIR][i];
                if (kn < KnBins[0] || double.IsNaN(dir)) calm++;
                else
                {
                    int sector = (int)Math.Round(((dir % 360) + 360) % 360 / 22.5) % 16;
                    int bin = 0; for (int b = 1; b < KnBins.Length; b++) if (kn >= KnBins[b]) bin = b;
                    rose[sector, bin]++;
                }
                if (loc.Hour >= s.AfternoonFrom && loc.Hour < s.AfternoonTo)
                {
                    double[] a; if (!aft.TryGetValue(ds.Day[i], out a)) aft[ds.Day[i]] = a = new double[2];
                    a[0] += ws; a[1]++;
                }
            }
            var windyDays = aft.Where(kv => kv.Value[1] >= 12 && kv.Value[0] / kv.Value[1] >= s.WindyKmh).Select(kv => kv.Key).OrderBy(x => x).ToList();
            int bestStreak = 0, cur = 0, prev = int.MinValue; int bsEnd = 0;
            foreach (var d in windyDays) { cur = d == prev + 1 ? cur + 1 : 1; if (cur > bestStreak) { bestStreak = cur; bsEnd = d; } prev = d; }
            var w = new JW().Obj().P("from", fromMs).P("to", toMs).P("samples", total).P("calmPct", total > 0 ? 100.0 * calm / total : 0, 1);
            w.Key("hourMonth").Arr();
            for (int m = 0; m < 12; m++) { w.Arr(); for (int h = 0; h < 24; h++) w.Num(hmN[m, h] > 0 ? hm[m, h] / hmN[m, h] : double.NaN, 2); w.EndArr(); }
            w.EndArr();
            w.Key("rose").Arr();
            for (int sct = 0; sct < 16; sct++) { w.Arr(); for (int b = 0; b < 6; b++) w.Num(total > 0 ? 100.0 * rose[sct, b] / total : 0, 2); w.EndArr(); }
            w.EndArr();
            w.Key("knBins").Arr(); foreach (var b in KnBins) w.Num(b, 0); w.EndArr();
            w.P("windyKmh", s.WindyKmh).P("afternoonFrom", s.AfternoonFrom).P("afternoonTo", s.AfternoonTo);
            w.P("windyDays", windyDays.Count).P("afternoonDays", aft.Count(kv => kv.Value[1] >= 12)).P("windyStreak", bestStreak);
            if (bestStreak > 0) w.P("windyStreakEnd", Tz.DateOf(bsEnd).ToString("yyyy-MM-dd", Inv));
            w.Key("windyList").Arr(); foreach (var d in windyDays) w.Str(Tz.DateOf(d).ToString("yyyy-MM-dd", Inv)); w.EndArr();
            return w.End().ToString();
        }

        // ------------------------------------------------------------------ óránkénti hőtérkép és havi napi menet
        public static string HourlyJson(Dataset ds, string metric, string ranges = null)
        {
            var inDay = DayFilter(ranges, 0);
            Func<int, double> get;
            switch (metric)
            {
                case "hum": get = i => V(ds, Col.HA, i, true, Flag.Hum); break;
                case "wind": get = i => V(ds, Col.WSA, i, true, Flag.Wind); break;
                case "sun": get = i => V(ds, Col.SOL, i, true, Flag.Solar); break;
                case "pres": get = i => Pres(ds, i, true); break;
                case "dew": get = i => V(ds, Col.DA, i, true, Flag.Dew); break;
                case "vpd": get = i => { double t = V(ds, Col.TA, i, true, Flag.Temp), h = V(ds, Col.HA, i, true, Flag.Hum); return double.IsNaN(t) || double.IsNaN(h) ? double.NaN : Drought.Es(t) * (1 - h / 100); }; break;
                default: get = i => V(ds, Col.TA, i, true, Flag.Temp); break;
            }
            var dayIdx = new Dictionary<int, int>(); var days = new List<int>();
            var sums = new List<double[]>(); var cnts = new List<int[]>();
            var cyc = new double[12, 24]; var cycN = new int[12, 24];
            for (int i = 0; i < ds.N; i++)
            {
                if (!inDay(ds.Day[i])) continue;
                double v = get(i);
                if (double.IsNaN(v)) continue;
                int d = ds.Day[i], h = ds.Hour[i];
                int k;
                if (!dayIdx.TryGetValue(d, out k)) { k = days.Count; dayIdx[d] = k; days.Add(d); sums.Add(new double[24]); cnts.Add(new int[24]); }
                sums[k][h] += v; cnts[k][h]++;
                int m = Tz.DateOf(d).Month - 1;
                cyc[m, h] += v; cycN[m, h]++;
            }
            var w = new JW().Obj().P("metric", metric);
            w.Key("days").Arr(); foreach (var d in days) w.Str(Tz.DateOf(d).ToString("yyyy-MM-dd", Inv)); w.EndArr();
            w.Key("values").Arr();
            for (int k = 0; k < days.Count; k++) { w.Arr(); for (int h = 0; h < 24; h++) w.Num(cnts[k][h] > 0 ? sums[k][h] / cnts[k][h] : double.NaN, 1); w.EndArr(); }
            w.EndArr();
            w.Key("cycle").Arr();
            for (int m = 0; m < 12; m++) { w.Arr(); for (int h = 0; h < 24; h++) w.Num(cycN[m, h] > 0 ? cyc[m, h] / cycN[m, h] : double.NaN, 2); w.EndArr(); }
            w.EndArr();
            return w.End().ToString();
        }

        // ------------------------------------------------------------------ adatminőség
        public static string QualityJson(Dataset ds, string ranges = null)
        {
            var inDay = DayFilter(ranges, 0);
            bool all = string.IsNullOrEmpty(ranges);
            int n = 0, flagged = 0; var counts = new int[Flag.Bits.Length];
            for (int i = 0; i < ds.N; i++)
            {
                if (!inDay(ds.Day[i])) continue;
                n++; if (ds.Flags[i] != 0) flagged++;
                for (int b = 0; b < Flag.Bits.Length; b++) if ((ds.Flags[i] & Flag.Bits[b]) != 0) counts[b]++;
            }
            var w = new JW().Obj().P("n", n).P("flagged", all ? ds.FlaggedRows : flagged);
            w.Key("gaps").Arr();
            foreach (var g in ds.Gaps)
            {
                if (!all && !inDay(Tz.DayNumber(Tz.Local(g.From))) && !inDay(Tz.DayNumber(Tz.Local(g.To)))) continue;
                w.Obj().Ms("from", g.From).Ms("to", g.To).P("minutes", (g.To - g.From) / 60).End();
            }
            w.EndArr();
            w.Key("flags").Arr();
            for (int b = 0; b < Flag.Bits.Length; b++)
            {
                int bit = Flag.Bits[b];
                w.Obj().P("code", Flag.Codes[b]).P("text", Flag.Texts[b]).P("count", all ? ds.FlagCounts[b] : counts[b]).Key("samples").Arr();
                int shown = 0;
                for (int i = 0; i < ds.N && shown < 150; i++)
                {
                    if ((ds.Flags[i] & bit) == 0 || !inDay(ds.Day[i])) continue;
                    string val = bit == Flag.UV ? "UV " + ds.C[Col.UV][i] : bit == Flag.SOLAR ? ds.C[Col.SOL][i] + " W/m²" : bit == Flag.PRES ? ds.C[Col.PMIN][i] + " hPa"
                        : bit == Flag.RAIN ? ds.C[Col.PRATE][i] + " mm/h" : bit == Flag.WIND ? ds.C[Col.WGH][i] + " km/h" : bit == Flag.HUM ? ds.C[Col.HA][i] + " %"
                        : ds.C[Col.TH][i].ToString("0.0", Inv) + " °C";
                    w.Obj().Ms("t", ds.T[i]).P("value", val.Replace('.', ',')).End();
                    shown++;
                }
                w.EndArr().End();
            }
            w.EndArr();
            return w.End().ToString();
        }

        // ------------------------------------------------------------------ változórács a saját képletekhez
        static readonly string[] VarNames = { "hom", "hom_max", "hom_min", "harmat", "para", "legnyomas", "szel", "lokes", "szelirany", "eso", "eso_osszeg", "napsutes", "uv", "vpd" };

        public static string VarsJson(Dataset ds, NameValueCollection q)
        {
            string res = q["res"] ?? "hour";
            long fromMs, toMs;
            long.TryParse(q["from"] ?? "", out fromMs); long.TryParse(q["to"] ?? "", out toMs);
            if (fromMs == 0) fromMs = ds.T[0] * 1000; if (toMs == 0) toMs = ds.T[ds.N - 1] * 1000;
            if (res == "5perc" && toMs - fromMs > 31L * 86400000) fromMs = toMs - 31L * 86400000;
            // a képlet időeltolásaihoz előre is ráhagyunk (legfeljebb 30 nap)
            long pad = 30L * 86400;
            int i0 = ds.LowerBound(fromMs / 1000 - (res == "5perc" ? 3 * 86400 : pad)), i1 = ds.LowerBound(toMs / 1000 + 1);
            var keys = new List<long>(); var idx = new Dictionary<long, int>();
            int nv = VarNames.Length;
            var acc = new List<double[]>(); var cnt = new List<int[]>();
            for (int i = i0; i < i1; i++)
            {
                long key;
                if (res == "day") key = Tz.LocalMidnightEpoch(Tz.Local(ds.T[i]).Date);
                else if (res == "hour") key = ds.T[i] / 3600 * 3600;
                else key = ds.T[i];
                int k;
                if (!idx.TryGetValue(key, out k)) { k = keys.Count; idx[key] = k; keys.Add(key); acc.Add(Enumerable.Repeat(double.NaN, nv).ToArray()); cnt.Add(new int[nv]); }
                double t = V(ds, Col.TA, i, true, Flag.Temp), h = V(ds, Col.HA, i, true, Flag.Hum);
                var vals = new[]
                {
                    t, V(ds, Col.TH, i, true, Flag.Temp), V(ds, Col.TL, i, true, Flag.Temp), V(ds, Col.DA, i, true, Flag.Dew), h, Pres(ds, i, true),
                    V(ds, Col.WSA, i, true, Flag.Wind), V(ds, Col.WGH, i, true, Flag.Wind), ds.C[Col.WDIR][i], V(ds, Col.PRATE, i, true, Flag.RainRate),
                    RainInc(ds, i, true), V(ds, Col.SOL, i, true, Flag.Solar), V(ds, Col.UV, i, true, Flag.Uv),
                    double.IsNaN(t) || double.IsNaN(h) ? double.NaN : Drought.Es(t) * (1 - h / 100)
                };
                var a = acc[k]; var c = cnt[k];
                for (int j = 0; j < nv; j++)
                {
                    double v = vals[j];
                    if (double.IsNaN(v)) continue;
                    // aggregálás: max/min/összeg, ahol annak van értelme; különben átlag
                    if (j == 1 || j == 7 || j == 9 || j == 12) a[j] = double.IsNaN(a[j]) ? v : Math.Max(a[j], v);
                    else if (j == 2) a[j] = double.IsNaN(a[j]) ? v : Math.Min(a[j], v);
                    else if (j == 10) a[j] = (double.IsNaN(a[j]) ? 0 : a[j]) + v;
                    else { a[j] = (double.IsNaN(a[j]) ? 0 : a[j]) + v; c[j]++; }
                }
            }
            var w = new JW().Obj().P("res", res).P("from", fromMs).P("to", toMs);
            w.Key("t").Arr(); foreach (var k in keys) w.Long(k * 1000); w.EndArr();
            for (int j = 0; j < nv; j++)
            {
                w.Key(VarNames[j]).Arr();
                for (int k = 0; k < keys.Count; k++)
                {
                    double v = acc[k][j];
                    if (cnt[k][j] > 0) v /= cnt[k][j];
                    w.Num(v, 3);
                }
                w.EndArr();
            }
            // napi felbontásnál a szárazság-modul értékei is
            if (res == "day" || res == "hour")
            {
                var r = Drought.Compute(ds, DroughtStore.Get());
                var di = new Dictionary<string, int>(); for (int i = 0; i < r.Dates.Count; i++) di[r.Dates[i]] = i;
                Func<long, int> dayOf = k => { int x; return di.TryGetValue(Tz.Local(k).ToString("yyyy-MM-dd", Inv), out x) ? x : -1; };
                w.Key("et0").Arr(); foreach (var k in keys) { int x = dayOf(k); w.Num(x >= 0 ? r.Et0[x] : double.NaN, 2); } w.EndArr();
                w.Key("fwi").Arr(); foreach (var k in keys) { int x = dayOf(k); w.Num(x >= 0 ? r.Fwi[x] : double.NaN, 1); } w.EndArr();
                foreach (var p in r.Profiles)
                {
                    w.Key("szaraz_" + p.P.id).Arr(); foreach (var k in keys) { int x = dayOf(k); w.Num(x >= 0 ? p.Depl[x] * 100 : double.NaN, 1); } w.EndArr();
                }
            }
            return w.End().ToString();
        }

        // ------------------------------------------------------------------ időjárás egy időpontban (futónapló)
        public static string AtJson(Dataset ds, long ms)
        {
            long t = ms / 1000;
            int k = ds.LowerBound(t), best = -1; long bd = long.MaxValue;
            for (int j = Math.Max(0, k - 1); j <= Math.Min(ds.N - 1, k); j++) { long d = Math.Abs(ds.T[j] - t); if (d < bd) { bd = d; best = j; } }
            if (best < 0 || bd > 2700) return "{\"found\":false}";
            int i = best;
            double temp = ds.C[Col.TA][i], hi = ds.C[Col.HIA][i], wc = ds.C[Col.WCA][i];
            double feels = temp >= 27 && !float.IsNaN(ds.C[Col.HIA][i]) ? hi : temp <= 10 && !float.IsNaN(ds.C[Col.WCA][i]) ? wc : temp;
            return new JW().Obj().P("found", true).Ms("t", ds.T[i]).P("temp", temp, 1).P("feels", feels, 1).P("wind", ds.C[Col.WSA][i], 1).P("gust", ds.C[Col.WGH][i], 1)
                .P("hum", ds.C[Col.HA][i], 0).P("dew", ds.C[Col.DA][i], 1).P("solar", ds.C[Col.SOL][i], 0).P("rainRate", ds.C[Col.PRATE][i], 1).End().ToString();
        }
    }

    // Egyszerű JSON-tárak az appdata_megjelenítő mappában (kerti napló, futónapló)
    static class JsonStore
    {
        static readonly object Lock = new object();
        static readonly string[] Allowed = { "naplo", "futas" };

        public static string Handle(string name, string method, string body, out int status)
        {
            status = 200;
            if (Array.IndexOf(Allowed, name) < 0) { status = 404; return "{}"; }
            string path = Path.Combine(AppState.L.App, name + ".json");
            lock (Lock)
            {
                if (method == "POST")
                {
                    try { new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(body ?? ""); }
                    catch { status = 400; return "{\"error\":\"érvénytelen JSON\"}"; }
                    Guard.CreateDirectory(AppState.L.App);
                    string tmp = path + ".tmp";
                    Guard.WriteAllText(tmp, body, new UTF8Encoding(false));
                    if (File.Exists(path)) Guard.Replace(tmp, path, null); else Guard.Move(tmp, path);
                    return "{\"ok\":true}";
                }
                try { if (File.Exists(path)) return File.ReadAllText(path, Encoding.UTF8); }
                catch { }
                return "[]";
            }
        }
    }

    // A Letöltő riasztás-beállításai (appdata_letöltő\beallitasok.ini) a Megjelenítőből szerkesztve
    static class AlertSettings
    {
        public static readonly string[][] Keys =
        {
            new[] { "RiasztasFagy", "true" }, new[] { "FagyHatar", "1" },
            new[] { "RiasztasNyomas", "true" }, new[] { "NyomasEses3h", "4" },
            new[] { "RiasztasSzel", "true" }, new[] { "LokesHatar", "60" },
            new[] { "RiasztasEso", "true" }, new[] { "EsoIntenzitas", "30" },
            new[] { "RiasztasHoseg", "true" }, new[] { "HosegHatar", "33" },
            new[] { "RiasztasIsmetlesOra", "6" }, new[] { "CsendesTol", "22" }, new[] { "CsendesIg", "7" }
        };

        public static string Handle(string method, string body, out int status)
        {
            status = 200;
            string ini = AppState.L.DlIni;
            if (method == "POST")
            {
                Dictionary<string, object> d;
                try { d = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(body ?? "{}"); }
                catch { status = 400; return "{\"error\":\"érvénytelen kérés\"}"; }
                if (!File.Exists(ini)) { status = 400; return "{\"error\":\"Még nincs beallitasok.ini – előbb indítsd el egyszer a Letöltőt.\"}"; }
                var lines = File.ReadAllLines(ini, Encoding.UTF8).ToList();
                foreach (var kv in d)
                {
                    if (!Keys.Any(k => k[0] == kv.Key)) continue;
                    string val = Convert.ToString(kv.Value, CultureInfo.InvariantCulture).ToLowerInvariant() == "true" ? "true" : Convert.ToString(kv.Value, CultureInfo.InvariantCulture).ToLowerInvariant() == "false" ? "false" : Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                    int idx = lines.FindIndex(l => l.TrimStart().StartsWith(kv.Key + "=", StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) lines[idx] = kv.Key + "=" + val; else lines.Add(kv.Key + "=" + val);
                }
                Guard.WriteAllLines(ini, lines, new UTF8Encoding(true));
            }
            var cur = Settings.ReadIni(ini);
            var w = new JW().Obj().P("hasIni", File.Exists(ini));
            foreach (var k in Keys) { string v; w.P(k[0], cur.TryGetValue(k[0], out v) ? v : k[1]); }
            return w.End().ToString();
        }

        public static string Test(out int status)
        {
            status = 200;
            string exe = AppState.L.FindDownloader();
            if (exe == null) return "{\"error\":\"Nem találom a Letöltő.exe-t.\"}";
            try { Process.Start(new ProcessStartInfo(exe, "--teszt-ertesites") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppState.L.Base }); }
            catch (Exception ex) { return "{\"error\":\"" + ex.Message.Replace("\"", "'") + "\"}"; }
            return "{\"ok\":true}";
        }
    }
}
