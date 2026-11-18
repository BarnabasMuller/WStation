using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using WStation.Core;
using WStation.Core.Common;
using Xunit;

namespace WStation.Tests
{
    /// <summary>
    /// Golden összevetés (PLAN-3.0 14.2/3): a <c>tests/golden/hivasok.json</c> minden hívása a 3.0 magon (<c>Api.Handle</c>), a generált
    /// mintaadaton (<c>tools/adatgen</c>), és a válasz szerkezeti összevetése a 2.2.8 rögzített válaszával: a kulcsok neve és sorrendje,
    /// a számok számként, a szövegek és logikai értékek pontosan, az állapotkód is. Az időfüggő mezők (<c>hivasok.json</c> › <c>idofuggo</c>)
    /// név szerint kimaradnak, kivéve a <c>now.ageMin</c>-t, amely rögzített órával (<c>Clock.Freeze</c>) egyezik; az ismert, indokolt eltérések az <see cref="IsmertElteresek"/> listában.
    /// </summary>
    public class GoldenOsszevetesTests
    {
        // Indokolt eltérések a 2.2.8-tól: (hívás, mező) → ok. Mindegyiket a PR leírása is felsorolja.
        static readonly Dictionary<(string Id, string Mezo), string> IsmertElteresek = new()
        {
            { ("update", "current"), "a futó program verziója: a mag szerelvényének verziója (3.0), nem 2.2.8" },
        };

        public static IEnumerable<object[]> Hivasok() => GoldenMinta.Hivasok().Select(h => new object[] { h.Id });

        [GoldenFact]
        public void A_generalt_mintaadat_ellenorzoosszege_egyezik_a_rogzitettel()
        {
            Assert.True(GoldenMinta.Egyezik, GoldenMinta.Uzenet);
        }

        [GoldenTheory]
        [MemberData(nameof(Hivasok))]
        public void A_valasz_egyezik_a_2_2_8_valaszaval(string id)
        {
            var h = GoldenMinta.Hivasok().Single(x => x.Id == id);
            var ws = GoldenMinta.Betolt();
            int status;
            string body;
            // rögzített óra: a mintaadat utolsó mérésének ideje (today.now.t), így az ageMin a rögzített 0
            using (Ctx.Enter(ws))
            using (Clock.Freeze(GoldenMinta.UtolsoMeresUtc))
                body = Api.Handle(h.Utvonal, ParseQuery(h.Lekerdezes), "GET", out status);
            body = Normalizal(body ?? "", GoldenMinta.Mappa);

            Assert.Equal(h.Allapot, status);
            string vart = File.ReadAllText(Path.Combine(GoldenMinta.GoldenDir, h.Fajl), Encoding.UTF8);
            using var a = JsonDocument.Parse(vart);
            using var b = JsonDocument.Parse(body);
            // az ageMin rögzített órával egyezik a golden fájllal, ezért nem hagyjuk ki; a loadMs (Stopwatch) és a loadedAt marad
            var kihagy = new HashSet<string>(GoldenMinta.Idofuggo().Where(x => x.Id == id && x.Mezo != "now.ageMin").Select(x => x.Mezo));
            foreach (var k in IsmertElteresek.Keys.Where(k => k.Id == id)) kihagy.Add(k.Mezo);
            var elteres = new List<string>();
            Osszevet(a.RootElement, b.RootElement, "", kihagy, elteres);
            Assert.True(elteres.Count == 0,
                id + ": " + elteres.Count + " eltérés a 2.2.8 válaszától:\n" + string.Join("\n", elteres.Take(25)) + (elteres.Count > 25 ? "\n…" : ""));
        }

