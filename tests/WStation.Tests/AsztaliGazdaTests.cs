using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using WStation.Core.Common;
using WStation.Core;
using Xunit;

namespace WStation.Tests
{
    /// <summary>
    /// Az asztali gazda (PLAN-3.0 1.6): a felület fájljai a mag erőforrásai (12.), és a <c>WStation.Desktop.exe --teszt</c> önteszt
    /// a generált mintaadaton pontra ugyanazt adja, mint a 2.2.8 (<c>tests/golden/onteszt_2.2.8.txt</c>).
    /// </summary>
    public class AsztaliGazdaTests
    {
        static string RepoGyoker()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "WStation.sln"))) return d.FullName;
            throw new DirectoryNotFoundException("Nem találom a repó gyökerét (WStation.sln).");
        }

        [Fact]
        public void A_web_mappa_minden_fajlja_beagyazva_a_magba()
        {
            string web = Path.Combine(RepoGyoker(), "web");
            var vart = Directory.GetFiles(web).Select(f => "web/" + Path.GetFileName(f))
                .Concat(Directory.GetFiles(Path.Combine(web, "lang"), "*.json").Select(f => "web/lang/" + Path.GetFileName(f)))
                .Where(n => !Path.GetFileName(n).StartsWith('_'))
                .OrderBy(n => n, StringComparer.Ordinal).ToList();
            var kapott = typeof(Layout).Assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith("web/", StringComparison.Ordinal))
                .OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.Equal(vart, kapott);
            Assert.Contains("web/index.html", kapott);
            Assert.DoesNotContain("web/lang/_sablon.json", kapott);
        }

        [Fact]
        public void A_beagyazott_fajl_tartalma_a_web_mappaebol_valo()
        {
            string f = Path.Combine(RepoGyoker(), "web", "index.html");
            using var s = typeof(Layout).Assembly.GetManifestResourceStream("web/index.html")!;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            Assert.Equal(File.ReadAllBytes(f), ms.ToArray());
        }

        [WindowsFact]
        public void Az_onteszt_a_mintaadaton_ugyanazt_adja_mint_a_2_2_8()
        {
            string gyoker = RepoGyoker();
            // a teszt szerelvénnyel azonos konfiguráció (Debug / Release) kimenete; a megoldás fordítása készíti el
            string konf = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Debug" + Path.DirectorySeparatorChar) ? "Debug" : "Release";
            string exe = Path.Combine(gyoker, "src", "WStation.Desktop", "bin", konf, "net10.0-windows", "WStation.Desktop.exe");
            Assert.True(File.Exists(exe), "Nincs meg az asztali program: " + exe + " – előbb: dotnet build WStation.sln -c " + konf);

            string dir = Path.Combine(Path.GetTempPath(), "wstation-onteszt-" + Path.GetRandomFileName());
            try
            {
                WStation.AdatGen.Generator.Write(dir);
                var psi = new ProcessStartInfo(exe, "--teszt --mappa \"" + dir + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                using var p = Process.Start(psi)!;
                var ki = p.StandardOutput.ReadToEndAsync();
                var hiba = p.StandardError.ReadToEndAsync();
                Assert.True(p.WaitForExit(600_000), "Az önteszt 10 perc alatt nem fejeződött be.");
                p.WaitForExit();

                string eredmeny = Path.Combine(dir, Layout.AppName, "teszt_eredmeny.txt");
                Assert.True(File.Exists(eredmeny), "Nincs eredményfájl: " + eredmeny + "\n" + ki.Result + hiba.Result);
                var kapott = Pontok(File.ReadAllLines(eredmeny, Encoding.UTF8));
                var vart = Pontok(File.ReadAllLines(Path.Combine(gyoker, "tests", "golden", "onteszt_2.2.8.txt"), Encoding.UTF8));
                Assert.Equal(vart, kapott);

                string osszesen = File.ReadAllLines(eredmeny, Encoding.UTF8).Single(l => l.StartsWith("Összesen:", StringComparison.Ordinal));
                Assert.Equal("Összesen: 39 rendben, 1 hiba, 4 kihagyva.", osszesen);
                Assert.Equal(1, p.ExitCode);   // a 2.2.8-on is 1: a felhőzet-becslés pontja a mintaadaton hibás (a mintaadat tulajdonsága)
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        // a pontok sorai a részletek („ – …”) nélkül, ahogy a tests/golden/onteszt_2.2.8.txt is rögzíti őket
        static List<string> Pontok(IEnumerable<string> sorok) => sorok
            .Where(l => l.StartsWith("[OK]", StringComparison.Ordinal) || l.StartsWith("[HIBA]", StringComparison.Ordinal) || l.StartsWith("[--]", StringComparison.Ordinal))
            .Select(l => { int i = l.IndexOf(" – ", StringComparison.Ordinal); return (i < 0 ? l : l.Substring(0, i)).TrimEnd(); })
            .ToList();
    }

    /// <summary>Fact, amely csak Windowson fut (az asztali program csak ott indítható).</summary>
    public sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Az asztali program csak Windowson fut."; }
    }
}
