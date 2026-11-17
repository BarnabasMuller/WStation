using System;
using System.IO;

namespace WStation.AdatGen;

/// <summary>Belépési pont: <c>adatgen &lt;kimeneti mappa&gt;</c> – a mappába a Megjelenítő által várt szerkezetet írja.</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] is "-h" or "--help" or "/?")
        {
            Console.Error.WriteLine("Használat: adatgen <kimeneti mappa>");
            Console.Error.WriteLine("Kitalált állomás (" + Generator.StationId + ") 14 hónapnyi mérését írja a <mappa>\\data mappába.");
            return args.Length == 1 ? 0 : 2;
        }
        try
        {
            Generator.Write(args[0]);
            Console.WriteLine("Kész: " + Path.GetFullPath(args[0]));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("A mappa nem írható: " + ex.Message);
            return 1;
        }
    }
}
