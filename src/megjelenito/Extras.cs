using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Idojaras
{
    // A felület beállításai (téma, fülek, heti kártyák stb.) – appdata_megjelenítő\felulet.json
    static class UiSettings
    {
        static readonly object Lock = new object();
        static string FilePath { get { return Path.Combine(AppState.L.App, "felulet.json"); } }

        public static string Handle(string method, string body, out int status)
        {
            status = 200;
            lock (Lock)
            {
                if (method == "POST")
                {
                    try { new JavaScriptSerializer().DeserializeObject(body ?? ""); }
                    catch { status = 400; return "{\"error\":\"érvénytelen JSON\"}"; }
                    Guard.CreateDirectory(AppState.L.App);
                    string tmp = FilePath + ".tmp";
                    Guard.WriteAllText(tmp, body, new UTF8Encoding(false));
                    if (File.Exists(FilePath)) Guard.Replace(tmp, FilePath, null); else Guard.Move(tmp, FilePath);
                    return "{\"ok\":true}";
                }
                try
                {
                    if (File.Exists(FilePath))
                    {
                        string t = File.ReadAllText(FilePath, Encoding.UTF8);
                        new JavaScriptSerializer().DeserializeObject(t);
                        return t;
                    }
                }
                catch (Exception ex) { Program.Log("A felulet.json sérült, alapértékekkel indulok: " + ex.Message); }
                return "{}";
            }
        }

        // a felület nyelve (az exporthoz)
        public static string Lang()
        {
            try
            {
                int st;
                var d = new JavaScriptSerializer().DeserializeObject(Handle("GET", null, out st)) as System.Collections.Generic.Dictionary<string, object>;
                object v;
                if (d != null && d.TryGetValue("lang", out v) && v != null && Langs.Exists(Convert.ToString(v))) return Convert.ToString(v);
            }
            catch { }
            return "hu";
        }

        // a választott mértékegységek (units.temp/rain/wind/pres) – az exporthoz
        public static string Unit(string key, string def)
        {
            try
            {
                int st;
                var d = new JavaScriptSerializer().DeserializeObject(Handle("GET", null, out st)) as System.Collections.Generic.Dictionary<string, object>;
                object u;
                if (d != null && d.TryGetValue("units", out u) && u is System.Collections.Generic.Dictionary<string, object>)
                {
                    object v;
                    if (((System.Collections.Generic.Dictionary<string, object>)u).TryGetValue(key, out v) && v != null) return Convert.ToString(v);
                }
                if (key == "wind" && d != null && d.ContainsKey("windUnit")) return Convert.ToString(d["windUnit"]);
            }
            catch { }
            return def;
        }
    }

    // Egyszerű Excel-export: napi összesítő CSV a felület nyelvén és számformátumával (9.9)
    static class Export
    {
        static readonly CultureInfo Hu = CultureInfo.GetCultureInfo("hu-HU");

        public static string FileName()
        {
            return "idojaras_napi_osszesito_" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv";
        }

        static CultureInfo Ci = Hu;
        static string[] Dirs = { "É", "ÉÉK", "ÉK", "KÉK", "K", "KDK", "DK", "DDK", "D", "DDNy", "DNy", "NyDNy", "Ny", "NyÉNy", "ÉNy", "ÉÉNy" };

        static string N(double v, int dec)
        {
            return double.IsNaN(v) ? "" : Math.Round(v, dec).ToString(dec > 0 ? "0." + new string('0', dec) : "0", Ci);
        }

        static string Dir(double deg)
        {
            if (double.IsNaN(deg)) return "";
            return Dirs[(int)Math.Round(((deg % 360) + 360) % 360 / 22.5) % 16];
        }

        // a nyelv számformátuma és égtájai; tizedesvesszőnél „;”, tizedespontnál „,” az elválasztó (ahogy az adott nyelvű Excel várja)
        static string SetLang(string lang)
        {
            Ci = Hu;
            Dirs = new[] { "É", "ÉÉK", "ÉK", "KÉK", "K", "KDK", "DK", "DDK", "D", "DDNy", "DNy", "NyDNy", "Ny", "NyÉNy", "ÉNy", "ÉÉNy" };
            var m = Langs.Meta(lang);
            if (m != null)
            {
                object l, dirs;
                if (m.TryGetValue("locale", out l)) { try { Ci = CultureInfo.GetCultureInfo(Convert.ToString(l)); } catch { Ci = Hu; } }
                if (m.TryGetValue("dirs", out dirs) && dirs is object[] && ((object[])dirs).Length == 16) Dirs = ((object[])dirs).Select(x => Convert.ToString(x)).ToArray();
            }
            return Ci.NumberFormat.NumberDecimalSeparator == "," ? ";" : ",";
        }

        public static string DailyCsv(Dataset ds, bool raw = false)
        {
            var sb = new StringBuilder();
            if (ds == null) return "";
            string lang = UiSettings.Lang(), sep = SetLang(lang);
            Func<string, string> L = h => Langs.T(h, lang);
            // a felületen választott mértékegységekben (belül minden metrikus)
            bool f = UiSettings.Unit("temp", "C") == "F", inch = UiSettings.Unit("rain", "mm") == "in", inHg = UiSettings.Unit("pres", "hPa") == "inHg";
            string wu = UiSettings.Unit("wind", "kmh");
            string tl = f ? "°F" : "°C", rl = inch ? "in" : "mm", pl = inHg ? "inHg" : "hPa";
            string wl = wu == "kn" ? L("csomó") : wu == "ms" ? "m/s" : wu == "mph" ? "mph" : wu == "bft" ? "Bft" : "km/h";
            Func<double, double> T = c => f ? c * 9 / 5 + 32 : c;
            Func<double, double> TD = c => f ? c * 9 / 5 : c;
            Func<double, double> R = mm => inch ? mm / 25.4 : mm;
            Func<double, double> P = h => inHg ? h * 0.02953 : h;
            Func<double, double> W = k => double.IsNaN(k) ? k : wu == "kn" ? k / 1.852 : wu == "ms" ? k / 3.6 : wu == "mph" ? k / 1.609344 : wu == "bft" ? Math.Min(12, Math.Pow(Math.Max(0, k / 3.6) / 0.836, 2.0 / 3)) : k;
            int rd = inch ? 1 : 0, pd = inHg ? 2 : 1, wd = wu == "bft" ? 2 : 1;
            string[] head =
            {
                "Dátum", "Hét napja", "Lefedettség (%)", "Mérések", "Min. hőmérséklet (" + tl + ")", "Min. időpont", "Max. hőmérséklet (" + tl + ")", "Max. időpont",
                "Átlaghőmérséklet (" + tl + ")", "Hőingás (" + tl + ")", "Átl. harmatpont (" + tl + ")", "Min. pára (%)", "Max. pára (%)", "Átl. pára (%)",
                "Min. légnyomás (" + pl + ")", "Max. légnyomás (" + pl + ")", "Átl. légnyomás (" + pl + ")", "Átlagszél (" + wl + ")", "Max. szél (" + wl + ")", "Max. lökés (" + wl + ")",
                "Lökés időpontja", "Uralkodó szélirány", "Szélirány (°)", "Csapadék (" + rl + ")", "Max. intenzitás (" + rl + "/h)", "Napsütéses órák (h)",
                "Napenergia (kWh/m²)", "Max. napsugárzás (W/m²)", "Max. UV"
            };
            sb.Append(string.Join(sep, head.Select(h => L(h)))).Append("\r\n");
            Func<long, string> time = t => t == 0 ? "" : Tz.Local(t).ToString("HH:mm");
            // alapból a tisztított adat (a gyanús mérések nélkül), kérésre a nyers
            foreach (var d in raw ? ds.DaysRaw : ds.Days)
            {
                var row = new[]
                {
                    d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Ci.DateTimeFormat.GetDayName(d.Date.DayOfWeek), N(d.Coverage * 100, 0), d.Count.ToString(),
                    N(T(d.Tmin), 1), time(d.TminT), N(T(d.Tmax), 1), time(d.TmaxT), N(T(d.Tavg), 1), N(TD(d.Tmax - d.Tmin), 1), N(T(d.DewAvg), 1),
                    N(d.HumMin, 0), N(d.HumMax, 0), N(d.HumAvg, 0), N(P(d.PresMin), pd), N(P(d.PresMax), pd), N(P(d.PresAvg), pd),
                    N(W(d.WindAvg), wd), N(W(d.WindMax), 1), N(W(d.GustMax), 1), time(d.GustMaxT), Dir(d.WindDir), N(d.WindDir, 0),
                    N(R(d.Rain), 2 + rd), N(R(d.RainRateMax), 1 + rd), N(d.SunHours, 1), N(d.Energy, 2), N(d.SolarMax, 0), N(d.UvMax, 0)
                };
                sb.Append(string.Join(sep, row)).Append("\r\n");
            }
            return sb.ToString();
        }

        // ablakos módban mentés-párbeszédablakkal; böngészős módban a felület letöltésként kéri le
        public static string SaveWithDialog(Dataset ds, bool raw = false)
        {
            var win = Program.Window;
            if (win == null || win.IsDisposed) return "{\"download\":true}";
            string csv = DailyCsv(ds, raw);
            string saved = null, error = null;
            win.Invoke((Action)(() =>
            {
                using (var d = new SaveFileDialog
                {
                    Title = "Napi összesítő mentése (Excelhez)",
                    Filter = "CSV (Excel) (*.csv)|*.csv",
                    FileName = FileName(),
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                })
                {
                    if (d.ShowDialog(win) != DialogResult.OK) return;
                    try { File.WriteAllText(d.FileName, csv, new UTF8Encoding(true)); saved = d.FileName; } // kivétel: a felhasználó maga választotta a helyet
                    catch (Exception ex) { error = ex.Message; }
                }
            }));
            var w = new JW().Obj();
            if (saved != null) w.P("saved", true).P("path", saved).P("rows", ds.Days.Count);
            else if (error != null) w.P("saved", false).P("error", "A fájl nem menthető: " + error);
            else w.P("saved", false).P("cancelled", true);
            return w.End().ToString();
        }
    }

    // Biztonsági mentés másolása tetszőleges helyre és visszaállítás egy zipből (13.2).
    // A célt / a forrást a felhasználó választja ki (mint az Excel-exportnál), a visszaállítás csak a programmappába ír.
    static class BackupTools
    {
        public static string Dir { get { return Path.Combine(AppState.L.DlApp, "mentesek"); } }
        public static FileInfo Latest()
        {
            return Directory.Exists(Dir) ? new DirectoryInfo(Dir).GetFiles("mentes_*.zip").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault() : null;
        }

        public static string CopyLatest()
        {
            var z = Latest();
            if (z == null) return "{\"error\":\"Még nincs biztonsági mentés – előbb készíts egyet.\"}";
            var win = Program.Window;
            if (win == null || win.IsDisposed) return "{\"download\":true}";
            string saved = null, error = null;
            win.Invoke((Action)(() =>
            {
                using (var d = new SaveFileDialog { Title = "Biztonsági mentés másolása (pl. pendrive-ra vagy OneDrive-ba)", Filter = "Zip (*.zip)|*.zip", FileName = z.Name })
                {
                    if (d.ShowDialog(win) != DialogResult.OK) return;
                    try { File.Copy(z.FullName, d.FileName, true); saved = d.FileName; }   // kivétel az őr alól: a felhasználó választotta a helyet
                    catch (Exception ex) { error = ex.Message; }
                }
            }));
            var w = new JW().Obj();
            if (saved != null) { w.P("saved", true).P("path", saved); ChangeLog.Add("Biztonsági mentés", "másolat", null, saved, null); }
            else if (error != null) w.P("error", "A másolat nem készült el: " + error);
            else w.P("cancelled", true);
            return w.End().ToString();
        }

        // a zip melyik bejegyzése hova kerülhet vissza (más nem: se mappán kívülre, se programfájl)
        internal static string Target(string entry)
        {
            string e = entry.Replace('\\', '/');
            if (e.Contains("..") || e.StartsWith("/") || e.Contains(":")) return null;
            var parts = e.Split('/');
            if (parts.Length == 2 && parts[0] == "data" && e.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return Path.Combine(AppState.L.Data, parts[1]);
            bool cfg = e.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) || e.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            if (parts.Length == 2 && parts[0] == Layout.DlAppName && cfg) return Path.Combine(AppState.L.DlApp, parts[1]);
            if (parts.Length == 2 && parts[0] == Layout.AppName && cfg) return Path.Combine(AppState.L.App, parts[1]);
            // a presetek közé csak .json kerülhet (más, pl. .exe nem – 2.2.7.1)
            if (parts.Length == 3 && parts[0] == Layout.AppName && parts[1] == "presetek" && parts[2].Length > 5 && parts[2].EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return Path.Combine(AppState.L.App, "presetek", parts[2]);
            return null;
        }

        public static string Restore()
        {
            var win = Program.Window;
            if (win == null || win.IsDisposed) return "{\"error\":\"A visszaállítás a program ablakában érhető el (böngészős módban nem).\"}";
            string src = null;
            win.Invoke((Action)(() =>
            {
                using (var d = new OpenFileDialog { Title = "Visszaállítás biztonsági mentésből", Filter = "WStation biztonsági mentés (*.zip)|*.zip", InitialDirectory = Directory.Exists(Dir) ? Dir : AppState.L.Base })
                    if (d.ShowDialog(win) == DialogResult.OK) src = d.FileName;
            }));
            if (src == null) return "{\"cancelled\":true}";
            // munkapéldányból dolgozunk: a visszaállítás előtti mentés a mentesek mappa legrégebbi zipjét törölheti,
            // ami épp a kiválasztott fájl is lehet
            string work = Path.Combine(AppState.L.App, "visszaallitas_munka.zip");
            // közben ne induljon el az ütemezett Letöltő (ugyanazokat a fájlokat írná); a végén visszakapcsoljuk
            bool paused = false;
            var plan = new List<string[]>();   // [cél, ideiglenes, régi példány]
            try
            {
                if (DownloaderRunning()) return "{\"error\":\"Épp fut egy letöltés. Várd meg a végét, majd próbáld újra.\"}";
                Guard.CreateDirectory(AppState.L.App);
                Guard.Copy(src, work, true);
                using (var zfs = File.OpenRead(work))
                using (var zip = new System.IO.Compression.ZipArchive(zfs, System.IO.Compression.ZipArchiveMode.Read))
                {
                    var items = zip.Entries.Where(en => en.Length > 0).Select(en => new { en, t = Target(en.FullName) }).Where(x => x.t != null).ToList();
                    if (!items.Any(x => x.t.StartsWith(AppState.L.Data, StringComparison.OrdinalIgnoreCase)))
                        return "{\"error\":\"Ez nem a WStation biztonsági mentése (nincs benne mérési adat).\"}";
                    // méretkorlát: egy hibás vagy szándékosan felfújt zip ne töltse meg a lemezt
                    if (items.Any(x => x.en.Length > MaxEntry) || items.Sum(x => x.en.Length) > MaxTotal)
                        return "{\"error\":\"A mentésben túl nagy fájl van – ez nem egy WStation-mentés.\"}";
                    // előbb a mostani állapotról is mentés készül, hogy a visszaállítás visszavonható legyen
                    string exe = AppState.L.FindDownloader();
                    if (exe != null)
                        using (var p = Process.Start(new ProcessStartInfo(exe, "--mentes --most") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppState.L.Base }))
                            if (p != null && !p.WaitForExit(600000)) { try { p.Kill(); } catch { } }   // nagy adatnál percekig tarthat
                    paused = Kozos.Utemezes.Pause(true);
                    // 1. kibontás ideiglenes fájlokba (a mostani fájlokhoz még nem nyúlunk)
                    long total = 0;
                    foreach (var x in items)
                    {
                        Guard.CreateDirectory(Path.GetDirectoryName(x.t));
                        string tmp = x.t + ".visszaallit";
                        plan.Add(new[] { x.t, tmp, null });
                        using (var es = x.en.Open()) using (var fs = Guard.Create(tmp))
                        {
                            var buf = new byte[81920]; int k;
                            while ((k = es.Read(buf, 0, buf.Length)) > 0)
                            {
                                total += k;
                                if (total > MaxTotal) throw new IOException("a mentés kibontva túl nagy");
                                fs.Write(buf, 0, k);
                            }
                        }
                        // a Letöltő beállításfájljából az API-kulcs és az állomás a mostani marad, az ApiBaseUrl nem jön át
                        if (x.t.Equals(AppState.L.DlIni, StringComparison.OrdinalIgnoreCase)) KeepSecrets(tmp, x.t);
                        // a kerti napló és a futónapló hibás tételei már visszaállításkor kimaradnak (2.2.8)
                        string bn = Path.GetFileName(x.t).ToLowerInvariant();
                        if (bn == "naplo.json" || bn == "futas.json") CleanStore(tmp, bn.Substring(0, bn.Length - 5));
                    }
                    // 2. csere egyenként, a régi példány megtartásával; hiba esetén minden visszaáll
                    try
                    {
                        foreach (var pl in plan)
                        {
                            if (File.Exists(pl[0])) { pl[2] = pl[0] + ".visszaallit_regi"; if (File.Exists(pl[2])) Guard.Delete(pl[2]); Guard.Replace(pl[1], pl[0], pl[2]); }
                            else Guard.Move(pl[1], pl[0]);
                            pl[1] = null;   // kész
                        }
                    }
                    catch
                    {
                        for (int i = plan.Count - 1; i >= 0; i--)
                        {
                            var pl = plan[i];
                            if (pl[1] != null) continue;   // ez még nem cserélődött
                            try { if (pl[2] != null) Guard.Replace(pl[2], pl[0], null); else Guard.Delete(pl[0]); } catch { }
                        }
                        throw;
                    }
                    foreach (var pl in plan) if (pl[2] != null) try { Guard.Delete(pl[2]); } catch { }
                }
                ChangeLog.Add("Biztonsági mentés", "visszaállítás", null, Path.GetFileName(src) + " (" + plan.Count + " fájl)", null);
                AppState.RefreshAsync(false);
                return new JW().Obj().P("restored", plan.Count).P("file", Path.GetFileName(src)).End().ToString();
            }
            catch (Exception ex) { return new JW().Obj().P("error", "A visszaállítás nem sikerült, a mostani állapot megmaradt: " + ex.Message).End().ToString(); }
            finally
            {
                foreach (var pl in plan) if (pl[1] != null) try { if (File.Exists(pl[1])) Guard.Delete(pl[1]); } catch { }
                if (paused) Kozos.Utemezes.Pause(false);
                try { if (File.Exists(work)) Guard.Delete(work); } catch { }
            }
        }

        const long MaxEntry = 500L * 1024 * 1024, MaxTotal = 2048L * 1024 * 1024;

        // fut-e most a Letöltő ebből a mappából (pl. az ütemezett feladat)
        static bool DownloaderRunning()
        {
            foreach (var n in Layout.DownloaderNames)
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(n)))
                {
                    try { if (string.Equals(Path.GetDirectoryName(p.MainModule.FileName), AppState.L.Base, StringComparison.OrdinalIgnoreCase)) return true; }
                    catch { }
                }
            return false;
        }

        // a visszaállított beallitasok.ini-ben a mostani ApiKey / StationId marad (ha van), az ApiBaseUrl a mostani (vagy semmi)
        // egy visszaállított napló / futónapló tételenkénti ellenőrzése (ugyanaz a szabály, mint betöltéskor);
        // ha nem lista vagy nem olvasható, üres listát hagy (a betöltés is így tenne)
        internal static void CleanStore(string tmp, string name)
        {
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            object[] arr;
            try { arr = js.DeserializeObject(File.ReadAllText(tmp, Encoding.UTF8)) as object[]; } catch { arr = null; }
            int dropped;
            var clean = JsonStore.Clean(name, arr ?? new object[0], out dropped);
            if (arr != null && dropped == 0) return;
            Guard.WriteAllText(tmp, js.Serialize(clean), new UTF8Encoding(false));
            Program.Log("Visszaállítás: a " + name + ".json " + (arr == null ? "nem volt érvényes lista" : dropped + " hibás tétele kimaradt") + ".");
        }

        internal static void KeepSecrets(string tmp, string current)
        {
            var cur = File.Exists(current) ? Settings.ReadIni(current) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var keep = new[] { "ApiKey", "StationId", "ApiBaseUrl" };
            var lines = File.ReadAllLines(tmp, Encoding.UTF8).Where(l =>
            {
                string t = l.TrimStart(); int eq = t.IndexOf('=');
                if (eq <= 0) return true;
                string k = t.Substring(0, eq).Trim();
                return !(k.Equals("ApiBaseUrl", StringComparison.OrdinalIgnoreCase) || ((k.Equals("ApiKey", StringComparison.OrdinalIgnoreCase) || k.Equals("StationId", StringComparison.OrdinalIgnoreCase)) && HasValue(cur, k)));
            }).ToList();
            foreach (var k in keep) if (HasValue(cur, k)) lines.Add(Guard.IniLine(k, cur[k]));
            Guard.WriteAllLines(tmp, lines, new UTF8Encoding(true));
        }
        static bool HasValue(Dictionary<string, string> d, string k) { string v; return d.TryGetValue(k, out v) && !string.IsNullOrWhiteSpace(v); }
    }
}
