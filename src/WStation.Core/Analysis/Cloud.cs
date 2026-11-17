using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Idojaras
{
    // Felhőzet becslése a saját állomás méréseiből (TERV_4 5. pont)
    //  nappal: tisztaégbolt-index k = G_mért · k_Rs / G_tiszta (Haurwitz), okta = 8 · ((1 − k) / A)^(1/B) (Kasten–Czeplak megfordítva),
    //          a k 30 perces átlagából és szórásából a felhő jellege;
    //  éjjel:  a lehűlés üteme szélcsendben (derült: gyors, borult: lassú), harmatpont-különbség (köd).
    // A METAR-ellenőrzés csak kiegészítő kiértékelés: nélküle minden működik.
    static class Cloud
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public const int BinSec = 600;   // 10 perces lépés

        public class Bin
        {
            public long T;                 // a lépés közepe (epoch s)
            public double Pct = double.NaN, Okta = double.NaN, K = double.NaN, Ks = double.NaN, SunH = double.NaN, Gc = double.NaN, G = double.NaN;
            public string Src = "", Type = "", Conf = "";
        }

        // ------------------------------------------------------------------ napállás (NOAA-közelítés, kb. 0,5° pontos)
        public static double SunElevation(long epoch, double latDeg, double lonDeg) { double az; return SunPos(epoch, latDeg, lonDeg, out az); }

        // magasság (°) és irány (°, északtól az óramutató járása szerint)
        public static double SunPos(long epoch, double latDeg, double lonDeg, out double azDeg)
        {
            double jd = epoch / 86400.0 + 2440587.5, n = jd - 2451545.0;
            double L = Norm360(280.460 + 0.9856474 * n), g = Norm360(357.528 + 0.9856003 * n) * Math.PI / 180;
            double lam = (L + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g)) * Math.PI / 180;
            double eps = (23.439 - 0.0000004 * n) * Math.PI / 180;
            double ra = Math.Atan2(Math.Cos(eps) * Math.Sin(lam), Math.Cos(lam));
            double dec = Math.Asin(Math.Sin(eps) * Math.Sin(lam));
            double gmst = ((18.697374558 + 24.06570982441908 * n) % 24 + 24) % 24;
            double ha = (gmst * 15 + lonDeg) * Math.PI / 180 - ra;
            double lat = latDeg * Math.PI / 180;
            double h = Math.Asin(Math.Sin(lat) * Math.Sin(dec) + Math.Cos(lat) * Math.Cos(dec) * Math.Cos(ha));
            azDeg = Norm360(Math.Atan2(-Math.Sin(ha), Math.Tan(dec) * Math.Cos(lat) - Math.Sin(lat) * Math.Cos(ha)) * 180 / Math.PI);
            return h * 180 / Math.PI;
        }

        // ------------------------------------------------------------------ az állomás saját „tiszta égbolti” görbéje
        // A mért sugárzás felső percentilise napmagasság × irány cellánként, a teljes adatsorból. Ez tartalmazza az állomás
        // árnyékolását (fák, házak alacsony napnál) és az érzékelő eltérését, amit egyetlen szorzó nem tudna.
        // Ahol kevés a minta, a Haurwitz-modell × k_Rs a tartalék.
        const int HB = 2, AB = 10;   // cellaméret: 2° magasság, 10° irány
        static object envLock = new object();
        static Dataset envFor; static double[,] env;

        static double[,] Envelope(Dataset ds, Settings s)
        {
            lock (envLock)
            {
                if (envFor == ds && env != null) return env;
                double lat = Lat(ds, s), lon = Lon(ds, s);
                var cells = new List<float>[90 / HB + 1, 360 / AB];
                for (int i = 0; i < ds.N; i++)
                {
                    double g = V(ds, Col.SOL, i, Flag.Solar);
                    if (double.IsNaN(g) || g <= 0) continue;
                    double az, h = SunPos(ds.T[i], lat, lon, out az);
                    if (h < 3) continue;
                    int hb = Math.Min(90 / HB, (int)(h / HB)), ab = Math.Min(360 / AB - 1, (int)(az / AB));
                    (cells[hb, ab] ?? (cells[hb, ab] = new List<float>())).Add((float)g);
                }
                var e = new double[90 / HB + 1, 360 / AB];
                for (int hb = 0; hb <= 90 / HB; hb++)
                    for (int ab = 0; ab < 360 / AB; ab++)
                    {
                        // a cella és a szomszédai együtt (több minta, simább görbe)
                        var all = new List<float>();
                        for (int dh = -1; dh <= 1; dh++)
                            for (int da = -1; da <= 1; da++)
                            {
                                int h2 = hb + dh, a2 = (ab + da + 360 / AB) % (360 / AB);
                                if (h2 < 0 || h2 > 90 / HB || cells[h2, a2] == null) continue;
                                all.AddRange(cells[h2, a2]);
                            }
                        if (all.Count < 60) { e[hb, ab] = double.NaN; continue; }
                        all.Sort();
                        e[hb, ab] = all[(int)(all.Count * 0.95)];   // 95. percentilis: a derült napok szintje (a felhőperem-csúcsok nélkül)
                    }
                envFor = ds; env = e;
                return e;
            }
        }

        // a tiszta égbolti sugárzás ennél az állomásnál (W/m²)
        static double ClearAt(Dataset ds, Settings s, long t, double lat, double lon, out double h)
        {
            double az;
            h = SunPos(t, lat, lon, out az);
            var e = Envelope(ds, s);
            int hb = Math.Min(90 / HB, Math.Max(0, (int)(h / HB))), ab = Math.Min(360 / AB - 1, (int)(az / AB));
            double model = ClearSky(h);
            double v = e[hb, ab];
            // a burkoló nem lehet a modell fölött (erős felhőperem-felerősítés), és nem lehet nagyon alatta (kevés derült nap a cellában)
            if (double.IsNaN(v)) return model;
            return Math.Max(model * 0.25, Math.Min(model * 1.1, v)) ;
        }
        static double Norm360(double x) { return (x % 360 + 360) % 360; }

        // tiszta égbolti globálsugárzás (Haurwitz, W/m²)
        public static double ClearSky(double hDeg)
        {
            if (hDeg <= 0) return 0;
            double s = Math.Sin(hDeg * Math.PI / 180);
            return 1098 * s * Math.Exp(-0.057 / s);
        }

        // okta a tisztaégbolt-indexből (Kasten–Czeplak: k = 1 − A·(N/8)^B megfordítva)
        public static double OktaFromK(double k, Settings s)
        {
            if (double.IsNaN(k)) return double.NaN;
            // a burkoló a legtisztább napokat adja; egy átlagos derült nap (pára, por) ennél kb. 10–20%-kal kevesebb:
            // a „derült-küszöb” fölött derültnek vesszük, alatta erre normálva számolunk
            k = k / Math.Max(0.3, s.CloudClearK);
            if (k >= 1) return 0;
            double x = Math.Max(0, 1 - k) / s.CloudA;
            return Math.Max(0, Math.Min(8, 8 * Math.Pow(Math.Min(1, x), 1 / s.CloudB)));
        }

        static double Lat(Dataset ds, Settings s) { return double.IsNaN(ds.Lat) ? s.FallbackLat : ds.Lat; }
        static double Lon(Dataset ds, Settings s) { return double.IsNaN(ds.Lon) ? s.FallbackLon : ds.Lon; }

        static double V(Dataset ds, int col, int i, int mask)
        {
            if ((ds.Flags[i] & mask) != 0) return double.NaN;
            return ds.C[col][i];
        }

        // ------------------------------------------------------------------ becslés a [from, to) időszakra, 10 perces lépésekben
        public static List<Bin> Estimate(Dataset ds, Settings s, long from, long to)
        {
            var res = new List<Bin>();
            if (ds == null || ds.N == 0) return res;
            double lat = Lat(ds, s), lon = Lon(ds, s);
            from = Math.Max(from, ds.T[0]) / BinSec * BinSec;
            to = Math.Min(to, ds.T[ds.N - 1] + 1);
            long half = Math.Max(5, s.CloudWindowMin) * 60 / 2;
            for (long t0 = from; t0 < to; t0 += BinSec)
            {
                long t = t0 + BinSec / 2;
                var b = new Bin { T = t };
                double h = SunElevation(t, lat, lon);
                b.SunH = h;
                int a = ds.LowerBound(t - half), z = ds.LowerBound(t + half);
                if (z <= a) { res.Add(b); continue; }   // nincs mérés
                // eső az ablakban → biztosan felhős
                double rain = 0, maxRate = 0, gust = 0;
                for (int i = a; i < z; i++)
                {
                    double r = V(ds, Col.PRATE, i, Flag.RainRate); if (r > maxRate) maxRate = r;
                    double g = V(ds, Col.WGH, i, Flag.Wind); if (g > gust) gust = g;
                }
                rain = maxRate;
                if (h >= s.CloudHmin)
                {
                    // nappal: k mintánként, majd ablakátlag és szórás
                    var ks = new List<double>();
                    double gs = 0, gcs = 0; int gn = 0;
                    for (int i = a; i < z; i++)
                    {
                        double g = V(ds, Col.SOL, i, Flag.Solar), hc;
                        double gc = ClearAt(ds, s, ds.T[i], lat, lon, out hc);
                        if (double.IsNaN(g) || gc < 30) continue;
                        ks.Add(Math.Min(1.4, g * s.CloudKrs / gc));
                        gs += g; gcs += gc; gn++;
                    }
                    if (gn > 0) { b.G = gs / gn; b.Gc = gcs / gn; }
                    if (ks.Count >= 2)
                    {
                        double m = ks.Average(), sd = Math.Sqrt(ks.Sum(x => (x - m) * (x - m)) / (ks.Count - 1));
                        b.K = m; b.Ks = sd; b.Src = "nappal";
                        b.Okta = OktaFromK(m, s);
                        if (rain > 0.5) b.Okta = Math.Max(b.Okta, 6);
                        b.Pct = b.Okta / 8 * 100;
                        b.Type = DayType(ds, s, a, z, m, sd, ks.Max(), rain, gust);
                        b.Conf = h >= 30 ? "jó" : "közepes";
                    }
                }
                else if (h < -6)
                {
                    // éjjel: lehűlés az előző 60 percben, szélcsendben
                    Night(ds, s, b, t, rain);
                }
                else b.Conf = "";   // szürkület / alacsony nap: nem becsülhető megbízhatóan
                res.Add(b);
            }
            return res;
        }

        static string DayType(Dataset ds, Settings s, int a, int z, double m, double sd, double kmax, double rate, double gust)
        {
            if (rate >= s.StormRate && gust >= s.StormGust) return "zivatarfelhő (cumulonimbus)";
            if (rate > 0.5) return m < 0.3 ? "esőréteg (nimbostratus), esik" : "záporfelhő, esik";
            // köd: reggeli alacsony k, telített levegő, szélcsend
            double hum = double.NaN, spread = double.NaN, wind = double.NaN;
            int mid = (a + z) / 2;
            if (mid < ds.N)
            {
                hum = V(ds, Col.HA, mid, Flag.Hum);
                double tt = V(ds, Col.TA, mid, Flag.Temp), td = V(ds, Col.DA, mid, Flag.Dew);
                spread = tt - td; wind = V(ds, Col.WSA, mid, Flag.Wind);
            }
            if (m < 0.4 && hum >= 97 && spread < 1 && wind < 3) return "köd vagy nagyon alacsony felhő";
            if (sd >= s.CloudCuStd || kmax > 1.05) return "gomolyfelhők (cumulus) – hol süt, hol nem";
            double kn = m / Math.Max(0.3, s.CloudClearK);   // a derült-küszöbhöz viszonyítva
            if (kn >= 1) return "derült";
            if (kn >= 0.85) return "vékony magas felhő (cirrus) – fátyolos napsütés";
            if (kn >= 0.5) return "középmagas felhőréteg (altostratus / altocumulus)";
            if (kn >= 0.35) return "vastag felhőréteg";
            return "alacsony felhőréteg (stratus / stratocumulus), borult";
        }

        static void Night(Dataset ds, Settings s, Bin b, long t, double rate)
        {
            b.Src = "éjjel"; b.Conf = "gyenge";
            int i1 = ds.LowerBound(t) - 1, i0 = ds.LowerBound(t - 3600);
            if (i1 < 0 || i0 >= ds.N || i1 <= i0 || ds.T[i1] - ds.T[i0] < 2400) { b.Conf = ""; return; }
            double t0 = V(ds, Col.TA, i0, Flag.Temp), t1 = V(ds, Col.TA, i1, Flag.Temp);
            if (double.IsNaN(t0) || double.IsNaN(t1)) { b.Conf = ""; return; }
            double rateC = (t0 - t1) / ((ds.T[i1] - ds.T[i0]) / 3600.0);   // °C/óra lehűlés
            double wind = 0; int wn = 0;
            for (int i = i0; i <= i1; i++) { double w = V(ds, Col.WSA, i, Flag.Wind); if (!double.IsNaN(w)) { wind += w; wn++; } }
            wind = wn > 0 ? wind / wn : double.NaN;
            double hum = V(ds, Col.HA, i1, Flag.Hum), spread = t1 - V(ds, Col.DA, i1, Flag.Dew);
            if (rate > 0.5) { b.Okta = 7; b.Type = "esik – felhős"; }
            else if (hum >= 97 && spread < 1) { b.Okta = 7; b.Type = "párás, ködös lehet"; }
            else if (!double.IsNaN(wind) && wind > s.CloudNightWind) { b.Conf = ""; b.Type = "szeles – éjjel nem becsülhető"; return; }
            else if (rateC >= s.CloudNightClear) { b.Okta = 1; b.Type = "derült (gyors lehűlés)"; }
            else if (rateC >= s.CloudNightPart) { b.Okta = 4; b.Type = "részben felhős"; }
            else { b.Okta = 7; b.Type = "felhős (alig hűl)"; }
            b.Pct = b.Okta / 8 * 100;
        }

        // ------------------------------------------------------------------ JSON
        static void P(JW w, string k, double v, int dec) { w.P(k, v, dec); }

        public static string SeriesJson(Dataset ds, Settings s, long fromMs, long toMs)
        {
            var bins = Estimate(ds, s, fromMs / 1000, toMs / 1000);
            var w = new JW().Obj().P("binSec", BinSec);
            w.Key("t").Arr(); foreach (var b in bins) w.Long(b.T * 1000); w.EndArr();
            w.Key("pct").Arr(); foreach (var b in bins) w.Num(b.Pct, 0); w.EndArr();
            w.Key("okta").Arr(); foreach (var b in bins) w.Num(b.Okta, 1); w.EndArr();
            w.Key("k").Arr(); foreach (var b in bins) w.Num(b.K, 2); w.EndArr();
            w.Key("ks").Arr(); foreach (var b in bins) w.Num(b.Ks, 2); w.EndArr();
            w.Key("g").Arr(); foreach (var b in bins) w.Num(b.G, 0); w.EndArr();       // mért sugárzás (ablakátlag)
            w.Key("gc").Arr(); foreach (var b in bins) w.Num(b.Gc, 0); w.EndArr();     // derült égbolt ennél az állomásnál
            w.Key("h").Arr(); foreach (var b in bins) w.Num(b.SunH, 1); w.EndArr();
            w.Key("src").Arr(); foreach (var b in bins) w.Str(b.Src); w.EndArr();
            w.Key("type").Arr(); foreach (var b in bins) w.Str(b.Type); w.EndArr();
            w.Key("conf").Arr(); foreach (var b in bins) w.Str(b.Conf); w.EndArr();
            return w.End().ToString();
        }

        // a legutóbbi becsülhető lépés (az Áttekintés kártyájához) és a mai nappali átlag
        public static string NowJson(Dataset ds, Settings s)
        {
            var w = new JW().Obj();
            if (ds == null || ds.N == 0) return w.End().ToString();
            long last = ds.T[ds.N - 1];
            var bins = Estimate(ds, s, last - 36 * 3600, last + 1);
            var cur = bins.LastOrDefault(b => !double.IsNaN(b.Pct));
            if (cur != null && last - cur.T < 3 * 3600)
                w.Key("now").Obj().Ms("t", cur.T).P("pct", cur.Pct, 0).P("okta", cur.Okta, 1).P("src", cur.Src).P("type", cur.Type).P("conf", cur.Conf).P("k", cur.K, 2).P("ks", cur.Ks, 2).End();
            else w.Key("now").Null();
            // az utolsó nap nappali átlaga
            var day = bins.Where(b => b.Src == "nappal" && Tz.DayNumber(Tz.Local(b.T)) == Tz.DayNumber(Tz.Local(last))).ToList();
            if (day.Count > 0) w.P("dayPct", day.Average(b => b.Pct), 0).P("dayType", Dominant(day));
            w.P("sunH", SunElevation(last, Lat(ds, s), Lon(ds, s)), 1);
            return w.End().ToString();
        }

        static string Dominant(List<Bin> bins)
        {
            var g = bins.Where(b => b.Type.Length > 0).GroupBy(b => b.Type).OrderByDescending(x => x.Count()).FirstOrDefault();
            return g == null ? "" : g.Key;
        }

        // napi nappali átlag (a heti összesítőhöz, a jelentéshez, a naptárhoz)
        public static string DaysJson(Dataset ds, Settings s)
        {
            var w = new JW().Obj();
            if (ds == null || ds.N == 0) return w.End().ToString();
            var bins = Estimate(ds, s, ds.T[0], ds.T[ds.N - 1] + 1).Where(b => b.Src == "nappal").ToList();
            var byDay = bins.GroupBy(b => Tz.DayNumber(Tz.Local(b.T))).OrderBy(g => g.Key).ToList();
            w.Key("date").Arr(); foreach (var g in byDay) w.Str(Tz.DateOf(g.Key).ToString("yyyy-MM-dd", Inv)); w.EndArr();
            w.Key("pct").Arr(); foreach (var g in byDay) w.Num(g.Average(b => b.Pct), 0); w.EndArr();
            w.Key("n").Arr(); foreach (var g in byDay) w.Long(g.Count()); w.EndArr();
            w.Key("type").Arr(); foreach (var g in byDay) w.Str(Dominant(g.ToList())); w.EndArr();
            return w.End().ToString();
        }

        // ------------------------------------------------------------------ METAR-ellenőrzés (opcionális; a Letöltő tölti le: data\metar_<ICAO>.csv)
        public class Metar { public long T; public double Okta; public bool Cb; public string Cover = "", Raw = ""; }

        public static List<Metar> ReadMetar(string dataDir, out string icao)
        {
            icao = null;
            var res = new List<Metar>();
            try
            {
                var f = Directory.GetFiles(dataDir, "metar_*.csv").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (f == null) return res;
                icao = Path.GetFileNameWithoutExtension(f).Substring(6).ToUpperInvariant();
                var lines = File.ReadAllLines(f, Encoding.UTF8);
                for (int i = 1; i < lines.Length; i++)
                {
                    var p = lines[i].Split(';');
                    long t; double o;
                    if (p.Length < 5 || !long.TryParse(p[0], NumberStyles.Integer, Inv, out t) || !double.TryParse(p[4].Replace(',', '.'), NumberStyles.Float, Inv, out o)) continue;
                    res.Add(new Metar { T = t, Cover = p[3], Okta = o, Cb = p.Length > 5 && p[5] == "1", Raw = p.Length > 6 ? p[6] : "" });
                }
            }
            catch { }
            return res;
        }

        static int Cat(double okta) { return okta <= 2 ? 0 : okta <= 5 ? 1 : 2; }   // derült / részben felhős / borult

        public static string MetarJson(Dataset ds, Settings s, Layout L)
        {
            string icao;
            var mets = ReadMetar(L.Data, out icao);
            var w = new JW().Obj().P("icao", icao ?? "").P("count", mets.Count);
            if (mets.Count == 0 || ds == null || ds.N == 0) { w.P("pairs", 0); return w.End().ToString(); }
            long a = Math.Max(ds.T[0], mets.Min(m => m.T)) - 900, b = Math.Min(ds.T[ds.N - 1], mets.Max(m => m.T)) + 900;
            var bins = b > a ? Estimate(ds, s, a, b) : new List<Bin>();
            var byT = new SortedList<long, Bin>(); foreach (var x in bins) byT[x.T] = x;
            var keys = byT.Keys.ToList();
            var conf = new int[3, 3]; int pairs = 0, agree = 0, dayN = 0, dayAgree = 0, nightN = 0, nightAgree = 0; double absDiff = 0, dayDiff = 0;
            var kRatios = new List<double>();
            foreach (var m in mets)
            {
                int idx = keys.BinarySearch(m.T); if (idx < 0) idx = ~idx;
                Bin best = null; long bd = long.MaxValue;
                for (int j = Math.Max(0, idx - 1); j <= Math.Min(keys.Count - 1, idx); j++) { long d = Math.Abs(keys[j] - m.T); if (d < bd) { bd = d; best = byT[keys[j]]; } }
                if (best == null || bd > 900 || double.IsNaN(best.Okta)) continue;
                int ce = Cat(best.Okta), cm = Cat(m.Okta);
                conf[cm, ce]++; pairs++; if (ce == cm) agree++;
                absDiff += Math.Abs(best.Okta - m.Okta);
                if (best.Src == "nappal") { dayN++; if (ce == cm) dayAgree++; dayDiff += Math.Abs(best.Okta - m.Okta); if (m.Okta <= 1 && !double.IsNaN(best.K) && best.K > 0.3) kRatios.Add(best.K); }
                else { nightN++; if (ce == cm) nightAgree++; }
            }
            w.P("pairs", pairs).P("agree", pairs > 0 ? 100.0 * agree / pairs : double.NaN, 0).P("meanAbsOkta", pairs > 0 ? absDiff / pairs : double.NaN, 1);
            w.P("dayPairs", dayN).P("dayAgree", dayN > 0 ? 100.0 * dayAgree / dayN : double.NaN, 0).P("dayMeanAbsOkta", dayN > 0 ? dayDiff / dayN : double.NaN, 1);
            w.P("nightPairs", nightN).P("nightAgree", nightN > 0 ? 100.0 * nightAgree / nightN : double.NaN, 0);
            w.Key("matrix").Arr(); for (int i = 0; i < 3; i++) { w.Arr(); for (int j = 0; j < 3; j++) w.Long(conf[i, j]); w.EndArr(); } w.EndArr();
            // javaslat a derült-küszöbre: a METAR szerint derült (0–1 okta) nappali időpontokban mért k mediánja (kicsit alatta)
            if (kRatios.Count >= 10)
            {
                kRatios.Sort();
                double med = kRatios[kRatios.Count / 2];
                w.P("clearK", med, 3).P("suggestClearK", Math.Max(0.5, Math.Min(1.1, med * 0.98)), 2).P("clearN", kRatios.Count).P("curClearK", s.CloudClearK, 2);
            }
            w.Ms("from", mets.Min(m => m.T)).Ms("to", mets.Max(m => m.T));
            return w.End().ToString();
        }
    }
}
