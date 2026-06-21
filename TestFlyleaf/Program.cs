using System;
using System.Reflection;

class Program
{
    static void Main()
    {
        try {
            var asm = Assembly.LoadFrom(@"C:\apps\timelaps_ex\Timelapse\src\Timelapse\bin\Debug\net10.0-windows\win-x64\FlyleafLib.dll");
            var type = asm.GetType("FlyleafLib.MediaPlayer.Player");
            foreach (var prop in type.GetProperties())
            {
                Console.WriteLine(prop.Name + " : " + prop.PropertyType.Name);
            }
        } catch (Exception e) {
            Console.WriteLine(e.Message);
        }
    }
}
