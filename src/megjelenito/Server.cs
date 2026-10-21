using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Idojaras
{
    // Kis HTTP-szerver csak a saját gépen (localhost). Ezt látja a WebView2 ablak és a tartalék böngésző is.
    class Server
    {
        HttpListener listener;
        public int Port;
        public string Url { get { return "http://localhost:" + Port + "/"; } }
        // munkamenet-azonosító (2.2.7.2): minden indításkor új, véletlen érték. Csak a program saját ablaka (ill. a
        // böngészős módban megnyitott cím) kapja meg; ebből HttpOnly cookie lesz, és a helyi API csak ezzel válaszol –
        // így egy másik helyi program nem töltheti le pl. a biztonsági mentést
        public readonly string Token = NewToken();
        // portfüggő név (2.2.8): a localhost cookie-jai minden portnak közösek, így két egyszerre futó példány
        // (két külön mappából, böngészős módban) nem írja felül egymás azonosítóját
        string CookieName { get { return "wst_" + Port; } }
        public string AuthUrl { get { return Url + "?t=" + Token; } }
        static string NewToken()
        {
            var b = new byte[24];
            using (var r = System.Security.Cryptography.RandomNumberGenerator.Create()) r.GetBytes(b);
            return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
        }
        bool Authed(HttpListenerRequest req)
        {
            var c = req.Cookies[CookieName];
            if (c == null || c.Value == null || c.Value.Length != Token.Length) return false;
            int diff = 0;
            for (int i = 0; i < Token.Length; i++) diff |= c.Value[i] ^ Token[i];   // állandó idejű összevetés
            return diff == 0;
        }
        public string WebDir; // fejlesztéshez: ha meg van adva, a felület fájljait innen olvassa

        public void Start(int port)
        {
            var rnd = new Random();
            for (int attempt = 0; attempt < 30; attempt++)
            {
                int p = port > 0 ? port : rnd.Next(49200, 60000);
                var l = new HttpListener();
                l.Prefixes.Add("http://localhost:" + p + "/");
                try
                {
                    l.Start();
                    listener = l;
                    Port = p;
                    break;
                }
                catch (HttpListenerException)
                {
                    l.Close();
                    if (port > 0) throw;
                }
            }
            if (listener == null) throw new Exception("Nem sikerült helyi portot nyitni a felülethez.");
            var th = new Thread(Loop) { IsBackground = true, Name = "http" };
            th.Start();
        }

        public void Stop()
        {
            try { listener.Stop(); listener.Close(); } catch { }
        }

        void Loop()
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch { break; }
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var resp = ctx.Response;
            try
            {
                // csak a saját gépről, a saját címünkre érkező kéréseket szolgáljuk ki
                string host = (req.Headers["Host"] ?? "").ToLowerInvariant();
                if (!req.IsLocal || !(host == "localhost:" + Port || host == "127.0.0.1:" + Port))
                {
                    resp.StatusCode = 403;
                    resp.Close();
                    return;
                }
                string path = req.Url.AbsolutePath;
                // belépés a munkamenet-azonosítóval: cookie, majd átirányítás a címre (az azonosító így nem marad a címsorban)
                if ((path == "/" || path == "/index.html") && req.QueryString["t"] != null)
                {
                    if (req.QueryString["t"] == Token)
                    {
                        resp.Headers.Add("Set-Cookie", CookieName + "=" + Token + "; Path=/; HttpOnly; SameSite=Strict");
                        resp.StatusCode = 302;
                        resp.RedirectLocation = "/";
                    }
                    else resp.StatusCode = 403;
                    resp.Close();
                    return;
                }
                if (path.StartsWith("/api/") && !Authed(req))
                {
                    var nb = Encoding.UTF8.GetBytes("{\"error\":\"Nincs jogosultság: a felületet a programból nyisd meg.\"}");
                    resp.StatusCode = 401;
                    resp.ContentType = "application/json; charset=utf-8";
                    resp.OutputStream.Write(nb, 0, nb.Length);
                    resp.Close();
                    return;
                }
                byte[] body;
                string type;
                int status = 200;
                if (path.StartsWith("/api/"))
                {
                    // módosító kérések csak a saját felületről: saját fejléc és (ha van) saját Origin kell,
                    // így egy idegen weboldal nem tud a háttérből beállítást írni
                    string reqBody = null;
                    if (req.HttpMethod != "GET")
                    {
                        string origin = req.Headers["Origin"];
                        if (req.Headers["X-Idojaras"] != "1" || (origin != null && origin != "http://localhost:" + Port && origin != "http://127.0.0.1:" + Port))
                        {
                            resp.StatusCode = 403;
                            resp.Close();
                            return;
                        }
                        // a kéréstörzs legfeljebb 10 MB (a legnagyobb valós JSON is ennek töredéke): egy óriási kérés ne fogyassza el a memóriát
                        if (req.ContentLength64 > MaxBody) { resp.StatusCode = 413; resp.Close(); return; }
                        using (var sr = new StreamReader(req.InputStream, Encoding.UTF8))
                        {
                            var buf = new char[8192]; var sb = new StringBuilder(); int n;
                            while ((n = sr.Read(buf, 0, buf.Length)) > 0)
                            {
                                sb.Append(buf, 0, n);
                                if (sb.Length > MaxBody) { resp.StatusCode = 413; resp.Close(); return; }
                            }
                            reqBody = sb.ToString();
                        }
                    }
                    var latestZip = path == "/api/backup/latest.zip" ? BackupTools.Latest() : null;
                    if (latestZip != null)
                    {
                        // böngészős módban a legutóbbi mentés letöltésként
                        body = File.ReadAllBytes(latestZip.FullName);
                        type = "application/zip";
                        resp.Headers["Content-Disposition"] = "attachment; filename=\"" + latestZip.Name + "\"";
                    }
                    else if (path == "/api/export.csv")
                    {
                        // böngészős módban letöltésként adjuk
                        body = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Export.DailyCsv(AppState.Data, req.QueryString["raw"] == "1"))).ToArray();
                        type = "text/csv; charset=utf-8";
                        resp.Headers["Content-Disposition"] = "attachment; filename=\"" + Export.FileName() + "\"";
                    }
                    else
                    {
                        string json = Api.Handle(path, req.QueryString, req.HttpMethod, reqBody, out status);
                        body = Encoding.UTF8.GetBytes(json);
                        type = "application/json; charset=utf-8";
                    }
                    resp.Headers["Cache-Control"] = "no-store";
                }
                else
                {
                    if (path == "/") path = "/index.html";
                    body = Static(path.TrimStart('/'));
                    if (body == null) { status = 404; body = Encoding.UTF8.GetBytes("Nem található"); type = "text/plain; charset=utf-8"; }
                    else type = Mime(path);
                    resp.Headers["Cache-Control"] = "no-cache";
                    // tartalombiztonsági szabály (2.2.7.1): csak a program saját szkriptjei futhatnak; ha egy adatfájlból mégis
                    // HTML kerülne az oldalra, a benne lévő kód (pl. onerror=…) nem fut le, és az oldal semmit nem küldhet ki
                    resp.Headers["Content-Security-Policy"] = Csp;
                    resp.Headers["X-Content-Type-Options"] = "nosniff";
                }
                resp.StatusCode = status;
                resp.ContentType = type;
                resp.ContentLength64 = body.Length;
                resp.OutputStream.Write(body, 0, body.Length);
                resp.Close();
            }
            catch (Exception ex)
            {
                try
                {
                    resp.StatusCode = 500;
                    var b = Encoding.UTF8.GetBytes("{\"error\":" + "\"" + ex.Message.Replace("\"", "'").Replace("\\", "/") + "\"}");
                    resp.ContentType = "application/json; charset=utf-8";
                    resp.OutputStream.Write(b, 0, b.Length);
                    resp.Close();
                }
                catch { }
            }
        }

        const int MaxBody = 10 * 1024 * 1024;
        const string Csp = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; " +
                           "object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

        byte[] Static(string name)
        {
            if (name.Contains("..") || name.Contains("\\") || name.Contains(":")) return null;
            if (WebDir != null)
            {
                // fejlesztői mód: csak a WebDir alatti fájl (abszolút vagy meghajtós útvonal nem)
                string root = Path.GetFullPath(WebDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string f = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
                if (!f.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
                return File.Exists(f) ? File.ReadAllBytes(f) : null;
            }
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("web/" + name))
            {
                if (s == null) return null;
                var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        static string Mime(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": return "application/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                case ".ico": return "image/x-icon";
                case ".json": return "application/json; charset=utf-8";
                default: return "application/octet-stream";
            }
        }
    }
}
