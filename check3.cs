using System;
using System.Reflection;

class Program
{
    static void Main()
    {
        var asm = Assembly.LoadFrom(@"C:\apps\timelaps_ex\Timelapse\src\Timelapse\bin\Debug\net10.0-windows\win-x64\FlyleafLib.dll");
        Type t = asm.GetType("FlyleafLib.MediaPlayer.Status");
        if (t != null && t.IsEnum)
        {
            foreach (var n in Enum.GetNames(t))
                Console.WriteLine(n);
        }
    }
}
