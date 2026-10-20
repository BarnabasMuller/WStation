using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Idojaras
{
    // Csapadékmérő-kalibráció: kézi mérések tól–ig időszakokra → szorzó (Σ kézi / Σ állomás), globálisan vagy időszakosan.
    // appdata_megjelenítő\kalibracio.json; a nyers érték megmarad (Dataset.RawPtot / RawPrate).
    static class Calib
    {
        public class Row { public string from = "", to = "", note = ""; public double manual; }
        public class Cfg { public bool enabled = true; public string mode = "global"; public double factor = 0; public List<Row> rows = new List<Row>(); }

        public class RowRes
        {
            public long A, B; public double Manual, Station = double.NaN, Ratio = double.NaN;
            public bool Valid; public string Error; public List<string> Warn = new List<string>();
        }
        public class Result
        {
            public Cfg Cfg; public List<RowRes> Rows = new List<RowRes>();
            public double Computed = double.NaN, Mean = double.NaN, Std = double.NaN, SumManual, SumStation;
            public bool Active; public double Global = 1;
            // időszakos módban: napszám → szorzó
            public Dictionary<int, double> ByDay;
            public double FactorAt(long t, int day)
            {
                if (!Active) return 1;
                if (ByDay != null) { double f; return ByDay.TryGetValue(day, out f) ? f : Global; }
                return Global;
            }
        }

        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();
        static string FilePath { get { return Path.Combine(AppState.L.App, "kalibracio.json"); } }
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static Cfg Load()
        {
            try { if (File.Exists(FilePath)) return Js.Deserialize<Cfg>(File.ReadAllText(FilePath, Encoding.UTF8)) ?? new Cfg(); }
            catch (Exception ex) { Program.Log("A kalibracio.json sérült: " + ex.Message); }
            return new Cfg();
        }

        public static string Validate(Cfg c)
        {
            if (c == null) return "érvénytelen kalibráció";
            if (c.mode != "global" && c.mode != "period") return "a mód global vagy period lehet";
            if (c.factor != 0 && (c.factor < 0.3 || c.factor > 3)) return "a kézi szorzó 0,3 és 3 között legyen (0 = számolt)";
            if (c.rows == null) c.rows = new List<Row>();
            if (c.rows.Count > 500) return "legfeljebb 500 sor";
            foreach (var r in c.rows)
            {
                long a, b;
                if (!ParseLocal(r.from, out a) || !ParseLocal(r.to, out b)) return "érvénytelen dátum: " + r.from + " – " + r.to;
                if (double.IsNaN(r.manual) || r.manual < 0 || r.manual > 2000) return "a kézi mérés 0 és 2000 mm között legyen";
                if ((r.note ?? "").Length > 200) return "a megjegyzés legfeljebb 200 karakter";
            }
            return null;
        }

        public static void Save(Cfg c)
        {
            Guard.CreateDirectory(AppState.L.App);
            string tmp = FilePath + ".tmp";
            Guard.WriteAllText(tmp, Js.Serialize(c), new UTF8Encoding(false));
            if (File.Exists(FilePath)) Guard.Replace(tmp, FilePath, null); else Guard.Move(tmp, FilePath);
        }

        // "2026-07-01T06:00" helyi idő → epoch
        public static bool ParseLocal(string s, out long epoch)
        {
            epoch = 0;
            DateTime d;
            if (!DateTime.TryParseExact(s ?? "", new[] { "yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm", "yyyy-MM-dd" }, Inv, DateTimeStyles.None, out d)) return false;
            try { epoch = (long)(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(d, DateTimeKind.Unspecified), Tz.Zone) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }
            catch { epoch = (long)(d - new DateTime(1970, 1, 1)).TotalSeconds - 3600; }
            return true;
        }

        // az állomás csapadéka (a napi halmozott értékek növekményeiből) a tól–ig időszakban
        public static double StationSum(Dataset ds, float[] ptot, long a, long b)
        {
            int i0 = ds.LowerBound(a), i1 = ds.LowerBound(b + 1);
            if (i0 >= i1) return double.NaN;
            double sum = 0, prev = double.NaN; int prevDay = int.MinValue; bool first = true;
            // kiindulás: az időszak előtti utolsó érték ugyanazon a napon
            if (i0 > 0 && ds.Day[i0 - 1] == ds.Day[i0] && !float.IsNaN(ptot[i0 - 1])) { prev = ptot[i0 - 1]; prevDay = ds.Day[i0 - 1]; first = false; }
            for (int i = i0; i < i1; i++)
            {
                double v = ptot[i];
                if (double.IsNaN(v)) continue;
                if (ds.Day[i] != prevDay)
                {
                    // új nap: a halmozott érték éjféltől számít (az időszak legelső, nap közbeni mérése csak kiindulópont)
                    if (!first) sum += v;
                }
                else sum += v >= prev ? v - prev : v;   // csökkenés = nap közbeni nullázás
                prev = v; prevDay = ds.Day[i]; first = false;
            }
            return sum;
        }

        public static Result Evaluate(Dataset ds, Cfg c, int gapMinutes)
        {
            var res = new Result { Cfg = c };
            float[] raw = ds.RawPtot ?? ds.C[Col.PTOT];
            var ratios = new List<double>();
            foreach (var r in c.rows)
            {
                var rr = new RowRes { Manual = r.manual };
                res.Rows.Add(rr);
                if (!ParseLocal(r.from, out rr.A) || !ParseLocal(r.to, out rr.B)) { rr.Error = "Érvénytelen dátum."; continue; }
                if (rr.A >= rr.B) { rr.Error = "A „tól” későbbi (vagy ugyanaz), mint az „ig”."; continue; }
                if (ds.N == 0 || rr.B < ds.T[0] || rr.A > ds.T[ds.N - 1]) { rr.Error = "Erre az időszakra nincs mérés."; continue; }
                rr.Station = StationSum(ds, raw, rr.A, rr.B);
                // adathiány az időszakban
                int i0 = ds.LowerBound(rr.A), i1 = ds.LowerBound(rr.B + 1); long gapSec = Math.Max(6, gapMinutes) * 60L; int gaps = 0; long gapLen = 0;
                if (i0 < ds.N && ds.T[i0] - rr.A > gapSec) { gaps++; gapLen += ds.T[i0] - rr.A; }
                for (int i = Math.Max(i0 + 1, 1); i < i1; i++) if (ds.T[i] - ds.T[i - 1] > gapSec) { gaps++; gapLen += ds.T[i] - ds.T[i - 1]; }
                if (i1 > 0 && i1 <= ds.N && rr.B - ds.T[i1 - 1] > gapSec) { gaps++; gapLen += rr.B - ds.T[i1 - 1]; }
                if (gaps > 0) rr.Warn.Add("Adathiány az időszakban: " + gaps + " db, összesen kb. " + (gapLen / 3600.0).ToString("0.#", Inv).Replace('.', ',') + " óra – az állomás összege kevesebb lehet.");
                bool m0 = r.manual <= 0, s0 = double.IsNaN(rr.Station) || rr.Station <= 0.05;
                if (m0 && s0) rr.Error = "A kézi és az állomás mérése is 0 mm – ebből nem számolható arány (nem esett?).";
                else if (s0) rr.Error = "Az állomás 0 mm-t mért, a kézi mérés " + Fmt(r.manual) + " mm – eldugult a csapadékmérő?";
                else if (m0) rr.Error = "A kézi mérés 0 mm, az állomás " + Fmt(rr.Station) + " mm-t mért – elírás, vagy nem gyűjtött a kézi mérő?";
                else
                {
                    rr.Ratio = r.manual / rr.Station; rr.Valid = true;
                    if (rr.Ratio < 0.3 || rr.Ratio > 3) rr.Warn.Add("Szokatlan arány (" + Fmt(rr.Ratio, 2) + ") – érdemes ellenőrizni.");
                }
            }
            // átfedések
            for (int i = 0; i < res.Rows.Count; i++)
                for (int j = i + 1; j < res.Rows.Count; j++)
                {
                    var x = res.Rows[i]; var y = res.Rows[j];
                    if (x.A < y.B && y.A < x.B && x.A < x.B && y.A < y.B) { x.Warn.Add("Átfed a(z) " + (j + 1) + ". sorral."); y.Warn.Add("Átfed a(z) " + (i + 1) + ". sorral."); }
                }
            var valid = res.Rows.Where(x => x.Valid).ToList();
            if (valid.Count > 0)
            {
                res.SumManual = valid.Sum(x => x.Manual); res.SumStation = valid.Sum(x => x.Station);
                res.Computed = res.SumManual / res.SumStation;
                res.Mean = valid.Average(x => x.Ratio);
                res.Std = valid.Count > 1 ? Math.Sqrt(valid.Sum(x => (x.Ratio - res.Mean) * (x.Ratio - res.Mean)) / (valid.Count - 1)) : 0;
            }
            res.Global = c.factor > 0 ? c.factor : double.IsNaN(res.Computed) ? 1 : res.Computed;
            res.Active = c.enabled && Math.Abs(res.Global - 1) > 1e-9 || (c.enabled && c.mode == "period" && valid.Count > 0);
            if (c.enabled && c.mode == "period" && valid.Count > 0 && c.factor <= 0 && ds.N > 0)
            {
                // minden nap a hozzá időben legközelebbi kalibrációs időszak arányát kapja
                res.ByDay = new Dictionary<int, double>();
                var mids = valid.Select(x => new { mid = (x.A + x.B) / 2, x.A, x.B, x.Ratio }).ToList();
                foreach (int day in ds.Day.Distinct())
                {
                    long noon = Tz.LocalMidnightEpoch(Tz.DateOf(day)) + 12 * 3600;   // helyi dél (nyári időszámítással is)
                    var inside = mids.FirstOrDefault(m => noon >= m.A && noon <= m.B);
                    res.ByDay[day] = inside != null ? inside.Ratio : mids.OrderBy(m => Math.Abs(m.mid - noon)).First().Ratio;
                }
            }
            return res;
        }

        static string Fmt(double v, int dec = 1) { return v.ToString(dec == 2 ? "0.00" : "0.#", Inv).Replace('.', ','); }

        // a Loader hívja a feldolgozás elején: a nyers értékek mentése, majd a szorzó alkalmazása
        public static void Apply(Dataset ds, Settings s)
        {
            if (ds.RawPtot == null) { ds.RawPtot = (float[])ds.C[Col.PTOT].Clone(); ds.RawPrate = (float[])ds.C[Col.PRATE].Clone(); }
            Cfg c;
            try { c = Load(); } catch { c = new Cfg(); }
            var ev = Evaluate(ds, c, s.GapMinutes);
            ds.CalibRes = ev;
            float[] pt = ds.C[Col.PTOT], pr = ds.C[Col.PRATE];
            for (int i = 0; i < ds.N; i++)
            {
                double f = ev.FactorAt(ds.T[i], ds.Day[i]);
                pt[i] = float.IsNaN(ds.RawPtot[i]) ? float.NaN : (float)(ds.RawPtot[i] * f);
                pr[i] = float.IsNaN(ds.RawPrate[i]) ? float.NaN : (float)(ds.RawPrate[i] * f);
            }
        }

        public static string Json(Result ev)
        {
            var w = new JW().Obj();
            var c = ev.Cfg;
            w.P("enabled", c.enabled).P("mode", c.mode).P("factor", c.factor, 3).P("active", ev.Active).P("global", ev.Global, 3)
             .P("computed", ev.Computed, 3).P("mean", ev.Mean, 3).P("std", ev.Std, 3).P("sumManual", ev.SumManual, 1).P("sumStation", ev.SumStation, 1);
            w.Key("rows").Arr();
            for (int i = 0; i < c.rows.Count; i++)
            {
                var r = c.rows[i]; var x = ev.Rows[i];
                w.Obj().P("from", r.from).P("to", r.to).P("manual", r.manual, 2).P("note", r.note ?? "")
                 .P("station", x.Station, 2).P("ratio", x.Ratio, 3).P("valid", x.Valid);
                if (x.Error != null) w.P("error", x.Error);
                w.Key("warn").Arr(); foreach (var m in x.Warn) w.Str(m); w.EndArr();
                w.End();
            }
            w.EndArr();
            return w.End().ToString();
        }

        public static string Handle(string method, string body, out int status)
        {
            status = 200;
            var ds = AppState.Data;
            if (method == "POST")
            {
                Cfg c;
                try { c = Js.Deserialize<Cfg>(body ?? "{}"); } catch { status = 400; return "{\"error\":\"érvénytelen kérés\"}"; }
                string err = Validate(c);
                if (err != null) { status = 400; return new JW().Obj().P("error", err).End().ToString(); }
                var old = Load();
                Save(c);
                double of = ds != null && ds.CalibRes != null ? ds.CalibRes.Global : 1;
                ChangeLog.Add("Csapadék-kalibráció", "beállítás", Js.Serialize(old), Js.Serialize(c), new Dictionary<string, object> { { "type", "calib" }, { "value", Js.Serialize(old) } });
                if (ds == null) return "{\"ok\":true}";
                var ev = Evaluate(ds, c, AppState.S.GapMinutes);
                AppState.ReloadAsync();   // a kalibrált csapadék mindenhol (napi összesítő, mérleg, FWI, rekordok…)
                return Json(ev);
            }
            if (ds == null) { status = 503; return "{\"error\":\"nincs adat\"}"; }
            return Json(Evaluate(ds, Load(), AppState.S.GapMinutes));
        }
    }
}
