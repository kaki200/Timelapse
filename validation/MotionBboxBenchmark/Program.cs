using System.Diagnostics;
using System.Buffers;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;
using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;
using Timelapse.MotionDetection;
using Cv2 = OpenCvSharp.Cv2;
using CvMat = OpenCvSharp.Mat;
using CvMatType = OpenCvSharp.MatType;
using CvRect = OpenCvSharp.Rect;
using CvScalar = OpenCvSharp.Scalar;

internal static class Program
{
    private sealed record FrameWork(
        long PresentationTimestampTicks,
        long BytesCopied,
        double CaptureMilliseconds,
        MotionBboxResult? MotionResult,
        byte[]? Pixels,
        int PixelWidth,
        int PixelHeight,
        int Stride);

    private sealed record MemorySample(double ElapsedSeconds, double WorkingSetMb, double PrivateMb, int Handles);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0 || !File.Exists(args[0]))
        {
            Console.Error.WriteLine("Usage: MotionBboxBenchmark <video> [seconds] [width] [rate] [off|frame_diff|mog2|hybrid] [output-json]");
            return 2;
        }

        string video = Path.GetFullPath(args[0]);
        int seconds = args.Length > 1 && int.TryParse(args[1], out int parsedSeconds) ? Math.Max(3, parsedSeconds) : 12;
        uint width = args.Length > 2 && uint.TryParse(args[2], out uint parsedWidth) ? Math.Max(160U, parsedWidth) : 640U;
        double requestedRate = args.Length > 3 && double.TryParse(args[3], out double parsedRate) ? Math.Clamp(parsedRate, 5.0, 30.0) : 15.0;
        string mode = args.Length > 4 ? args[4].ToLowerInvariant() : "frame_diff";
        string? outputPath = args.Length > 5 ? Path.GetFullPath(args[5]) : null;
        MotionBboxAlgorithm algorithm = mode switch
        {
            "mog2" => MotionBboxAlgorithm.Mog2,
            "hybrid" => MotionBboxAlgorithm.Hybrid,
            _ => MotionBboxAlgorithm.FrameDifference
        };
        bool analysisEnabled = mode != "off";
        int exitCode = 3;

        Application application = new();
        Engine.Start(new EngineConfig
        {
            FFmpegPath = Path.Combine(AppContext.BaseDirectory, "FFmpeg")
        });

        using Player player = new();
        using MotionBboxEngine motionEngine = new();
        Canvas overlay = new() { Background = Brushes.Transparent, IsHitTestVisible = false };
        Rectangle[] rectangles = Enumerable.Range(0, 4).Select(index =>
        {
            Rectangle rectangle = new()
            {
                Stroke = index == 0 ? Brushes.Orange : Brushes.Gold,
                StrokeThickness = index == 0 ? 4 : 2,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            overlay.Children.Add(rectangle);
            return rectangle;
        }).ToArray();
        FlyleafHost host = new() { Player = player, Content = overlay };
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = host
        };

        window.Loaded += async (_, _) =>
        {
            try
            {
                window.UpdateLayout();
                TaskCompletionSource<OpenCompletedArgs> opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
                player.OpenCompleted += (_, eventArgs) => opened.TrySetResult(eventArgs);
                player.OpenAsync(video);
                OpenCompletedArgs completed = await opened.Task.WaitAsync(TimeSpan.FromSeconds(20));
                if (completed.Error != null)
                {
                    Console.Error.WriteLine(completed.Error);
                    exitCode = 3;
                    return;
                }

                player.Play();
                long initialPts = player.CurTime;
                bool playbackAdvanced = false;
                for (int retry = 0; retry < 20; retry++)
                {
                    await Task.Delay(100);
                    if (player.CurTime > initialPts)
                    {
                        playbackAdvanced = true;
                        break;
                    }
                    player.Play();
                }
                Console.Error.WriteLine($"Playback ready: advanced={playbackAdvanced}, playing={player.IsPlaying}, canPlay={player.CanPlay}, pts={player.CurTime}");

                MotionBboxSettings settings = new()
                {
                    AnalysisWidth = (int)width,
                    Algorithm = algorithm,
                    BottomBandMode = BottomBandMode.Automatic
                };
                Process process = Process.GetCurrentProcess();
                process.Refresh();
                TimeSpan cpuBefore = process.TotalProcessorTime;
                long gcBefore = GC.GetTotalAllocatedBytes(true);
                long workingSetBefore = process.WorkingSet64;
                long privateBytesBefore = process.PrivateMemorySize64;
                int handlesBefore = process.HandleCount;
                int[] collectionsBefore = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
                List<MemorySample> memorySamples = [new(0, workingSetBefore / 1048576.0, privateBytesBefore / 1048576.0, handlesBefore)];
                double nextMemorySampleSeconds = 60;
                Stopwatch wall = Stopwatch.StartNew();
                List<double> captureMilliseconds = [];
                List<double> analysisMilliseconds = [];
                List<double> displayMilliseconds = [];
                List<double> endToEndMilliseconds = [];
                List<long> pts = [];
                List<double> startupCaptureMilliseconds = [];
                List<double> startupAnalysisMilliseconds = [];
                List<double> startupDisplayMilliseconds = [];
                List<double> startupEndToEndMilliseconds = [];
                List<long> startupPts = [];
                List<double> steadyCaptureMilliseconds = [];
                List<double> steadyAnalysisMilliseconds = [];
                List<double> steadyDisplayMilliseconds = [];
                List<double> steadyEndToEndMilliseconds = [];
                List<long> steadyPts = [];
                int failed = 0;
                int displayed = 0;
                int boxesDisplayed = 0;
                long bytesCopied = 0;
                FrameWork? previewFrame = null;
                long nextDue = 0;
                while (wall.Elapsed < TimeSpan.FromSeconds(seconds))
                {
                    if (!player.IsPlaying && player.Duration > 0 && player.CurTime >= player.Duration - TimeSpan.FromMilliseconds(500).Ticks)
                    {
                        player.CurTime = 0;
                        player.Play();
                        await Task.Delay(50);
                    }
                    Stopwatch endToEnd = Stopwatch.StartNew();
                    bool capturePreview = previewFrame == null;
                    FrameWork frame = await Task.Run(() => CaptureAndAnalyze(player, motionEngine, video, width, settings, analysisEnabled, capturePreview));
                    captureMilliseconds.Add(frame.CaptureMilliseconds);
                    bool startupSample = wall.Elapsed.TotalSeconds < 2.0;
                    (startupSample ? startupCaptureMilliseconds : steadyCaptureMilliseconds).Add(frame.CaptureMilliseconds);
                    bytesCopied += frame.BytesCopied;
                    if (frame.BytesCopied == 0)
                    {
                        failed++;
                    }
                    else
                    {
                        pts.Add(frame.PresentationTimestampTicks);
                        (startupSample ? startupPts : steadyPts).Add(frame.PresentationTimestampTicks);
                    }

                    Stopwatch display = Stopwatch.StartNew();
                    if (frame.MotionResult != null)
                    {
                        analysisMilliseconds.Add(frame.MotionResult.Timings.TotalMilliseconds);
                        (startupSample ? startupAnalysisMilliseconds : steadyAnalysisMilliseconds).Add(frame.MotionResult.Timings.TotalMilliseconds);
                        DrawCandidates(rectangles, frame.MotionResult.Candidates, host, player);
                        displayed++;
                        boxesDisplayed += frame.MotionResult.Candidates.Count;
                        if (previewFrame == null && frame.MotionResult.Candidates.Count > 0)
                        {
                            previewFrame = frame;
                        }
                    }
                    display.Stop();
                    displayMilliseconds.Add(display.Elapsed.TotalMilliseconds);
                    (startupSample ? startupDisplayMilliseconds : steadyDisplayMilliseconds).Add(display.Elapsed.TotalMilliseconds);
                    endToEnd.Stop();
                    endToEndMilliseconds.Add(endToEnd.Elapsed.TotalMilliseconds);
                    (startupSample ? startupEndToEndMilliseconds : steadyEndToEndMilliseconds).Add(endToEnd.Elapsed.TotalMilliseconds);

                    nextDue += (long)(Stopwatch.Frequency / requestedRate);
                    TimeSpan delay = TimeSpan.FromSeconds((nextDue - wall.ElapsedTicks) / (double)Stopwatch.Frequency);
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay);
                    }
                    if (wall.Elapsed.TotalSeconds >= nextMemorySampleSeconds)
                    {
                        process.Refresh();
                        memorySamples.Add(new MemorySample(
                            wall.Elapsed.TotalSeconds,
                            process.WorkingSet64 / 1048576.0,
                            process.PrivateMemorySize64 / 1048576.0,
                            process.HandleCount));
                        nextMemorySampleSeconds += 60;
                    }
                }

                wall.Stop();
                player.Pause();
                process.Refresh();
                memorySamples.Add(new MemorySample(
                    wall.Elapsed.TotalSeconds,
                    process.WorkingSet64 / 1048576.0,
                    process.PrivateMemorySize64 / 1048576.0,
                    process.HandleCount));
                int newPts = 0;
                for (int index = 1; index < pts.Count; index++)
                {
                    if (pts[index] > pts[index - 1])
                    {
                        newPts++;
                    }
                }

                double newFrameFps = (newPts + (pts.Count > 0 ? 1 : 0)) / Math.Max(0.001, wall.Elapsed.TotalSeconds);
                double displayUpdateFps = analysisEnabled ? displayed / Math.Max(0.001, wall.Elapsed.TotalSeconds) : 0.0;
                double cpuCorePercent = 100.0 * (process.TotalProcessorTime - cpuBefore).TotalSeconds / Math.Max(0.001, wall.Elapsed.TotalSeconds);
                string? previewPath = outputPath == null || previewFrame == null
                    ? null
                    : Path.Combine(Path.GetDirectoryName(outputPath)!, Path.GetFileNameWithoutExtension(outputPath) + "_preview.png");
                var result = new
                {
                    source = video,
                    mode,
                    requested_width = width,
                    requested_rate_fps = requestedRate,
                    source_width = player.Video.Width,
                    source_height = player.Video.Height,
                    source_fps = player.Video.FPS,
                    source_duration_seconds = TimeSpan.FromTicks(player.Duration).TotalSeconds,
                    duration_seconds = wall.Elapsed.TotalSeconds,
                    attempts = captureMilliseconds.Count,
                    successful_snapshots = pts.Count,
                    failed_snapshots = failed,
                    distinct_pts_updates = newPts + (pts.Count > 0 ? 1 : 0),
                    new_frame_fps = newFrameFps,
                    displayed_results = displayed,
                    display_update_fps = displayUpdateFps,
                    boxes_displayed = boxesDisplayed,
                    capture_ms = Summarize(captureMilliseconds),
                    analysis_ms = Summarize(analysisMilliseconds),
                    display_ms = Summarize(displayMilliseconds),
                    end_to_end_ms = Summarize(endToEndMilliseconds),
                    startup_first_2_seconds = new
                    {
                        new_frame_fps = CountIncreasing(startupPts) / Math.Max(0.001, Math.Min(2.0, wall.Elapsed.TotalSeconds)),
                        capture_ms = Summarize(startupCaptureMilliseconds),
                        analysis_ms = Summarize(startupAnalysisMilliseconds),
                        display_ms = Summarize(startupDisplayMilliseconds),
                        end_to_end_ms = Summarize(startupEndToEndMilliseconds)
                    },
                    steady_state = new
                    {
                        new_frame_fps = CountIncreasing(steadyPts) / Math.Max(0.001, wall.Elapsed.TotalSeconds - 2.0),
                        capture_ms = Summarize(steadyCaptureMilliseconds),
                        analysis_ms = Summarize(steadyAnalysisMilliseconds),
                        display_ms = Summarize(steadyDisplayMilliseconds),
                        end_to_end_ms = Summarize(steadyEndToEndMilliseconds)
                    },
                    process_cpu_percent_one_core_equals_100 = cpuCorePercent,
                    process_cpu_percent_total_capacity = cpuCorePercent / Environment.ProcessorCount,
                    process_working_set_mb = process.WorkingSet64 / 1048576.0,
                    process_private_mb = process.PrivateMemorySize64 / 1048576.0,
                    process_working_set_start_mb = workingSetBefore / 1048576.0,
                    process_private_start_mb = privateBytesBefore / 1048576.0,
                    process_working_set_delta_mb = (process.WorkingSet64 - workingSetBefore) / 1048576.0,
                    process_private_delta_mb = (process.PrivateMemorySize64 - privateBytesBefore) / 1048576.0,
                    handles_start = handlesBefore,
                    handles_end = process.HandleCount,
                    handles_delta = process.HandleCount - handlesBefore,
                    memory_samples = memorySamples,
                    gc_collections = new
                    {
                        gen0 = GC.CollectionCount(0) - collectionsBefore[0],
                        gen1 = GC.CollectionCount(1) - collectionsBefore[1],
                        gen2 = GC.CollectionCount(2) - collectionsBefore[2]
                    },
                    managed_allocated_mb = (GC.GetTotalAllocatedBytes(false) - gcBefore) / 1048576.0,
                    copied_megabytes = bytesCopied / 1048576.0,
                    worker_queue_max = 1,
                    dropped_timer_ticks = Math.Max(0, (int)Math.Floor(wall.Elapsed.TotalSeconds * requestedRate) - captureMilliseconds.Count),
                    preview_path = previewPath,
                    definitions = new
                    {
                        new_frame_fps = "successful snapshots with a new/increasing Flyleaf CurTime divided by wall time; the first successful PTS is counted",
                        display_update_fps = "motion results applied to the reusable WPF overlay rectangles divided by wall time",
                        cpu = "process TotalProcessorTime/wall time; total_capacity additionally divides by logical processor count"
                    }
                };
                if (previewPath != null && previewFrame != null)
                {
                    SavePreview(previewFrame, previewPath);
                }
                string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
                if (outputPath != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                    File.WriteAllText(outputPath, json);
                }
                exitCode = newFrameFps >= 10.0 && (!analysisEnabled || displayUpdateFps >= 10.0) ? 0 : 4;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                exitCode = 5;
            }
            finally
            {
                window.Close();
            }
        };

        application.Run(window);
        host.Dispose();
        return exitCode;
    }

    private static FrameWork CaptureAndAnalyze(
        Player player,
        MotionBboxEngine engine,
        string sourceId,
        uint width,
        MotionBboxSettings settings,
        bool analysisEnabled,
        bool capturePreview)
    {
        Stopwatch capture = Stopwatch.StartNew();
        BitmapSource? snapshot = player.TakeSnapshotToBitmapSource(width, 0);
        long pts = player.CurTime;
        if (snapshot == null || snapshot.PixelWidth <= 0 || snapshot.PixelHeight <= 0)
        {
            capture.Stop();
            return new FrameWork(pts, 0, capture.Elapsed.TotalMilliseconds, null, null, 0, 0, 0);
        }

        BitmapSource bgra = snapshot.Format == PixelFormats.Bgra32
            ? snapshot
            : new FormatConvertedBitmap(snapshot, PixelFormats.Bgra32, null, 0);
        int stride = bgra.PixelWidth * 4;
        int bufferLength = stride * bgra.PixelHeight;
        byte[] pixels = ArrayPool<byte>.Shared.Rent(bufferLength);
        try
        {
            bgra.CopyPixels(pixels, stride, 0);
            capture.Stop();
            MotionBboxResult? result = analysisEnabled
                ? engine.ProcessBgra(pixels, bgra.PixelWidth, bgra.PixelHeight, stride, pts, sourceId, 1, settings)
                : null;
            byte[]? previewPixels = capturePreview && result?.Candidates.Count > 0
                ? pixels.AsSpan(0, bufferLength).ToArray()
                : null;
            return new FrameWork(pts, bufferLength, capture.Elapsed.TotalMilliseconds, result, previewPixels, bgra.PixelWidth, bgra.PixelHeight, stride);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    private static void DrawCandidates(Rectangle[] rectangles, IReadOnlyList<MotionCandidate> candidates, FlyleafHost host, Player player)
    {
        double viewportWidth = host.ActualWidth;
        double viewportHeight = host.ActualHeight;
        double sourceWidth = player.Video.Width;
        double sourceHeight = player.Video.Height;
        double videoWidth = viewportWidth;
        double videoHeight = viewportHeight;
        if (sourceWidth > 0 && sourceHeight > 0 && viewportWidth > 0 && viewportHeight > 0)
        {
            if (viewportWidth / viewportHeight <= sourceWidth / sourceHeight)
            {
                videoHeight = viewportWidth * sourceHeight / sourceWidth;
            }
            else
            {
                videoWidth = sourceWidth * viewportHeight / sourceHeight;
            }
        }
        double offsetX = (viewportWidth - videoWidth) / 2.0;
        double offsetY = (viewportHeight - videoHeight) / 2.0;
        for (int index = 0; index < rectangles.Length; index++)
        {
            Rectangle rectangle = rectangles[index];
            if (index >= candidates.Count)
            {
                rectangle.Visibility = Visibility.Collapsed;
                continue;
            }
            MotionCandidate candidate = candidates[index];
            rectangle.Visibility = Visibility.Visible;
            rectangle.Width = Math.Max(1, candidate.Width * videoWidth);
            rectangle.Height = Math.Max(1, candidate.Height * videoHeight);
            rectangle.Opacity = candidate.IsHeld ? 0.55 : 1.0;
            Canvas.SetLeft(rectangle, offsetX + candidate.X * videoWidth);
            Canvas.SetTop(rectangle, offsetY + candidate.Y * videoHeight);
        }
    }

    private static object Summarize(List<double> values)
    {
        if (values.Count == 0)
        {
            return new { count = 0, p50 = 0.0, p95 = 0.0, max = 0.0 };
        }
        values.Sort();
        double Percentile(double percentile)
        {
            int index = (int)Math.Ceiling(percentile * values.Count) - 1;
            return values[Math.Clamp(index, 0, values.Count - 1)];
        }
        return new { count = values.Count, p50 = Percentile(0.50), p95 = Percentile(0.95), max = values[^1] };
    }

    private static int CountIncreasing(IReadOnlyList<long> timestamps)
    {
        if (timestamps.Count == 0)
        {
            return 0;
        }
        int count = 1;
        for (int index = 1; index < timestamps.Count; index++)
        {
            if (timestamps[index] > timestamps[index - 1])
            {
                count++;
            }
        }
        return count;
    }

    private static unsafe void SavePreview(FrameWork frame, string path)
    {
        if (frame.Pixels == null || frame.MotionResult == null)
        {
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        fixed (byte* pointer = frame.Pixels)
        {
            using CvMat source = CvMat.FromPixelData(frame.PixelHeight, frame.PixelWidth, CvMatType.CV_8UC4, (IntPtr)pointer, frame.Stride);
            using CvMat annotated = source.Clone();
            foreach (MotionCandidate candidate in frame.MotionResult.Candidates)
            {
                int x = Math.Clamp((int)Math.Round(candidate.X * frame.PixelWidth), 0, frame.PixelWidth - 1);
                int y = Math.Clamp((int)Math.Round(candidate.Y * frame.PixelHeight), 0, frame.PixelHeight - 1);
                int width = Math.Clamp((int)Math.Round(candidate.Width * frame.PixelWidth), 1, frame.PixelWidth - x);
                int height = Math.Clamp((int)Math.Round(candidate.Height * frame.PixelHeight), 1, frame.PixelHeight - y);
                Cv2.Rectangle(annotated, new CvRect(x, y, width, height), new CvScalar(0, 165, 255, 255), 3);
            }
            Cv2.ImWrite(path, annotated);
        }
    }
}
