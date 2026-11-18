using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WStation.Core;
using WStation.Core.Common;
using Xunit;

namespace WStation.Tests
{
    /// <summary>
    /// 2.1 – <c>Ctx</c> és <c>WorkspaceContext</c> (PLAN-3.0 6.2, 16. S2): két munkaterület egy folyamatban nem keveredik, belépés nélkül
    /// a környezet kivételt dob, a háttérmunka és a helyi szerver a saját munkaterületén fut.
    /// </summary>
    public class MunkateruletTests
    {
        // ---------------------------------------------------------------- két munkaterület (közös, egyszer betöltve)

        /// <summary>
        /// A: a generált mintaadat változatlanul. B: ugyanez, de csak a mérések első fele, más időzónával, más hiányküszöbbel és helynévvel –
        /// így az A és a B válasza minden vizsgált hívásban eltér, és a keveredés látszik.
        /// </summary>
        static readonly Lazy<(WorkspaceContext A, WorkspaceContext B)> Ketto = new(() =>
        {
            string a = TesztKornyezet.UjMappa("ws-a"), b = TesztKornyezet.UjMappa("ws-b");
            WStation.AdatGen.Generator.Write(a);
            WStation.AdatGen.Generator.Write(b);
            string obs = Path.Combine(b, Layout.DataName, "idojaras_adatok.csv");
            var sorok = File.ReadAllLines(obs, Encoding.UTF8);
            File.WriteAllLines(obs, sorok.Take(1 + (sorok.Length - 1) / 2), new UTF8Encoding(true));
            var lb = Layout.For(b);
            lb.Ensure();
            File.WriteAllText(lb.Settings, "Idozona=America/New_York\r\nHianyPerc=60\r\nHelyNev=Masikfalu\r\n", new UTF8Encoding(true));
            return (TesztKornyezet.Betolt(a), TesztKornyezet.Betolt(b));
        }, LazyThreadSafetyMode.ExecutionAndPublication);

        // időtől nem függő (vagy rögzített órával determinisztikus) olvasó hívások; a status a mappa útvonalát is visszaadja
        static readonly (string Utvonal, string Lekerdezes)[] Hivasok =
        {
            ("/api/status", ""),
            ("/api/days", ""),
            ("/api/series", "n=300"),
            ("/api/records", ""),
            ("/api/hourly", "metric=temp"),
            ("/api/quality", ""),
            ("/api/cloud/days", ""),
            ("/api/drought", ""),
            ("/api/today", ""),
        };

        static readonly DateTime RogzitettOra = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        static string Hiv(string utvonal, string lekerdezes)
        {
            var q = new NameValueCollection();
            foreach (var p in lekerdezes.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int i = p.IndexOf('=');
                q.Add(p.Substring(0, i), p.Substring(i + 1));
            }
            string body = Api.Handle(utvonal, q, "GET", out int status);
            Assert.Equal(200, status);
            return body;
        }

