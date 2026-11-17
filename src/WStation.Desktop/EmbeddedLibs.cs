#nullable disable
using System;
using System.IO;
using System.Reflection;
using WStation.Core.Common;

namespace WStation.Desktop
{
    // 1.5: a 2.x Layout.cs-éből (Windows-függő: a WebView2 könyvtárai) – változatlanul
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
