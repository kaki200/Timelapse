using System;
using System.Reflection;

class Program
{
    static void Main()
    {
        var asm = Assembly.LoadFrom(@"C:\apps\timelaps_ex\Timelapse\src\Timelapse\bin\Debug\net10.0-windows\win-x64\FlyleafLib.dll");
        Type[] types;
        try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; }
        
        foreach (var t in types)
        {
            if (t != null && t.Name == "Player")
            {
                Console.WriteLine("--- Properties ---");
                foreach (var p in t.GetProperties())
                {
                    if (p.Name == "CurTime" || p.Name.Contains("Time") || p.Name.Contains("Pos"))
                        Console.WriteLine(p.PropertyType.Name + " " + p.Name + " (CanWrite: " + p.CanWrite + ")");
                }
                Console.WriteLine("--- Methods ---");
                foreach (var m in t.GetMethods())
                {
                    if (m.Name.Contains("Seek") || m.Name.Contains("Time"))
                        Console.WriteLine(m.Name);
                }
            }
        }
    }
}
