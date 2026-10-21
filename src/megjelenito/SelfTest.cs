using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Idojaras
{
    // Idojaras.exe --teszt : a tisztítás, a napi csapadék, a hiányok, az időzóna és a cache ellenőrzése a mappa adatain
    static class SelfTest
    {
        static readonly StringBuilder log = new StringBuilder();
        static int ok, fail, skip;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // a mintaadat jelölt sorainak várt száma (2.2: + csapadékugrás, befagyott szélmérő, túlmelegedés 30 perces átlaggal)
        const int SampleFlagged = 836;

        static void Ok(bool cond, string what, string detail = "")
        {
            if (cond) ok++; else fail++;
            Line((cond ? "[OK]   " : "[HIBA] ") + what + (detail.Length > 0 ? " – " + detail : ""));
        }
        static void Skip(string what) { skip++; Line("[--]   " + what + " (kihagyva: nem a mintaadat)"); }
        static void Line(string s) { log.AppendLine(s); try { Console.WriteLine(s); } catch { } }
        static string N(double v, int d = 2) { return double.IsNaN(v) ? "NaN" : Math.Round(v, d).ToString(Inv); }

        public static int Run(Layout src)
        {
            Line("WStation – önteszt, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
            Line("Programmappa: " + src.Base);
            string tmp = Path.Combine(Path.GetTempPath(), "BudaorsIdojarasTeszt");
            Guard.SetRoot(tmp);   // a teszt csak a saját ideiglenes mappájába írhat
            try
            {
                // a data mappából, vagy (régi elrendezésnél) a programmappából
                string srcData = Directory.Exists(src.Data) && Directory.GetFiles(src.Data, "*.csv").Any(f => Loader.Classify(f) == "obs") ? src.Data : src.Base;
                var csvs = Directory.GetFiles(srcData, "*.csv").Where(f => Loader.Classify(f) == "obs").ToList();
                if (csvs.Count == 0) { Ok(false, "Részletes mérési CSV a data mappában", "nem találtam"); return Finish(src); }
                string srcIni = File.Exists(src.DlIni) ? src.DlIni : Path.Combine(src.Base, "beallitasok.ini");

                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                Directory.CreateDirectory(tmp);
                var a = Layout.For(Path.Combine(tmp, "A"));
                a.Ensure(); Directory.CreateDirectory(a.DlApp);
                foreach (var f in csvs) File.Copy(f, Path.Combine(a.Data, Path.GetFileName(f)));
                if (File.Exists(srcIni)) File.Copy(srcIni, a.DlIni);

                var s = Settings.Load(a);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var ds = Loader.Load(a, s, null);
                long t1 = sw.ElapsedMilliseconds;
                Ok(ds != null && ds.N > 0, "Betöltés", ds == null ? "null" : ds.N + " mérés, " + t1 + " ms");
                if (ds == null) return Finish(src);
                bool sample = ds.T[0] == 1767222275 && ds.N >= 82627;   // a fejlesztéskor használt mintaadat
                Line("Állomás: " + ds.Station + ", időszak: " + Tz.Local(ds.T[0]) + " – " + Tz.Local(ds.T[ds.N - 1]) + (sample ? " (ez a mintaadat)" : ""));

                // --- alap épség
                bool sorted = true;
                for (int i = 1; i < ds.N; i++) if (ds.T[i] <= ds.T[i - 1]) { sorted = false; break; }
                Ok(sorted, "Epoch szerint rendezett és duplikációmentes");
                if (sample) Ok(ds.N >= 82627, "Mérések száma a mintában", ds.N + " (várt: legalább 82627)"); else Skip("Mérések száma");

                // --- cache
                sw.Restart();
                var ds2 = Loader.Load(a, s, null);
                Ok(ds2.FromCache && ds2.N == ds.N, "Második indítás a cache-ből", sw.ElapsedMilliseconds + " ms, " + ds2.N + " mérés");
                Ok(double.IsNaN(ds.Lat) || Math.Abs(ds2.Lat - ds.Lat) < 1e-6, "Az állomás koordinátái a cache-ből is megvannak (2.2)", N(ds2.Lat, 4) + ", " + N(ds2.Lon, 4));

                // --- két átfedő CSV egyesítése
                var b = Layout.For(Path.Combine(tmp, "B"));
                b.Ensure(); Directory.CreateDirectory(b.DlApp);
                var lines = File.ReadAllLines(csvs[0], Encoding.UTF8);
                int total = lines.Length - 1, cut1 = total * 6 / 10, cut2 = total * 4 / 10;
                File.WriteAllLines(Path.Combine(b.Data, "elso.csv"), lines.Take(1 + cut1), new UTF8Encoding(true));
                File.WriteAllLines(Path.Combine(b.Data, "masodik.csv"), new[] { lines[0] }.Concat(lines.Skip(1 + cut2)), new UTF8Encoding(true));
                if (File.Exists(a.DlIni)) File.Copy(a.DlIni, b.DlIni);
                var db = Loader.Load(b, Settings.Load(b), null);
                int overlap = cut1 - cut2;
                Ok(csvs.Count > 1 || (db.N == total && db.DuplicateRows == overlap), "Két átfedő CSV egyesítése, duplikátumszűrés",
                   db.N + " egyedi mérés (várt: " + total + "), " + db.DuplicateRows + " duplikátum kiszűrve (várt: " + overlap + ")");

                // --- inkrementális feldolgozás: csak a megváltozott fájlt olvassa újra
                var extra = lines[lines.Length - 1].Split(';');
                long lastEpoch = long.Parse(extra[3], Inv);
                extra[3] = (lastEpoch + 300).ToString(Inv);
                File.AppendAllText(Path.Combine(b.Data, "masodik.csv"), string.Join(";", extra) + "\r\n", new UTF8Encoding(false));
                var db2 = Loader.Load(b, Settings.Load(b), null);
                int secondRows = total - cut2 + 1;
                Ok(!db2.FromCache && db2.ParsedRows == secondRows && db2.N == total + 1 && db2.NewRows == 1,
                   "Változáskor csak az új/megváltozott fájlt dolgozza fel",
                   "újraolvasott sorok: " + db2.ParsedRows + " (csak a masodik.csv: " + secondRows + "), új mérés: " + db2.NewRows);

                // --- 2.2: a CSV-ből törölt sor a cache-ből is eltűnik (csak a masodik.csv-ben lévő, utolsó előtti sor)
                var m2 = File.ReadAllLines(Path.Combine(b.Data, "masodik.csv"), Encoding.UTF8).ToList();
                m2.RemoveAt(m2.Count - 2);
                File.WriteAllLines(Path.Combine(b.Data, "masodik.csv"), m2, new UTF8Encoding(true));
                var db3 = Loader.Load(b, Settings.Load(b), null);
                Ok(db3.N == total, "A CSV-ből törölt sor a cache-ből is eltűnik (2.2)", db3.N + " mérés (várt: " + total + ")");

                // --- napi csapadék = a napi halmozott precipTotal maximuma
                int mismatch = 0, resets = 0;
                foreach (var d in ds.DaysRaw)
                {
                    double mx = double.NaN; double prev = double.NaN; bool reset = false;
                    for (int i = d.First; i <= d.Last; i++)
                    {
                        double v = ds.C[Col.PTOT][i];
                        if (double.IsNaN(v)) continue;
                        if (!double.IsNaN(prev) && v < prev - 0.25) reset = true;
                        if (double.IsNaN(mx) || v > mx) mx = v;
                        prev = v;
                    }
                    if (reset) { resets++; continue; }
                    if (!(double.IsNaN(mx) && double.IsNaN(d.Rain)) && Math.Abs(mx - d.Rain) > 0.001) mismatch++;
                }
                Ok(mismatch == 0, "Napi csapadék = a nap precipTotal-maximuma (nem összeg)", ds.DaysRaw.Count + " nap, eltérés: " + mismatch + ", nap közbeni nullázás: " + resets);
                if (sample)
                {
                    Ok(Math.Abs(RainOn(ds, 2026, 10, 10) - 20.32) < 0.01, "Csapadék 2026-10-10", N(RainOn(ds, 2026, 10, 10)) + " mm (a WU napi összesítője: 20,32)");
                    Ok(Math.Abs(RainOn(ds, 2026, 7, 17) - 38.1) < 0.01, "Csapadék 2026-07-17 (az év legcsapadékosabb napja)", N(RainOn(ds, 2026, 7, 17)) + " mm (várt: 38,1)");
                }
                else Skip("Ismert csapadékértékek");

                // nap közbeni nullázás (mesterséges adat): 0 → 2 mm, nullázás, 0 → 1,5 mm = 3,5 mm
                var syn = Synthetic(new double[] { 0, 1, 2, 2, 0, 0.5, 1.5 });
                var sd = Loader.ComputeDay(syn, syn.Day[0], 0, syn.N, false);
                Ok(Math.Abs(sd.Rain - 3.5) < 1e-6, "Nap közbeni számlálónullázás kezelése", N(sd.Rain) + " mm (várt: 3,5)");

                // --- összevetés a WU saját napi összesítőjével
                string napi = Path.Combine(srcData, "napi_osszesito.csv");
                if (File.Exists(napi)) CompareDaily(ds, napi);
                else Skip("Összevetés a napi_osszesito.csv-vel");

                // --- hiányok
                var big = ds.Gaps.Where(g => g.To - g.From >= 10 * 3600).ToList();
                Line("Hiányok: " + ds.Gaps.Count + " db > " + s.GapMinutes + " perc; 10 óránál hosszabb: " +
                     string.Join(", ", big.Select(g => Tz.Local(g.From).ToString("MM-dd HH:mm", Inv) + " → " + Tz.Local(g.To).ToString("MM-dd HH:mm", Inv) + " (" + (g.To - g.From) / 3600 + " óra)")));
                if (sample)
                {
                    Ok(big.Any(g => (g.To - g.From) / 3600 == 25 && Tz.Local(g.From).Month == 2 && Tz.Local(g.From).Day == 11), "A 25 órás hiány (02-11 → 02-12) megtalálva");
                    Ok(big.Any(g => (g.To - g.From) / 3600 == 15 && Tz.Local(g.To).Month == 3 && Tz.Local(g.To).Day == 9), "A 15 órás hiány (03-08 → 03-09) megtalálva");
                }
                // a grafikon adatában a hiánynál null pont van (a vonal megszakad)
                AppState.Data = ds; AppState.S = s; AppState.L = a;
                var g0 = big.Count > 0 ? big[0] : ds.Gaps.FirstOrDefault();
                if (g0 != null)
                {
                    var q = new NameValueCollection { { "from", ((g0.From - 6 * 3600) * 1000).ToString(Inv) }, { "to", ((g0.To + 6 * 3600) * 1000).ToString(Inv) }, { "n", "5000" } };
                    int st;
                    string json = Api.Handle("/api/series", q, "GET", out st);
                    var root = (Dictionary<string, object>)new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json);
                    var temp = (Dictionary<string, object>)((Dictionary<string, object>)root["groups"])["temp"];
                    var ts = ((object[])temp["t"]).Select(x => Convert.ToInt64(x)).ToList();
                    var vs = (object[])temp["v"];
                    bool hasBreak = false;
                    for (int i = 0; i < ts.Count; i++)
                        if (vs[i] == null && ts[i] > g0.From * 1000 && ts[i] < g0.To * 1000) hasBreak = true;
                    bool noBridge = true;
                    for (int i = 1; i < ts.Count; i++)
                        if (vs[i] != null && vs[i - 1] != null && ts[i] - ts[i - 1] > s.GapMinutes * 60000L) noBridge = false;
                    Ok(hasBreak && noBridge, "A grafikon nem köti össze a hiányt", "null-pont a hiányban: " + hasBreak + ", áthidaló szakasz: " + !noBridge);
                }

                // --- ritkítás
                {
                    var q = new NameValueCollection { { "n", "1500" } };
                    int st;
                    string json = Api.Handle("/api/series", q, "GET", out st);
                    var root = (Dictionary<string, object>)new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json);
                    var temp = (Dictionary<string, object>)((Dictionary<string, object>)root["groups"])["temp"];
                    int pts = ((object[])temp["t"]).Length;
                    var hi = ((object[])temp["hi"]).Where(x => x != null).Select(x => Convert.ToDouble(x)).Max();
                    double rawMax = Enumerable.Range(0, ds.N).Max(i => (double)ds.C[Col.TH][i]);
                    Ok(pts <= 1500 + 3 * (ds.Gaps.Count + 1) && pts > 1000, "LTTB-ritkítás a teljes időszakra", pts + " pont " + ds.N + " helyett");
                    Ok(Math.Abs(hi - rawMax) < 0.05, "Ritkítás után is megmarad a csúcsérték (min–max sáv)", "max a ritkított sávban: " + N(hi, 1) + ", nyers: " + N(rawMax, 1));
                }

                // --- nyári időszámítás
                var dst1 = ds.DaysRaw.FirstOrDefault(d => d.Date == new DateTime(2026, 3, 29));
                var dst2Start = Tz.LocalMidnightEpoch(new DateTime(2026, 10, 25));
                var dst2End = Tz.LocalMidnightEpoch(new DateTime(2026, 10, 26));
                Ok((dst2End - dst2Start) == 25 * 3600, "Téli időszámítás napja (2026-10-25) 25 órás", (dst2End - dst2Start) / 3600 + " óra");
                if (dst1 != null)
                {
                    var hours = new HashSet<int>();
                    for (int i = dst1.First; i <= dst1.Last; i++) hours.Add(ds.Hour[i]);
                    Ok(dst1.End - dst1.Start == 23 * 3600 && !hours.Contains(2), "Nyári időszámítás napja (2026-03-29) 23 órás, 2 óra kimarad",
                       (dst1.End - dst1.Start) / 3600 + " óra, " + dst1.Count + " mérés, órák: " + hours.Count);
                    // a helyi éjfélnél váltson napot (UTC 23:00 télen, 22:00 nyáron)
                    var mar30 = ds.DaysRaw.FirstOrDefault(d => d.Date == new DateTime(2026, 3, 30));
                    if (mar30 != null) Ok(Tz.Local(ds.T[mar30.First]).Hour == 0 && (ds.T[mar30.First] - 22 * 3600) % 86400 < 3600, "Napváltás helyi éjfélkor (nyári idő, UTC 22:00)", "első mérés: " + Tz.Local(ds.T[mar30.First]).ToString("HH:mm", Inv));
                }
                else Skip("Nyári időszámítás napja");

                // --- minőségjelzők és rekordértékek
                Line("Jelölt mérések: " + ds.FlaggedRows + " – " + string.Join(", ", Flag.Codes.Select((c, i) => c + ": " + ds.FlagCounts[i]).Where((x, i) => ds.FlagCounts[i] > 0)));
                int imax = 0, imin = 0, iuv = 0;
                for (int i = 1; i < ds.N; i++)
                {
                    if (ds.C[Col.TH][i] > ds.C[Col.TH][imax]) imax = i;
                    if (ds.C[Col.TL][i] < ds.C[Col.TL][imin]) imin = i;
                    if (ds.C[Col.UV][i] > ds.C[Col.UV][iuv]) iuv = i;
                }
                double cleanMax = ds.Days.Where(d => !double.IsNaN(d.Tmax)).Max(d => d.Tmax);
                double cleanUv = ds.Days.Where(d => !double.IsNaN(d.UvMax)).Max(d => d.UvMax);
                if (sample)
                {
                    Ok(Math.Abs(ds.C[Col.TH][imax] - 42.7) < 0.01 && (ds.Flags[imax] & Flag.SUN) != 0, "A 42,7 °C-os csúcs (08-05) napsütés-gyanúsként jelölve", Tz.Local(ds.T[imax]).ToString("MM-dd HH:mm", Inv) + ", jelző: " + Flag.Describe(ds.Flags[imax]));
                    Ok(Math.Abs(ds.C[Col.TL][imin] + 10.5) < 0.01 && Tz.Local(ds.T[imin]).Month == 1 && Tz.Local(ds.T[imin]).Day == 20, "Abszolút minimum −10,5 °C (01-20)", Tz.Local(ds.T[imin]).ToString("MM-dd HH:mm", Inv));
                    Ok(Math.Abs(ds.C[Col.UV][iuv] - 130) < 0.01 && (ds.Flags[iuv] & Flag.UV) != 0 && cleanUv <= s.UvMax, "UV = 130 jelölve és a tiszta adatból kizárva", "tiszta UV-max: " + N(cleanUv, 1));
                    Ok(cleanMax < 42.7, "A tiszta napi maximum nem tartalmazza a gyanús csúcsot", "tiszta max: " + N(cleanMax, 1) + " °C");
                    int qc0 = ds.Qc.Count(x => x == 0);
                    Ok(ds.FlagCounts[0] == qc0 && qc0 == 306, "qcStatus = 0 sorok jelölve", qc0 + " sor");
                }
                else Skip("Ismert rekordértékek");

                // --- napi végpont (naptár, heti összesítő)
                {
                    int sdays;
                    var root = (Dictionary<string, object>)new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(Api.Handle("/api/days", new NameValueCollection { { "clean", "1" } }, "GET", out sdays));
                    var dates = (object[])root["date"]; var rain = (object[])root["rain"]; var pc = (object[])root["presChg"];
                    int k = Array.IndexOf(dates, "2026-10-10");
                    Ok(sdays == 200 && dates.Length == ds.Days.Count && (!sample || (k >= 0 && Math.Abs(Convert.ToDouble(rain[k]) - 20.32) < 0.01)) && pc.Any(x => x != null),
                       "Napi végpont (naptár, heti összesítő)", dates.Length + " nap" + (sample ? ", 10-10: " + rain[k] + " mm" : ""));
                }

                // --- szárazság: a FAO-56 hivatalos számpéldái és a mérleg
                {
                    var g = new DGlobal { elevation = 100, albedo = 0.23 };
                    double ra = Drought.Ra(50.8, 187);   // FAO-56, 18. példa: Brüsszel, július 6.
                    double et0 = Drought.Et0Pm(21.5, 12.3, 84, 63, double.NaN, 2.078, 22.07, ra, g);
                    Ok(Math.Abs(ra - 41.09) < 0.3 && Math.Abs(et0 - 3.9) < 0.15, "FAO-56 18. példa (Brüsszel): Ra és ET₀", "Ra = " + N(ra, 2) + " (várt 41,09), ET₀ = " + N(et0, 2) + " mm/nap (várt 3,9)");
                    var g2 = new DGlobal { windHeight = 10, roofFactor = 1 };
                    Ok(Math.Abs(Drought.U2(10 / 3.6, g2) - 2.078) < 0.01, "Szél átszámítása 10 m-ről 2 m-re (FAO-56, 47. egyenlet)", N(Drought.U2(10 / 3.6, g2), 3) + " m/s (várt 2,078)");
                    double ro = Drought.Runoff(50, 80, 0.2);
                    Ok(Math.Abs(ro - 13.8) < 0.1 && Drought.Runoff(10, 80, 0.2) == 0, "SCS lefolyás (P = 50 mm, CN = 80)", N(ro, 1) + " mm (várt 13,8); 10 mm-nél 0");
                    var cfg = DConfig.Defaults();
                    var res = Drought.Compute(ds, cfg);
                    var lawn = res.Profiles.First(p => p.P.id == "gyep");
                    bool bounded = lawn.Dr.All(x => x >= 0 && x <= lawn.Taw[0] + 1e-9) && lawn.Ks.All(x => x >= 0 && x <= 1);
                    int jul = res.Dates.IndexOf("2026-07-15");
                    double et0Jul = jul >= 0 ? Enumerable.Range(jul - 14, 30).Average(i => res.Et0[i]) : double.NaN;
                    Ok(bounded && (!sample || (et0Jul > 4 && et0Jul < 7.5)), "Talajvíz-mérleg korlátai, nyári ET₀ ésszerű", "Dr 0..TAW, Ks 0..1" + (sample ? "; júliusi átlag ET₀ = " + N(et0Jul, 2) + " mm/nap" : ""));
                    // a rögzített öntözés csökkenti a vízhiányt
                    var cfg2 = DConfig.Defaults();
                    cfg2.log.Add(new DIrrigation { date = res.Dates[res.Dates.Count - 2], zone = "gyep", unit = "mm", amount = 15 });
                    var res2 = Drought.Compute(ds, cfg2);
                    double d1 = lawn.Dr[lawn.Dr.Length - 1], d2 = res2.Profiles.First(p => p.P.id == "gyep").Dr[lawn.Dr.Length - 1];
                    Ok(d2 < d1, "A rögzített locsolás beszámít a mérlegbe", "vízhiány " + N(d1, 1) + " → " + N(d2, 1) + " mm (15 mm locsolás után)");
                }

                // --- kanadai FWI: Van Wagner & Pickett (1985) referencia-adatsorának első napja (ápr. 13., T 17, RH 42, szél 25, eső 0)
                {
                    double F = Drought.FfmcStep(17, 42, 25, 0, 85), P = Drought.DmcStep(17, 42, 0, 6, 4), Dc = Drought.DcStep(17, 0, 15, 4);
                    double isi = Drought.IsiCalc(F, 25), bui = Drought.BuiCalc(P, Dc), fwi = Drought.FwiCalc(isi, bui);
                    bool okF = Math.Abs(F - 87.7) < 0.15 && Math.Abs(P - 8.5) < 0.15 && Math.Abs(Dc - 19.0) < 0.15 && Math.Abs(isi - 10.9) < 0.2 && Math.Abs(bui - 8.5) < 0.15 && Math.Abs(fwi - 10.1) < 0.2;
                    Ok(okF, "Kanadai FWI referenciapélda (Van Wagner & Pickett 1985)", "FFMC " + N(F, 1) + " DMC " + N(P, 1) + " DC " + N(Dc, 1) + " ISI " + N(isi, 1) + " BUI " + N(bui, 1) + " FWI " + N(fwi, 1) + " (várt 87,7 / 8,5 / 19,0 / 10,9 / 8,5 / 10,1)");
                    var r3 = Drought.Compute(ds, DConfig.Defaults());
                    bool gddOk = r3.GddCum.All(x => x >= 0) && r3.Fwi.All(x => x >= 0 && !double.IsNaN(x));
                    Ok(gddOk && (!sample || r3.GddCum[r3.GddCum.Length - 1] > 1500), "Hőösszeg és FWI a mintaadaton", "GDD (10 °C) " + N(r3.GddCum[r3.GddCum.Length - 1], 0) + " °C·nap, FWI max " + N(r3.Fwi.Max(), 1));
                    var rain = Analysis.RainEvents(ds, true, 30);
                    Ok(rain.Count > 0 && rain.All(e => e.End >= e.Start && e.Total >= 0.2), "Esőesemények felismerése", rain.Count + " esemény, a legnagyobb " + N(rain.Max(e => e.Total), 1) + " mm");

                    // --- 2.1: csapadék-kalibráció (az állomás összege egy napra = a WU napi összesítője)
                    if (sample)
                    {
                        long ta, tb;
                        Calib.ParseLocal("2026-07-17T00:00", out ta); Calib.ParseLocal("2026-07-18T00:00", out tb);
                        double st = Calib.StationSum(ds, ds.RawPtot ?? ds.C[Col.PTOT], ta, tb);
                        var cc = new Calib.Cfg { rows = { new Calib.Row { from = "2026-07-17T00:00", to = "2026-07-18T00:00", manual = 41.91 }, new Calib.Row { from = "2026-08-01T00:00", to = "2026-08-02T00:00", manual = 5 } } };
                        var ev = Calib.Evaluate(ds, cc, s.GapMinutes);
                        Ok(Math.Abs(st - 38.1) < 0.05 && Math.Abs(ev.Global - 1.1) < 0.01 && ev.Rows[1].Error != null && ev.Rows[1].Error.Contains("eldugult"),
                            "Csapadék-kalibráció: állomásösszeg, szorzó, „eldugult?” hibajelzés", "07-17: " + N(st, 2) + " mm (várt 38,1), szorzó " + N(ev.Global, 3) + " (várt 1,100), 08-01: " + (ev.Rows[1].Error ?? "nincs hiba"));
                    }
                    // --- 2.1: időszak-szűrő (több tartomány, nyitott végek)
                    var fl = Analysis.DayFilter("2026-06-01_2026-06-30,_2026-01-05", 0);
                    int d0601 = Tz.DayNumber(new DateTime(2026, 6, 1)), d0701 = Tz.DayNumber(new DateTime(2026, 7, 1)), d0103 = Tz.DayNumber(new DateTime(2026, 1, 3));
                    Ok(fl(d0601) && !fl(d0701) && fl(d0103) && !fl(d0103 + 3), "Időszak-szűrő (több tartomány, nyitott vég)", "jún. 1: " + fl(d0601) + ", júl. 1: " + fl(d0701) + ", jan. 3: " + fl(d0103) + ", jan. 6: " + fl(d0103 + 3));
                    // --- 2.1: öntözési program napjai (H-Sze-P, illetve 3 naponta, évente ismétlődő időszak)
                    var pg = new DProgram { days = "1,3,5" }; var sc = new DSchedule { from = "2026-09-01", to = "2026-09-30" };
                    int on = Enumerable.Range(0, 30).Count(i => DroughtStore.ProgramOn(pg, sc, new DateTime(2026, 9, 1).AddDays(i)));
                    var pg3 = new DProgram { every = 3 }; var scy = new DSchedule { from = "05-01", to = "09-30", yearly = true };
                    int on3 = Enumerable.Range(0, 365).Count(i => DroughtStore.ProgramOn(pg3, scy, new DateTime(2026, 1, 1).AddDays(i)));
                    Ok(on == 13 && on3 == 51, "Öntözési program napjai", "szeptember H-Sze-P: " + on + " (várt 13), 3 naponta máj.–szept.: " + on3 + " (várt 51)");
                    // --- 2.2: febr. 29-i kezdetű éves időszak nem szökőévben sem dob kivételt
                    string err29 = null; int on29 = 0;
                    try { var sc29 = new DSchedule { from = "02-29", to = "03-31", yearly = true }; on29 = Enumerable.Range(0, 365).Count(i => DroughtStore.ProgramOn(pg3, sc29, new DateTime(2026, 1, 1).AddDays(i))); }
                    catch (Exception ex) { err29 = ex.Message; }
                    Ok(err29 == null && on29 > 0, "Febr. 29-i éves időszak nem szökőévben (2.2)", err29 ?? on29 + " öntözési nap");
                    // --- 2.2: csapadékugrás (júl. 17. 29,5 mm / 5 perc, febr. 15. olvadás) és befagyott szélmérő (jan. 7.)
                    if (sample)
                    {
                        int iRate = -1; for (int i = 0; i < ds.N; i++) if ((ds.Flags[i] & Flag.RainRate) == 0 && !float.IsNaN(ds.C[Col.PRATE][i]) && (iRate < 0 || ds.C[Col.PRATE][i] > ds.C[Col.PRATE][iRate])) iRate = i;
                        var feb15 = ds.Days.FirstOrDefault(x => x.Date == new DateTime(2026, 2, 15));
                        var jan7 = ds.Days.FirstOrDefault(x => x.Date == new DateTime(2026, 1, 7));
                        Ok(iRate >= 0 && ds.C[Col.PRATE][iRate] < 100 && feb15 != null && Math.Abs(feb15.Rain - 23.62) < 0.01 && jan7 != null && double.IsNaN(jan7.WindAvg),
                           "Csapadékugrás és befagyott szélmérő jelölése (2.2)", "tiszta intenzitásrekord: " + (iRate >= 0 ? N(ds.C[Col.PRATE][iRate], 0) : "–") + " mm/h (a 177 kimarad), febr. 15. összege marad: " + (feb15 != null ? N(feb15.Rain) : "–") + " mm, jan. 7. szél: " + (jan7 != null ? N(jan7.WindAvg) : "–"));
                    }
                    else Skip("Csapadékugrás és befagyott szélmérő jelölése");
                    // --- 2.2: felhőzet-becslés (napállás, okta-képlet, futás a mintán)
                    long solstice = (long)(new DateTime(2026, 6, 21, 11, 0, 0, DateTimeKind.Utc) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                    double hs = Cloud.SunElevation(solstice, 47.46, 18.96);
                    var cs = new Settings();
                    double o1 = Cloud.OktaFromK(cs.CloudClearK * 1.0, cs), o0 = Cloud.OktaFromK(0.05, cs);
                    var cb = Cloud.Estimate(ds, s, ds.T[ds.N - 1] - 86400, ds.T[ds.N - 1]);
                    int est = cb.Count(x => !double.IsNaN(x.Pct));
                    Ok(Math.Abs(hs - 65.6) < 1 && o1 == 0 && o0 > 7.5 && est > 50, "Felhőzet-becslés (2.2)",
                       "napmagasság jún. 21. 13:00 helyi: " + N(hs, 1) + "° (várt ~65,6), okta k_derültnél: " + N(o1, 1) + ", nagyon gyenge sugárzásnál: " + N(o0, 1) + ", becsült 10 perces lépés az utolsó napon: " + est);
                    // --- 2.2: hibás program javításkor csak az az egy esik ki
                    var rc = DConfig.Defaults();
                    rc.programs.Add(new DProgram { id = "jo", name = "Jó", zone = rc.profiles[0].id, days = "1", unit = "mm", amount = 5 });
                    rc.programs.Add(new DProgram { id = "rossz", name = "Rossz", zone = "nincs-ilyen", days = "1", unit = "mm", amount = 5 });
                    rc.schedules.Add(new DSchedule { id = "s1", program = "jo", from = "2026-05-01", to = "2026-05-31" });
                    rc.schedules.Add(new DSchedule { id = "s2", program = "rossz", from = "2026-05-01", to = "2026-05-31" });
                    DroughtStore.Repair(rc);
                    Ok(rc.programs.Count == 1 && rc.programs[0].id == "jo" && rc.schedules.Count == 1, "Javításkor csak a hibás öntözési program esik ki (2.2)", rc.programs.Count + " program, " + rc.schedules.Count + " időszak marad (várt: 1, 1)");
                    // --- 2.1: gyanús-mérés szabályok kapcsolói
                    var s2x = Settings.FromDict(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "JelolWuBizonytalan", "false" }, { "JelolNapsutes", "false" } }, a, new Settings());
                    int rowsAll, rowsLess;
                    var cAll = Loader.FlagPreview(ds, s, out rowsAll); var cLess = Loader.FlagPreview(ds, s2x, out rowsLess);
                    Ok(cLess[0] == 0 && cLess[9] == 0 && rowsLess < rowsAll && (!sample || rowsAll == SampleFlagged), "Gyanús-mérés szabályok kapcsolhatók", "mind: " + rowsAll + ", qc0 és napsütés nélkül: " + rowsLess);
                }

                // --- 2.1: nyelvi fájlok (a beágyazott web/lang/*.json) és az írás-őr
                var codes = Langs.Codes().ToList();
                Ok(codes.Contains("en") && codes.Contains("de") && codes.Contains("es") && Langs.T("Csapadék", "en") == "Rain" && Langs.T("Fagyveszély", "de") == "Frostgefahr",
                    "Nyelvi fájlok betöltése és fordítás", string.Join(", ", codes) + "; Csapadék → " + Langs.T("Csapadék", "en") + ", Fagyveszély → " + Langs.T("Fagyveszély", "de"));
                bool denied = false;
                try { Guard.WriteAllText(Path.Combine(Path.GetTempPath(), "idojaras_guard_teszt.txt"), "x"); } catch (UnauthorizedAccessException) { denied = true; }
                Ok(denied && Guard.Inside(Path.Combine(tmp, "x.txt")), "Írás-őr: a programmappán kívülre nem ír", denied ? "a mappán kívüli írás megtagadva" : "NEM tagadta meg!");

                // --- a "Ma" és a meta végpont nem dob hibát
                int s1, s2;
                string m = Api.Handle("/api/meta", new NameValueCollection(), "GET", out s1);
                string td = Api.Handle("/api/today", new NameValueCollection(), "GET", out s2);
                Ok(s1 == 200 && s2 == 200 && td.Contains("\"now\""), "A meta és a Ma végpont működik", m.Length + " + " + td.Length + " bájt");

                // --- biztonsági javítások (2.2.7.1)
                bool injOk = false, keyOk = false;
                try { Guard.IniLine("CsendesIg", "7\r\nApiBaseUrl=http://pelda.invalid"); } catch (ArgumentException) { injOk = true; }
                try { Guard.IniLine("A=B", "1"); } catch (ArgumentException) { keyOk = true; }
                Ok(injOk && keyOk && Guard.IniLine("CsendesIg", "7") == "CsendesIg=7", "Ini-írás: sortörés és „=” a kulcsban elutasítva (2.2.7.1)");

                if (!File.Exists(a.DlIni)) File.WriteAllLines(a.DlIni, new[] { "CsendesIg=7" });
                int sa1, sa2; string ar = Api.Handle("/api/alerts", new NameValueCollection(), "POST", "{\"CsendesIg\":\"7\\nApiBaseUrl=x\"}", out sa1);
                Api.Handle("/api/alerts", new NameValueCollection(), "POST", "{\"CsendesIg\":\"6\",\"FagyHatar\":\"-2.5\"}", out sa2);
                var al = Settings.ReadIni(a.DlIni); string q1, q2;
                al.TryGetValue("CsendesIg", out q1); al.TryGetValue("FagyHatar", out q2);
                Ok(sa1 == 400 && sa2 == 200 && q1 == "6" && q2 == "-2.5" && !File.ReadAllText(a.DlIni).Contains("ApiBaseUrl=x"),
                   "Riasztás-beállítás: hibás érték elutasítva, helyes elmentve (2.2.7.1)", sa1 + " / " + sa2 + " / " + q1 + " / " + q2);

                var js = new System.Web.Script.Serialization.JavaScriptSerializer();
                int drop1, drop2;
                var n1 = JsonStore.Clean("naplo", (object[])js.DeserializeObject("[{\"id\":\"d1\",\"date\":\"2026-10-10\",\"text\":\"metszés\",\"amount\":2}," +
                    "{\"id\":\"d2\",\"date\":\"\\\"><img src=x>\",\"text\":\"x\"},{\"date\":\"2026-10-11\",\"amount\":\"5\"},{\"id\":\"a b\",\"date\":\"2026-10-12\"}]"), out drop1);
                var n2 = JsonStore.Clean("futas", (object[])js.DeserializeObject("[{\"id\":1,\"t\":1792000000000,\"who\":\"A\",\"intensity\":5},{\"id\":2,\"t\":1792000000000,\"intensity\":\"<b>\"},{\"who\":\"C\"}]"), out drop2);
                Ok(n1.Count == 1 && drop1 == 3 && n2.Count == 1 && drop2 == 2, "Napló / futásnapló: hibás tételek kiszűrve (2.2.7.1)", "napló " + n1.Count + "/" + (n1.Count + drop1) + ", futás " + n2.Count + "/" + (n2.Count + drop2));

                Ok(BackupTools.Target(Layout.AppName + "/presetek/x.exe") == null && BackupTools.Target(Layout.AppName + "/presetek/talaj__a.json") != null
                   && BackupTools.Target("data/../x.csv") == null && BackupTools.Target("data/idojaras_adatok.csv") != null && BackupTools.Target("x.exe") == null,
                   "Visszaállítás: csak engedélyezett fájlok (presetek közé csak .json) (2.2.7.1)");

                string cur = Path.Combine(tmp, "jelenlegi.ini"), bak = Path.Combine(tmp, "mentesbol.ini");
                File.WriteAllLines(cur, new[] { "ApiKey=JELENLEGI", "StationId=ALLOMAS1", "MentesNaponta=7" });
                File.WriteAllLines(bak, new[] { "ApiKey=REGI", "StationId=ALLOMAS2", "ApiBaseUrl=http://pelda.invalid", "MentesNaponta=3" });
                BackupTools.KeepSecrets(bak, cur);
                var kept = Settings.ReadIni(bak); string v1, v2, v3, v4;
                kept.TryGetValue("ApiKey", out v1); kept.TryGetValue("StationId", out v2); kept.TryGetValue("ApiBaseUrl", out v3); kept.TryGetValue("MentesNaponta", out v4);
                Ok(v1 == "JELENLEGI" && v2 == "ALLOMAS1" && v3 == null && v4 == "3", "Visszaállítás: a mostani API-kulcs és állomás marad, idegen ApiBaseUrl nem jön át (2.2.7.1)", v1 + " / " + v2 + " / " + (v3 ?? "–") + " / " + v4);
            }
            catch (Exception ex)
            {
                Ok(false, "Váratlan hiba a teszt közben", ex.ToString());
            }
            finally
            {
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }
            return Finish(src);
        }

        static double RainOn(Dataset ds, int y, int mo, int d)
        {
            var day = ds.DaysRaw.FirstOrDefault(x => x.Date == new DateTime(y, mo, d));
            return day == null ? double.NaN : day.Rain;
        }

        static Dataset Synthetic(double[] tot)
        {
            int n = tot.Length;
            var ds = new Dataset { N = n, T = new long[n], Qc = new sbyte[n], C = new float[Col.Count][], Flags = new int[n], Day = new int[n], Hour = new byte[n] };
            for (int c = 0; c < Col.Count; c++) ds.C[c] = Enumerable.Repeat(float.NaN, n).ToArray();
            long t0 = Tz.LocalMidnightEpoch(new DateTime(2026, 6, 1)) + 3600;
            for (int i = 0; i < n; i++)
            {
                ds.T[i] = t0 + i * 300; ds.Qc[i] = 1; ds.C[Col.PTOT][i] = (float)tot[i];
                ds.Day[i] = Tz.DayNumber(Tz.Local(ds.T[i]));
            }
            return ds;
        }

        static void CompareDaily(Dataset ds, string path)
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            var h = lines[0].TrimStart('﻿').Split(';').ToList();
            int iD = h.IndexOf("datum"), iTx = h.IndexOf("tempHigh"), iTn = h.IndexOf("tempLow"), iR = h.IndexOf("precipTotal"), iG = h.IndexOf("windgustHigh");
            string last = Tz.Local(ds.T[ds.N - 1]).ToString("yyyy-MM-dd", Inv);
            int cmp = 0, bad = 0;
            var notes = new List<string>();
            for (int i = 1; i < lines.Length; i++)
            {
                var f = lines[i].Split(';');
                if (f.Length <= iR || f[iD] == last) continue; // a mai nap még tart
                DateTime date;
                if (!DateTime.TryParseExact(f[iD], "yyyy-MM-dd", Inv, DateTimeStyles.None, out date)) continue;
                var d = ds.DaysRaw.FirstOrDefault(x => x.Date == date);
                if (d == null) continue;
                Func<string, double> P = x => double.Parse(x.Replace(',', '.'), Inv);
                double tx = P(f[iTx]), tn = P(f[iTn]), r = P(f[iR]), g = P(f[iG]);
                cmp++;
                bool good = Math.Abs(tx - d.Tmax) <= 0.15 && Math.Abs(tn - d.Tmin) <= 0.15 && Math.Abs(r - d.Rain) <= 0.05 && Math.Abs(g - d.GustMax) <= 0.15;
                if (!good) { bad++; notes.Add(f[iD] + ": WU max/min/csap/lökés " + tx + "/" + tn + "/" + r + "/" + g + " vs " + N(d.Tmax, 1) + "/" + N(d.Tmin, 1) + "/" + N(d.Rain) + "/" + N(d.GustMax, 1)); }
            }
            Ok(cmp > 0 && bad == 0, "Napi min/max/csapadék/lökés egyezik a WU napi összesítőjével", cmp + " nap összevetve" + (notes.Count > 0 ? "; " + string.Join("; ", notes) : ""));
        }

        static int Finish(Layout src)
        {
            Line("");
            Line("Összesen: " + ok + " rendben, " + fail + " hiba, " + skip + " kihagyva.");
            try { Directory.CreateDirectory(src.App); File.WriteAllText(Path.Combine(src.App, "teszt_eredmeny.txt"), log.ToString(), new UTF8Encoding(true)); } catch { }
            return fail == 0 ? 0 : 1;
        }
    }
}
