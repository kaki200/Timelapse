using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Processing;
using Timelapse.Images;
using Image = SixLabors.ImageSharp.Image;

internal static class Program
{
    private sealed record Settings(string Name, double Brightness = 0, int Contrast = 0, bool Sharpen = false, bool Mono = false, bool Edges = false, int Rotation = 0);

    [STAThread]
    private static int Main(string[] args)
    {
        byte[] pixels = new byte[96 * 64 * 4];
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 96; x++)
            {
                int i = (y * 96 + x) * 4;
                pixels[i] = (byte)(x * 255 / 95);
                pixels[i + 1] = (byte)(y * 255 / 63);
                pixels[i + 2] = (byte)((x * 3 + y * 5) % 256);
                pixels[i + 3] = 255;
            }
        BitmapSource source = BitmapSource.Create(96, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 96 * 4);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using MemoryStream png = new();
        encoder.Save(png);
        List<object> measurements = [];
        Settings[] cases = [new("neutral"), new("lightness", Brightness: 1.25), new("contrast", Contrast: 40),
            new("darker", Brightness: .75, Contrast: -30), new("rotate90", Rotation: 90), new("rotate180", Rotation: 180),
            new("rotate270", Rotation: 270), new("kirsch", Edges: true), new("sharpen", Sharpen: true), new("equalize", Mono: true),
            new("lightness-fraction", Brightness: 1.1), new("combined-color", Brightness: 1.1, Contrast: 15),
            new("combined-no-equalize", Brightness: 1.1, Contrast: 15, Sharpen: true, Edges: true, Rotation: 90),
            new("combined", Brightness: 1.1, Contrast: 15, Sharpen: true, Edges: true, Mono: true, Rotation: 90)];
        bool failed = false;
        foreach (Settings settings in cases)
        {
            png.Position = 0;
            using var baseline = Image.Load(png);
            baseline.Mutate(context =>
            {
                if (settings.Rotation != 0) context.Rotate(settings.Rotation);
                if (settings.Brightness != 0) context.Lightness((float)settings.Brightness);
                if (settings.Contrast != 0) context.Contrast(1 + settings.Contrast / 100f);
                if (settings.Edges) context.DetectEdges(KnownEdgeDetectorKernels.Kirsch);
                if (settings.Sharpen) context.GaussianSharpen(3f);
                if (settings.Mono) context.HistogramEqualization();
            });
            using MemoryStream original = new();
            baseline.Save(original, new BmpEncoder());
            original.Position = 0;
            BitmapFrame expected = BitmapFrame.Create(original, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            png.Position = 0;
            BitmapFrame actual = ImageProcess.StreamToImageProcessedBitmap(png, settings.Brightness, settings.Contrast,
                settings.Sharpen, settings.Mono, settings.Edges, false, 1, settings.Rotation, RotateFlipType.RotateNoneFlipNone).GetAwaiter().GetResult();
            if (actual == null || actual.PixelWidth != expected.PixelWidth || actual.PixelHeight != expected.PixelHeight || !actual.IsFrozen)
                throw new Exception($"Invalid output: {settings.Name}");
            byte[] first = GetPixels(expected);
            byte[] second = GetPixels(actual);
            double total = 0;
            int maximum = 0;
            for (int i = 0; i < first.Length; i++)
            {
                int difference = Math.Abs(first[i] - second[i]);
                total += difference;
                maximum = Math.Max(maximum, difference);
            }
            double mean = total / first.Length;
            measurements.Add(new { settings.Name, MeanAbsoluteError = mean, MaximumError = maximum });
            Console.WriteLine($"{settings.Name}: mean={mean:F4}, max={maximum}");
            if (mean > 1.0 || maximum > 5) failed = true;
        }
        using MemoryStream broken = new([1, 2, 3]);
        if (ImageProcess.StreamToImageProcessedBitmap(broken, 0, 0, false, false, false, false, 1, 0, RotateFlipType.RotateNoneFlipNone).Result != null)
            throw new Exception("Corrupt input was not rejected");
        if (args.Length > 0) File.WriteAllText(args[0], JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
        if (failed) return 1;
        Console.WriteLine("PASS: image adjustments match the previous implementation within 1/255 mean and 5/255 maximum; corrupt input rejected; frozen cross-thread output.");
        return 0;
    }

    private static byte[] GetPixels(BitmapSource image)
    {
        FormatConvertedBitmap converted = new(image, PixelFormats.Bgra32, null, 0);
        byte[] output = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(output, converted.PixelWidth * 4, 0);
        return output;
    }
}
