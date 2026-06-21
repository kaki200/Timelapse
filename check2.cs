using System;
using System.Reflection;

class Program
{
    static void Main()
    {
        var asm = Assembly.LoadFrom(@"C:\apps\timelaps_ex\Timelapse\src\Timelapse\bin\Debug\net10.0-windows\win-x64\FlyleafLib.dll");
        Type t = asm.GetType("FlyleafLib.MediaPlayer.PlaybackStoppedArgs");
        if (t != null)
        {
            foreach (var p in t.GetProperties())
                Console.WriteLine(p.PropertyType.Name + " " + p.Name);
        }
    }
}
