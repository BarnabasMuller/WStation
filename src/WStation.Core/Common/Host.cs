namespace WStation.Core.Common
{
    // IDEIGLENES (1.5): a 2.x-ben a Megjelenítő ablakához (WinForms) és a Windowshoz kötött műveletek a mag ezen a felületen
    // át éri el; a megvalósítás az asztali gazdában van (WStation.Desktop). Ha nincs gazda (teszt, később a szerver), a mag úgy
    // viselkedik, mint a 2.x böngészős módja (nincs ablak). Az S2 (környezet, Ctx) és az 1.6 (asztali gazda) váltja ki.

    /// <summary>A Windows-ablakhoz kötött műveletek (párbeszédablakok, Intéző, frissítő indítása).</summary>
    public interface IDesktopHost
    {
        /// <summary>Van-e programablak (a 2.x-ben: <c>Program.Window != null &amp;&amp; !IsDisposed</c>).</summary>
        bool HasWindow { get; }

        /// <summary>Mentés-párbeszédablak a programablak fölött; a választott útvonal, vagy <c>null</c>, ha a felhasználó mégsem mentett.</summary>
        string? SaveFileDialog(string title, string filter, string fileName, string? initialDirectory);

        /// <summary>Megnyitás-párbeszédablak a programablak fölött; a választott útvonal, vagy <c>null</c>.</summary>
        string? OpenFileDialog(string title, string filter, string? initialDirectory);

        /// <summary>Egy mappa megnyitása a Windows Intézőben (hiba esetén kivétel).</summary>
        void OpenFolder(string path);

        /// <summary>A telepítő (frissítés) elindítása (a 2.x-ben <c>UseShellExecute = true</c>; hiba esetén kivétel).</summary>
        void StartInstaller(string exe, string arguments, string workingDirectory);

        /// <summary>A program bezárása a frissítéshez: az ablak bezárul, ablak nélkül a folyamat kilép (a 2.x szerint).</summary>
        void CloseForUpdate();
    }

    /// <summary>A Letöltő automatikus futtatása (a 2.x-ben a Windows Feladatütemezője – <c>Kozos.Utemezes</c>).</summary>
    public interface ITaskScheduling
    {
        int[] Choices { get; }
        ScheduleSpec QuerySpec();
        bool SetSpec(string exePath, ScheduleSpec s, out string message);
        bool Pause(bool pause);
        string Text(ScheduleSpec s);
    }

    /// <summary>A gazda által beállított megvalósítások (ideiglenes, 1.5).</summary>
    public static class Host
    {
        /// <summary>Az asztali gazda; <c>null</c>, ha nincs programablak (teszt, szerver).</summary>
        public static IDesktopHost? Desktop { get; set; }

        /// <summary>A Feladatütemező; <c>null</c>, ha a gépen nincs (nem Windows).</summary>
        public static ITaskScheduling? Scheduler { get; set; }
    }
}
