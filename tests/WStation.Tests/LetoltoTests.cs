using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using WStation.Core;
using Xunit;

namespace WStation.Tests
{
    /// <summary>
    /// A Windows-letöltő (PLAN-3.0 1.7): a <c>WStation.Downloader</c> a 2.x <c>Letolto.cs</c>-ét változatlan logikával futtatja.
    /// A tesztek a <c>tests/LetoltoProba</c> indítóval futnak: az egy helyi, hamis WU-kiszolgálót indít kitalált válaszokkal, a
    /// letöltő kéréseit erre irányítja (internet nem kell, a letöltő kódja nem változik), és ugyanabban a folyamatban elindítja a
    /// letöltőt. Minden futás külön folyamat (a letöltő statikus állapota, a mutex és a kilépési kód így valódi).
    /// </summary>
    /// <remarks>
    /// A <c>Letolto228</c> mappa a 2.2.8 kimenete ugyanezekre a válaszokra: a <c>tests\LetoltoProba\forditas_228.cmd</c> a
    /// <c>release/2.x</c> forrásából, a régi fordítóval lefordítja a 2.2.8 letöltőjét és ugyanezt az indítót (.NET Framework 4.8);
    /// azzal futtatva ugyanezeket az eseteket keletkeztek a fájlok (2026-11-17). Az összevetés bájtra történik; csak a futás idejétől
    /// függő mezők (a pillanatnyi állapot „letoltve” oszlopa, a beállításfájlba írt „Új beállítások (dátum)” sor) vannak kitakarva.
    /// </remarks>
    public class LetoltoTests
    {
        internal const string Allomas = "ITESZT1";
        internal const string Kulcs = "TESZTKULCS0000000000000000000001";   // kitalált (17/11)

        [LetoltoFact]
        public void Hianyzo_kulcs_es_allomas_eseten_1_es_kod_es_a_beallitasfajl_sablonja_bajtra_a_2_2_8_e()
        {
            using var m = new Mappa();
            var (kod, ki) = Letolto.Fut(m.Ut, "ok");
            Assert.Equal(1, kod);
            Assert.Contains("Hiányzik az ApiKey vagy a StationId", ki);
            Assert.Equal(Letolto.Minta("kulcs", "appdata_letöltő", "beallitasok.ini"), File.ReadAllBytes(m.Fajl("appdata_letöltő", "beallitasok.ini")));
            Assert.Equal("", File.ReadAllText(m.Fajl("keresek.txt")));   // hálózatot nem ért el
            Assert.False(Directory.Exists(m.Fajl("data")) && Directory.GetFiles(m.Fajl("data")).Length > 0);
        }

        [LetoltoFact]
        public void Ervenytelen_kulcs_401_eseten_2_es_kod()
        {
            using var m = new Mappa();
            m.Ini();
            var (kod, ki) = Letolto.Fut(m.Ut, "401");
            Assert.Equal(2, kod);
            Assert.Contains("Az API kulcs érvénytelen vagy lejárt (401)", ki);
            // az első kérés (pillanatnyi állapot) után leáll
            Assert.Equal(new[] { "/v2/pws/observations/current?stationId=ITESZT1&format=json&units=m&numericPrecision=decimal&apiKey=<kulcs>" },
                         File.ReadAllLines(m.Fajl("keresek.txt")));
        }

        [LetoltoFact]
        public void Sikeres_letoltes_0_as_kod_es_a_CSV_fajlok_bajtra_a_2_2_8_eivel_egyeznek()
        {
            using var m = new Mappa();
            m.Ini();
            var (kod, ki) = Letolto.Fut(m.Ut, "ok");
            Assert.True(kod == 0, ki);
            Assert.Contains("Kész.", ki);
            Ellenoriz(m, "ok");

            // második futás: a friss lekérdezések még nem esedékesek, az előzmény-napok rendben vannak – nincs új hívás, a fájlok nem változnak
            var (kod2, ki2) = Letolto.Fut(m.Ut, "ok");
            Assert.True(kod2 == 0, ki2);
            Assert.Contains("a pillanatnyi és 24 órás lekérdezést kihagyom", ki2);
            Assert.Contains("nincs hiányzó nap a beállított időszakban", ki2);
            Ellenoriz(m, "ok", kereses: false);
        }

