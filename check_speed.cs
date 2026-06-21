using System;
using System.Reflection;

class Program
{
    static void Main()
    {
        try {
            var player = new FlyleafLib.MediaPlayer.Player();
            player.Speed = -1.0;
            Console.WriteLine("Speed after setting -1: " + player.Speed);
        } catch (Exception e) {
            Console.WriteLine("Exception: " + e.Message);
        }
    }
}
