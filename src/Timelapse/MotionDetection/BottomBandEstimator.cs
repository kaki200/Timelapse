using System;
using OpenCvSharp;

namespace Timelapse.MotionDetection
{
    internal sealed class BottomBandEstimator
    {
        private const int RequiredSamples = 8;
        private long[] darkCounts = [];
        private long[] brightCounts = [];
        private long[] intensitySums = [];
        private int scanRows;
        private int width;
        private int samples;

        public int BandPixels { get; private set; }
        public bool IsReady => this.samples >= RequiredSamples;

        public void Reset()
        {
            this.darkCounts = [];
            this.brightCounts = [];
            this.intensitySums = [];
            this.scanRows = 0;
            this.width = 0;
            this.samples = 0;
            this.BandPixels = 0;
        }

        public unsafe bool Observe(Mat grayscale, int maximumScanRows)
        {
            if (grayscale.Empty() || grayscale.Type() != MatType.CV_8UC1 || this.samples >= RequiredSamples)
            {
                return false;
            }

            int currentScanRows = Math.Clamp(maximumScanRows, 1, Math.Max(1, grayscale.Rows - 1));
            if (this.samples == 0 || this.width != grayscale.Cols || this.scanRows != currentScanRows)
            {
                this.Reset();
                this.width = grayscale.Cols;
                this.scanRows = currentScanRows;
                this.darkCounts = new long[currentScanRows];
                this.brightCounts = new long[currentScanRows];
                this.intensitySums = new long[currentScanRows + 1];
            }

            byte* data = (byte*)grayscale.DataPointer;
            long step = grayscale.Step();
            int columns = grayscale.Cols;
            for (int offset = 1; offset <= this.scanRows + 1; offset++)
            {
                int y = grayscale.Rows - offset;
                if (y < 0)
                {
                    break;
                }
                byte* row = data + y * step;
                long sum = 0;
                long dark = 0;
                long bright = 0;
                for (int x = 0; x < columns; x++)
                {
                    byte value = row[x];
                    sum += value;
                    dark += value < 25 ? 1 : 0;
                    bright += value > 180 ? 1 : 0;
                }
                this.intensitySums[offset - 1] += sum;
                if (offset <= this.scanRows)
                {
                    this.darkCounts[offset - 1] += dark;
                    this.brightCounts[offset - 1] += bright;
                }
            }

            this.samples++;
            if (!this.IsReady)
            {
                return false;
            }

            int previous = this.BandPixels;
            int band = 0;
            long rowSamples = (long)this.width * this.samples;
            for (int index = 0; index < this.scanRows; index++)
            {
                double darkRatio = this.darkCounts[index] / (double)Math.Max(1, rowSamples);
                if (darkRatio >= 0.78)
                {
                    band = index + 1;
                }
                else if (band >= 8)
                {
                    break;
                }
                else
                {
                    band = 0;
                }
            }

            if (band >= 8)
            {
                long bright = 0;
                for (int index = 0; index < band; index++)
                {
                    bright += this.brightCounts[index];
                }
                double brightRatio = bright / (double)Math.Max(1, rowSamples * band);
                int aboveIndex = Math.Min(band, this.intensitySums.Length - 1);
                double topMean = this.intensitySums[Math.Max(0, band - 1)] / (double)Math.Max(1, rowSamples);
                double aboveMean = this.intensitySums[aboveIndex] / (double)Math.Max(1, rowSamples);
                bool clearTopEdge = aboveMean - topMean >= 8.0;
                if (brightRatio < 0.0005 && !clearTopEdge)
                {
                    // Avoid treating uniformly dark night-time ground as an information strip.
                    band = 0;
                }
            }

            this.BandPixels = band;
            return previous != this.BandPixels;
        }
    }
}
