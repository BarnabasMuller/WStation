// K0 próba (WStation 3.0): fut-e a .NET 10 a NAS-on, és mit tud. Minden lépés külön try – egy hiba nem állítja meg a többit.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

var dir = AppContext.BaseDirectory;
var work = Path.Combine(dir, "munka_" + Environment.ProcessId);
Directory.CreateDirectory(work);

void Line(string s) { Console.WriteLine(s); Console.Out.Flush(); }
void Step(string name, Action a)
{
    var sw = Stopwatch.StartNew();
    try { a(); Line("[OK  ] " + name + " (" + sw.ElapsedMilliseconds + " ms)"); }
    catch (Exception ex) { Line("[HIBA] " + name + ": " + ex.GetType().Name + ": " + ex.Message); }
}
long Mem() { var p = Process.GetCurrentProcess(); p.Refresh(); return p.WorkingSet64 / (1024 * 1024); }

Line("=== WStation K0 próba ===");

Step("1. környezet", () =>
{
    Line("  .NET: " + RuntimeInformation.FrameworkDescription);
    Line("  OS: " + RuntimeInformation.OSDescription + " | arch: " + RuntimeInformation.OSArchitecture);
    Line("  RID: " + RuntimeInformation.RuntimeIdentifier + " | CPU: " + Environment.ProcessorCount);
    try { foreach (var l in File.ReadLines("/proc/meminfo").Take(3)) Line("  " + l); } catch { }
    try { Line("  kernel: " + File.ReadAllText("/proc/version").Trim()); } catch { }
    try { foreach (var l in File.ReadLines("/etc.defaults/VERSION")) if (l.StartsWith("productversion") || l.StartsWith("buildnumber") || l.StartsWith("majorversion")) Line("  DSM " + l); } catch { }
    Line("  felhasználó: " + Environment.UserName + " | mappa: " + dir);
    Line("  memória most: " + Mem() + " MB");
});

Step("2. HTTPS a WU felé", () =>
{
    using var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    var r = h.GetAsync("https://api.weather.com/").GetAwaiter().GetResult();
    Line("  HTTP " + (int)r.StatusCode + " (bármilyen válasz = a titkosított kapcsolat működik)");
});

Step("3. időzóna Europe/Budapest", () =>
{
    var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");
    var nyar = TimeZoneInfo.ConvertTimeFromUtc(new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc), tz);
    var tel = TimeZoneInfo.ConvertTimeFromUtc(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), tz);
    Line("  nyár 12:00 UTC -> " + nyar.ToString("HH:mm") + " (várt 14:00), tél 12:00 UTC -> " + tel.ToString("HH:mm") + " (várt 13:00)");
    if (nyar.Hour != 14 || tel.Hour != 13) throw new Exception("rossz átváltás");
});

