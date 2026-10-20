// WStation – Megjelenítő – főprogram és ablakok
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

[assembly: System.Reflection.AssemblyTitle("WStation")]
[assembly: System.Reflection.AssemblyProduct("WStation – Megjelenítő")]
[assembly: System.Reflection.AssemblyVersion("2.2.4.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.2.4.0")]

namespace Idojaras
{
    static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int pid);

        [STAThread]
        static int Main(string[] args)
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            string webDir = null;
            bool browser = false, test = false, noOpen = false;
            int port = 0;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--ikon" && i + 1 < args.Length) { AppIcon.WriteIco(args[i + 1]); return 0; } // a build használja
                if (a == "--teszt") test = true;
                else if (a == "--bongeszo") browser = true;
                else if (a == "--nincs-megnyitas") { browser = true; noOpen = true; }
                else if (a == "--mappa" && i + 1 < args.Length) dir = Path.GetFullPath(args[++i]);
                else if (a == "--web" && i + 1 < args.Length) webDir = Path.GetFullPath(args[++i]);
                else if (a == "--port" && i + 1 < args.Length) int.TryParse(args[++i], out port);
            }

            if (test)
            {
                AttachConsole(-1);
                return SelfTest.Run(Layout.For(dir));
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // egy mappából egyszerre csak egy példány fusson (a cache miatt)
            bool created;
            string key = Hash(dir.ToLowerInvariant());
            var mutex = new Mutex(true, "BudaorsIdojaras_" + key, out created);
            if (!created)
            {
                // a futó példány hozza előre az ablakát (15.2: a „már fut” üzenet idős felhasználónak úgy tűnt, mintha nem indulna)
                try { EventWaitHandle ev; if (EventWaitHandle.TryOpenExisting("WStation_Show_" + key, out ev)) { ev.Set(); ev.Dispose(); return 4; } }
                catch { }
                MessageBox.Show("A program már fut.", "WStation", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 4;
            }
            var showEv = new EventWaitHandle(false, EventResetMode.AutoReset, "WStation_Show_" + key);
            var showThread = new Thread(() => { while (showEv.WaitOne()) BringToFront(); }) { IsBackground = true };
            showThread.Start();

            // mappák létrehozása, régi (egy mappás) elrendezés átalakítása, WebView2 DLL-ek kicsomagolása
            var L = Layout.For(dir);
            Guard.SetRoot(dir);   // innentől a program csak a saját mappájába írhat
            AppState.L = L;
            try
            {
                L.Ensure();
                var moved = L.MigrateOld();
                if (moved.Count > 0) Log("Régi elrendezés átalakítva, áthelyezve: " + string.Join(", ", moved));
            }
            catch (Exception ex)
            {
                MessageBox.Show("A program nem tud írni ebbe a mappába:\n" + dir + "\n\n" + ex.Message + "\n\nTedd a programot egy saját mappába (pl. Dokumentumok\\Időjárás).",
                    "WStation", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 5;
            }
            try { EmbeddedLibs.Init(L.Lib); }
            catch (Exception ex) { Log("A WebView2 DLL-ek nem csomagolhatók ki: " + ex.Message); }
            AppState.RefreshAsync(false);

            if (webDir != null) Langs.Dir = System.IO.Path.Combine(webDir, "lang");
            var server = new Server { WebDir = webDir };
            try { server.Start(port); }
            catch (Exception ex)
            {
                MessageBox.Show("A program nem tudott elindulni:\n" + ex.Message, "WStation", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }

            if (!browser)
            {
                string reason = null;
                try
                {
                    if (RunWebView(server.Url, out reason)) { server.Stop(); GC.KeepAlive(mutex); return 0; }
                }
                catch (Exception ex) { reason = ex.Message; }
                Log("A WebView2 ablak nem indult (" + reason + "), böngészős módra váltok.");
            }

            ServerUrl = server.Url;
            Application.Run(new FallbackForm(server.Url, !noOpen));
            server.Stop();
            GC.KeepAlive(mutex);
            return 0;
        }

        // külön metódus, hogy a hiányzó WebView2 DLL-ek miatti hibát is el tudjuk kapni
        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool RunWebView(string url, out string reason)
        {
            reason = null;
            try { CoreWebView2Environment.SetLoaderDllFolderPath(AppState.L.Lib); } catch { }
            CoreWebView2Environment.GetAvailableBrowserVersionString(); // kivételt dob, ha nincs WebView2 futtatókörnyezet
            var f = new MainForm(url);
            Window = f;
            Application.Run(f);
            if (f.FailedToStart) { reason = f.FailReason; return false; }
            return true;
        }

        // a főablak (mentés-párbeszédablakokhoz); böngészős módban null
        public static volatile Form Window;
        static volatile string ServerUrl;

        // második indításkor: az ablak előre (kis méretből vissza); böngészős módban a felület újra megnyílik
        static void BringToFront()
        {
            var w = Window;
            if (w != null && !w.IsDisposed)
            {
                try
                {
                    w.BeginInvoke((Action)(() =>
                    {
                        if (w.WindowState == FormWindowState.Minimized) w.WindowState = FormWindowState.Normal;
                        w.Show(); w.TopMost = true; w.Activate(); w.TopMost = false;
                    }));
                }
                catch { }
            }
            else if (ServerUrl != null) OpenExternal(ServerUrl);
        }

        public static void Log(string msg)
        {
            try { Guard.AppendAllText(AppState.L.ErrorLog, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + Environment.NewLine); }
            catch { }
        }

        public static void OpenExternal(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        static string Hash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return h.ToString("x8");
        }
    }

    class MainForm : Form
    {
        readonly WebView2 web;
        readonly string url;
        public bool FailedToStart;
        public string FailReason;

        public MainForm(string url)
        {
            this.url = url;
            Text = "WStation";
            Icon = AppIcon.Create();
            BackColor = Color.FromArgb(243, 245, 249);
            MinimumSize = new Size(900, 600);
            Size = new Size(1440, 920);
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;

            web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
            string udf = Path.Combine(AppState.L.App, "WebView2");
            web.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = udf };
            Controls.Add(web);
            Load += OnLoad;
        }

        async void OnLoad(object sender, EventArgs e)
        {
            try
            {
                await web.EnsureCoreWebView2Async(null);
                var cw = web.CoreWebView2;
                cw.Settings.AreDevToolsEnabled = AppState.S.DevTools;
                cw.Settings.AreDefaultContextMenusEnabled = AppState.S.DevTools;
                cw.Settings.IsStatusBarEnabled = false;
                cw.Settings.IsZoomControlEnabled = true;
                cw.NewWindowRequested += (o, a) => { a.Handled = true; Program.OpenExternal(a.Uri); };
                cw.DocumentTitleChanged += (o, a) => { if (!string.IsNullOrEmpty(cw.DocumentTitle)) Text = cw.DocumentTitle; };
                web.Source = new Uri(url);
            }
            catch (Exception ex)
            {
                FailedToStart = true;
                FailReason = ex.Message;
                Close();
            }
        }
    }

    // Ha nincs WebView2: a felület az alapértelmezett böngészőben nyílik meg, ez az ablak tartja életben a programot
    class FallbackForm : Form
    {
        float k = 1;
        int S(int v) { return (int)Math.Round(v * k); }
        Point P(int x, int y) { return new Point(S(x), S(y)); }
        Size Z(int w, int h) { return new Size(S(w), S(h)); }

        public FallbackForm(string url, bool open)
        {
            using (var g = CreateGraphics()) k = g.DpiX / 96f;
            AutoScaleMode = AutoScaleMode.None;
            Text = "WStation";
            Icon = AppIcon.Create();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = Z(460, 190);
            Font = new Font("Segoe UI", 10f);
            BackColor = Color.White;

            var lbl = new Label
            {
                Text = "A felület a böngésződben nyílt meg.\r\n\r\nAmíg ez az ablak nyitva van, a program fut.\r\nHa bezárod, a program leáll.",
                Location = P(20, 18), Size = Z(420, 90)
            };
            var btnOpen = new Button { Text = "Megnyitás újra a böngészőben", Location = P(20, 125), Size = Z(250, 40) };
            btnOpen.Click += (s, e) => Program.OpenExternal(url);
            var btnExit = new Button { Text = "Kilépés", Location = P(290, 125), Size = Z(150, 40) };
            btnExit.Click += (s, e) => Close();
            Controls.Add(lbl); Controls.Add(btnOpen); Controls.Add(btnExit);
            if (open) Shown += (s, e) => Program.OpenExternal(url);
        }
    }

    // Az ablak ikonja: kék négyzet, nap és felhő
    static class AppIcon
    {
        static Icon cached;

        public static Icon Create()
        {
            if (cached != null) return cached;
            var bmp = Draw(64);
            cached = Icon.FromHandle(bmp.GetHicon());
            return cached;
        }

        public static Bitmap Draw(int size)
        {
            var bmp = new Bitmap(size, size);
            float k = size / 64f;
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var path = Rounded(new RectangleF(2 * k, 2 * k, 60 * k, 60 * k), 14 * k))
                using (var br = new LinearGradientBrush(new PointF(0, 0), new PointF(0, size), Color.FromArgb(56, 132, 244), Color.FromArgb(28, 84, 196)))
                    g.FillPath(br, path);
                using (var sun = new SolidBrush(Color.FromArgb(255, 200, 61)))
                    g.FillEllipse(sun, 30 * k, 11 * k, 22 * k, 22 * k);
                using (var cloud = new SolidBrush(Color.White))
                {
                    g.FillEllipse(cloud, 11 * k, 30 * k, 20 * k, 18 * k);
                    g.FillEllipse(cloud, 21 * k, 23 * k, 24 * k, 24 * k);
                    g.FillEllipse(cloud, 35 * k, 31 * k, 18 * k, 16 * k);
                    g.FillRectangle(cloud, 20 * k, 38 * k, 26 * k, 10 * k);
                }
            }
            return bmp;
        }

        // több méretű, PNG-alapú .ico fájl (az exe ikonjához)
        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 256 };
            var pngs = sizes.Select(sz =>
            {
                using (var bmp = Draw(sz))
                using (var ms = new MemoryStream()) { bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png); return ms.ToArray(); }
            }).ToList();
            using (var bw = new BinaryWriter(File.Create(path))) // csak a buildhez (ikonkészítés)
            {
                bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    bw.Write((byte)0); bw.Write((byte)0); bw.Write((short)1); bw.Write((short)32);
                    bw.Write(pngs[i].Length); bw.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) bw.Write(p);
            }
        }

        static GraphicsPath Rounded(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
