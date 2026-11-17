using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace Timelapse.MotionDetection
{
    internal sealed class MotionCandidateTracker
    {
        private sealed class Track
        {
            public int Id { get; init; }
            public Rect2d Box { get; set; }
            public double Score { get; set; }
            public long LastSeenTicks { get; set; }
        }

        private readonly List<Track> tracks = [];
        private int nextTrackId = 1;

        public void Reset()
        {
            this.tracks.Clear();
            this.nextTrackId = 1;
        }

        public IReadOnlyList<MotionCandidate> Update(
            IReadOnlyList<(Rect2d Box, double Score)> detections,
            long presentationTimestampTicks,
            MotionBboxSettings settings,
            double usableHeightRatio)
        {
            // Only tracks that existed before this frame may be matched. New tracks are
            // appended below; including them in this loop would index past matched[] when a
            // frame contains more than one disconnected motion region.
            int existingTrackCount = this.tracks.Count;
            bool[] matched = new bool[existingTrackCount];
            List<Track> current = [];
            foreach ((Rect2d box, double score) in detections.OrderByDescending(item => item.Score))
            {
                int bestIndex = -1;
                double bestCost = double.MaxValue;
                for (int index = 0; index < existingTrackCount; index++)
                {
                    if (matched[index])
                    {
                        continue;
                    }

                    Rect2d previous = this.tracks[index].Box;
                    double overlap = IntersectionOverUnion(previous, box);
                    double centerDistance = NormalizedCenterDistance(previous, box);
                    double previousArea = Math.Max(1e-9, previous.Width * previous.Height);
                    double areaRatio = box.Width * box.Height / previousArea;
                    if ((overlap < 0.02 && centerDistance > 0.16) || areaRatio < 0.18 || areaRatio > 5.5)
                    {
                        continue;
                    }

                    double cost = centerDistance - overlap * 0.35;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestIndex = index;
                    }
                }

                Track track;
                if (bestIndex >= 0)
                {
                    matched[bestIndex] = true;
                    track = this.tracks[bestIndex];
                    const double currentWeight = 0.58;
                    track.Box = Blend(track.Box, box, currentWeight);
                    track.Score = track.Score * (1.0 - currentWeight) + score * currentWeight;
                    track.LastSeenTicks = presentationTimestampTicks;
                }
                else
                {
                    track = new Track
                    {
                        Id = this.nextTrackId++,
                        Box = box,
                        Score = score,
                        LastSeenTicks = presentationTimestampTicks
                    };
                    this.tracks.Add(track);
                }
                current.Add(track);
            }

            long holdTicks = TimeSpan.FromMilliseconds(settings.HoldMilliseconds).Ticks;
            foreach (Track track in this.tracks.ToArray())
            {
                long age = presentationTimestampTicks - track.LastSeenTicks;
                if (age < 0 || age > holdTicks)
                {
                    this.tracks.Remove(track);
                    continue;
                }
                if (!current.Contains(track))
                {
                    current.Add(track);
                }
            }

            return current
                .OrderBy(track => track.LastSeenTicks == presentationTimestampTicks ? 0 : 1)
                .ThenByDescending(track => track.Score)
                .Take(settings.MaximumCandidates)
                .Select(track => ToCandidate(track, presentationTimestampTicks, settings.PaddingFactor, usableHeightRatio))
                .ToArray();
        }

        private static MotionCandidate ToCandidate(Track track, long pts, double padding, double usableHeightRatio)
        {
            double centerX = track.Box.X + track.Box.Width / 2.0;
            double centerY = track.Box.Y + track.Box.Height / 2.0;
            double width = Math.Min(1.0, track.Box.Width * padding);
            double height = Math.Min(usableHeightRatio, track.Box.Height * padding);
            double x = Math.Clamp(centerX - width / 2.0, 0.0, Math.Max(0.0, 1.0 - width));
            double y = Math.Clamp(centerY - height / 2.0, 0.0, Math.Max(0.0, usableHeightRatio - height));
            return new MotionCandidate(track.Id, x, y, width, height, track.Score, track.LastSeenTicks != pts);
        }

        private static Rect2d Blend(Rect2d previous, Rect2d current, double currentWeight)
        {
            double previousWeight = 1.0 - currentWeight;
            return new Rect2d(
                previous.X * previousWeight + current.X * currentWeight,
                previous.Y * previousWeight + current.Y * currentWeight,
                previous.Width * previousWeight + current.Width * currentWeight,
                previous.Height * previousWeight + current.Height * currentWeight);
        }

        private static double IntersectionOverUnion(Rect2d left, Rect2d right)
        {
            double x1 = Math.Max(left.Left, right.Left);
            double y1 = Math.Max(left.Top, right.Top);
            double x2 = Math.Min(left.Right, right.Right);
            double y2 = Math.Min(left.Bottom, right.Bottom);
            double intersection = Math.Max(0.0, x2 - x1) * Math.Max(0.0, y2 - y1);
            double union = left.Width * left.Height + right.Width * right.Height - intersection;
            return intersection / Math.Max(1e-9, union);
        }

        private static double NormalizedCenterDistance(Rect2d left, Rect2d right)
        {
            double dx = left.X + left.Width / 2.0 - right.X - right.Width / 2.0;
            double dy = left.Y + left.Height / 2.0 - right.Y - right.Height / 2.0;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
