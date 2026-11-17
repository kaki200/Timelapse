using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Timelapse.Images
{
    /// <summary>Runs the bundled FFmpeg tool without requiring a writable install directory or PATH.</summary>
    internal static class FfmpegThumbnail
    {
        internal static MemoryStream Extract(string mediaPath, float? seconds, string executablePath = null)
        {
            return ExtractAsync(mediaPath, seconds, executablePath).GetAwaiter().GetResult();
        }

        private static async Task<MemoryStream> ExtractAsync(string mediaPath, float? seconds, string executablePath)
        {
            executablePath ??= Path.Combine(AppContext.BaseDirectory, "FFmpeg", "ffmpeg.exe");
            ProcessStartInfo startInfo = new(executablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(executablePath)
            };
            foreach (string argument in new[] { "-hide_banner", "-loglevel", "error", "-nostdin" })
                startInfo.ArgumentList.Add(argument);
            if (seconds.HasValue && float.IsFinite(seconds.Value) && seconds.Value > 0)
            {
                startInfo.ArgumentList.Add("-ss");
                startInfo.ArgumentList.Add(seconds.Value.ToString("R", CultureInfo.InvariantCulture));
            }
            foreach (string argument in new[]
            {
                "-i", Path.GetFullPath(mediaPath), "-map", "0:v:0", "-frames:v", "1",
                "-an", "-sn", "-dn", "-c:v", "bmp", "-f", "image2pipe", "pipe:1"
            })
                startInfo.ArgumentList.Add(argument);

            using Process process = new() { StartInfo = startInfo };
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
            MemoryStream output = new();
            try
            {
                process.Start();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                Task copy = process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
                try
                {
                    await Task.WhenAll(copy, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
                }
                catch
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                    try { await copy.ConfigureAwait(false); } catch { }
                    throw;
                }
                string error = await stderr.ConfigureAwait(false);
                if (process.ExitCode != 0 || output.Length == 0)
                    throw new InvalidOperationException($"FFmpeg thumbnail failed ({process.ExitCode}): {error}");
                output.Position = 0;
                return output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }
    }
}
