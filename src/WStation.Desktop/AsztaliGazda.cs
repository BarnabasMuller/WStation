using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WStation.Core.Common;

namespace WStation.Desktop
{
    // IDEIGLENES (1.5): a 2.x mag Windows-függő részei (a Megjelenítő ablakához kötött párbeszédablakok, az Intéző, a frissítő
    // indítása és a program bezárása, a Feladatütemező) – a mag a WStation.Core.Common.Host felületen át éri el őket.
    // A kód a 2.x Extras.cs, Maint.cs és Api.cs fájljából való, változatlan logikával. Az 1.6 (asztali gazda) köti be:
    // AsztaliGazda.Install(() => Program.Window) a Program.Main elején.
    sealed class AsztaliGazda : IDesktopHost
    {
        readonly Func<Form?> window;

        AsztaliGazda(Func<Form?> window) { this.window = window; }

        /// <summary>A gazda bekötése: a mag innentől ezen át nyit párbeszédablakot és éri el a Feladatütemezőt.</summary>
        public static void Install(Func<Form?> window)
        {
            // a schtasks a konzol (OEM) kódlapján ír (magyar gépen 852) – a modern .NET-ben ez csak regisztráció után érhető el
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Host.Desktop = new AsztaliGazda(window);
            Host.Scheduler = new WinTaskScheduling();
        }

        public bool HasWindow { get { var win = window(); return win != null && !win.IsDisposed; } }

        public string? SaveFileDialog(string title, string filter, string fileName, string? initialDirectory)
        {
            var win = window();
            if (win == null || win.IsDisposed) return null;
            string? res = null;
            win.Invoke((Action)(() =>
            {
                using (var d = new SaveFileDialog { Title = title, Filter = filter, FileName = fileName })
                {
                    if (initialDirectory != null) d.InitialDirectory = initialDirectory;
                    if (d.ShowDialog(win) == DialogResult.OK) res = d.FileName;
                }
            }));
            return res;
        }

        public string? OpenFileDialog(string title, string filter, string? initialDirectory)
        {
            var win = window();
            if (win == null || win.IsDisposed) return null;
            string? res = null;
            win.Invoke((Action)(() =>
            {
                using (var d = new OpenFileDialog { Title = title, Filter = filter })
                {
                    if (initialDirectory != null) d.InitialDirectory = initialDirectory;
                    if (d.ShowDialog(win) == DialogResult.OK) res = d.FileName;
                }
            }));
            return res;
        }

        public void OpenFolder(string path)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }

        public void StartInstaller(string exe, string arguments, string workingDirectory)
        {
            Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = true, WorkingDirectory = workingDirectory });
        }

        public void CloseForUpdate()
        {
            var win = window();
            if (win != null && !win.IsDisposed) win.BeginInvoke((Action)(() => win.Close()));
            else ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(800); Environment.Exit(0); });
        }
    }

    // a Windows Feladatütemezője (Utemezes – a 2.x Kozos.Utemezes) a mag ITaskScheduling felülete mögött
    sealed class WinTaskScheduling : ITaskScheduling
    {
        public int[] Choices => Utemezes.Choices;
        public ScheduleSpec QuerySpec() => Utemezes.QuerySpec();
        public bool SetSpec(string exePath, ScheduleSpec s, out string message) => Utemezes.SetSpec(exePath, s, out message);
        public bool Pause(bool pause) => Utemezes.Pause(pause);
        public string Text(ScheduleSpec s) => Utemezes.Text(s);
    }
}