Step("4. fájlzár (kizárólagos megnyitás kétszer)", () =>
{
    var f = Path.Combine(work, "zar_proba.tmp");
    using (var a = new FileStream(f, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
    {
        bool blocked = false;
        try { using var b = new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None); } catch (IOException) { blocked = true; }
        Line("  második megnyitás " + (blocked ? "elutasítva (jó)" : "SIKERÜLT (a zár nem működik ezen a köteten)"));
    }
    File.Delete(f);
});

Step("5. atomi csere és hozzáfűzés a köteten", () =>
{
    var f = Path.Combine(work, "csere_proba.txt"); var t = f + ".tmp";
    File.WriteAllText(f, "regi"); File.WriteAllText(t, "uj");
    File.Replace(t, f, null);
    File.AppendAllText(f, "+hozzafuzve");
    var x = File.ReadAllText(f); File.Delete(f);
    Line("  eredmény: " + x);
    if (x != "uj+hozzafuzve") throw new Exception("váratlan tartalom");
});

Step("6. 20 MB-os CSV írása és beolvasása", () =>
{
    var f = Path.Combine(work, "proba.csv");
    var rnd = new Random(1); long size = 0; int n = 0;
    var sw0 = Stopwatch.StartNew();
    using (var w = new StreamWriter(f, false, new UTF8Encoding(false)))
    {
        w.WriteLine("stationID;obsTimeLocal;obsTimeUtc;epoch;tz;lat;lon;solar;uv;dir;humHi;humLo;humAvg;tHi;tLo;tAvg;wind;gust;dew;pMax;pMin;rate;total");
        long ep = 1700000000;
        while (size < 20L * 1024 * 1024)
        {
            var line = "TEST1;2026-01-01 00:00:00;2026-01-01T00:00:00Z;" + (ep + n * 300) + ";Europe/Budapest;47,46;18,96;" + rnd.Next(0, 900) + ";" + rnd.Next(0, 9) + ";" + rnd.Next(0, 360) + ";" + rnd.Next(40, 99) + ";" + rnd.Next(30, 90) + ";" + rnd.Next(30, 99) + ";" + rnd.Next(-10, 35) + ",5;" + rnd.Next(-10, 35) + ",2;" + rnd.Next(-10, 35) + ",1;" + rnd.Next(0, 30) + ",3;" + rnd.Next(0, 60) + ",4;" + rnd.Next(-10, 20) + ",6;1013,2;1012,8;0,0;0,0";
            w.WriteLine(line); size += line.Length + 1; n++;
        }
    }
    Line("  írás: " + n + " sor, " + sw0.ElapsedMilliseconds + " ms");
    long m0 = Mem(); var sw = Stopwatch.StartNew();
    var rows = new List<double[]>();
    foreach (var l in File.ReadLines(f).Skip(1))
    {
        var p = l.Split(';'); var v = new double[17];
        for (int i = 0; i < 17; i++) double.TryParse(p[i + 6].Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i]);
        rows.Add(v);
    }
    Line("  beolvasás + feldolgozás: " + sw.ElapsedMilliseconds + " ms, " + rows.Count + " sor; memória előtte " + m0 + " MB, utána " + Mem() + " MB");
    rows = null; GC.Collect();
    File.Delete(f);
});

Step("7. jelszókivonat (PBKDF2-SHA256) sebessége", () =>
{
    var salt = RandomNumberGenerator.GetBytes(16); int it = 10000; var sw = Stopwatch.StartNew();
    Rfc2898DeriveBytes.Pbkdf2("proba-jelszo", salt, it, HashAlgorithmName.SHA256, 32);
    var ms = Math.Max(1, sw.ElapsedMilliseconds);
    Line("  " + it + " kör: " + ms + " ms -> 300 ms-ba kb. " + (it * 300 / ms) + " kör fér");
});

Step("8. AES-GCM és ECDSA", () =>
{
    Line("  AES-GCM támogatott: " + AesGcm.IsSupported);
    using var k = new AesGcm(RandomNumberGenerator.GetBytes(32), 16);
    var nonce = RandomNumberGenerator.GetBytes(12); var pt = Encoding.UTF8.GetBytes("szelep"); var ct = new byte[pt.Length]; var tag = new byte[16];
    k.Encrypt(nonce, pt, ct, tag);
    using var e = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var sig = e.SignData(pt, HashAlgorithmName.SHA256);
    Line("  ECDSA aláírás ellenőrizve: " + e.VerifyData(pt, sig, HashAlgorithmName.SHA256));
});

Step("9. webszerver (Kestrel) 60 mp-ig a 18443-as porton, saját tanúsítvánnyal", () =>
{
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var req = new CertificateRequest("CN=wstation-k0", key, HashAlgorithmName.SHA256);
    using var tmp = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(7));
    var cert = X509CertificateLoader.LoadPkcs12(tmp.Export(X509ContentType.Pfx), null);
    var b = WebApplication.CreateSlimBuilder();
    b.WebHost.ConfigureKestrel(o => o.ListenAnyIP(18443, l => l.UseHttps(cert)));
    b.Logging.ClearProviders();
    var app = b.Build();
    int hits = 0;
    app.MapGet("/", () => { Interlocked.Increment(ref hits); return Results.Text("<h1>WStation K0: a NAS webszervere működik</h1>", "text/html; charset=utf-8"); });
    app.StartAsync().GetAwaiter().GetResult();
    Line("  fut: nyisd meg a böngészőben: https://<a NAS címe>:18443 (60 mp-ig) – memória: " + Mem() + " MB");
    Thread.Sleep(60000);
    app.StopAsync().GetAwaiter().GetResult();
    Line("  böngészős megnyitások száma: " + hits);
});

try { Directory.Delete(work, true); } catch { }
Line("memória a végén: " + Mem() + " MB");
Line("=== vége ===");
