using System;
using System.Collections.Generic;

namespace Timelapse.MotionDetection
{
    public enum MotionBboxAlgorithm
    {
        FrameDifference,
        Mog2,
        Hybrid
    }

    public enum BottomBandMode
    {
        Automatic,
        Manual,
        None
    }

    public sealed record MotionBboxSettings
    {
        public int AnalysisWidth { get; init; } = 640;
        public MotionBboxAlgorithm Algorithm { get; init; } = MotionBboxAlgorithm.FrameDifference;
        public int DifferenceThreshold { get; init; } = 18;
        public double MinimumAreaFraction { get; init; } = 0.00008;
        public double MaximumAreaFraction { get; init; } = 0.55;
        public double MergeGapFraction { get; init; } = 0.04;
        public double MaximumMergedAreaFraction { get; init; } = 0.35;
        public double PaddingFactor { get; init; } = 1.35;
        public int HoldMilliseconds { get; init; } = 350;
        public int MaximumCandidates { get; init; } = 4;
        public BottomBandMode BottomBandMode { get; init; } = BottomBandMode.Automatic;
        public int ManualBottomBandPixels { get; init; }

        public MotionBboxSettings Validated()
        {
            return this with
            {
                AnalysisWidth = Math.Clamp(AnalysisWidth, 320, 960),
                DifferenceThreshold = Math.Clamp(DifferenceThreshold, 5, 64),
                MinimumAreaFraction = Math.Clamp(MinimumAreaFraction, 0.00002, 0.02),
                MaximumAreaFraction = Math.Clamp(MaximumAreaFraction, 0.10, 0.90),
                MergeGapFraction = Math.Clamp(MergeGapFraction, 0.0, 0.12),
                MaximumMergedAreaFraction = Math.Clamp(MaximumMergedAreaFraction, 0.10, 0.80),
                PaddingFactor = Math.Clamp(PaddingFactor, 1.0, 2.0),
                HoldMilliseconds = Math.Clamp(HoldMilliseconds, 0, 1500),
                MaximumCandidates = Math.Clamp(MaximumCandidates, 1, 8),
                ManualBottomBandPixels = Math.Max(0, ManualBottomBandPixels)
            };
        }
    }

    public sealed record MotionCandidate(
        int TrackId,
        double X,
        double Y,
        double Width,
        double Height,
        double MotionScore,
        bool IsHeld);

    public sealed record MotionBboxTimings(
        double PreprocessMilliseconds,
        double MotionMilliseconds,
        double ComponentsMilliseconds,
        double TrackingMilliseconds,
        double TotalMilliseconds);

    public sealed record MotionBboxResult(
        string SourceId,
        long GenerationId,
        long PresentationTimestampTicks,
        int OriginalWidth,
        int OriginalHeight,
        int AnalysisWidth,
        int AnalysisHeight,
        int BottomBandPixels,
        bool IsInitializing,
        bool GlobalChangeSuppressed,
        IReadOnlyList<MotionCandidate> Candidates,
        MotionBboxTimings Timings);
}
