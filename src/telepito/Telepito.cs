// WStation – telepítő
// Abba a mappába telepít, ahonnan elindították (<ott>\Időjárás\), vagy helyben frissít, ha egy telepítésen belül indul.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Kozos;

[assembly: AssemblyTitle("WStation – telepítő")]
[assembly: AssemblyProduct("WStation")]
[assembly: AssemblyVersion("2.2.4.0")]
[assembly: AssemblyFileVersion("2.2.4.0")]

namespace Telepito
{
    static class Names
    {
        public const string Root = "WStation";
        public const string LegacyRoot = "Időjárás";                  // 2.2.1-ig ez volt az alapmappa neve
        public const string LegacyShortcut = "Budaörsi időjárás.lnk"; // 2.2.1-ig ez volt a parancsikon neve
        // a programfájlok neve a telepítő nyelvén (TERV_4 2. pont: pl. angolul Viewer.exe / Downloader.exe)
        public static string Downloader { get { return Langs.FileName("downloader", Langs.Current); } }
        public static string Viewer { get { return Langs.FileName("viewer", Langs.Current); } }
        public const string Data = "data";
        public const string DlApp = "appdata_letöltő";
        public const string ViewApp = "appdata_megjelenítő";
        public const string Shortcut = "WStation.lnk";
        public const string Changelog = "Változások.txt";

        // beágyazott erőforrás -> fájlnév a telepített mappában (a programok neve nyelvfüggő)
        public static string[][] Files
        {
            get
            {
                return new[]
                {
                    new[] { "bin/Letolto.exe", Downloader },
                    new[] { "bin/Megjelenito.exe", Viewer },
                    new[] { "docs/OLVASS_EL.txt", "OLVASS_EL.txt" },
                    new[] { "docs/Letolto_leiras.txt", "Letöltő – leírás.txt" },
                    new[] { "docs/Megjelenito_leiras.txt", "Megjelenítő – leírás.txt" },
                    new[] { "docs/VALTOZASOK.txt", Changelog },
                };
            }
        }

        // az első meglévő program a mappában, bármelyik nyelvi néven (null, ha nincs)
        public static string FindExe(string dir, string kind)
        {
            foreach (var n in Langs.AllFileNames(kind)) { string p = Path.Combine(dir, n); if (File.Exists(p)) return p; }
            return null;
        }

        // korábbi verziók gyökérben hagyott fájljai -> hova kerülnek
        public static readonly string[] OldDownloaderFiles = { "WU_Letolto.exe", "OLVASS_EL.md", "Letolto.exe" };
        public static readonly string[] OldViewerFiles = { "Idojaras.exe", "Megjelenito.exe", "OLVASS_EL_megjelenito.md", "Microsoft.Web.WebView2.Core.dll",
                                                           "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll", "WebView2_LICENSE.txt" };
        static readonly string[] LegacyProcessNames = { "Megjelenito", "Letolto", "Idojaras", "WU_Letolto" };
        public static IEnumerable<string> ProcessNames
        {
            get { return Langs.AllFileNames("viewer").Concat(Langs.AllFileNames("downloader")).Select(Path.GetFileNameWithoutExtension).Concat(LegacyProcessNames).Distinct(StringComparer.OrdinalIgnoreCase); }
        }
    }

    enum Found { Empty, Current, Older, Newer, OldLayout }

    class Survey
    {
        public Found Kind;
        public Version Installed;     // null, ha nincs (vagy nem állapítható meg)
        public bool HasData, HasApiKey;
        public int ScheduleMinutes;   // a meglévő ütemezés
        public string Text = "";
    }

    class Result
    {
        public bool Ok, Updated, HasApiKey;
        public string Target, Error, ScheduleMessage;
        public Version From;
        public List<string> Log = new List<string>();
    }

    static class Installer
    {
        public static readonly Version Me = Assembly.GetExecutingAssembly().GetName().Version;

        public static bool IsInstallDir(string d)
        {
            try
            {
                return Names.FindExe(d, "viewer") != null || Names.FindExe(d, "downloader") != null
                    || (Directory.Exists(Path.Combine(d, Names.Data)) && Directory.Exists(Path.Combine(d, Names.DlApp)))
                    || (File.Exists(Path.Combine(d, "Idojaras.exe")) && File.Exists(Path.Combine(d, "WebView2Loader.dll")));
            }
            catch { return false; }
        }