        [LetoltoFact]
        public void Mas_formatum_es_regi_fajl_tabulator_tizedespont_hibas_ertekek_bajtra_a_2_2_8_e()
        {
            using var m = new Mappa();
            m.Ini("CsvDelimiter=tab", "DecimalSeparator=.", "Units=e", "MaxCallsPerRun=abc", "SaveCurrent=nem");
            Directory.CreateDirectory(m.Fajl("data"));
            // régi, vesszővel tagolt fájl duplikátummal, hiányos sorral és idegen állomással (a 2.2.8 ugyanebből indult)
            File.WriteAllText(m.Fajl("data", "idojaras_adatok.csv"),
                "stationID,obsTimeLocal,obsTimeUtc,epoch,tz,tempHigh\r\n" +
                "ITESZT1,2026-09-01 11:55:00,2026-09-01T09:55:00Z,1788256500,Europe/Budapest,21.0\r\n" +
                "ITESZT1,2026-09-01 11:55:00,2026-09-01T09:55:00Z,1788256500,Europe/Budapest,21.1\r\n" +
                "ITESZT1,2026-09-01 12:00:00\r\n" +
                "IMASIK9,2026-09-01 12:00:00,2026-09-01T10:00:00Z,1788256800,Europe/Budapest,\"1,5\"\r\n", new UTF8Encoding(false));
            var (kod, ki) = Letolto.Fut(m.Ut, "ok");
            Assert.True(kod == 0, ki);
            Assert.Contains("Units=e értéke már nem támogatott", ki);
            Assert.Contains("Érvénytelen érték: MaxCallsPerRun=abc", ki);
            Assert.False(File.Exists(m.Fajl("data", "pillanatnyi_allapot.csv")));
            Ellenoriz(m, "formatum");
        }

        [LetoltoFact]
        public void Metar_kapcsoloval_a_felhozet_fajl_a_2_2_8_alakjaban()
        {
            using var m = new Mappa();
            m.Ini("MetarEllenorzes=true", "MetarKod=lhbp");
            var (kod, ki) = Letolto.Fut(m.Ut, "ok", new[] { "--metar" });
            Assert.True(kod == 0, ki);
            Assert.Equal(new[] { "/api/data/metar?ids=LHBP&format=json&hours=…" }, File.ReadAllLines(m.Fajl("keresek.txt")));
            byte[] b = File.ReadAllBytes(m.Fajl("data", "metar_LHBP.csv"));
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, b.Take(3).ToArray());
            var sorok = Encoding.UTF8.GetString(b, 3, b.Length - 3).Split("\r\n");
            Assert.Equal("epoch;obsTimeUtc;icao;cover;okta;cb;raw", sorok[0]);
            Assert.Equal("", sorok[^1]);
            // a hamis kiszolgáló a futás órájához mért 1–4 órával korábbi jelentéseket adja; az ismeretlen fedettségű kimarad
            long ora = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600 * 3600;
            var vart = new[]
            {
                (ora - 10800, "CAVOK;0.0;0;LHBP 010830Z 00000KT CAVOK 17/14 Q1014"),
                (ora - 7200, "OVC;8.0;0;LHBP 010930Z 21006KT 9999 OVC012 19/14 Q1014"),
                (ora - 3600, "SCT;6.0;1;LHBP 011030Z 22008KT 9999 FEW040CB SCT100 21/13 Q1013, NOSIG"),
            };
            var kapott = sorok.Skip(1).Take(sorok.Length - 2).ToArray();
            Assert.Equal(3, kapott.Length);
            for (int i = 0; i < 3; i++)
            {
                // ha a futás épp óraváltáskor volt, egy órával elcsúszhat
                var p = kapott[i].Split(';', 4);
                long t = long.Parse(p[0]);
                Assert.True(t == vart[i].Item1 || t == vart[i].Item1 - 3600, kapott[i]);
                Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(t).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"), p[1]);
                Assert.Equal("LHBP", p[2]);
                Assert.Equal(vart[i].Item2, p[3]);
            }
        }

