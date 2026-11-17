using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using Size = OpenCvSharp.Size;

namespace Timelapse.Images
{
    public static class ImageProcess
    {
        // Preserve the existing parameter meanings and operation order, using OpenCV
        // for adjustment and WPF for decoding. No separately licensed imaging runtime is needed.
        public static async Task<BitmapFrame> StreamToImageProcessedBitmap(MemoryStream input, double brightness, int contrast, bool sharpen, bool mono, bool detectEdges, bool useGamma, float gammaValue, int rotation, RotateFlipType rotateFlip)
        {
            if (input == null || !input.CanRead) return null;
            return await Task.Run(() =>
            {
                try
                {
                    input.Position = 0;
                    if (useGamma)
                    {
                        using System.Drawing.Image drawingImage = System.Drawing.Image.FromStream(input);
                        drawingImage.RotateFlip(rotateFlip);
                        using Bitmap bitmap = AdjustGamma(drawingImage, gammaValue);
                        using MemoryStream output = new();
                        bitmap.Save(output, ImageFormat.Bmp);
                        output.Position = 0;
                        BitmapFrame gammaFrame = BitmapFrame.Create(output, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        gammaFrame.Freeze();
                        return gammaFrame;
                    }

                    BitmapFrame decoded = BitmapFrame.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    var converted = new FormatConvertedBitmap(decoded, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                    int stride = converted.PixelWidth * 4;
                    byte[] pixels = new byte[stride * converted.PixelHeight];
                    converted.CopyPixels(pixels, stride, 0);
                    using Mat bgra = new(converted.PixelHeight, converted.PixelWidth, MatType.CV_8UC4);
                    Marshal.Copy(pixels, 0, bgra.Data, pixels.Length);
                    Rotate(bgra, rotation);
                    using Mat alpha = new();
                    using Mat image = new();
                    Cv2.ExtractChannel(bgra, alpha, 3);
                    Cv2.CvtColor(bgra, image, ColorConversionCodes.BGRA2BGR);
                    if (brightness != 0) ApplyColorScale(image, 1f, (float)brightness - 1f);
                    if (contrast != 0)
                    {
                        float multiplier = 1f + contrast / 100f;
                        ApplyColorScale(image, multiplier, .5f - .5f * multiplier);
                    }
                    if (detectEdges) Kirsch(image);
                    if (sharpen) Sharpen(image);
                    if (mono) EqualizeLuminance(image);
                    Cv2.CvtColor(image, bgra, ColorConversionCodes.BGR2BGRA);
                    if (!detectEdges) Cv2.InsertChannel(alpha, bgra, 3);
                    pixels = new byte[bgra.Rows * bgra.Cols * 4];
                    Marshal.Copy(bgra.Data, pixels, 0, pixels.Length);
                    BitmapSource result = BitmapSource.Create(bgra.Cols, bgra.Rows, decoded.DpiX, decoded.DpiY,
                        System.Windows.Media.PixelFormats.Bgra32, null, pixels, bgra.Cols * 4);
                    BitmapFrame processed = BitmapFrame.Create(result);
                    processed.Freeze();
                    return processed;
                }
                catch
                {
                    return null;
                }
            }).ConfigureAwait(true);
        }

        private static void Rotate(Mat image, int degrees)
        {
            degrees = ((degrees % 360) + 360) % 360;
            if (degrees == 0) return;
            using Mat rotated = new();
            if (degrees % 90 == 0)
                Cv2.Rotate(image, rotated, degrees == 90 ? RotateFlags.Rotate90Clockwise : degrees == 180 ? RotateFlags.Rotate180 : RotateFlags.Rotate90Counterclockwise);
            else
            {
                double radians = degrees * Math.PI / 180;
                int width = (int)Math.Ceiling(Math.Abs(image.Width * Math.Cos(radians)) + Math.Abs(image.Height * Math.Sin(radians)));
                int height = (int)Math.Ceiling(Math.Abs(image.Width * Math.Sin(radians)) + Math.Abs(image.Height * Math.Cos(radians)));
                using Mat matrix = Cv2.GetRotationMatrix2D(new Point2f((image.Width - 1) / 2f, (image.Height - 1) / 2f), -degrees, 1);
                matrix.Set(0, 2, matrix.At<double>(0, 2) + (width - image.Width) / 2.0);
                matrix.Set(1, 2, matrix.At<double>(1, 2) + (height - image.Height) / 2.0);
                Cv2.WarpAffine(image, rotated, matrix, new Size(width, height), InterpolationFlags.Cubic);
            }
            rotated.CopyTo(image);
        }

        private static Mat Luminance(Mat image)
        {
            using Mat coefficients = Mat.FromArray(new float[,] { { 0.0722f, 0.7152f, 0.2126f } });
            using Mat floating = new();
            image.ConvertTo(floating, MatType.CV_32FC3);
            Mat gray = new();
            Cv2.Transform(floating, gray, coefficients);
            return gray;
        }

        private static void ApplyColorScale(Mat image, float multiplier, float offset)
        {
            byte[] values = new byte[256];
            for (int i = 0; i < values.Length; i++)
            {
                float value = (i * (1f / 255f)) * multiplier + offset;
                values[i] = (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
            }
            using Mat lookup = Mat.FromArray(values);
            Cv2.LUT(image, lookup, image);
        }

        private static void Kirsch(Mat image)
        {
            using Mat gray = new(image.Size(), MatType.CV_8UC1);
            byte[] colors = new byte[image.Rows * image.Cols * 3];
            byte[] grays = new byte[image.Rows * image.Cols];
            Marshal.Copy(image.Data, colors, 0, colors.Length);
            for (int i = 0; i < grays.Length; i++)
            {
                float value = colors[i * 3 + 2] / 255f * .2126f + colors[i * 3 + 1] / 255f * .7152f
                    + colors[i * 3] / 255f * (1f - (.2126f + .7152f));
                grays[i] = (byte)Math.Clamp((int)(value * 255f + .5f), 0, 255);
            }
            Marshal.Copy(grays, 0, gray.Data, grays.Length);
            using Mat maximum = Mat.Zeros(image.Size(), MatType.CV_8UC1);
            (int X, int Y)[] ring = [(0,0),(1,0),(2,0),(2,1),(2,2),(1,2),(0,2),(0,1)];
            for (int direction = 0; direction < 8; direction++)
            {
                using Mat kernel = Mat.Zeros(3, 3, MatType.CV_32FC1);
                for (int i = 0; i < ring.Length; i++)
                    kernel.Set(ring[i].Y, ring[i].X, (i - direction + 8) % 8 < 3 ? 5f : -3f);
                using Mat pass = new();
                Cv2.Filter2D(gray, pass, -1, kernel, borderType: BorderTypes.Replicate);
                Cv2.Max(maximum, pass, maximum);
            }
            Cv2.CvtColor(maximum, image, ColorConversionCodes.GRAY2BGR);
        }

        private static void Sharpen(Mat image)
        {
            using Mat gaussian = Cv2.GetGaussianKernel(19, 3, MatType.CV_32F);
            using Mat kernel = -gaussian;
            kernel.Set(9, 0, kernel.At<float>(9, 0) + 2);
            using Mat horizontalKernel = kernel.T();
            using Mat horizontal = new();
            // The previous processor quantized/clipped after each one-dimensional pass.
            Cv2.Filter2D(image, horizontal, -1, horizontalKernel, borderType: BorderTypes.Replicate);
            Cv2.Filter2D(horizontal, image, -1, kernel, borderType: BorderTypes.Replicate);
        }

        private static void EqualizeLuminance(Mat image)
        {
            using Mat luminance = Luminance(image);
            using Mat gray = new();
            luminance.ConvertTo(gray, MatType.CV_8UC1);
            using Mat equalized = new();
            Cv2.EqualizeHist(gray, equalized);
            Cv2.CvtColor(equalized, image, ColorConversionCodes.GRAY2BGR);
        }

        private static Bitmap AdjustGamma(System.Drawing.Image image, float gamma)
        {
            if (gamma <= 0) gamma = .1f;
            using ImageAttributes attributes = new();
            attributes.SetGamma(gamma);
            System.Drawing.Point[] points = [new(0, 0), new(image.Width, 0), new(0, image.Height)];
            System.Drawing.Rectangle rectangle = new(0, 0, image.Width, image.Height);
            Bitmap bitmap = new(image.Width, image.Height);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.DrawImage(image, points, rectangle, GraphicsUnit.Pixel, attributes);
            return bitmap;
        }
    }
}
