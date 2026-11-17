using System.Reflection;

namespace WStation.Core.Common
{
    /// <summary>A program verziója – a Directory.Build.props egyetlen verziószámából.</summary>
    public static class BuildInfo
    {
        /// <summary>A verzió szövege, pl. „3.0.0-alpha.1” (a fordító által hozzáfűzött commit-azonosító nélkül).</summary>
        public static string Version
        {
            get
            {
                var a = typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                string v = a != null ? a.InformationalVersion : "";
                int plus = v.IndexOf('+');
                return plus >= 0 ? v.Substring(0, plus) : v;
            }
        }
    }
}