        static Version ExeVersion(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var v = FileVersionInfo.GetVersionInfo(path);
                return new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart, v.FilePrivatePart);
            }
            catch { return null; }
        }

        static bool IsObsCsv(string f)
        {
            try
            {
                using (var sr = new StreamReader(f, Encoding.UTF8, true))
                {
                    string h = sr.ReadLine() ?? "";
                    return h.Contains("stationID") && (h.Contains("epoch") || h.Contains("datum"));
                }
            }
            catch { return false; }
        }

        public static Survey Look(string target)
        {
            var s = new Survey();
            if (!Directory.Exists(target)) { s.Kind = Found.Empty; s.Text = "Új telepítés: létrejön az „" + Path.GetFileName(target) + "” mappa a programokkal, leírásokkal és a data, " + Names.DlApp + ", " + Names.ViewApp + " mappákkal."; }
            else
            {
                var vv = ExeVersion(Names.FindExe(target, "viewer") ?? Path.Combine(target, Names.Viewer));
                var vd = ExeVersion(Names.FindExe(target, "downloader") ?? Path.Combine(target, Names.Downloader));
                var v1 = ExeVersion(Path.Combine(target, "Idojaras.exe"));
                s.Installed = new[] { vv, vd, v1 }.Where(v => v != null).OrderByDescending(v => v).FirstOrDefault();
                bool rootCsv = Directory.GetFiles(target, "*.csv").Any(IsObsCsv);
                bool oldExe = Names.OldDownloaderFiles.Concat(Names.OldViewerFiles).Any(n => File.Exists(Path.Combine(target, n)));
                s.HasData = rootCsv || (Directory.Exists(Path.Combine(target, Names.Data)) && Directory.GetFiles(Path.Combine(target, Names.Data), "*.csv").Length > 0);

                if (vv == null && vd == null && (rootCsv || oldExe))
                {
                    s.Kind = Found.OldLayout;
                    s.Text = "Régi elrendezést találtam (a CSV-k és a programok egy mappában). Átrendezem: az adatok a data, a beállítások az appdata mappákba kerülnek; semmi nem vész el.";
                }
                else if (s.Installed == null)
                {
                    s.Kind = Found.Empty;
                    s.Text = Directory.EnumerateFileSystemEntries(target).Any()
                        ? "A mappa nem üres, de nincs benne telepítés. Mellé telepítem a programokat; a meglévő fájlokhoz nem nyúlok."
                        : "Új telepítés az üres mappába.";
                }
                else
                {
                    var inst = new Version(s.Installed.Major, s.Installed.Minor, Math.Max(0, s.Installed.Build));
                    var me = new Version(Me.Major, Me.Minor, Math.Max(0, Me.Build));
                    int cmp = inst.CompareTo(me);
                    s.Kind = cmp < 0 ? Found.Older : cmp == 0 ? Found.Current : Found.Newer;
                    string vi = VersionText(s.Installed), vm = VersionText(Me);
                    s.Text = s.Kind == Found.Older ? "Régebbi verzió van itt (" + vi + "). Frissítem " + vm + "-re; az adatok és a beállítások megmaradnak."
                           : s.Kind == Found.Current ? "Ez a verzió (" + vm + ") már telepítve van. Újratelepítem (javításhoz); az adatok és a beállítások megmaradnak."
                           : "Figyelem: itt egy ÚJABB verzió van (" + vi + "), mint ez a telepítő (" + vm + "). Csak akkor folytasd, ha vissza akarsz állni.";
                    if (rootCsv || oldExe) s.Text += " A gyökérben maradt régi fájlokat is elrendezem.";
                }
                s.HasApiKey = HasApiKey(Path.Combine(target, Names.DlApp, "beallitasok.ini")) || HasApiKey(Path.Combine(target, "beallitasok.ini"));
            }
            string exe;
            try { s.ScheduleMinutes = Utemezes.Query(out exe); } catch { }
            return s;
        }

        public static string VersionText(Version v)
        {
            if (v == null) return "?";
            return v.Build > 0 ? v.Major + "." + v.Minor + "." + v.Build : v.Major + "." + v.Minor;
        }

        // Régi adatok keresése: a telepítő mappája és közvetlen almappái (régi egy mappás vagy új elrendezés)
        public static string FindOldData(string startDir, string target)
        {
            var cands = new List<string> { startDir };
            try { cands.AddRange(Directory.GetDirectories(startDir)); } catch { }
            string best = null; long bestLen = 0;
            string t = Norm(target);
            foreach (var d in cands)
            {
                if (Norm(d) == t) continue;
                string csv = MainCsv(d);
                if (csv == null) continue;
                long len = new FileInfo(csv).Length;
                if (len > bestLen) { best = d; bestLen = len; }
            }
            return best;
        }

        public static string Norm(string p)
        {
            try { return Path.GetFullPath(p).TrimEnd('\\').ToLowerInvariant(); } catch { return (p ?? "").ToLowerInvariant(); }
        }

        static string MainCsv(string dir)
        {
            foreach (var p in new[] { Path.Combine(dir, "idojaras_adatok.csv"), Path.Combine(dir, Names.Data, "idojaras_adatok.csv") })
                if (File.Exists(p)) return p;
            return null;
        }

        public static string RunningIn(string target)
        {
            string t = Norm(target);
            foreach (var name in Names.ProcessNames)
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        string path = p.MainModule.FileName;
                        if (Norm(Path.GetDirectoryName(path)) == t) return Path.GetFileName(path);
                    }
                    catch { }
                }
            return null;
        }

        public static Result Install(string target, string importFrom, bool shortcut, int scheduleMinutes, Action<string, int> progress)
        {
            var r = new Result { Target = target };
            try
            {
                Guard.SetRoot(target);   // a telepítő csak a célmappába ír (kivétel: asztali parancsikon, ütemezett feladat)
                var survey = Look(target);
                r.From = survey.Installed;
                r.Updated = survey.Kind == Found.Older || survey.Kind == Found.Current || survey.Kind == Found.Newer || survey.Kind == Found.OldLayout;

                string running = RunningIn(target);
                if (running != null)
                    throw new Exception("A(z) " + running + " most fut ebből a mappából. Zárd be (a Megjelenítőt és a Letöltőt is), majd próbáld újra.");

                progress("Mappák létrehozása...", 5);
                Guard.CreateDirectory(target);
                string data = Path.Combine(target, Names.Data), dlApp = Path.Combine(target, Names.DlApp), viewApp = Path.Combine(target, Names.ViewApp);
                Guard.CreateDirectory(data);
                Guard.CreateDirectory(dlApp);
                Guard.CreateDirectory(viewApp);

                if (r.Updated)
                {
                    progress("Biztonsági mentés a beállításokról...", 10);
                    BackupSettings(dlApp, r);
                    BackupSettings(viewApp, r);
                }

                progress("Régi fájlok elrendezése...", 15);
                MigrateInPlace(target, r);

                // a programfájlok mindig frissülnek, az adatok és a beállítások soha nem íródnak felül.
                // Egyben cserél (16.7): előbb minden új fájl .uj-ként, és csak ha mind megvan, jönnek a cserék; ha egy csere
                // nem sikerül (pl. közben elindult az ütemezett Letöltő), a már kicseréltek visszaállnak – nem marad félig frissített állapot.
                var asm = Assembly.GetExecutingAssembly();
                var plan = new List<string[]>();   // [cél, .uj, név]
                int i = 0;
                foreach (var f in Names.Files)
                {
                    // a leírások a választott nyelven (docs/<kód>/…), ha van ilyen; különben magyarul
                    string res = LocalDoc(asm, f[0]), name = res != f[0] ? Langs.T(f[1]) : f[1];
                    progress("Másolás: " + name, 20 + 40 * i++ / Names.Files.Length);
                    string dest = Path.Combine(target, name);
                    string tmp = dest + ".uj";
                    using (var s = asm.GetManifestResourceStream(res))
                    {
                        if (s == null) throw new Exception("Hiányzik a telepítőből: " + f[0]);
                        using (var fs = Guard.OpenWrite(tmp, FileMode.Create, FileAccess.Write, FileShare.None)) s.CopyTo(fs);
                    }
                    plan.Add(new[] { dest, tmp, f[1] });
                }
                progress("A programfájlok cseréje...", 62);
                bool paused = Kozos.Utemezes.Pause(true);   // közben ne induljon el az ütemezett Letöltő
                var done = new List<string[]>();
                try
                {
                    running = RunningIn(target);
                    if (running != null) throw new Exception("A(z) " + running + " közben elindult ebből a mappából. Zárd be, majd próbáld újra.");
                    foreach (var x in plan)
                    {
                        string bak = x[0] + ".regi";
                        try
                        {
                            if (File.Exists(bak)) Guard.Delete(bak);
                            if (File.Exists(x[0])) Guard.Replace(x[1], x[0], bak); else Guard.Move(x[1], x[0]);
                            done.Add(new[] { x[0], File.Exists(bak) ? bak : null });
                        }
                        catch (IOException)
                        {
                            throw new Exception("A(z) " + x[2] + " most használatban van, ezért nem cserélhető. Zárd be a Megjelenítőt és a Letöltőt, majd próbáld újra.");
                        }
                    }
                }
                catch
                {
                    // visszaállítás: a már kicserélt fájlok a régire, az újonnan létrehozottak törlődnek; a maradék .uj-k is
                    for (int k = done.Count - 1; k >= 0; k--)
                        try { if (done[k][1] != null) Guard.Replace(done[k][1], done[k][0], null); else Guard.Delete(done[k][0]); } catch { }
                    foreach (var x in plan) try { if (File.Exists(x[1])) Guard.Delete(x[1]); } catch { }
                    throw;
                }
                finally { if (paused) Kozos.Utemezes.Pause(false); }
                foreach (var x in done) if (x[1] != null) try { Guard.Delete(x[1]); } catch { }
                r.Log.Add("programok és leírások: " + (r.Updated ? "frissítve" : "telepítve"));

                // más nyelvű nevek eltávolítása (nyelvváltás, ill. a 2.2.1-es magyar nevekről), hogy ne legyen két Megjelenítő
                foreach (var kind in new[] { "viewer", "downloader" })
                {
                    string keep = kind == "viewer" ? Names.Viewer : Names.Downloader;
                    foreach (var n in Langs.AllFileNames(kind))
                    {
                        string p = Path.Combine(target, n);
                        if (n.Equals(keep, StringComparison.OrdinalIgnoreCase) || !File.Exists(p)) continue;
                        try { Guard.Delete(p); r.Log.Add("más nyelvű program törölve: " + n); } catch { }
                    }
                }
                foreach (var f in Names.Files.Where(x => x[0].StartsWith("docs/")))
                {
                    string cur = LocalDoc(asm, f[0]) != f[0] ? Langs.T(f[1]) : f[1];
                    foreach (var c in new[] { "hu" }.Concat(Langs.Codes()))
                    {
                        string other = c == "hu" ? f[1] : Langs.T(f[1], c);
                        string p = Path.Combine(target, other);
                        if (other.Equals(cur, StringComparison.OrdinalIgnoreCase) || !File.Exists(p)) continue;
                        try { Guard.Delete(p); } catch { }
                    }
                }

                if (!string.IsNullOrEmpty(importFrom) && Norm(importFrom) != Norm(target))
                {
                    progress("Meglévő adatok átvétele...", 75);
                    Import(importFrom, target, r);
                }

                if (shortcut)
                {
                    progress("Parancsikon...", 85);
                    try
                    {
                        string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        CreateShortcut(Path.Combine(desk, Names.Shortcut), Path.Combine(target, Names.Viewer), target);
                        r.Log.Add("asztali parancsikon");
                        // a régi nevű (2.2.1-ig „Budaörsi időjárás”) parancsikon törlése – csak ha ebbe a mappába mutat
                        string oldLnk = Path.Combine(desk, Names.LegacyShortcut);
                        if (File.Exists(oldLnk) && ShortcutTargetsDir(oldLnk, target)) { File.Delete(oldLnk); r.Log.Add("a régi nevű parancsikon törölve"); }
                    }
                    catch (Exception ex) { r.Log.Add("A parancsikon nem jött létre: " + ex.Message); }
                }

                progress("Automatikus letöltés...", 92);
                string msg;
                try
                {
                    if (scheduleMinutes < 0)
                    {
                        // marad az ütemezés, de ha a feladat egy (most törölt) más nevű Letöltőre mutat, átirányítjuk
                        var spec = Utemezes.QuerySpec();
                        string dl = Path.Combine(target, Names.Downloader);
                        if (spec.kind != "off" && (spec.exe == null || Norm(spec.exe) != Norm(dl)) && (spec.exe == null || Norm(Path.GetDirectoryName(spec.exe)) == Norm(target)))
                            Utemezes.SetSpec(dl, spec, out msg);
                        else msg = "Automatikus letöltés: változatlan (" + Utemezes.Text(spec) + ").";
                    }
                    else if (scheduleMinutes > 0) Utemezes.Set(Path.Combine(target, Names.Downloader), scheduleMinutes, out msg);
                    else if (survey.ScheduleMinutes > 0) Utemezes.Remove(out msg);
                    else msg = "Automatikus letöltés: kikapcsolva.";
                }
                catch (Exception ex) { msg = "Az ütemezés nem sikerült: " + ex.Message; }
                r.ScheduleMessage = msg;

                Guard.WriteAllText(Path.Combine(viewApp, "verzio.txt"), VersionText(Me) + "\r\n" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\r\n", Encoding.UTF8);
                r.HasApiKey = HasApiKey(Path.Combine(dlApp, "beallitasok.ini"));
                progress("Kész.", 100);
                r.Ok = true;
            }
            catch (UnauthorizedAccessException)
            {
                r.Error = "Ebbe a mappába nincs írási jogod:\n" + target + "\n\nVálassz egy saját mappát (pl. Dokumentumok).";
            }
            catch (Exception ex) { r.Error = ex.Message; }
            return r;
        }

        static void BackupSettings(string appDir, Result r)
        {
            var files = Directory.GetFiles(appDir).Where(f => f.EndsWith(".ini") || f.EndsWith(".json")).ToList();
            string presets = Path.Combine(appDir, "presetek");
            if (files.Count == 0 && !Directory.Exists(presets)) return;
            string root = Path.Combine(appDir, "mentes_beallitasok");
            string dir = Path.Combine(root, DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
            Guard.CreateDirectory(dir);
            foreach (var f in files) Guard.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);
            if (Directory.Exists(presets))
            {
                Guard.CreateDirectory(Path.Combine(dir, "presetek"));
                foreach (var f in Directory.GetFiles(presets)) Guard.Copy(f, Path.Combine(dir, "presetek", Path.GetFileName(f)), true);
            }
            // a legutóbbi 5 mentés marad meg
            var old = new DirectoryInfo(root).GetDirectories().OrderByDescending(d => d.Name).Skip(5).ToList();
            foreach (var d in old) try { d.Delete(true); } catch { }
            r.Log.Add("beállítások mentve: " + Path.GetFileName(appDir) + "\\mentes_beallitasok");
        }

        // a korábbi (egy mappás, ill. 1.0-s) verziók gyökérben hagyott fájljainak elrendezése
        static void MigrateInPlace(string target, Result r)
        {
            string data = Path.Combine(target, Names.Data), dlApp = Path.Combine(target, Names.DlApp), viewApp = Path.Combine(target, Names.ViewApp);
            int moved = 0;
            foreach (var f in Directory.GetFiles(target, "*.csv"))
                if (IsObsCsv(f)) moved += MoveSafe(f, Path.Combine(data, Path.GetFileName(f)), r);
            foreach (var n in new[] { "beallitasok.ini", "lekerdezes_allapot.json", "lekerdezes_naplo.log", "lekerdezes_naplo.log.regi" })
                moved += MoveSafe(Path.Combine(target, n), Path.Combine(dlApp, n), r);
            foreach (var n in new[] { "megjelenito.ini", "megjelenito_hiba.log", "teszt_eredmeny.txt" })
                moved += MoveSafe(Path.Combine(target, n), Path.Combine(viewApp, n), r);
            foreach (var n in Names.OldDownloaderFiles)
                moved += MoveSafe(Path.Combine(target, n), Path.Combine(dlApp, "regi_fajlok", n), r);
            foreach (var n in Names.OldViewerFiles)
                moved += MoveSafe(Path.Combine(target, n), Path.Combine(viewApp, "regi_fajlok", n), r);
            string oldCache = Path.Combine(target, "cache");
            if (Directory.Exists(oldCache) && File.Exists(Path.Combine(oldCache, "meresek.bin")))
            {
                try { Guard.DeleteDirectory(oldCache, true); moved++; } catch { }
            }
            if (moved > 0) r.Log.Add("régi elrendezés átalakítva (" + moved + " elem a helyére került)");
        }

        static int MoveSafe(string from, string to, Result r)
        {
            if (!File.Exists(from)) return 0;
            Guard.CreateDirectory(Path.GetDirectoryName(to));
            if (File.Exists(to))
            {
                // ütközésnél egyik sem vész el: a régi külön néven kerül mellé
                to = Path.Combine(Path.GetDirectoryName(to), Path.GetFileNameWithoutExtension(to) + "_regi_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + Path.GetExtension(to));
            }
            Guard.Move(from, to);
            return 1;
        }

        // CSV-k a data mappába, a letöltő beállításai/állapota az appdata_letöltő mappába – meglévőt nem ír felül
        static void Import(string src, string target, Result r)
        {
            string data = Path.Combine(target, Names.Data), dlApp = Path.Combine(target, Names.DlApp);
            var srcData = new[] { src, Path.Combine(src, Names.Data) };
            var srcApp = new[] { src, Path.Combine(src, Names.DlApp) };
            int n = 0;
            foreach (var name in new[] { "idojaras_adatok.csv", "napi_osszesito.csv", "pillanatnyi_allapot.csv" })
            {
                string from = srcData.Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);
                if (from == null) continue;
                string to = Path.Combine(data, name);
                if (File.Exists(to))
                {
                    if (name != "idojaras_adatok.csv") continue;
                    if (new FileInfo(to).Length == new FileInfo(from).Length && SameStart(to, from)) continue; // ugyanaz a fájl
                    // a megjelenítő minden CSV-t egyesít és kiszűri a duplikátumokat, így külön néven is átvehető
                    to = Path.Combine(data, "idojaras_adatok_atvett_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                }
                Guard.Copy(from, to);
                r.Log.Add("átvéve: " + name);
                n++;
            }
            string ini = Path.Combine(dlApp, "beallitasok.ini");
            string srcIni = srcApp.Select(d => Path.Combine(d, "beallitasok.ini")).FirstOrDefault(File.Exists);
            if (srcIni != null && (!File.Exists(ini) || !HasApiKey(ini)) && HasApiKey(srcIni))
            {
                Guard.Copy(srcIni, ini, true);
                r.Log.Add("átvéve: beallitasok.ini (API kulcs, állomás)");
                foreach (var name in new[] { "lekerdezes_allapot.json", "lekerdezes_naplo.log" })
                {
                    string from = srcApp.Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);
                    string to = Path.Combine(dlApp, name);
                    if (from != null && !File.Exists(to)) { Guard.Copy(from, to); r.Log.Add("átvéve: " + name); }
                }
            }
            if (n == 0) r.Log.Add("A megadott helyen nem találtam új átvehető adatot.");
        }

        static bool SameStart(string a, string b)
        {
            var ba = new byte[65536]; var bb = new byte[65536];
            using (var fa = File.OpenRead(a)) using (var fb = File.OpenRead(b))
            {
                int na = fa.Read(ba, 0, ba.Length), nb = fb.Read(bb, 0, bb.Length);
                return na == nb && ba.Take(na).SequenceEqual(bb.Take(nb));
            }
        }

        public static bool HasApiKey(string ini)
        {
            try
            {
                if (!File.Exists(ini)) return false;
                foreach (var line in File.ReadAllLines(ini, Encoding.UTF8))
                {
                    string l = line.Trim();
                    if (l.StartsWith("ApiKey=", StringComparison.OrdinalIgnoreCase) && l.Length > 7) return true;
                }
            }
            catch { }
            return false;
        }

        // a telepítőben választott nyelv a programoknak is: felulet.json (lang) és – ha már van – a Letöltő beállítása (Nyelv)
        public static void SaveLanguage(string target, string code)
        {
            if (string.IsNullOrEmpty(code)) return;
            try
            {
                var js = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                string dir = Path.Combine(target, Names.ViewApp), ui = Path.Combine(dir, "felulet.json");
                Dictionary<string, object> d = null;
                if (File.Exists(ui)) try { d = js.DeserializeObject(File.ReadAllText(ui, Encoding.UTF8)) as Dictionary<string, object>; } catch { }
                if (d == null) d = new Dictionary<string, object>();
                if (code != "hu" || d.ContainsKey("lang"))
                {
                    d["lang"] = code;
                    Guard.CreateDirectory(dir);
                    Guard.WriteAllText(ui, js.Serialize(d), new UTF8Encoding(false));
                }
                string ini = Path.Combine(target, Names.DlApp, "beallitasok.ini");
                if (File.Exists(ini))
                {
                    var lines = File.ReadAllLines(ini, Encoding.UTF8).ToList();
                    int i = lines.FindIndex(l => l.TrimStart().StartsWith("Nyelv=", StringComparison.OrdinalIgnoreCase));
                    if (i >= 0) lines[i] = "Nyelv=" + code; else lines.Add("Nyelv=" + code);
                    Guard.WriteAllText(ini, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(true));
                }
            }
            catch { }
        }

        // "docs/X.txt" -> "docs/<nyelv>/X.txt", ha a telepítőben van ilyen fordítás
        public static string LocalDoc(Assembly asm, string res)
        {
            if (!res.StartsWith("docs/") || Langs.Current == "hu") return res;
            string loc = "docs/" + Langs.Current + "/" + res.Substring(5);
            return asm.GetManifestResourceNames().Contains(loc) ? loc : res;
        }

        // a változásnapló azon szakaszai, amik a "from" verziónál újabbak
        public static string NewsSince(Version from)
        {
            string text;
            var asm0 = Assembly.GetExecutingAssembly();
            using (var s = asm0.GetManifestResourceStream(LocalDoc(asm0, "docs/VALTOZASOK.txt")))
            using (var sr = new StreamReader(s, Encoding.UTF8)) text = sr.ReadToEnd();
            var sb = new StringBuilder();
            bool take = false;
            foreach (var line in text.Split('\n').Select(l => l.TrimEnd('\r')))
            {
                var m = Regex.Match(line, @"^== (\d+\.\d+(?:\.\d+)?)");
                if (m.Success)
                {
                    var v = new Version(m.Groups[1].Value);
                    var v3 = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
                    take = from == null || v3.CompareTo(new Version(from.Major, from.Minor, Math.Max(0, from.Build))) > 0;
                    if (take) sb.Append(sb.Length > 0 ? "\n" : "").Append(line.Trim('=', ' ')).Append(":\n");
                    continue;
                }
                if (take && line.StartsWith("- ")) sb.Append("  • ").Append(line.Substring(2)).Append("\n");
            }
            return sb.ToString().TrimEnd();
        }

        static void CreateShortcut(string lnk, string target, string workDir)
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(t);
            try
            {
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                var st = sc.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
                st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "WStation – Megjelenítő" });
                st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                Marshal.FinalReleaseComObject(sc);
            }
            finally { Marshal.FinalReleaseComObject(sh); }
        }

        // a parancsikon a megadott mappába mutat-e (a régi nevű csak akkor törlődik)
        static bool ShortcutTargetsDir(string lnk, string dir)
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                object sh = Activator.CreateInstance(t);
                try
                {
                    object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                    string tp = Convert.ToString(sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null));
                    Marshal.FinalReleaseComObject(sc);
                    return tp.Length > 0 && Norm(Path.GetDirectoryName(tp)) == Norm(dir);
                }
                finally { Marshal.FinalReleaseComObject(sh); }
            }
            catch { return false; }
        }
    }

    // ------------------------------------------------------------------ felület

    class MainForm : Form
    {
        readonly TextBox txtTarget, txtImport;
        readonly CheckBox chkImport, chkShortcut, chkSchedule;
        readonly ComboBox cmbSchedule;
        bool schedKeep;   // a lista első eleme: „jelenlegi megtartása” (a meglévő, akár egyedi ütemezés érintetlen marad)
        readonly Label lblInfo, lblStatus;
        readonly ProgressBar bar;
        readonly Button btnInstall, btnCancel, btnTarget, btnImport;
        readonly Panel body, done;
        readonly ComboBox cmbLang;
        readonly Dictionary<Control, string> orig = new Dictionary<Control, string>();
        static string T(string hu) { return Langs.T(hu); }
        Result result;
        Survey survey;
        readonly string startDir;

        float k = 1;
        int S(int v) { return (int)Math.Round(v * k); }
        Point P(int x, int y) { return new Point(S(x), S(y)); }
        Size Z(int w, int h) { return new Size(S(w), S(h)); }

        static readonly Color Accent = Color.FromArgb(37, 99, 235);
        static readonly Color Muted = Color.FromArgb(99, 112, 131);
        static readonly Color Warn = Color.FromArgb(176, 92, 0);

        public MainForm(string startDir, string defaultTarget)
        {
            this.startDir = startDir;
            using (var g = CreateGraphics()) k = g.DpiX / 96f;
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Text = "WStation – telepítő " + Installer.VersionText(Installer.Me);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Font = new Font("Segoe UI", 9.75f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = Z(620, 520);
            BackColor = Color.White;

            var header = new Panel { Dock = DockStyle.Top, Height = S(92) };
            header.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new LinearGradientBrush(header.ClientRectangle, Color.FromArgb(47, 111, 232), Color.FromArgb(24, 63, 143), 0f))
                    g.FillRectangle(br, header.ClientRectangle);
                var ico = Icon;
                if (ico != null) g.DrawIcon(new Icon(ico, S(56), S(56)), S(22), S(18));
                using (var f1 = new Font("Segoe UI Semibold", 17f))
                using (var f2 = new Font("Segoe UI", 9.75f))
                {
                    g.DrawString(T("WStation"), f1, Brushes.White, S(92), S(16));
                    using (var b2 = new SolidBrush(Color.FromArgb(215, 228, 255)))
                        g.DrawString(T("Letöltő és megjelenítő a házi meteorológiai állomáshoz") + " · " + Installer.VersionText(Installer.Me), f2, b2, S(94), S(52));
                }
            };

            // nyelvválasztó (a Windows nyelvéből ajánlott; a web\lang mappa nyelvi fájljaiból)
            Langs.Current = Langs.FromSystem();
            cmbLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = P(470, 8), Width = S(138) };
            var langCodes = new List<string> { "hu" }; langCodes.AddRange(Langs.Codes());
            foreach (var c in langCodes)
            {
                var m = Langs.Meta(c); object n = null;
                if (m != null) m.TryGetValue("name", out n);
                cmbLang.Items.Add(c == "hu" ? "Magyar" : Convert.ToString(n ?? c));
            }
            cmbLang.SelectedIndex = Math.Max(0, langCodes.IndexOf(Langs.Current));
            cmbLang.SelectedIndexChanged += (s, e) => { Langs.Current = langCodes[cmbLang.SelectedIndex]; ApplyLang(); header.Invalidate(); UpdateInfo(); };
            header.Controls.Add(cmbLang);

            body = new Panel { Location = P(0, 92), Size = Z(620, 428) };
            int y = 16;
            body.Controls.Add(new Label { Text = "Telepítés helye", Font = new Font("Segoe UI Semibold", 9.75f), Location = P(24, y), AutoSize = true });
            y += 24;
            txtTarget = new TextBox { Location = P(24, y), Width = S(452), Text = defaultTarget };
            btnTarget = new Button { Text = "Módosítás…", Location = P(486, y - 2), Size = Z(110, 28) };
            btnTarget.Click += (s, e) => PickTarget();
            body.Controls.Add(txtTarget); body.Controls.Add(btnTarget);
            y += 32;
            lblInfo = new Label { Location = P(24, y), Size = Z(572, 58), ForeColor = Muted };
            body.Controls.Add(lblInfo);
            y += 64;

            chkImport = new CheckBox { Text = "Meglévő adatok átvétele (mérések, API kulcs) innen:", Location = P(24, y), AutoSize = true };
            body.Controls.Add(chkImport);
            y += 26;
            txtImport = new TextBox { Location = P(44, y), Width = S(432) };
            btnImport = new Button { Text = "Tallózás…", Location = P(486, y - 2), Size = Z(110, 28) };
            btnImport.Click += (s, e) => PickImport();
            body.Controls.Add(txtImport); body.Controls.Add(btnImport);
            chkImport.CheckedChanged += (s, e) => { txtImport.Enabled = btnImport.Enabled = chkImport.Checked; };
            y += 38;

            chkShortcut = new CheckBox { Text = "Parancsikon az asztalra (WStation)", Location = P(24, y), AutoSize = true, Checked = true };
            body.Controls.Add(chkShortcut);
            y += 30;
            chkSchedule = new CheckBox { Text = "Automatikus letöltés a háttérben (Feladatütemező):", Location = P(24, y), AutoSize = true, Checked = true };
            cmbSchedule = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = P(400, y - 3), Width = S(196) };
            foreach (var m in Utemezes.Choices) cmbSchedule.Items.Add(Utemezes.Text(m));
            cmbSchedule.SelectedIndex = Array.IndexOf(Utemezes.Choices, 60);
            chkSchedule.CheckedChanged += (s, e) => cmbSchedule.Enabled = chkSchedule.Checked;
            body.Controls.Add(chkSchedule); body.Controls.Add(cmbSchedule);
            y += 38;

            bar = new ProgressBar { Location = P(24, y), Size = Z(572, 10), Visible = false };
            lblStatus = new Label { Location = P(24, y + 16), Size = Z(572, 40), ForeColor = Muted };
            body.Controls.Add(bar); body.Controls.Add(lblStatus);

            btnInstall = new Button { Text = "Telepítés", Location = P(366, 372), Size = Z(130, 36), BackColor = Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Semibold", 9.75f) };
            btnInstall.FlatAppearance.BorderSize = 0;
            btnInstall.Click += (s, e) => DoInstall();
            btnCancel = new Button { Text = "Mégse", Location = P(506, 372), Size = Z(90, 36) };
            btnCancel.Click += (s, e) => Close();
            body.Controls.Add(btnInstall); body.Controls.Add(btnCancel);
            AcceptButton = btnInstall;

            done = new Panel { Location = P(0, 92), Size = Z(620, 428), Visible = false, AutoScroll = true };

            Controls.Add(body); Controls.Add(done); Controls.Add(header);
            txtTarget.TextChanged += (s, e) => UpdateInfo();
            ResumeLayout(true);

            ApplyLang();
            UpdateInfo();
            string old = Installer.FindOldData(startDir, txtTarget.Text);
            txtImport.Text = old ?? "";
            chkImport.Checked = old != null;
            txtImport.Enabled = btnImport.Enabled = chkImport.Checked;
        }

        // a feliratok fordítása az eredeti (magyar) szövegből
        void ApplyLang()
        {
            Text = T("WStation – telepítő") + " " + Installer.VersionText(Installer.Me);
            Action<Control> walk = null;
            walk = c =>
            {
                foreach (Control x in c.Controls)
                {
                    if (x is Label || x is Button || x is CheckBox)
                    {
                        string o;
                        if (!orig.TryGetValue(x, out o)) orig[x] = o = x.Text;
                        x.Text = T(o);
                    }
                    walk(x);
                }
            };
            walk(this);
        }

        void UpdateInfo()
        {
            string t = txtTarget.Text.Trim();
            if (t.Length == 0) return;
            survey = Installer.Look(t);
            lblInfo.Text = T(survey.Text);
            orig[lblInfo] = survey.Text;
            lblInfo.ForeColor = survey.Kind == Found.Newer ? Color.Firebrick : survey.Kind == Found.OldLayout ? Warn : Muted;
            orig[btnInstall] = survey.Kind == Found.Empty ? "Telepítés" : "Frissítés";
            btnInstall.Text = T(orig[btnInstall]);
            // meglévő ütemezésnél alapból az marad, ahogy van (a Beállításokban megadott napi / heti / egyedi is),
            // különben egy frissítés csendben „óránként”-re írná át
            cmbSchedule.Items.Clear();
            foreach (var m in Utemezes.Choices) cmbSchedule.Items.Add(T(Utemezes.Text(m)));
            schedKeep = survey.ScheduleMinutes > 0;
            if (schedKeep)
            {
                chkSchedule.Checked = true;
                cmbSchedule.Items.Insert(0, T("jelenlegi megtartása") + " (" + T(Utemezes.Text(Utemezes.QuerySpec())) + ")");
                cmbSchedule.SelectedIndex = 0;
            }
            else cmbSchedule.SelectedIndex = Array.IndexOf(Utemezes.Choices, 60);
        }

        void PickTarget()
        {
            using (var d = new FolderBrowserDialog { Description = T("Hova kerüljön az „Időjárás” mappa? (Egy meglévő telepítést is választhatsz.)"), SelectedPath = startDir })
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    string p = d.SelectedPath.TrimEnd('\\');
                    txtTarget.Text = Installer.IsInstallDir(p) || Path.GetFileName(p).Equals(Names.Root, StringComparison.OrdinalIgnoreCase) ? p : Path.Combine(p, Names.Root);
                }
        }

        void PickImport()
        {
            using (var d = new FolderBrowserDialog { Description = T("Melyik mappában vannak a korábbi adatok (idojaras_adatok.csv)?"), SelectedPath = txtImport.Text.Length > 0 ? txtImport.Text : startDir })
                if (d.ShowDialog(this) == DialogResult.OK) txtImport.Text = d.SelectedPath;
        }

        void DoInstall()
        {
            string target = txtTarget.Text.Trim();
            if (target.Length == 0) return;
            try { target = Path.GetFullPath(target); }
            catch { MessageBox.Show(this, T("Érvénytelen mappa."), Text); return; }
            survey = Installer.Look(target);
            if (survey.Kind == Found.Newer &&
                MessageBox.Show(this, T("A célmappában újabb verzió van, mint ez a telepítő") + " (" + Installer.VersionText(survey.Installed) + " > " + Installer.VersionText(Installer.Me) + ").\n\n" + T("Biztosan visszaállsz a régebbire?"),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            string import = chkImport.Checked ? txtImport.Text.Trim() : null;
            if (!string.IsNullOrEmpty(import) && !Directory.Exists(import))
            {
                MessageBox.Show(this, T("Az átvételhez megadott mappa nem létezik:") + "\n" + import, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int sel = Math.Max(0, cmbSchedule.SelectedIndex);
            // -1: a meglévő ütemezés változatlan marad
            int minutes = !chkSchedule.Checked ? 0 : schedKeep && sel == 0 ? -1 : Utemezes.Choices[sel - (schedKeep ? 1 : 0)];
            foreach (Control c in body.Controls) if (c != bar && c != lblStatus) c.Enabled = false;
            bar.Visible = true;
            lblStatus.ForeColor = Muted;
            result = Installer.Install(target, import, chkShortcut.Checked, minutes, (msg, pct) =>
            {
                lblStatus.Text = T(msg); bar.Value = Math.Min(100, pct);
                Application.DoEvents();
            });
            if (!result.Ok)
            {
                lblStatus.ForeColor = Color.Firebrick;
                lblStatus.Text = T("Nem sikerült") + ": " + T(result.Error).Replace("\n", " ");
                MessageBox.Show(this, T(result.Error), T("WStation – telepítő"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                foreach (Control c in body.Controls) c.Enabled = true;
                txtImport.Enabled = btnImport.Enabled = chkImport.Checked;
                cmbSchedule.Enabled = chkSchedule.Checked;
                return;
            }
            Installer.SaveLanguage(result.Target, Langs.Current);
            ShowDone();
        }

        void ShowDone()
        {
            body.Visible = false;
            done.Visible = true;
            int y = 18;
            string title = result.Updated ? "✔  " + T("A frissítés kész") + (result.From != null ? " (" + Installer.VersionText(result.From) + " → " + Installer.VersionText(Installer.Me) + ")" : "") : "✔  " + T("A telepítés kész");
            done.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI Semibold", 14f), ForeColor = Color.FromArgb(22, 128, 61), Location = P(24, y), AutoSize = true });
            y += 40;
            done.Controls.Add(new Label { Text = T("Helye") + ": " + result.Target, Location = P(24, y), Size = Z(572, 22), ForeColor = Muted });
            y += 26;
            string next = result.HasApiKey
                ? "Az API kulcs be van állítva. Indítsd el a Megjelenítőt; a Frissítés gomb letölti a legújabb adatokat."
                : "Következő lépés: indítsd el egyszer a Letöltőt. Megkérdezi a Weather Underground API kulcsot és az állomás azonosítóját (pl. IBUDAP123), majd letölti az adatokat.";
            done.Controls.Add(new Label { Text = T(next), Location = P(24, y), Size = Z(572, 40) });
            y += 44;

            var lines = new List<string>();
            lines.AddRange(result.Log.Where(l => !l.StartsWith("programok")));
            if (!string.IsNullOrEmpty(result.ScheduleMessage)) lines.Add(result.ScheduleMessage);
            string news = result.Updated ? Installer.NewsSince(result.From) : "";
            var info = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(246, 248, 251),
                Location = P(24, y), Size = Z(572, 190), Font = new Font("Segoe UI", 9f),
                Text = string.Join("\r\n", lines.Select(l => "• " + T(l))) + (news.Length > 0 ? "\r\n\r\n" + T("Újdonságok") + ":\r\n" + news.Replace("\n", "\r\n") : "")
            };
            done.Controls.Add(info);

            var b1 = new Button { Text = T(result.HasApiKey ? "Megjelenítő indítása" : "Letöltő indítása"), Location = P(24, 372), Size = Z(190, 36), BackColor = Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Semibold", 9.75f) };
            b1.FlatAppearance.BorderSize = 0;
            b1.Click += (s, e) => { Launch(Path.Combine(result.Target, result.HasApiKey ? Names.Viewer : Names.Downloader)); Close(); };
            var b2 = new Button { Text = T("Mappa megnyitása"), Location = P(224, 372), Size = Z(150, 36) };
            b2.Click += (s, e) => Launch(result.Target);
            var b3 = new Button { Text = T("Bezárás"), Location = P(506, 372), Size = Z(90, 36) };
            b3.Click += (s, e) => Close();
            done.Controls.Add(b1); done.Controls.Add(b2); done.Controls.Add(b3);
            AcceptButton = b1;
            b1.Focus();
        }

        static void Launch(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = File.Exists(path) ? Path.GetDirectoryName(path) : path }); }
            catch (Exception ex) { MessageBox.Show(T("Nem sikerült megnyitni:") + "\n" + path + "\n\n" + ex.Message); }
        }
    }

    static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int pid);

        [STAThread]
        static int Main(string[] args)
        {
            string startDir = Path.GetDirectoryName(Application.ExecutablePath);
            // egy meglévő telepítésen belül indítva helyben frissít
            // (ha mellette egy régi, „Időjárás” nevű telepítés van, azt frissíti, nem telepít újat „WStation” néven)
            string legacyDir = Path.Combine(startDir, Names.LegacyRoot);
            string defaultTarget = Installer.IsInstallDir(startDir) ? startDir : Installer.IsInstallDir(legacyDir) ? legacyDir : Path.Combine(startDir, Names.Root);
            // a Megjelenítő frissítés-ellenőrzője a telepített mappát adja át
            int ci = Array.FindIndex(args, a => a.Equals("--cel", StringComparison.OrdinalIgnoreCase));
            if (ci >= 0 && ci + 1 < args.Length && Installer.IsInstallDir(args[ci + 1])) defaultTarget = Path.GetFullPath(args[ci + 1]);

            // parancssori (felület nélküli) telepítés:
            //   --csendes [<célmappa>] [--atvetel <forrás>] [--parancsikon] [--utemezes <perc>] [--felmeres]
            int qi = Array.FindIndex(args, a => a.Equals("--csendes", StringComparison.OrdinalIgnoreCase));
            if (qi >= 0)
            {
                AttachConsole(-1);
                try { Console.OutputEncoding = Encoding.UTF8; } catch { }
                string target = qi + 1 < args.Length && !args[qi + 1].StartsWith("--") ? Path.GetFullPath(args[qi + 1]) : defaultTarget;
                int ai = Array.FindIndex(args, a => a.Equals("--atvetel", StringComparison.OrdinalIgnoreCase));
                string import = ai >= 0 && ai + 1 < args.Length ? Path.GetFullPath(args[ai + 1]) : null;
                bool sc = args.Any(a => a.Equals("--parancsikon", StringComparison.OrdinalIgnoreCase));
                int si = Array.FindIndex(args, a => a.Equals("--utemezes", StringComparison.OrdinalIgnoreCase));
                int minutes = 0;
                if (si >= 0 && si + 1 < args.Length) int.TryParse(args[si + 1], out minutes);
                var sv = Installer.Look(target);
                Console.WriteLine("Felmérés: " + sv.Kind + " – " + sv.Text);
                if (args.Any(a => a.Equals("--felmeres", StringComparison.OrdinalIgnoreCase))) return 0;
                var r = Installer.Install(target, import, sc, minutes, (m, p) => { });
                Console.WriteLine(r.Ok ? "OK " + (r.Updated ? "(frissítés) " : "") + r.Target : "HIBA: " + r.Error);
                foreach (var l in r.Log) Console.WriteLine("  " + l);
                if (r.ScheduleMessage != null) Console.WriteLine("  " + r.ScheduleMessage);
                Console.WriteLine("  API kulcs beállítva: " + (r.HasApiKey ? "igen" : "nem"));
                return r.Ok ? 0 : 1;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(startDir, defaultTarget));
            return 0;
        }
    }
}
