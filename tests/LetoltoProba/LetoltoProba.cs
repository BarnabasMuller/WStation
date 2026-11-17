// Próbaindító a letöltő tesztjeihez (PLAN-3.0 1.7): egy helyi, hamis Weather Underground- és METAR-kiszolgálót indít
// kitalált válaszokkal, a letöltő https://api.weather.com/ és https://aviationweather.gov/ kéréseit erre irányítja át
// (WebRequest.RegisterPrefix – csak ebben a folyamatban), majd ugyanebben a folyamatban elindítja a letöltő Main-jét.
// A letöltő kódja így változatlan (a 2.2.8 az ApiBaseUrl-t csak https://…weather.com címre engedi), és internet nem kell.
//
// Ugyanez a forrás fordul a 3.0 letöltőjével (LetoltoProba.csproj, .NET 10) és a 2.2.8-cal is (.NET Framework 4.8, a régi
// csc.exe-vel – C# 5): így a kettő ugyanazokra a válaszokra adott kimenete bájtra összevethető.
//
// Használat: LetoltoProba.exe <letöltő szerelvény: név vagy útvonal> <forgatókönyv> <kéréslista-fájl> [a letöltő kapcsolói…]
//   forgatókönyv: ok      – sikeres válaszok (rögzített, kitalált mérések az ITESZT1 állomásra)
//                 401     – minden WU-kérésre 401 (érvénytelen kulcs)
//                 halozat – az átirányítás egy zárt kapura mutat (hálózati hiba)
// Kilépési kód: a letöltőé. A kéréslista-fájlba soronként a kérés útvonala és paraméterei kerülnek, a kulcs helyett
// „<kulcs>”, a dátum helyett az állomás mai napjához mért eltolás („date=T-1”), a METAR órája helyett „hours=…”.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace WStation.Tests.LetoltoProba
{
    static class Program
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const int TzPerc = 120;   // a kitalált állomás helyi ideje: UTC+2 (nyári idő)
        static string forgatokonyv, kereslista;
        static readonly object zar = new object();

        static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Használat: LetoltoProba.exe <letöltő szerelvény> <ok|401|halozat> <kéréslista-fájl> [kapcsolók…]");
                return 99;
            }
            forgatokonyv = args[1];
            kereslista = args[2];
            var letoltoArgs = new string[args.Length - 3];
            Array.Copy(args, 3, letoltoArgs, 0, letoltoArgs.Length);

            int port = SzabadKapu();
            if (forgatokonyv != "halozat")
            {
                var hl = new HttpListener();
                hl.Prefixes.Add("http://localhost:" + port.ToString(Inv) + "/");
                hl.Start();
                var t = new Thread(() => Kiszolgal(hl)) { IsBackground = true };
                t.Start();
            }
            var at = new Atiranyito("http://localhost:" + port.ToString(Inv));
            WebRequest.RegisterPrefix("https://api.weather.com/", at);
            WebRequest.RegisterPrefix("https://aviationweather.gov/", at);

            Assembly asm = File.Exists(args[0]) ? Assembly.LoadFrom(args[0]) : Assembly.Load(args[0]);
            object r = asm.EntryPoint.Invoke(null, new object[] { letoltoArgs });
            return (int)r;
        }

        static int SzabadKapu()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        sealed class Atiranyito : IWebRequestCreate
        {
            readonly string cel;
            public Atiranyito(string cel) { this.cel = cel; }
            public WebRequest Create(Uri uri) { return WebRequest.CreateHttp(cel + uri.PathAndQuery); }
        }

        static void Kiszolgal(HttpListener hl)
        {
            while (true)
            {
                HttpListenerContext c;
                try { c = hl.GetContext(); }
                catch (Exception) { return; }   // leállítás
                try { Valaszol(c); }
                catch (Exception ex) { Console.Error.WriteLine("LetoltoProba: " + ex.Message); }
            }
        }

        static void Valaszol(HttpListenerContext c)
        {
            string path = c.Request.Url.AbsolutePath;
            string query = c.Request.Url.Query;
            Naploz(path, query);
            int status = 200;
            string body;
            if (path == "/api/data/metar") body = Metar();
            else if (forgatokonyv == "401")
            {
                status = 401;
                body = "{\"metadata\":{\"transaction_id\":\"teszt\"},\"success\":false,\"errors\":[{\"error\":{\"code\":\"CDN-0001\",\"message\":\"Invalid apiKey.\"}}]}";
            }
            else if (path == "/v2/pws/observations/current") body = "{\"observations\":[" + Pillanatnyi() + "]}";
            else if (path == "/v2/pws/observations/all/1day") body = "{\"observations\":[" + Meresek(3, 7) + "]}";
            else if (path == "/v2/pws/history/all") body = "{\"observations\":[" + Meresek(0, 6) + "]}";
            else if (path == "/v2/pws/dailysummary/7day") body = "{\"summaries\":[" + Napi("2026-08-31", 1788213600L - 86400 + 22 * 3600) + "," + Napi("2026-09-01", 1788213600L + 22 * 3600) + "]}";
            else { status = 404; body = "{\"errors\":[{\"error\":{\"code\":\"teszt\",\"message\":\"nincs ilyen\"}}]}"; }
            byte[] b = new UTF8Encoding(false).GetBytes(body);
            c.Response.StatusCode = status;
            c.Response.ContentType = "application/json; charset=utf-8";
            c.Response.ContentLength64 = b.Length;
            c.Response.OutputStream.Write(b, 0, b.Length);
            c.Response.OutputStream.Close();
        }

        static void Naploz(string path, string query)
        {
            string q = Regex.Replace(query, "apiKey=[^&]*", "apiKey=<kulcs>");
            q = Regex.Replace(q, "hours=[0-9]+", "hours=…");
            q = Regex.Replace(q, "date=([0-9]{8})", m =>
            {
                DateTime d = DateTime.ParseExact(m.Groups[1].Value, "yyyyMMdd", Inv);
                DateTime ma = DateTime.UtcNow.AddMinutes(TzPerc).Date;
                return "date=T-" + ((int)(ma - d).TotalDays).ToString(Inv);
            });
            lock (zar) File.AppendAllText(kereslista, path + q + "\r\n", new UTF8Encoding(false));
        }

        // ------------------------------------------------------------ kitalált válaszok (ITESZT1, 2026-09-01, UTC+2)

        static string Ido(long epoch, int tz)
        {
            DateTime u = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(epoch);
            return u.ToString("yyyy-MM-ddTHH:mm:ssZ", Inv) + "|" + u.AddMinutes(tz).ToString("yyyy-MM-dd HH:mm:ss", Inv);
        }

        static string D(decimal d) { return d.ToString(Inv); }

        static string Pillanatnyi()
        {
            long e = 1788256800L + 8 * 300;
            string[] t = Ido(e, TzPerc).Split('|');
            return "{\"stationID\":\"ITESZT1\",\"obsTimeUtc\":\"" + t[0] + "\",\"obsTimeLocal\":\"" + t[1] + "\","
                 + "\"neighborhood\":\"Kőszeg – Árvíztűrő; \\\"teszt\\\"\",\"softwareType\":\"Teszt\\nállomás 1.0\",\"country\":\"HU\","
                 + "\"solarRadiation\":512.30,\"lon\":19.654321,\"realtimeFrequency\":null,\"epoch\":" + e.ToString(Inv) + ",\"lat\":47.123456,"
                 + "\"uv\":4.0,\"winddir\":225,\"humidity\":61,\"qcStatus\":1,"
                 + "\"metric\":{\"temp\":21.4,\"heatIndex\":21.4,\"dewpt\":13.6,\"windChill\":21.4,\"windSpeed\":7.2,\"windGust\":11.5,"
                 + "\"pressure\":1013.21,\"precipRate\":0.00,\"precipTotal\":0.25,\"elev\":110}}";
        }

        // részletes mérések 5 percenként; az [ettol, eddig) sorszámúak. A 4-es a „1day” válaszban más csapadékösszeggel jön (frissítés).
        static string Meresek(int ettol, int eddig)
        {
            var sb = new StringBuilder();
            for (int i = ettol; i < eddig; i++)
            {
                if (sb.Length > 0) sb.Append(',');
                long e = 1788256800L + i * 300;
                string[] t = Ido(e, TzPerc).Split('|');
                decimal th = 21.4m + i * 0.1m, tl = 20.9m + i * 0.1m;
                string precipTotal = i == 4 && ettol > 0 ? "0.30" : D(0.25m + (i > 4 ? 0.05m : 0m));
                sb.Append("{\"stationID\":\"ITESZT1\",\"tz\":\"Europe/Budapest\",\"obsTimeUtc\":\"").Append(t[0]).Append("\",\"obsTimeLocal\":\"").Append(t[1])
                  .Append("\",\"epoch\":").Append(e.ToString(Inv)).Append(",\"lat\":47.123456,\"lon\":19.654321,\"solarRadiationHigh\":").Append(D(512.3m - i * 10.5m))
                  .Append(",\"uvHigh\":4.0,\"winddirAvg\":").Append((200 + i * 5).ToString(Inv)).Append(",\"humidityHigh\":64,\"humidityLow\":60,\"humidityAvg\":")
                  .Append(D(62m + i * 0.5m)).Append(",\"qcStatus\":1,\"metric\":{\"tempHigh\":").Append(D(th)).Append(",\"tempLow\":").Append(D(tl))
                  .Append(",\"tempAvg\":").Append(D((th + tl) / 2)).Append(",\"windspeedHigh\":11.5,\"windspeedLow\":").Append(i == 2 ? "null" : "0.0")
                  .Append(",\"windspeedAvg\":5.30,\"windgustHigh\":14.8,\"windgustLow\":0.0,\"windgustAvg\":7.2,\"dewptHigh\":13.8,\"dewptLow\":13.1,\"dewptAvg\":13.5,")
                  .Append("\"windchillHigh\":").Append(D(th)).Append(",\"windchillLow\":").Append(D(tl)).Append(",\"windchillAvg\":21.2,")
                  .Append("\"heatindexHigh\":").Append(D(th)).Append(",\"heatindexLow\":").Append(D(tl)).Append(",\"heatindexAvg\":21.2,")
                  .Append("\"pressureMax\":").Append(D(1013.21m - i * 0.07m)).Append(",\"pressureMin\":").Append(D(1012.88m - i * 0.07m))
                  .Append(",\"pressureTrend\":").Append(i == 1 ? "-5E-2" : "-0.05").Append(",\"precipRate\":0.00,\"precipTotal\":").Append(precipTotal)
                  .Append(",\"qcStatus\":1}}");
            }
            return sb.ToString();
        }

        static string Napi(string nap, long epoch)
        {
            string[] t = Ido(epoch, TzPerc).Split('|');
            return "{\"stationID\":\"ITESZT1\",\"tz\":\"Europe/Budapest\",\"obsTimeUtc\":\"" + t[0] + "\",\"obsTimeLocal\":\"" + t[1] + "\",\"epoch\":" + epoch.ToString(Inv) + ","
                 + "\"lat\":47.123456,\"lon\":19.654321,\"solarRadiationHigh\":801.2,\"uvHigh\":6.0,\"winddirAvg\":210,\"humidityHigh\":93,\"humidityLow\":41,"
                 + "\"humidityAvg\":67.4,\"qcStatus\":1,\"metric\":{\"tempHigh\":27.3,\"tempLow\":14.1,\"tempAvg\":20.15,\"windspeedHigh\":24.1,\"windspeedLow\":0.0,"
                 + "\"windspeedAvg\":6.3,\"windgustHigh\":38.9,\"windgustLow\":0.0,\"windgustAvg\":9.0,\"dewptHigh\":15.2,\"dewptLow\":11.0,\"dewptAvg\":13.4,"
                 + "\"pressureMax\":1016.03,\"pressureMin\":1011.92,\"pressureTrend\":0.00,\"precipRate\":12.70,\"precipTotal\":" + (nap == "2026-08-31" ? "3.30" : "0.25") + "}}";
        }

        // METAR (aviationweather.gov): az utolsó órákra, az idő a futás idejéhez igazítva (a letöltő a 400 napnál régebbieket eldobja)
        static string Metar()
        {
            long most = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            long ora = most - most % 3600;
            return "[{\"icaoId\":\"LHBP\",\"obsTime\":" + (ora - 3600).ToString(Inv) + ",\"rawOb\":\"LHBP 011030Z 22008KT 9999 FEW040CB SCT100 21/13 Q1013; NOSIG\",\"cover\":\"SCT\","
                 + "\"clouds\":[{\"cover\":\"FEW\",\"base\":4000},{\"cover\":\"BKN\",\"base\":10000}]},"
                 + "{\"icaoId\":\"LHBP\",\"obsTime\":" + (ora - 7200).ToString(Inv) + ",\"rawOb\":\"LHBP 010930Z 21006KT 9999 OVC012 19/14 Q1014\",\"cover\":\"OVC\",\"clouds\":[]},"
                 + "{\"icaoId\":\"LHBP\",\"obsTime\":" + (ora - 10800).ToString(Inv) + ",\"rawOb\":\"LHBP 010830Z 00000KT CAVOK 17/14 Q1014\",\"cover\":\"CAVOK\"},"
                 + "{\"icaoId\":\"LHBP\",\"obsTime\":" + (ora - 14400).ToString(Inv) + ",\"rawOb\":\"LHBP 010730Z ismeretlen\",\"cover\":\"?\"}]";
        }
    }
}