        // a rögzítő (tools/golden/Rogzito.cs) szabálya szerint: "a=b&c=d", dekódolás nélkül
        static NameValueCollection ParseQuery(string q)
        {
            var c = new NameValueCollection();
            foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq < 0) c[part] = ""; else c[part.Substring(0, eq)] = part.Substring(eq + 1);
            }
            return c;
        }

        // a mintamappa (gépfüggő) útvonala helyett „<MAPPA>” – mint a rögzítőben
        static string Normalizal(string body, string dir)
        {
            foreach (var form in new[] { dir.Replace("\\", "\\\\"), dir, dir.Replace('\\', '/') })
                body = Regex.Replace(body, Regex.Escape(form), GoldenMinta.MappaJel, RegexOptions.IgnoreCase);
            return body;
        }

        /// <summary>Szerkezeti összevetés: a kulcsok sorrendje is számít, a számok számként.</summary>
        internal static void Osszevet(JsonElement a, JsonElement b, string path, HashSet<string> kihagy, List<string> elteres)
        {
            if (kihagy.Contains(path)) return;
            string hol = path.Length == 0 ? "(gyökér)" : path;
            if (a.ValueKind != b.ValueKind)
            {
                elteres.Add(hol + ": típus " + a.ValueKind + " ≠ " + b.ValueKind + " (várt: " + Rovid(a) + ", kapott: " + Rovid(b) + ")");
                return;
            }
            switch (a.ValueKind)
            {
                case JsonValueKind.Object:
                {
                    var ka = a.EnumerateObject().Select(p => p.Name).ToList();
                    var kb = b.EnumerateObject().Select(p => p.Name).ToList();
                    if (!ka.SequenceEqual(kb))
                    {
                        elteres.Add(hol + ": kulcsok (név vagy sorrend) – várt: [" + string.Join(",", ka) + "], kapott: [" + string.Join(",", kb) + "]");
                        return;
                    }
                    foreach (var p in a.EnumerateObject())
                        Osszevet(p.Value, b.GetProperty(p.Name), path.Length == 0 ? p.Name : path + "." + p.Name, kihagy, elteres);
                    return;
                }
                case JsonValueKind.Array:
                {
                    int na = a.GetArrayLength(), nb = b.GetArrayLength();
                    if (na != nb) { elteres.Add(hol + ": tömbhossz " + na + " ≠ " + nb); return; }
                    int i = 0;
                    using (var ea = a.EnumerateArray().GetEnumerator())
                    using (var eb = b.EnumerateArray().GetEnumerator())
                        while (ea.MoveNext() && eb.MoveNext()) { Osszevet(ea.Current, eb.Current, path + "[" + i + "]", kihagy, elteres); i++; }
                    return;
                }
                case JsonValueKind.Number:
                {
                    // a két futtatókörnyezet ugyanazt a double-t néha más szöveggel írja ki (pl. 17 vagy 15 jegy): számként egyezzen
                    double x = a.GetDouble(), y = b.GetDouble();
                    if (!x.Equals(y)) elteres.Add(hol + ": " + a.GetRawText() + " ≠ " + b.GetRawText());
                    return;
                }
                case JsonValueKind.String:
                    if (a.GetString() != b.GetString()) elteres.Add(hol + ": " + Rovid(a) + " ≠ " + Rovid(b));
                    return;
                default:   // true, false, null
                    return;
            }
        }

        static string Rovid(JsonElement e)
        {
            string s = e.GetRawText();
            return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
        }
    }

    /// <summary>
    /// A golden összevetés környezete: a generált mintaadat egy ideiglenes mappában, az ellenőrzőösszege, és a betöltött mag.
    /// A mintaadat ellenőrzőösszegét a <c>tests/golden/mintaadat.sha256</c>-hoz veti. Ha nem Windowson eltér (a generátor
    /// <c>Math.Sin</c> / <c>Math.Cos</c> hívásai platformonként az utolsó jegyben eltérhetnek), az összevetés ki van hagyva –
    /// másik adaton a 2.2.8 válaszai nem érvényesek, és ez nem a mag hibája. Windowson az eltérés hiba.
    /// </summary>
    static class GoldenMinta
    {
        public const string MappaJel = "<MAPPA>";

        public sealed record Hivas(string Id, string Utvonal, string Lekerdezes, int Allapot, string Fajl);

        public static string GoldenDir { get; } = KeresGoldenDir();

        static string KeresGoldenDir()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "WStation.sln"))) return Path.Combine(d.FullName, "tests", "golden");
            throw new DirectoryNotFoundException("Nem találom a repó gyökerét (WStation.sln).");
        }

        public static List<Hivas> Hivasok()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenDir, "hivasok.json"), Encoding.UTF8));
            return doc.RootElement.GetProperty("hivasok").EnumerateArray().Select(h => new Hivas(
                h.GetProperty("id").GetString()!, h.GetProperty("utvonal").GetString()!, h.GetProperty("lekerdezes").GetString()!,
                h.GetProperty("allapot").GetInt32(), h.GetProperty("fajl").GetString()!)).ToList();
        }

        /// <summary>A mintaadat utolsó mérésének ideje (a golden <c>today.json</c> › <c>now.t</c>, ms): erre rögzítjük az órát.</summary>
        public static DateTime UtolsoMeresUtc
        {
            get
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenDir, "today.json"), Encoding.UTF8));
                long ms = doc.RootElement.GetProperty("now").GetProperty("t").GetInt64();
                return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
            }
        }

        public static List<(string Id, string Mezo)> Idofuggo()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenDir, "hivasok.json"), Encoding.UTF8));
            return doc.RootElement.GetProperty("idofuggo").EnumerateArray()
                .Select(x => (x.GetProperty("id").GetString()!, x.GetProperty("mezo").GetString()!)).ToList();
        }

        static readonly Lazy<(string Mappa, bool Egyezik, string Uzenet)> Minta = new(General, LazyThreadSafetyMode.ExecutionAndPublication);

        public static string Mappa => Minta.Value.Mappa;
        public static bool Egyezik => Minta.Value.Egyezik;
        public static string Uzenet => Minta.Value.Uzenet;

        /// <summary>Ki kell-e hagyni az összevetést (nem Windowson, eltérő mintaadatnál); az ok, vagy <c>null</c>.</summary>
        public static string? KihagyasOka
        {
            get
            {
                if (OperatingSystem.IsWindows()) return null;
                if (!Egyezik) return Uzenet + " – ezen a rendszeren az összevetés kimarad.";
                // a 2.x Tz a Windows-azonosítóval keresi a zónát (Central Europe Standard Time); ha ez itt nem oldható fel,
                // a mag a gép zónájára esne vissza, és minden napi érték eltolódna. Az IANA-név elsőbbsége a 7.5 dolga, nem az 1.5-é.
                if (!TimeZoneInfo.TryFindSystemTimeZoneById("Central Europe Standard Time", out _))
                    return "A „Central Europe Standard Time” időzóna ezen a rendszeren nem érhető el (" + RuntimeInfo()
                        + "); a 2.x Tz Windows-azonosítóval keres, az IANA-név elsőbbsége a PLAN-3.0 7.5 dolga – az összevetés kimarad.";
                return null;
            }
        }

        static (string, bool, string) General()
        {
            // 2.1: a közös tesztgyökér alatt (a Guard gyökere folyamatszintű; a munkaterület-tesztek mappái is itt vannak)
            string dir = TesztKornyezet.UjMappa("golden");
            WStation.AdatGen.Generator.Write(dir);
            string kapott = Osszegek(dir);
            string vart = File.ReadAllText(Path.Combine(GoldenDir, "mintaadat.sha256"), Encoding.UTF8).Replace("\r", "");
            if (kapott == vart) return (dir, true, "A generált mintaadat egyezik a rögzítettel.");
            var kl = kapott.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var vl = vart.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            string elt = string.Join("; ", vl.Where(v => !kl.Contains(v)).Select(v => v.Substring(v.IndexOf("  ", StringComparison.Ordinal) + 2)));
            return (dir, false, "A generált mintaadat ellenőrzőösszege eltér a tests/golden/mintaadat.sha256-tól (" + RuntimeInfo() + "; eltérő fájl: "
                + (elt.Length > 0 ? elt : "?") + "). A 2.2.8 válaszai erre az adatra nem érvényesek.");
        }

        static string RuntimeInfo() => System.Runtime.InteropServices.RuntimeInformation.OSDescription + ", " + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;

        // a rögzítő Osszegek() függvénye szerint: data\*.csv és appdata_letöltő\*.ini, a relatív út szerint rendezve
        static string Osszegek(string dir)
        {
            var files = Directory.GetFiles(Path.Combine(dir, Layout.DataName), "*.csv")
                .Concat(Directory.GetFiles(Path.Combine(dir, Layout.DlAppName), "*.ini"))
                .Select(f => (f, rel: Path.GetRelativePath(dir, f).Replace('\\', '/')))
                .OrderBy(x => x.rel, StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (var (f, rel) in files)
                sb.Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))).ToLowerInvariant()).Append("  ").Append(rel).Append('\n');
            return sb.ToString();
        }

        static readonly object BetoltZar = new();
        static WorkspaceContext? ws;

        /// <summary>A mag elindítása a mintaadaton – ugyanúgy, mint a rögzítőben (ablak és szerver nélkül). A munkaterületét adja vissza.</summary>
        public static WorkspaceContext Betolt()
        {
            lock (BetoltZar)
            {
                if (ws != null) return ws;
                Assert.True(Egyezik || !OperatingSystem.IsWindows(), Uzenet);
                // 2.1: a mintaadat egy munkaterület; a hívások ebbe lépnek be (Ctx.Enter)
                var w = TesztKornyezet.Betolt(Mappa);
                Thread.Sleep(500);   // az induláskori mentés-ellenőrzés (háttérszál) fusson le, mielőtt hívni kezdünk – mint a rögzítőben
                ws = w;
                return ws;
            }
        }
    }

    /// <summary>Fact, amely nem Windowson, eltérő mintaadatnál indoklással kimarad.</summary>
    public sealed class GoldenFactAttribute : FactAttribute
    {
        public GoldenFactAttribute() { string? ok = GoldenMinta.KihagyasOka; if (ok != null) Skip = ok; }
    }

    /// <summary>Theory, amely nem Windowson, eltérő mintaadatnál indoklással kimarad.</summary>
    public sealed class GoldenTheoryAttribute : TheoryAttribute
    {
        public GoldenTheoryAttribute() { string? ok = GoldenMinta.KihagyasOka; if (ok != null) Skip = ok; }
    }
}
