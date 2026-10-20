using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Kozos
{
    // A Letöltő automatikus futtatása a Windows Feladatütemezőjével (schtasks, a felhasználó nevében, rendszergazda nélkül)
    static class Utemezes
    {
        public const string TaskName = "WStation – letöltés";
        // a korábbi verziók feladatneve: felismerjük, és az új beállításakor töröljük
        static readonly string[] LegacyTaskNames = { "Budaörsi időjárás – letöltés" };
        public static readonly int[] Choices = { 15, 30, 60, 180 };
        public static readonly string[] DayCodes = { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
        static readonly string[] DayXml = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        static readonly string[] DayHu = { "H", "K", "Sze", "Cs", "P", "Szo", "V" };

        // ütemezés leírása: kind = off | interval | daily | weekly
        public class Spec
        {
            public string kind = "off";
            public int minutes;               // interval: percben (15 … 1440)
            public string time = "06:00";     // daily / weekly: HH:MM
            public List<string> days = new List<string>();   // weekly: MON … SUN
            public string exe;                // a feladat által futtatott program
        }

        // a schtasks a konzol (OEM) kódlapján ír, magyar gépen 852
        static readonly Encoding Oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);

        static int Run(string args, out string output)
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Oem, StandardErrorEncoding = Oem
            };
            using (var p = Process.Start(psi))
            {
                // mindkét kimenet aszinkron: egy szinkron ReadToEnd a kilépésig blokkolna, és az időkorlát
                // sosem lépne életbe (egy beragadt schtasks percekig feltartotta a telepítőt)
                var oTask = p.StandardOutput.ReadToEndAsync();
                var eTask = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(20000))
                {
                    try { p.Kill(); } catch { }
                    output = "Az ütemező (schtasks) 20 másodperc alatt nem válaszolt.";
                    return -1;
                }
                p.WaitForExit();   // a kimenetek teljes kiolvasása
                output = (oTask.Result + eTask.Result).Trim();
                return p.ExitCode;
            }
        }

        public static Spec QuerySpec()
        {
            // az új nevű feladat, ha nincs, a régi nevű (frissítés előtti) feladat
            var s = QuerySpecFor(TaskName);
            foreach (var n in LegacyTaskNames) if (s.kind == "off") s = QuerySpecFor(n);
            return s;
        }

        static void RemoveLegacy()
        {
            string o;
            foreach (var n in LegacyTaskNames)
                try { if (QuerySpecFor(n).kind != "off") Run("/Delete /F /TN \"" + n + "\"", out o); } catch { }
        }

        static Spec QuerySpecFor(string taskName)
        {
            var s = new Spec();
            string xml;
            try { if (Run("/Query /TN \"" + taskName + "\" /XML", out xml) != 0) return s; }
            catch { return s; }
            var m = Regex.Match(xml, @"<Command>(.*?)</Command>", RegexOptions.Singleline);
            if (m.Success) s.exe = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim('"');
            var st = Regex.Match(xml, @"<StartBoundary>[^<]*T(\d{2}):(\d{2})");
            if (st.Success) s.time = st.Groups[1].Value + ":" + st.Groups[2].Value;
            var iv = Regex.Match(xml, @"<Interval>P(?:(\d+)D)?T?(?:(\d+)H)?(?:(\d+)M)?</Interval>");
            if (iv.Success)
            {
                s.kind = "interval";
                s.minutes = (iv.Groups[1].Success ? int.Parse(iv.Groups[1].Value) * 1440 : 0) + (iv.Groups[2].Success ? int.Parse(iv.Groups[2].Value) * 60 : 0) + (iv.Groups[3].Success ? int.Parse(iv.Groups[3].Value) : 0);
            }
            else if (xml.Contains("<ScheduleByWeek>"))
            {
                s.kind = "weekly";
                for (int i = 0; i < 7; i++) if (xml.Contains("<" + DayXml[i] + " />") || xml.Contains("<" + DayXml[i] + "/>")) s.days.Add(DayCodes[i]);
            }
            else if (xml.Contains("<ScheduleByDay>"))
            {
                var di = Regex.Match(xml, @"<DaysInterval>(\d+)</DaysInterval>");
                s.kind = "daily";
                if (di.Success && di.Groups[1].Value != "1") { s.kind = "interval"; s.minutes = int.Parse(di.Groups[1].Value) * 1440; }
            }
            else s.kind = "interval";
            if (s.kind == "interval" && s.minutes <= 0) s.minutes = 1440;
            return s;
        }

        // kompatibilitás: 0 = nincs ütemezve, egyébként a gyakoriság percben (napi/heti: 1440 ill. 10080)
        public static int Query(out string exePath)
        {
            var s = QuerySpec();
            exePath = s.exe;
            return s.kind == "off" ? 0 : s.kind == "interval" ? s.minutes : s.kind == "daily" ? 1440 : 10080;
        }

        public static bool Set(string exePath, int minutes, out string message)
        {
            return SetSpec(exePath, new Spec { kind = minutes > 0 ? "interval" : "off", minutes = minutes }, out message);
        }

        static bool ValidTime(string t)
        {
            var m = Regex.Match(t ?? "", @"^(\d{2}):(\d{2})$");
            return m.Success && int.Parse(m.Groups[1].Value) < 24 && int.Parse(m.Groups[2].Value) < 60;
        }

        public static bool SetSpec(string exePath, Spec s, out string message)
        {
            if (s == null || s.kind == "off") return Remove(out message);
            string tr = "\\\"" + exePath + "\\\" --nowait";
            string when;
            switch (s.kind)
            {
                case "interval":
                    if (s.minutes < 5 || s.minutes > 1440 * 31) { message = "Érvénytelen gyakoriság (5 perc … 31 nap)."; return false; }
                    if (s.minutes % 1440 == 0) when = "/SC DAILY /MO " + (s.minutes / 1440) + " /ST " + (ValidTime(s.time) ? s.time : "06:00");
                    else if (s.minutes % 60 == 0 && s.minutes / 60 < 24) when = "/SC HOURLY /MO " + (s.minutes / 60);
                    else if (s.minutes < 1440) when = "/SC MINUTE /MO " + s.minutes;
                    else { message = "Egy napnál hosszabb gyakoriság csak egész napokban adható meg."; return false; }
                    break;
                case "daily":
                    if (!ValidTime(s.time)) { message = "Érvénytelen időpont (ÓÓ:PP)."; return false; }
                    when = "/SC DAILY /ST " + s.time;
                    break;
                case "weekly":
                    var days = (s.days ?? new List<string>()).Where(d => DayCodes.Contains(d)).Distinct().ToList();
                    if (days.Count == 0) { message = "Válassz legalább egy napot."; return false; }
                    if (!ValidTime(s.time)) { message = "Érvénytelen időpont (ÓÓ:PP)."; return false; }
                    when = "/SC WEEKLY /D " + string.Join(",", days) + " /ST " + s.time;
                    break;
                default: message = "Ismeretlen ütemezés."; return false;
            }
            string o;
            // elsőként saját feladat-XML-lel: akkumulátorról is fut, és a kimaradt futást (kikapcsolt / alvó gép) pótolja
            int code = CreateFromXml(exePath, s, out o);
            if (code != 0) code = Run("/Create /F /TN \"" + TaskName + "\" /TR \"" + tr + "\" " + when, out o);
            if (code == 0) RemoveLegacy();   // a régi nevű feladat ne fusson párhuzamosan
            message = code == 0 ? "Automatikus letöltés beállítva: " + Text(s) + "." : "Az ütemezés nem sikerült: " + o;
            return code == 0;
        }

        // A feladat XML-ből: a schtasks /SC alapbeállításai („csak hálózati tápellátásnál”, nincs „kimaradt futás pótlása”)
        // laptopon és ki-be kapcsolt gépen sok letöltést kihagynának.
        static int CreateFromXml(string exePath, Spec s, out string output)
        {
            output = "";
            if (Guard.Root == null) return -1;
            string xml = BuildTaskXml(exePath, s);
            if (xml == null) return -1;
            string file = System.IO.Path.Combine(Guard.Root, "utemezes_feladat.xml");
            try
            {
                Guard.WriteAllText(file, xml, Encoding.Unicode);   // a schtasks /XML UTF-16-ot vár
                return Run("/Create /F /TN \"" + TaskName + "\" /XML \"" + file + "\"", out output);
            }
            catch (Exception ex) { output = ex.Message; return -1; }
            finally { try { if (System.IO.File.Exists(file)) Guard.Delete(file); } catch { } }
        }

        internal static string BuildTaskXml(string exePath, Spec s)
        {
            string trigger;
            DateTime now = DateTime.Now;
            string time = ValidTime(s.time) ? s.time : "06:00";
            string startAt = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T" + time + ":00";
            switch (s.kind)
            {
                case "interval":
                    if (s.minutes % 1440 == 0)
                        trigger = "<CalendarTrigger><StartBoundary>" + startAt + "</StartBoundary><Enabled>true</Enabled><ScheduleByDay><DaysInterval>" + (s.minutes / 1440) + "</DaysInterval></ScheduleByDay></CalendarTrigger>";
                    else
                    {
                        string iv = s.minutes % 60 == 0 ? "PT" + (s.minutes / 60) + "H" : "PT" + s.minutes + "M";
                        trigger = "<TimeTrigger><Repetition><Interval>" + iv + "</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition>" +
                                  "<StartBoundary>" + now.AddMinutes(1).ToString("yyyy-MM-ddTHH:mm:00", CultureInfo.InvariantCulture) + "</StartBoundary><Enabled>true</Enabled></TimeTrigger>";
                    }
                    break;
                case "daily":
                    trigger = "<CalendarTrigger><StartBoundary>" + startAt + "</StartBoundary><Enabled>true</Enabled><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger>";
                    break;
                case "weekly":
                    var days = (s.days ?? new List<string>()).Where(d => DayCodes.Contains(d)).Distinct().ToList();
                    trigger = "<CalendarTrigger><StartBoundary>" + startAt + "</StartBoundary><Enabled>true</Enabled><ScheduleByWeek><DaysOfWeek>" +
                              string.Concat(days.Select(d => "<" + DayXml[Array.IndexOf(DayCodes, d)] + " />")) + "</DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek></CalendarTrigger>";
                    break;
                default: return null;
            }
            Func<string, string> x = v => System.Security.SecurityElement.Escape(v);
            string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                "<RegistrationInfo><Description>" + x("A Weather Underground mérések letöltése (Letöltő --nowait).") + "</Description></RegistrationInfo>" +
                "<Triggers>" + trigger + "</Triggers>" +
                "<Principals><Principal id=\"Author\"><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><StartWhenAvailable>true</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>" +
                "<IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings><Enabled>true</Enabled><Hidden>false</Hidden>" +
                "<ExecutionTimeLimit>PT2H</ExecutionTimeLimit><Priority>7</Priority></Settings>" +
                "<Actions Context=\"Author\"><Exec><Command>" + x("\"" + exePath + "\"") + "</Command><Arguments>--nowait</Arguments></Exec></Actions></Task>";
            return xml;
        }

        // telepítés idejére: az ütemezett letöltés ne induljon el a fájlcsere közben (16.7); true, ha volt mit szüneteltetni
        public static bool Pause(bool pause)
        {
            try
            {
                string o, xml;
                if (Run("/Query /TN \"" + TaskName + "\" /XML", out xml) != 0) return false;   // nincs ilyen feladat
                // a felhasználó által kikapcsolt feladatot nem kapcsoljuk be utána
                if (pause && Regex.IsMatch(xml, @"<Settings>[\s\S]*?<Enabled>false</Enabled>[\s\S]*?</Settings>")) return false;
                return Run("/Change /TN \"" + TaskName + "\" /" + (pause ? "DISABLE" : "ENABLE"), out o) == 0;
            }
            catch { return false; }
        }

        public static bool Remove(out string message)
        {
            if (QuerySpec().kind == "off") { message = "Nincs ütemezett letöltés."; return true; }
            string o = "";
            int code = QuerySpecFor(TaskName).kind == "off" ? 0 : Run("/Delete /F /TN \"" + TaskName + "\"", out o);
            RemoveLegacy();
            message = code == 0 ? "Az automatikus letöltés kikapcsolva." : "A törlés nem sikerült: " + o;
            return code == 0;
        }

        public static string Text(int minutes) { return Text(new Spec { kind = minutes > 0 ? "interval" : "off", minutes = minutes }); }

        public static string Text(Spec s)
        {
            switch (s.kind)
            {
                case "off": return "kikapcsolva";
                case "daily": return "naponta " + s.time + "-kor";
                case "weekly": return "hetente (" + string.Join(", ", s.days.Select(d => DayHu[Array.IndexOf(DayCodes, d)])) + ") " + s.time + "-kor";
                default:
                    int m = s.minutes;
                    if (m % 1440 == 0) return m == 1440 ? "naponta" : m / 1440 + " naponta";
                    if (m % 60 == 0) return m == 60 ? "óránként" : m / 60 + " óránként";
                    return m + " percenként";
            }
        }
    }
}
