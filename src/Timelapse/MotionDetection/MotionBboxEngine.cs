#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenCvSharp;

namespace Timelapse.MotionDetection
{
    public sealed class MotionBboxEngine : IDisposable
    {
        private sealed record HistoryFrame(long PresentationTimestampTicks, Mat Grayscale);
        private sealed record PixelCandidate(Rect Box, double Score);

        private readonly Queue<HistoryFrame> history = new();
        private readonly BottomBandEstimator bottomBandEstimator = new();
        private readonly MotionCandidateTracker tracker = new();
        private BackgroundSubtractorMOG2? mog2;
        private string sourceId = string.Empty;
        private long generationId = -1;
        private long lastPresentationTimestampTicks = -1;
        private MotionBboxSettings? activeSettings;
        private int mogFrames;
        private int activeBottomBandRows;
        private bool disposed;

        public void Reset()
        {
            while (this.history.TryDequeue(out HistoryFrame? frame))
            {
                frame.Grayscale.Dispose();
            }
            this.bottomBandEstimator.Reset();
            this.tracker.Reset();
            this.mog2?.Dispose();
            this.mog2 = null;
            this.mogFrames = 0;
            this.lastPresentationTimestampTicks = -1;
            this.activeBottomBandRows = 0;
        }

        public unsafe MotionBboxResult ProcessBgra(
            byte[] pixels,
            int originalWidth,
            int originalHeight,
            int stride,
            long presentationTimestampTicks,
            string frameSourceId,
            long frameGenerationId,
            MotionBboxSettings requestedSettings)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);
            MotionBboxSettings settings = requestedSettings.Validated();
            if (pixels.Length < stride * originalHeight || originalWidth <= 0 || originalHeight <= 0 || stride < originalWidth * 4)
            {
                throw new ArgumentException("The BGRA frame buffer dimensions are invalid.", nameof(pixels));
            }

            bool timelineReset = !string.Equals(this.sourceId, frameSourceId, StringComparison.Ordinal) ||
                                 this.generationId != frameGenerationId ||
                                 this.activeSettings != settings ||
                                 presentationTimestampTicks <= this.lastPresentationTimestampTicks ||
                                 (this.lastPresentationTimestampTicks >= 0 && presentationTimestampTicks - this.lastPresentationTimestampTicks > TimeSpan.FromSeconds(1.5).Ticks);
            if (timelineReset)
            {
                this.Reset();
                this.sourceId = frameSourceId;
                this.generationId = frameGenerationId;
                this.activeSettings = settings;
            }
            this.lastPresentationTimestampTicks = presentationTimestampTicks;

            Stopwatch total = Stopwatch.StartNew();
            Stopwatch stage = Stopwatch.StartNew();
            int analysisWidth = Math.Min(settings.AnalysisWidth, originalWidth);
            int analysisHeight = Math.Max(1, (int)Math.Round(originalHeight * analysisWidth / (double)originalWidth));
            using Mat smallBgra = new();
            using Mat grayscale = new();
            fixed (byte* pointer = pixels)
            {
                using Mat source = Mat.FromPixelData(originalHeight, originalWidth, MatType.CV_8UC4, (IntPtr)pointer, stride);
                Cv2.Resize(source, smallBgra, new Size(analysisWidth, analysisHeight), 0, 0, InterpolationFlags.Area);
            }
            Cv2.CvtColor(smallBgra, grayscale, ColorConversionCodes.BGRA2GRAY);
            Cv2.GaussianBlur(grayscale, grayscale, new Size(5, 5), 0);

            int maximumAutoScanRows = Math.Max(8, (int)Math.Round(Math.Min(80, originalHeight * 0.18) * analysisHeight / originalHeight));
            bool bandChanged = settings.BottomBandMode == BottomBandMode.Automatic && this.bottomBandEstimator.Observe(grayscale, maximumAutoScanRows);
            int bottomBandRows = settings.BottomBandMode switch
            {
                BottomBandMode.None => 0,
                BottomBandMode.Manual => Math.Clamp((int)Math.Round(settings.ManualBottomBandPixels * analysisHeight / (double)originalHeight), 0, analysisHeight - 1),
                _ => this.bottomBandEstimator.BandPixels
            };
            if (bandChanged && bottomBandRows != this.activeBottomBandRows)
            {
                this.ClearTemporalState();
            }
            this.activeBottomBandRows = bottomBandRows;
            int usableHeight = Math.Max(1, analysisHeight - bottomBandRows);
            using Mat usableGrayView = new(grayscale, new Rect(0, 0, analysisWidth, usableHeight));
            using Mat usableGray = usableGrayView.Clone();
            double preprocessMilliseconds = stage.Elapsed.TotalMilliseconds;

            stage.Restart();
            using Mat motionMask = new();
            bool initializing;
            bool globalChangeSuppressed;
            this.CreateMotionMask(usableGray, presentationTimestampTicks, settings, motionMask, out initializing, out globalChangeSuppressed);
            double motionMilliseconds = stage.Elapsed.TotalMilliseconds;

