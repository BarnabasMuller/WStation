using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Idojaras
{
    // A telepített mappa szerkezete:
    //   Időjárás\Letöltő.exe, Megjelenítő.exe, leírások
    //   Időjárás\data\                  – a CSV adatok
    //   Időjárás\appdata_letöltő\       – a letöltő beállításai, naplói
    //   Időjárás\appdata_megjelenítő\   – a megjelenítő beállításai, cache, naplók, WebView2
    class Layout
    {
        public const string DataName = "data";
        public const string AppName = "appdata_megjelenítő";
        public const string DlAppName = "appdata_letöltő";
        // a Letöltő bármelyik nyelvi nevén (a telepítő nyelvétől függ, pl. Downloader.exe), és a régi nevein
        public static IEnumerable<string> DownloaderNames { get { return Langs.AllFileNames("downloader").Concat(new[] { "Letolto.exe", "WU_Letolto.exe" }); } }

        public string Base, Data, App, Cache, Lib, DlApp;

        public static Layout For(string baseDir)
        {
            var l = new Layout { Base = baseDir.TrimEnd('\\') };
            l.Data = Path.Combine(l.Base, DataName);
            l.App = Path.Combine(l.Base, AppName);
            l.Cache = Path.Combine(l.App, "cache");
            l.Lib = Path.Combine(l.App, "lib");
            l.DlApp = Path.Combine(l.Base, DlAppName);
            return l;
        }

        public string Settings { get { return Path.Combine(App, "megjelenito.ini"); } }
        public string ErrorLog { get { return Path.Combine(App, "megjelenito_hiba.log"); } }
        public string DlIni { get { return Path.Combine(DlApp, "beallitasok.ini"); } }

        public void Ensure()
        {
            Guard.CreateDirectory(Data);
            Guard.CreateDirectory(App);
        }

        public string FindDownloader()
        {
            foreach (var n in DownloaderNames)
            {
                string p = Path.Combine(Base, n);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        // Régi (egy mappás) elrendezés átalakítása: a mappa gyökerében heverő fájlokat a helyükre teszi
        public List<string> MigrateOld()
        {
            var moved = new List<string>();
            foreach (var f in Directory.GetFiles(Base, "*.csv"))
            {
                string kind = Loader.Classify(f);
                if (kind == null && !IsDailySummary(f)) continue;
                Move(f, Path.Combine(Data, Path.GetFileName(f)), moved);
            }
            foreach (var n in new[] { "beallitasok.ini", "lekerdezes_allapot.json", "lekerdezes_naplo.log", "lekerdezes_naplo.log.regi" })
                Move(Path.Combine(Base, n), Path.Combine(DlApp, n), moved);
            Move(Path.Combine(Base, "megjelenito.ini"), Settings, moved);
            Move(Path.Combine(Base, "megjelenito_hiba.log"), ErrorLog, moved);
            return moved;
        }

        static bool IsDailySummary(string f)
        {
            try
            {
                using (var sr = new StreamReader(f))
                {
                    string h = sr.ReadLine() ?? "";
                    return h.Contains("datum") && h.Contains("stationID");
                }
            }
            catch { return false; }
        }

        static void Move(string from, string to, List<string> moved)
        {
            if (!File.Exists(from) || File.Exists(to)) return;
            try
            {
                Guard.CreateDirectory(Path.GetDirectoryName(to));
                Guard.Move(from, to);
                moved.Add(Path.GetFileName(from));
            }
            catch { }
        }
    }

    // A WebView2 DLL-ek az exe-be vannak ágyazva; induláskor az appdata_megjelenítő\lib mappába kerülnek
    static class EmbeddedLibs
    {
        static string libDir;
        static readonly string[] Names = { "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll" };

        static bool SameHash(byte[] a, byte[] b)
        {
            using (var h = System.Security.Cryptography.SHA256.Create())
                return Convert.ToBase64String(h.ComputeHash(a)) == Convert.ToBase64String(h.ComputeHash(b));
        }

        public static void Init(string dir)
        {
            libDir = dir;
            Guard.CreateDirectory(dir);
            var asm = Assembly.GetExecutingAssembly();
            foreach (var n in Names)
            {
                using (var s = asm.GetManifestResourceStream("lib/" + n))
                {
                    if (s == null) continue;
                    string target = Path.Combine(dir, n);
                    // tartalom szerint (SHA-256): egy azonos méretű, de más verziójú DLL is cserélődjön (12.6)
                    var mem = new MemoryStream(); s.CopyTo(mem);
                    byte[] want = mem.ToArray();
                    if (File.Exists(target) && new FileInfo(target).Length == want.Length && SameHash(File.ReadAllBytes(target), want)) continue;
                    try { using (var fs = Guard.Create(target)) fs.Write(want, 0, want.Length); }
                    catch (IOException) { }   // egy másik, épp futó példány zárolja: az a jó (azonos) verziót használja
                }
            }
            AppDomain.CurrentDomain.AssemblyResolve += (o, e) =>
            {
                string name = new AssemblyName(e.Name).Name;
                string p = Path.Combine(libDir, name + ".dll");
                return name.StartsWith("Microsoft.Web.WebView2", StringComparison.OrdinalIgnoreCase) && File.Exists(p) ? Assembly.LoadFrom(p) : null;
            };
        }
    }
}
