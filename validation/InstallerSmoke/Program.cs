using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using Timelapse.Images;

internal static class Program
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr NativeString();

    private static string Run(string executable, params string[] arguments)
    {
        ProcessStartInfo info = new(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.Environment["LC_ALL"] = "C";
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(true); throw new TimeoutException(executable); }
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0) throw new Exception($"{executable}: {stderr.Result}");
        return stdout.Result;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        string resultDirectory = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(resultDirectory);
        List<string> passed = [];
        string ffmpeg = Path.Combine(root, "FFmpeg", "ffmpeg.exe");
        string version = Run(ffmpeg, "-version");
        if (version.Contains("--enable-gpl") || version.Contains("--enable-nonfree")) throw new Exception("Unapproved FFmpeg build");
        foreach ((string dll, string symbol) in new[] { ("avutil-59.dll", "avutil_license"), ("avcodec-61.dll", "avcodec_license"), ("avformat-61.dll", "avformat_license") })
        {
            nint library = NativeLibrary.Load(Path.Combine(root, "FFmpeg", dll));
            string license = Marshal.PtrToStringUTF8(Marshal.GetDelegateForFunctionPointer<NativeString>(NativeLibrary.GetExport(library, symbol))());
            if (!license.Contains("LGPL version 2.1 or later")) throw new Exception($"Unexpected {dll} license: {license}");
            passed.Add($"{dll}: {license}");
        }
        NativeLibrary.SetDllImportResolver(typeof(Cv2).Assembly, (name, _, _) => name == "OpenCvSharpExtern" ? NativeLibrary.Load(Path.Combine(root, "OpenCvSharpExtern.dll")) : IntPtr.Zero);
        File.WriteAllText(Path.Combine(resultDirectory, "opencv-build-information.txt"), Cv2.GetBuildInformation());
        using (Mat image = new(32, 32, MatType.CV_8UC1, Scalar.Black))
        using (Mat threshold = new())
        {
            Cv2.Threshold(image, threshold, 18, 255, ThresholdTypes.Binary);
            if (Cv2.CountNonZero(threshold) != 0) throw new Exception("OpenCV smoke failed");
        }
        passed.Add("OpenCvSharp loads from the installed payload and performs image operations");
        string video = Path.Combine(resultDirectory, "日本語 空白の動画.avi");
        Run(ffmpeg, "-y", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=15:duration=3", "-c:v", "mjpeg", "-q:v", "3", video);
        using MemoryStream first = FfmpegThumbnail.Extract(video, null, ffmpeg);
        using MemoryStream later = FfmpegThumbnail.Extract(video, 1.5f, ffmpeg);
        BitmapFrame frame = BitmapFrame.Create(first, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        if (frame.PixelWidth != 320 || frame.PixelHeight != 240) throw new Exception("Invalid thumbnail dimensions");
        if (SHA256.HashData(first.ToArray()).SequenceEqual(SHA256.HashData(later.ToArray()))) throw new Exception("Seeking returned the first frame");
        passed.Add("Thumbnail first frame and requested-time seek, including Japanese/spaced paths");
        foreach (float invalidTime in new[] { float.NaN, float.PositiveInfinity, -1f })
        {
            using MemoryStream invalid = FfmpegThumbnail.Extract(video, invalidTime, ffmpeg);
            if (!invalid.ToArray().SequenceEqual(first.ToArray())) throw new Exception("Invalid timestamp should select the first frame");
        }
        passed.Add("Invalid timestamps safely fall back to the first frame");
        bool rejected = false;
        try { using MemoryStream _ = FfmpegThumbnail.Extract(video + ".missing", null, ffmpeg); }
        catch (InvalidOperationException) { rejected = true; }
        if (!rejected) throw new Exception("Missing video was not rejected");
        passed.Add("Missing input fails without hanging");
        string exiftool = Path.Combine(root, "exiftool.exe");
        string exifVersion = Run(exiftool, "-ver").Trim();
        if (exifVersion != "13.59") throw new Exception("Wrong ExifTool version");
        string metadata = Run(exiftool, "-j", "-FileType", "-ImageWidth", "-ImageHeight", video);
        if (!metadata.Contains("AVI") || !metadata.Contains("320")) throw new Exception("ExifTool did not read video metadata");
        passed.Add("ExifTool 13.59 reads AVI metadata using its installed Perl runtime");
        var result = new { At = DateTimeOffset.Now, Root = root, Passed = passed, FfmpegVersion = version, ExifToolVersion = exifVersion };
        File.WriteAllText(Path.Combine(resultDirectory, "smoke.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(passed, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