            stage.Restart();
            IReadOnlyList<PixelCandidate> components = initializing || globalChangeSuppressed
                ? []
                : this.ExtractAndMergeComponents(motionMask, settings);
            double componentsMilliseconds = stage.Elapsed.TotalMilliseconds;

            stage.Restart();
            double usableHeightRatio = usableHeight / (double)analysisHeight;
            IReadOnlyList<(Rect2d Box, double Score)> normalized = components.Select(component =>
            {
                Rect rectangle = component.Box;
                return (
                    new Rect2d(
                        rectangle.X / (double)analysisWidth,
                        rectangle.Y / (double)analysisHeight,
                        rectangle.Width / (double)analysisWidth,
                        rectangle.Height / (double)analysisHeight),
                    component.Score);
            }).ToArray();
            IReadOnlyList<MotionCandidate> candidates = this.tracker.Update(normalized, presentationTimestampTicks, settings, usableHeightRatio);
            double trackingMilliseconds = stage.Elapsed.TotalMilliseconds;
            total.Stop();

            int originalBottomBandPixels = (int)Math.Round(bottomBandRows * originalHeight / (double)analysisHeight);
            return new MotionBboxResult(
                frameSourceId,
                frameGenerationId,
                presentationTimestampTicks,
                originalWidth,
                originalHeight,
                analysisWidth,
                analysisHeight,
                originalBottomBandPixels,
                initializing || (settings.BottomBandMode == BottomBandMode.Automatic && !this.bottomBandEstimator.IsReady),
                globalChangeSuppressed,
                candidates,
                new MotionBboxTimings(preprocessMilliseconds, motionMilliseconds, componentsMilliseconds, trackingMilliseconds, total.Elapsed.TotalMilliseconds));
        }

        private void CreateMotionMask(
            Mat current,
            long presentationTimestampTicks,
            MotionBboxSettings settings,
            Mat destination,
            out bool initializing,
            out bool globalChangeSuppressed)
        {
            using Mat frameDifferenceMask = new();
            using Mat mogMask = new();
            bool needsFrameDifference = settings.Algorithm is MotionBboxAlgorithm.FrameDifference or MotionBboxAlgorithm.Hybrid;
            bool needsMog = settings.Algorithm is MotionBboxAlgorithm.Mog2 or MotionBboxAlgorithm.Hybrid;
            bool frameDifferenceReady = false;
            bool mogReady = false;

            if (needsFrameDifference)
            {
                HistoryFrame? reference = this.SelectReference(presentationTimestampTicks);
                if (reference != null && reference.Grayscale.Rows == current.Rows && reference.Grayscale.Cols == current.Cols)
                {
                    using Mat difference = new();
                    Cv2.Absdiff(current, reference.Grayscale, difference);
                    double meanDifference = Cv2.Mean(difference).Val0;
                    double threshold = settings.DifferenceThreshold + Math.Max(0.0, meanDifference - 6.0) * 0.45;
                    Cv2.Threshold(difference, frameDifferenceMask, threshold, 255, ThresholdTypes.Binary);
                    frameDifferenceReady = true;
                }
            }

            if (needsMog)
            {
                this.mog2 ??= BackgroundSubtractorMOG2.Create(45, 20, false);
                this.mog2.Apply(current, mogMask, 0.035);
                this.mogFrames++;
                mogReady = this.mogFrames >= 8;
            }

            if (settings.Algorithm == MotionBboxAlgorithm.Hybrid && frameDifferenceReady && mogReady)
            {
                Cv2.BitwiseAnd(frameDifferenceMask, mogMask, destination);
            }
            else if (settings.Algorithm == MotionBboxAlgorithm.Mog2 && mogReady)
            {
                mogMask.CopyTo(destination);
            }
            else if (frameDifferenceReady)
            {
                frameDifferenceMask.CopyTo(destination);
            }

            this.RememberFrame(current, presentationTimestampTicks);
            initializing = destination.Empty();
            globalChangeSuppressed = false;
            if (initializing)
            {
                return;
            }

            using Mat openKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
            using Mat closeKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(7, 7));
            Cv2.MorphologyEx(destination, destination, MorphTypes.Open, openKernel);
            Cv2.MorphologyEx(destination, destination, MorphTypes.Close, closeKernel);
            double changedRatio = Cv2.CountNonZero(destination) / (double)Math.Max(1, destination.Rows * destination.Cols);
            if (changedRatio > 0.58)
            {
                destination.SetTo(Scalar.Black);
                globalChangeSuppressed = true;
            }
        }

        private IReadOnlyList<PixelCandidate> ExtractAndMergeComponents(Mat mask, MotionBboxSettings settings)
        {
            int totalPixels = Math.Max(1, mask.Rows * mask.Cols);
            int minimumArea = Math.Max(18, (int)Math.Round(totalPixels * settings.MinimumAreaFraction));
            int maximumArea = Math.Max(minimumArea, (int)Math.Round(totalPixels * settings.MaximumAreaFraction));
            using Mat labels = new();
            using Mat stats = new();
            using Mat centroids = new();
            Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids, PixelConnectivity.Connectivity8, MatType.CV_32S);
            int statsRows = stats.Rows;
            {
                List<PixelCandidate> components = [];
                for (int label = 1; label < statsRows; label++)
                {
                    int area = stats.At<int>(label, (int)ConnectedComponentsTypes.Area);
                    if (area < minimumArea || area > maximumArea)
                    {
                        continue;
                    }
                    Rect box = new(
                        stats.At<int>(label, (int)ConnectedComponentsTypes.Left),
                        stats.At<int>(label, (int)ConnectedComponentsTypes.Top),
                        stats.At<int>(label, (int)ConnectedComponentsTypes.Width),
                        stats.At<int>(label, (int)ConnectedComponentsTypes.Height));
                    double boundingAreaFraction = box.Width * box.Height / (double)totalPixels;
                    if (boundingAreaFraction > settings.MaximumMergedAreaFraction)
                    {
                        // A thin connected outline or sweeping illumination edge can contain
                        // few changed pixels while its bounding rectangle covers the scene.
                        // Reject it before tracking rather than showing a near-full-frame box.
                        continue;
                    }
                    components.Add(new PixelCandidate(box, area / (double)totalPixels));
                }
                return MergeNearby(components, mask.Cols, mask.Rows, settings, totalPixels);
            }
        }

        private static IReadOnlyList<PixelCandidate> MergeNearby(
            IReadOnlyList<PixelCandidate> components,
            int width,
            int height,
            MotionBboxSettings settings,
            int totalPixels)
        {
            double gapX = width * settings.MergeGapFraction;
            double gapY = height * settings.MergeGapFraction;
            double diagonal = Math.Sqrt(width * width + height * height);
            List<PixelCandidate> clusters = [];
            foreach (PixelCandidate component in components.OrderByDescending(item => item.Box.Width * item.Box.Height).Take(16))
            {
                int bestIndex = -1;
                double bestDistance = double.MaxValue;
                for (int index = 0; index < clusters.Count; index++)
                {
                    Rect existing = clusters[index].Box;
                    Rect union = Union(existing, component.Box);
                    double mergedAreaFraction = union.Width * union.Height / (double)totalPixels;
                    double centerDistance = CenterDistance(existing, component.Box) / Math.Max(1.0, diagonal);
                    if (!PaddedIntersects(existing, component.Box, gapX, gapY) ||
                        mergedAreaFraction > settings.MaximumMergedAreaFraction ||
                        centerDistance > 0.24)
                    {
                        continue;
                    }
                    if (centerDistance < bestDistance)
                    {
                        bestDistance = centerDistance;
                        bestIndex = index;
                    }
                }

                if (bestIndex < 0)
                {
                    clusters.Add(component);
                }
                else
                {
                    PixelCandidate existing = clusters[bestIndex];
                    clusters[bestIndex] = new PixelCandidate(Union(existing.Box, component.Box), existing.Score + component.Score);
                }
            }
            return clusters.OrderByDescending(item => item.Score).Take(settings.MaximumCandidates * 2).ToArray();
        }

        private HistoryFrame? SelectReference(long currentTicks)
        {
            HistoryFrame? selected = null;
            foreach (HistoryFrame frame in this.history)
            {
                long age = currentTicks - frame.PresentationTimestampTicks;
                if (age >= TimeSpan.FromMilliseconds(160).Ticks && age <= TimeSpan.FromMilliseconds(550).Ticks)
                {
                    selected = frame;
                    break;
                }
            }
            return selected ?? this.history.FirstOrDefault();
        }

        private void RememberFrame(Mat current, long presentationTimestampTicks)
        {
            this.history.Enqueue(new HistoryFrame(presentationTimestampTicks, current.Clone()));
            while (this.history.Count > 7 ||
                   this.history.TryPeek(out HistoryFrame? oldest) && presentationTimestampTicks - oldest.PresentationTimestampTicks > TimeSpan.FromMilliseconds(650).Ticks)
            {
                this.history.Dequeue().Grayscale.Dispose();
            }
        }

        private void ClearTemporalState()
        {
            while (this.history.TryDequeue(out HistoryFrame? frame))
            {
                frame.Grayscale.Dispose();
            }
            this.tracker.Reset();
            this.mog2?.Dispose();
            this.mog2 = null;
            this.mogFrames = 0;
        }

        private static bool PaddedIntersects(Rect left, Rect right, double gapX, double gapY)
        {
            return !(left.Right + gapX < right.Left || right.Right + gapX < left.Left ||
                     left.Bottom + gapY < right.Top || right.Bottom + gapY < left.Top);
        }

        private static Rect Union(Rect left, Rect right)
        {
            int x1 = Math.Min(left.Left, right.Left);
            int y1 = Math.Min(left.Top, right.Top);
            int x2 = Math.Max(left.Right, right.Right);
            int y2 = Math.Max(left.Bottom, right.Bottom);
            return new Rect(x1, y1, x2 - x1, y2 - y1);
        }

        private static double CenterDistance(Rect left, Rect right)
        {
            double dx = left.X + left.Width / 2.0 - right.X - right.Width / 2.0;
            double dy = left.Y + left.Height / 2.0 - right.Y - right.Height / 2.0;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }
            this.Reset();
            this.disposed = true;
        }
    }
}