        [LetoltoFact]
        public void Mentes_kapcsoloval_zip_a_2_2_8_tartalmaval()
        {
            using var m = new Mappa();
            m.Ini();
            Assert.Equal(0, Letolto.Fut(m.Ut, "ok").Kod);
            var (kod, ki) = Letolto.Fut(m.Ut, "ok", new[] { "--mentes", "--most" });
            Assert.True(kod == 0, ki);
            Assert.Contains("Biztonsági mentés kész", ki);
            var zip = Directory.GetFiles(m.Fajl("appdata_letöltő", "mentesek"), "mentes_*.zip").Single();
            using var z = ZipFile.OpenRead(zip);
            Assert.Equal(new[] { "data/idojaras_adatok.csv", "data/napi_osszesito.csv", "data/pillanatnyi_allapot.csv",
                                 "appdata_letöltő/beallitasok.ini", "appdata_letöltő/lekerdezes_allapot.json" },
                         z.Entries.Select(e => e.FullName).ToArray());
            using var s = z.GetEntry("data/idojaras_adatok.csv")!.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            Assert.Equal(Letolto.Minta("ok", "data", "idojaras_adatok.csv"), ms.ToArray());
        }

        [LetoltoFact]
        [SupportedOSPlatform("windows")]
        public void Irasi_jog_nelkul_5_os_kod()
        {
            using var m = new Mappa();
            var di = new DirectoryInfo(m.Ut);
            var sec = di.GetAccessControl();
            var szabaly = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
                FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories | FileSystemRights.WriteData | FileSystemRights.AppendData,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny);
            sec.AddAccessRule(szabaly);
            di.SetAccessControl(sec);
            try
            {
                // a kéréslista és a kimenet a tiltott mappán kívül
                var (kod, ki) = Letolto.Fut(m.Ut, "ok", kereslista: m.Ut + "-keresek.txt");
                Assert.Equal(5, kod);
                Assert.Contains("Nincs írási jogom ebben a mappában", ki);
            }
            finally
            {
                sec.RemoveAccessRule(szabaly);
                di.SetAccessControl(sec);
                try { File.Delete(m.Ut + "-keresek.txt"); } catch (IOException) { }   // takarítás; ha nem megy, a Temp mappában marad
            }
        }

        [LetoltoFact]
        public void Futo_peldany_mellett_megvarja_a_masikat()
        {
            using var m = new Mappa();
            m.Ini();
            using var mutex = new Mutex(true, Letolto.MutexNev(m.Ut));
            (int Kod, string Ki) r = default;
            var t = new Thread(() => r = Letolto.Fut(m.Ut, "ok"));
            t.Start();
            // megvárjuk, amíg a letöltő a mutexre vár (a naplóba írja), aztán elengedjük (a tulajdonos – ez a – szál engedheti el)
            string naplo = m.Fajl("appdata_letöltő", "lekerdezes_naplo.log");
            var sw = Stopwatch.StartNew();
            while (!(File.Exists(naplo) && Olvas(naplo).Contains("Épp fut egy másik letöltés")) && sw.Elapsed.TotalSeconds < 60) Thread.Sleep(200);
            mutex.ReleaseMutex();
            t.Join();
            Assert.True(r.Kod == 0, r.Ki);
            Assert.Contains("Épp fut egy másik letöltés (pl. ütemezett feladat), megvárom", r.Ki);
            Assert.Contains("Kész.", r.Ki);
        }

        static string Olvas(string f)
        {
            using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            return sr.ReadToEnd();
        }

        [Fact]
        public void A_mutex_nev_kisbetusitese_invarians_modban_is_a_2_2_8_e()
        {
            // a letöltő mutexének neve a mappa útvonalának ToLowerInvariant()-jából készül; a 2.2.8 (.NET Framework) a magyar
            // ékezetes nagybetűket is kisbetűsítette – ha a 3.0 (invariáns mód) nem tenné, a két változat nem látná egymást
            Assert.Equal("c:\\időjárás\\árvíztűrő tükörfúrógép", "C:\\IDŐJÁRÁS\\ÁRVÍZTŰRŐ TÜKÖRFÚRÓGÉP".ToLowerInvariant());
        }

        [Fact]
        public void Az_asztali_gazda_az_uj_letoltot_talalja_meg_a_regi_elott()
        {
            using var m = new Mappa();
            var l = Layout.For(m.Ut);
            Assert.Null(l.FindDownloader());
            File.WriteAllText(m.Fajl("Letöltő.exe"), "");
            Assert.Equal(m.Fajl("Letöltő.exe"), l.FindDownloader());
            File.WriteAllText(m.Fajl("WStation.Downloader.exe"), "");
            Assert.Equal(m.Fajl("WStation.Downloader.exe"), l.FindDownloader());
            Assert.Equal("WStation.Downloader.exe", Layout.DownloaderNames.First());
        }

        [LetoltoFact]
        public void A_forditas_kimeneteben_a_letolto_az_asztali_program_mellett_van()
        {
            string dir = Path.GetDirectoryName(Letolto.AsztaliExe())!;
            foreach (var f in new[] { "WStation.Desktop.exe", "WStation.Downloader.exe", "WStation.Downloader.dll", "WStation.Downloader.runtimeconfig.json",
                                      "WStation.Downloader.deps.json", "WStation.Core.dll" })
                Assert.True(File.Exists(Path.Combine(dir, f)), "Hiányzik a közös kimenetből: " + f);
            Assert.Equal(Path.Combine(dir, "WStation.Downloader.exe"), Layout.For(dir).FindDownloader());
        }

        static void Ellenoriz(Mappa m, string eset, bool kereses = true)
        {
            foreach (var f in new[] { "idojaras_adatok.csv", "napi_osszesito.csv" })
                Assert.True(Letolto.Minta(eset, "data", f).SequenceEqual(File.ReadAllBytes(m.Fajl("data", f))), eset + ": " + f + " eltér a 2.2.8-étól");
            string pill = Path.Combine(Letolto.MintaMappa(eset), "data", "pillanatnyi_allapot.csv");
            if (File.Exists(pill))
                Assert.Equal(Letolto.LetoltveNelkul(File.ReadAllText(pill, Encoding.UTF8)), Letolto.LetoltveNelkul(File.ReadAllText(m.Fajl("data", "pillanatnyi_allapot.csv"), Encoding.UTF8)));
            Assert.Equal(Letolto.DatumNelkul(Letolto.Minta(eset, "appdata_letöltő", "beallitasok.ini")), Letolto.DatumNelkul(File.ReadAllBytes(m.Fajl("appdata_letöltő", "beallitasok.ini"))));
            Assert.Equal(kereses ? File.ReadAllText(Path.Combine(Letolto.MintaMappa(eset), "keresek.txt")) : "", File.ReadAllText(m.Fajl("keresek.txt")));
        }
    }

    /// <summary>A hosszabb (várakozó) esetek külön osztályban, hogy a többivel párhuzamosan fussanak.</summary>
    public class LetoltoLassuTests
    {
        [LetoltoFact]
        public void Zarolt_CSV_eseten_3_as_kod()
        {
            using var m = new Mappa();
            m.Ini();
            Assert.Equal(0, Letolto.Fut(m.Ut, "ok").Kod);
            using (new FileStream(m.Fajl("data", "idojaras_adatok.csv"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var (kod, ki) = Letolto.Fut(m.Ut, "ok");   // 4 próbálkozás 3 másodperces szünetekkel
                Assert.Equal(3, kod);
                Assert.Contains("Részletes mérések: nem tudom beolvasni a(z) idojaras_adatok.csv fájlt", ki);
            }
        }

        [LetoltoFact]
        public void Halozati_hiba_eseten_6_os_kod()
        {
            using var m = new Mappa();
            // csak egy kérés legyen esedékes (egy előzmény-nap), hogy a 4 próbálkozás szünetei (5 + 10 + 15 s) ne nyúljanak hosszúra:
            // a friss lekérdezés most volt (állapotfájl), a napi összesítő és a pillanatnyi állapot ki van kapcsolva
            m.Ini("BackfillDays=1", "MinMinutesBetweenRecent=60", "SaveCurrent=false", "SaveDailySummary=false");
            Directory.CreateDirectory(m.Fajl("data"));
            File.WriteAllText(m.Fajl("data", "idojaras_adatok.csv"),
                "stationID;obsTimeLocal;obsTimeUtc;epoch;tz\r\nITESZT1;2026-09-01 12:00:00;2026-09-01T10:00:00Z;1788256800;Europe/Budapest\r\n", new UTF8Encoding(true));
            File.WriteAllText(m.Fajl("appdata_letöltő", "lekerdezes_allapot.json"),
                "{\"stations\":{\"ITESZT1\":{\"lastRecentUtc\":\"" + DateTime.UtcNow.ToString("o") + "\",\"tzOffsetMin\":120,\"days\":{}}}}", new UTF8Encoding(false));
            var (kod, ki) = Letolto.Fut(m.Ut, "halozat");
            Assert.True(kod == 6, ki);
            Assert.Contains("hálózati hiba", ki);
            Assert.Contains("lekérdezés hálózati hiba miatt elmaradt", ki);
        }

        /// <summary>
        /// A „már futó példány” valódi 4-es kódja: a letöltő 15 percig vár a másikra. Csak kérésre fut
        /// (<c>WSTATION_HOSSZU_TESZT=1</c>); 2026-11-17-én a 2.2.8-cal párhuzamosan lefuttatva mindkettő 4-gyel lépett ki.
        /// </summary>
        [LetoltoFact]
        public void Futo_peldany_15_perc_utan_4_es_kod()
        {
            if (Environment.GetEnvironmentVariable("WSTATION_HOSSZU_TESZT") != "1") return;
            using var m = new Mappa();
            m.Ini();
            using var mutex = new Mutex(true, Letolto.MutexNev(m.Ut));
            var (kod, ki) = Letolto.Fut(m.Ut, "ok", idokorlatPerc: 20);
            mutex.ReleaseMutex();
            Assert.True(kod == 4, ki);
            Assert.Contains("A másik letöltés 15 perc alatt sem fejeződött be", ki);
        }
    }

    /// <summary>Csak Windowson: a letöltő Windows-program (WinForms-értesítés, Windows-mutex).</summary>
    public sealed class LetoltoFactAttribute : FactAttribute
    {
        public LetoltoFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "A letöltő csak Windowson fut."; }
    }

    /// <summary>Ideiglenes mappa (a letöltő „--mappa” kapcsolójához); a végén törlődik.</summary>
    sealed class Mappa : IDisposable
    {
        public string Ut { get; } = Path.Combine(Path.GetTempPath(), "wstation-letolto-" + Path.GetRandomFileName());
        public Mappa() { Directory.CreateDirectory(Ut); }
        public string Fajl(params string[] r) => Path.Combine(new[] { Ut }.Concat(r).ToArray());

        /// <summary>Beállításfájl a kitalált kulccsal és állomással (gyors futáshoz: 2 nap, szünet nélkül, mentés nélkül).</summary>
        public void Ini(params string[] tovabbi)
        {
            Directory.CreateDirectory(Fajl("appdata_letöltő"));
            var sorok = new[] { "ApiKey=" + LetoltoTests.Kulcs, "StationId=" + LetoltoTests.Allomas, "BackfillDays=2", "CallDelayMs=0", "MentesNaponta=0" }.Concat(tovabbi);
            File.WriteAllText(Fajl("appdata_letöltő", "beallitasok.ini"), string.Join("\r\n", sorok) + "\r\n", new UTF8Encoding(true));
        }

        public void Dispose()
        {
            try { Directory.Delete(Ut, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }   // takarítás; a Temp mappában maradhat
        }
    }

    static class Letolto
    {
        static string RepoGyoker()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "WStation.sln"))) return d.FullName;
            throw new DirectoryNotFoundException("Nem találom a repó gyökerét (WStation.sln).");
        }

        // a teszt szerelvénnyel azonos konfiguráció (Debug / Release) kimenete; a megoldás fordítása készíti el
        static string Konf => AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Debug" + Path.DirectorySeparatorChar) ? "Debug" : "Release";

        static string Indito()
        {
            string exe = Path.Combine(RepoGyoker(), "tests", "LetoltoProba", "bin", Konf, "net10.0-windows", "LetoltoProba.exe");
            Assert.True(File.Exists(exe), "Nincs meg a próbaindító: " + exe + " – előbb: dotnet build WStation.sln -c " + Konf);
            return exe;
        }

        public static string AsztaliExe()
        {
            string exe = Path.Combine(RepoGyoker(), "src", "WStation.Desktop", "bin", Konf, "net10.0-windows", "WStation.Desktop.exe");
            Assert.True(File.Exists(exe), "Nincs meg az asztali program: " + exe + " – előbb: dotnet build WStation.sln -c " + Konf);
            return exe;
        }

        public static string MintaMappa(string eset) => Path.Combine(RepoGyoker(), "tests", "WStation.Tests", "Letolto228", eset);
        public static byte[] Minta(string eset, params string[] r) => File.ReadAllBytes(Path.Combine(new[] { MintaMappa(eset) }.Concat(r).ToArray()));

        /// <summary>A letöltő futtatása a próbaindítóval; a kimenet (konzol) UTF-8, mint amikor a Megjelenítő olvassa.</summary>
        public static (int Kod, string Ki) Fut(string mappa, string forgatokonyv, string[]? kapcsolok = null, string? kereslista = null, int idokorlatPerc = 3)
        {
            var psi = new ProcessStartInfo(Indito())
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add("WStation.Downloader");
            psi.ArgumentList.Add(forgatokonyv);
            psi.ArgumentList.Add(kereslista ?? Path.Combine(mappa, "keresek.txt"));
            foreach (var k in kapcsolok ?? Array.Empty<string>()) psi.ArgumentList.Add(k);
            psi.ArgumentList.Add("--nowait");
            psi.ArgumentList.Add("--mappa");
            psi.ArgumentList.Add(mappa);
            if (kereslista == null) File.WriteAllText(Path.Combine(mappa, "keresek.txt"), "");
            using var p = Process.Start(psi)!;
            p.StandardInput.Close();
            var ki = p.StandardOutput.ReadToEndAsync();
            var hiba = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(idokorlatPerc * 60_000))
            {
                try { p.Kill(true); } catch (InvalidOperationException) { }   // már kilépett
                Assert.Fail("A letöltő " + idokorlatPerc + " perc alatt nem fejeződött be.");
            }
            p.WaitForExit();
            return (p.ExitCode, ki.Result + hiba.Result);
        }

        /// <summary>A letöltő mutexének neve (Letolto.cs: „WuPwsLetolto_” + FNV-1a a kisbetűs teljes útvonalra).</summary>
        public static string MutexNev(string mappa)
        {
            uint h = 2166136261;
            foreach (char c in Path.GetFullPath(mappa).ToLowerInvariant()) { h ^= c; h *= 16777619; }
            return "WuPwsLetolto_" + h.ToString("x8");
        }

        /// <summary>A pillanatnyi állapot „letoltve” oszlopa a letöltés ideje – az összevetésből kimarad.</summary>
        public static string LetoltveNelkul(string csv) => Regex.Replace(csv, @"(?m)^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d;", "<letoltve>;");

        /// <summary>A beállításfájl bővítésekor beírt „# --- Új beállítások (dátum) ---” sor dátuma kimarad az összevetésből.</summary>
        public static string DatumNelkul(byte[] ini) => Regex.Replace(Encoding.UTF8.GetString(ini), @"# --- Új beállítások \(\d{4}-\d\d-\d\d\) ---", "# --- Új beállítások (<dátum>) ---");
    }
}
