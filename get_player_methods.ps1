$source = @"
using System;
using System.Reflection;
public class Test {
    public static void Run() {
        var asm = Assembly.LoadFrom(@"C:\apps\timelaps_ex\Timelapse\src\Timelapse\bin\Debug\net10.0-windows\win-x64\FlyleafLib.dll");
        Type[] types;
        try { types = asm.GetTypes(); } catch(ReflectionTypeLoadException e) { types = e.Types; }
        foreach(var t in types) {
            if (t != null && t.Name == "Player") {
                foreach(var m in t.GetMethods()) {
                    Console.WriteLine(m.Name);
                }
            }
        }
    }
}
"@
Add-Type -TypeDefinition $source -Language CSharp
[Test]::Run()
