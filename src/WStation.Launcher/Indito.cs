// WStation – indító (PLAN-3.0 9.4). .NET Framework 4.8, a telepítővel egy eszközlánc.
//
// A 3.0 programjai (a .NET 10-es futtatókörnyezettel együtt) a telepített mappa app\ almappájában vannak. A mappa gyökerében
// ez az apró program áll a megszokott, nyelvfüggő néven (Megjelenítő.exe / Letöltő.exe, Viewer.exe / Downloader.exe …), így a
// parancsikon, a meglévő ütemezett feladat és a Megjelenítő letöltő-keresése (Layout.FindDownloader) változatlan nevekkel működik.
//
// Mit csinál: elindítja az app\WStation.Desktop.exe-t, ill. az app\WStation.Downloader.exe-t ugyanazokkal a kapcsolókkal (a
// parancssort betű szerint továbbadja), megvárja, és a kilépési kódját adja vissza (a Feladatütemező és a régi szkriptek ezt
// figyelik). A programok a saját mappájukat (az app\-ot) tekintenék a telepítésnek, ezért ha a hívó nem adott meg --mappa
// kapcsolót, az indító hozzáteszi a gyökérmappát – így a data\ és az appdata_* mappák a helyükön maradnak.
//
// Konzol és kimenet: ha az indítónak van szabványos kimenete (pl. a Megjelenítő Frissítés gombja átirányítja, vagy
// „Letöltő.exe > napló.txt”), a gyerek ugyanazokat a leírókat kapja; ha nincs (dupla kattintás, parancssor), az indító a szülő
// konzoljához csatlakozik, és a gyerek a saját AttachConsole(-1) hívásával ugyanahhoz – mintha közvetlenül indult volna.
//
// Leállítás: a gyerek egy „bezáráskor leállít” feladatcsoportba (job object) kerül, így ha az indítót leállítják (pl. a
// Megjelenítő a letöltés megszakításakor vagy időtúllépéskor), a gyerek is leáll – ahogy a 2.x-ben a program maga. A gyerek
// által indított további programok (böngésző, telepítő, WebView2) nem tartoznak a csoportba (SILENT_BREAKAWAY_OK).
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WStation.Launcher
{
    static class Indito
    {
#if LETOLTO
        const string Target = "WStation.Downloader.exe";
#else
        const string Target = "WStation.Desktop.exe";
#endif
        // a Letöltő kilépési kódjai közül a „váratlan hiba” (docs/Letolto_leiras.txt)
        const int FailCode = 9;

        static int Main()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;   // a telepített mappa, „\”-re végződik
            string exe = Path.Combine(Path.Combine(root, "app"), Target);
            if (!File.Exists(exe)) return Fail("Hiányzik a program: " + exe + "\nTelepítsd újra a WStationt (a telepítő az adatokat megtartja).");

            var cmd = new StringBuilder(Parancssor.Build(exe, Environment.CommandLine, Environment.GetCommandLineArgs(), root));

            IntPtr hIn = GetStdHandle(STD_INPUT), hOut = GetStdHandle(STD_OUTPUT), hErr = GetStdHandle(STD_ERROR);
            bool std = Valid(hIn) || Valid(hOut) || Valid(hErr);
            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
            if (std)
            {
                si.dwFlags = STARTF_USESTDHANDLES;
                si.hStdInput = hIn; si.hStdOutput = hOut; si.hStdError = hErr;
            }
            else AttachConsole(ATTACH_PARENT_PROCESS);   // ha nincs szülő konzol, nem sikerül – ez így rendben van

            IntPtr job = CreateJob();
            PROCESS_INFORMATION pi;
            if (!CreateProcess(exe, cmd, IntPtr.Zero, IntPtr.Zero, std, CREATE_SUSPENDED, IntPtr.Zero, null, ref si, out pi))
                return Fail("A program nem indítható: " + exe + " (Windows-hiba " + Marshal.GetLastWin32Error() + ")");
            try
            {
                // ha a csoportba nem tehető (pl. régi Windows), a program akkor is fut – csak a leállítás nem öröklődik
                if (job != IntPtr.Zero && !AssignProcessToJobObject(job, pi.hProcess)) { CloseHandle(job); job = IntPtr.Zero; }
                ResumeThread(pi.hThread);
                WaitForSingleObject(pi.hProcess, INFINITE);
                uint code;
                return GetExitCodeProcess(pi.hProcess, out code) ? unchecked((int)code) : FailCode;
            }
            finally
            {
                CloseHandle(pi.hThread);
                CloseHandle(pi.hProcess);
                // a csoportot csak a folyamat vége után zárjuk; ha az indítót előbb leállítják, a Windows zárja be (és leállítja a gyereket)
                if (job != IntPtr.Zero) CloseHandle(job);
            }
        }

        static bool Valid(IntPtr h) { return h != IntPtr.Zero && h != new IntPtr(-1); }

        static IntPtr CreateJob()
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return IntPtr.Zero;
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK;
            int len = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            IntPtr p = Marshal.AllocHGlobal(len);
            try
            {
                Marshal.StructureToPtr(info, p, false);
                if (SetInformationJobObject(job, JobObjectExtendedLimitInformation, p, (uint)len)) return job;
            }
            finally { Marshal.FreeHGlobal(p); }
            CloseHandle(job);
            return IntPtr.Zero;
        }

        static int Fail(string msg)
        {
            // a Letöltő indítója gyakran felügyelet nélkül fut (Feladatütemező): ott csak a kilépési kód és a hibakimenet;
            // a Megjelenítőé ablakban szól
#if LETOLTO
            try
            {
                if (Valid(GetStdHandle(STD_ERROR)) || AttachConsole(ATTACH_PARENT_PROCESS))
                    using (var w = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false))) w.WriteLine("WStation: " + msg);
            }
            catch (IOException) { }   // nincs hova írni: a kilépési kód jelzi a hibát
#else
            MessageBoxW(IntPtr.Zero, msg, "WStation", 0x10);
#endif
            return FailCode;
        }

        // ------------------------------------------------------------------ Windows-hívások

        const int STD_INPUT = -10, STD_OUTPUT = -11, STD_ERROR = -12;
        const int ATTACH_PARENT_PROCESS = -1;
        const int STARTF_USESTDHANDLES = 0x100;
        const uint CREATE_SUSPENDED = 0x4;
        const uint INFINITE = 0xFFFFFFFF;
        const int JobObjectExtendedLimitInformation = 9;
        const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000, JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK = 0x1000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STARTUPINFO
        {
            public int cb;
            public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GetStdHandle(int n);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(int pid);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
        [DllImport("kernel32.dll", SetLastError = true)] static extern uint ResumeThread(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr h, out uint code);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr sa, string name);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int cls, IntPtr info, uint len);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
#if !LETOLTO
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);
#endif
    }
}
