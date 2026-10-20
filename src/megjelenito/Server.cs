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
                        using (var sr = new StreamReader(req.InputStream, Encoding.UTF8)) reqBody = sr.ReadToEnd();
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

        byte[] Static(string name)
        {
            if (name.Contains("..")) return null;
            if (WebDir != null)
            {
                string f = Path.Combine(WebDir, name.Replace('/', Path.DirectorySeparatorChar));
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
