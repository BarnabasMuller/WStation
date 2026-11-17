using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace WStation.Tests
{
    /// <summary>A rögzített golden fájlok épsége (PLAN-3.0 14.2). Az összevetés a 3.0 útválasztójával az 1.5 csomagban készül.</summary>
    public class GoldenFajlokTests
    {
        static string GoldenDir()
        {
            // a repó gyökere: ahol a WStation.sln van
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "WStation.sln"))) return Path.Combine(d.FullName, "tests", "golden");
            throw new DirectoryNotFoundException("Nem találom a repó gyökerét (WStation.sln).");
        }

        [Fact]
        public void Minden_hivashoz_van_valaszfajl_es_ervenyes_JSON()
        {
            string dir = GoldenDir();
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "hivasok.json")));
            var ids = new HashSet<string>();
            foreach (var h in doc.RootElement.GetProperty("hivasok").EnumerateArray())
            {
                string id = h.GetProperty("id").GetString() ?? "";
                Assert.True(ids.Add(id), "ismétlődő azonosító: " + id);
                Assert.StartsWith("/api/", h.GetProperty("utvonal").GetString());
                string file = Path.Combine(dir, h.GetProperty("fajl").GetString() ?? "");
                Assert.True(File.Exists(file), "hiányzó válaszfájl: " + id);
                using var valasz = JsonDocument.Parse(File.ReadAllText(file));   // érvénytelen JSON-nál kivétel
            }
            Assert.True(ids.Count >= 60, "túl kevés rögzített hívás: " + ids.Count);
        }

        [Fact]
        public void Nincs_gazdatlan_valaszfajl()
        {
            string dir = GoldenDir();
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "hivasok.json")));
            var files = new HashSet<string>(StringComparer.Ordinal) { "hivasok.json" };
            foreach (var h in doc.RootElement.GetProperty("hivasok").EnumerateArray()) files.Add(h.GetProperty("fajl").GetString() ?? "");
            foreach (var f in Directory.GetFiles(dir, "*.json"))
                Assert.Contains(Path.GetFileName(f), files);
        }

        [Fact]
        public void A_golden_mappaban_nincs_gepfuggo_utvonal()
        {
            foreach (var f in Directory.GetFiles(GoldenDir(), "*.json"))
            {
                string s = File.ReadAllText(f);
                Assert.DoesNotContain(":\\\\", s);          // "C:\..." JSON-ban
                Assert.DoesNotContain("Users", s);
            }
        }
    }
}