        [Fact]
        public void Ket_munkaterulet_parhuzamos_keresei_1000_ismetlesben_sem_keverednek()
        {
            var (a, b) = Ketto.Value;
            using (Clock.Freeze(RogzitettOra))
            {
                // a várt válaszok: egymás után, a saját munkaterületen
                var vartA = new string[Hivasok.Length];
                var vartB = new string[Hivasok.Length];
                for (int h = 0; h < Hivasok.Length; h++)
                {
                    using (Ctx.Enter(a)) vartA[h] = Hiv(Hivasok[h].Utvonal, Hivasok[h].Lekerdezes);
                    using (Ctx.Enter(b)) vartB[h] = Hiv(Hivasok[h].Utvonal, Hivasok[h].Lekerdezes);
                    Assert.True(vartA[h] != vartB[h], "A két munkaterület válasza nem különbözik, a keveredés így nem látszana: " + Hivasok[h].Utvonal);
                }

                // 1000 kérés párhuzamosan, véletlen (rögzített magú) sorrendben, a két munkaterület között váltogatva
                var rnd = new Random(20261118);
                var terv = Enumerable.Range(0, 1000).Select(_ => (B: rnd.Next(2) == 1, H: rnd.Next(Hivasok.Length))).ToArray();
                var hibak = new System.Collections.Concurrent.ConcurrentBag<string>();
                Parallel.For(0, terv.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount) }, i =>
                {
                    var (melyikB, h) = terv[i];
                    var ws = melyikB ? b : a;
                    using (Ctx.Enter(ws))
                    {
                        string body = Hiv(Hivasok[h].Utvonal, Hivasok[h].Lekerdezes);
                        string vart = melyikB ? vartB[h] : vartA[h];
                        if (body != vart) hibak.Add(i + ". kérés (" + (melyikB ? "B" : "A") + ", " + Hivasok[h].Utvonal + "): eltérő válasz");
                        if (!ReferenceEquals(Ctx.Current, ws) || AppState.L.Base != ws.L.Base) hibak.Add(i + ". kérés: a környezet a kérés közben megváltozott");
                    }
                });
                Assert.True(hibak.IsEmpty, string.Join("\n", hibak.Take(20)));
            }
        }

        [Fact]
        public void A_munkateruletek_allapota_kulon_van()
        {
            var (a, b) = Ketto.Value;
            Assert.NotSame(a.Data, b.Data);
            Assert.True(a.Data.N > b.Data.N);
            Assert.NotEqual(a.L.Base, b.L.Base);
            Assert.NotSame(a.S, b.S);
            Assert.Equal(900, a.GapSec);    // HianyPerc=15 (alap)
            Assert.Equal(3600, b.GapSec);   // HianyPerc=60
            Assert.Equal("Europe/Budapest", a.Iana);
            // a B időzónája csak ott áll át, ahol a Windows-azonosító feloldható (a 2.x Tz így keres – PLAN-3.0 7.5)
            if (TimeZoneInfo.TryFindSystemTimeZoneById("Eastern Standard Time", out _)) Assert.Equal("America/New_York", b.Iana);
        }

        [Fact]
        public void A_hattermunka_a_sajat_munkateruleten_fut()
        {
            var (a, b) = Ketto.Value;
            // külön munkaterület külön mappában, hogy az újratöltés a közös (A, B) tesztekkel ne ütközzön
            string dir = TesztKornyezet.UjMappa("ws-c");
            WStation.AdatGen.Generator.Write(dir);
            var c = TesztKornyezet.Betolt(dir);
            var regiC = c.Data;
            var regiB = b.Data;
            using (Ctx.Enter(c)) AppState.ReloadAsync();
            // a hívó szál környezete visszaállt (itt nincs munkaterület)
            Assert.False(Ctx.HasCurrent);
            for (int i = 0; i < 1200 && (c.Data == regiC || c.Status != "ready"); i++) Thread.Sleep(50);
            Assert.Equal("ready", c.Status);
            Assert.NotSame(regiC, c.Data);   // a háttérbetöltés a C-be töltött
            Assert.Same(regiB, b.Data);      // a B-hez nem nyúlt
            Assert.Equal("ready", b.Status);
        }

        // ---------------------------------------------------------------- belépés nélkül

        static T UresKornyezetben<T>(Func<T> f)
        {
            // új szál, a hívó végrehajtási környezete nélkül: biztosan nincs belépett munkaterület
            T res = default!;
            Exception? hiba = null;
            using (ExecutionContext.SuppressFlow())
            {
                var t = new Thread(() => { try { res = f(); } catch (Exception ex) { hiba = ex; } });
                t.Start();
                t.Join();
            }
            if (hiba != null) throw hiba;
            return res;
        }

        [Fact]
        public void Belepes_nelkul_a_kornyezet_kivetelt_dob()
        {
            Assert.False(UresKornyezetben(() => Ctx.HasCurrent));
            var ex = Assert.Throws<InvalidOperationException>(() => UresKornyezetben(() => Ctx.Current));
            Assert.Contains("Nincs munkaterület-környezet", ex.Message);
            Assert.Throws<InvalidOperationException>(() => UresKornyezetben(() => AppState.L));
            Assert.Throws<InvalidOperationException>(() => UresKornyezetben(() => AppState.Status));
            Assert.Throws<InvalidOperationException>(() => UresKornyezetben(() => AppState.Data));
            Assert.Throws<InvalidOperationException>(() => UresKornyezetben(() => Tz.Zone));
            Assert.Throws<InvalidOperationException>(() => UresKornyezetben(() => Tz.Local(0)));
            Assert.Throws<InvalidOperationException>(() => UresKornyezetben(() => DroughtStore.RepairNote));
            Assert.Throws<InvalidOperationException>(() => UresKornyezetben(() => { AppState.RefreshAsync(false); return 0; }));
            Assert.Throws<ArgumentNullException>(() => Ctx.Enter(null!));
        }

        [Fact]
        public void A_belepes_beagyazhato_es_kilepeskor_visszaall()
        {
            UresKornyezetben(() =>
            {
                var x = new WorkspaceContext(Layout.For(Path.Combine(Path.GetTempPath(), "x")));
                var y = new WorkspaceContext(Layout.For(Path.Combine(Path.GetTempPath(), "y")));
                using (Ctx.Enter(x))
                {
                    Assert.Same(x, Ctx.Current);
                    var belso = Ctx.Enter(y);
                    Assert.Same(y, Ctx.Current);
                    Assert.Same(y.L, AppState.L);
                    belso.Dispose();
                    belso.Dispose();   // kétszeri eldobás nem lép ki kétszer
                    Assert.Same(x, Ctx.Current);
                    // az új szál örökli
                    WorkspaceContext? lat = null;
                    var t = new Thread(() => lat = Ctx.Current);
                    t.Start(); t.Join();
                    Assert.Same(x, lat);
                }
                Assert.False(Ctx.HasCurrent);
                return 0;
            });
        }

        [Fact]
        public void Uj_munkaterulet_kezdoallapota_a_2x_kezdoertekei()
        {
            var w = new WorkspaceContext(Layout.For(Path.Combine(Path.GetTempPath(), "z")));
            Assert.Equal("loading", w.Status);
            Assert.Equal("", w.Message);
            Assert.Equal("", w.Progress);
            Assert.Equal("", w.DownloadMessage);
            Assert.True(w.DownloadOk);
            Assert.Equal(0, w.DownloadSerial);
            Assert.Null(w.Data);
            Assert.NotNull(w.S);
            Assert.Equal("Europe/Budapest", w.Iana);
            Assert.NotNull(w.Zone);
            Assert.Equal(900, w.GapSec);
            Assert.Throws<ArgumentNullException>(() => new WorkspaceContext(null!));
        }

        // ---------------------------------------------------------------- a helyi szerver minden kérésnél belép

        [Fact]
        public void A_szerver_nem_indul_munkaterulet_nelkul()
        {
            Assert.Throws<InvalidOperationException>(() => new Server().Start(0));
        }

        [Fact]
        public async Task Ket_szerver_ket_munkaterulettel_a_sajatjat_szolgalja_ki()
        {
            var (a, b) = Ketto.Value;
            var sa = new Server { Workspace = a };
            var sb = new Server { Workspace = b };
            sa.Start(0);
            sb.Start(0);
            try
            {
                using var http = new HttpClient();
                async Task<string> Status(Server s)
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, s.Url + "api/status");
                    req.Headers.Add("Cookie", "wst_" + s.Port + "=" + s.Token);
                    var resp = await http.SendAsync(req);
                    Assert.Equal(200, (int)resp.StatusCode);
                    return await resp.Content.ReadAsStringAsync();
                }
                var feladatok = Enumerable.Range(0, 40).Select(i => (B: i % 2 == 1, T: Status(i % 2 == 1 ? sb : sa))).ToList();
                await Task.WhenAll(feladatok.Select(f => f.T));
                foreach (var (melyikB, t) in feladatok)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(await t);
                    Assert.Equal((melyikB ? b : a).L.Base, doc.RootElement.GetProperty("dir").GetString());
                }
            }
            finally
            {
                sa.Stop();
                sb.Stop();
            }
        }
    }
}
