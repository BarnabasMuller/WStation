// NET48: a telepítő is fordítja (WStation.Installer) – C# 10, csak .NET Framework 4.8-ban is meglévő API (PLAN-3.0 9.4)
#nullable disable
using System.Collections.Generic;

namespace WStation.Core.Common
{
    // A 2.x Kozos.Utemezes.Spec osztálya (1.5: a magba került, mert az API is használja – PLAN-3.0 7.1: ITaskScheduling)
    // ütemezés leírása: kind = off | interval | daily | weekly
    public class ScheduleSpec
    {
        public string kind = "off";
        public int minutes;               // interval: percben (15 … 1440)
        public string time = "06:00";     // daily / weekly: HH:MM
        public List<string> days = new List<string>();   // weekly: MON … SUN
        public string exe;                // a feladat által futtatott program
    }
}
