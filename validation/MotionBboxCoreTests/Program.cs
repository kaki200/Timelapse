using Timelapse.MotionDetection;

const int Width = 640;
const int Height = 360;
const int Stride = Width * 4;

List<string> failures = [];
Run("moving rectangle is detected", MovingRectangleIsDetected);
Run("static frame is quiet", StaticFrameIsQuiet);
Run("manual bottom band is excluded", ManualBottomBandIsExcluded);
Run("backward PTS resets temporal state", BackwardPtsResets);
Run("source generation changes reset temporal state", GenerationChangeResets);
Run("global luminance change is suppressed", GlobalChangeIsSuppressed);
Run("multiple new regions do not corrupt tracking", MultipleNewRegionsAreTracked);
Run("sparse full-frame outline is rejected", SparseFullFrameOutlineIsRejected);
Run("nearby fragments merge into one candidate", NearbyFragmentsMerge);
Run("padding is clipped above excluded band", PaddingClipsAboveBand);

if (failures.Count > 0)
{
    Console.Error.WriteLine($"FAILED ({failures.Count})");
    foreach (string failure in failures)
    {
        Console.Error.WriteLine(failure);
    }
    return 1;
}

Console.WriteLine("PASS: 10 deterministic motion BBox core tests");
return 0;

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL: {name}: {exception.Message}");
    }
}

void MovingRectangleIsDetected()
{
    using MotionBboxEngine engine = new();
    MotionBboxSettings settings = Settings();
    Process(engine, Frame(), 0, settings);
    MotionBboxResult result = Process(engine, Frame((220, 110, 70, 48, (byte)255)), 250, settings);
    Require(result.Candidates.Count > 0, "expected at least one candidate");
    MotionCandidate candidate = result.Candidates[0];
    Require(candidate.X < 0.50 && candidate.X + candidate.Width > 0.34, "candidate does not cover the moving rectangle");
}

void StaticFrameIsQuiet()
{
    using MotionBboxEngine engine = new();
    MotionBboxSettings settings = Settings();
    byte[] frame = Frame((180, 90, 60, 50, (byte)180));
    Process(engine, frame, 0, settings);
    MotionBboxResult result = Process(engine, frame, 250, settings);
    Require(result.Candidates.Count == 0, "unchanged image created a candidate");
}

void ManualBottomBandIsExcluded()
{
    using MotionBboxEngine engine = new();
    MotionBboxSettings settings = Settings() with
    {
        BottomBandMode = BottomBandMode.Manual,
        ManualBottomBandPixels = 80
    };
    Process(engine, Frame(), 0, settings);
    MotionBboxResult result = Process(engine, Frame((180, 310, 80, 35, (byte)255)), 250, settings);
    Require(result.BottomBandPixels == 80, $"expected 80px band, got {result.BottomBandPixels}");
    Require(result.Candidates.Count == 0, "motion inside excluded band was detected");
}

void BackwardPtsResets()
{
    using MotionBboxEngine engine = new();
    MotionBboxSettings settings = Settings();
    Process(engine, Frame(), 500, settings);
    Process(engine, Frame((100, 80, 60, 45, (byte)255)), 750, settings);
    MotionBboxResult result = Process(engine, Frame((140, 80, 60, 45, (byte)255)), 300, settings);
    Require(result.IsInitializing, "backward PTS did not reset the history");
    Require(result.Candidates.Count == 0, "stale candidate survived backward PTS");
}

void GenerationChangeResets()
{
    using MotionBboxEngine engine = new();
    MotionBboxSettings settings = Settings();
    Process(engine, Frame(), 0, settings, 1);
    Process(engine, Frame((100, 80, 60, 45, (byte)255)), 250, settings, 1);
    MotionBboxResult result = Process(engine, Frame((140, 80, 60, 45, (byte)255)), 500, settings, 2);
    Require(result.IsInitializing, "generation change did not reset the history");
    Require(result.Candidates.Count == 0, "stale candidate survived generation change");
}

void GlobalChangeIsSuppressed()
{
    using MotionBboxEngine engine = new();
    MotionBboxSettings settings = Settings();
    Process(engine, Frame(), 0, settings);
    MotionBboxResult result = Process(engine, SolidFrame(255), 250, settings);
    Require(result.GlobalChangeSuppressed, "full-frame luminance change was not suppressed");
    Require(result.Candidates.Count == 0, "global change produced a candidate");
}

void MultipleNewRegionsAreTracked()
{
    using MotionBboxEngine engine = new();
    MotionBboxSettings settings = Settings();
    Process(engine, Frame(), 0, settings);
    MotionBboxResult result = Process(engine, Frame(
        (70, 80, 45, 40, (byte)255),
        (480, 210, 55, 45, (byte)255)), 250, settings);
    Require(result.Candidates.Count == 2, $"expected two independent candidates, got {result.Candidates.Count}");
}

void SparseFullFrameOutlineIsRejected()
{
    using MotionBboxEngine engine = new();
    MotionBboxSettings settings = Settings();
    Process(engine, Frame(), 0, settings);
    byte[] outlined = Frame();
    DrawOutline(outlined, 5, 5, Width - 10, Height - 10, 9, 255);
    MotionBboxResult result = Process(engine, outlined, 250, settings);
    Require(result.Candidates.Count == 0, "near-full-frame outline produced an oversized candidate");
}

void NearbyFragmentsMerge()
{
    using MotionBboxEngine engine = new();
    MotionBboxSettings settings = Settings();
    Process(engine, Frame(), 0, settings);
    MotionBboxResult result = Process(engine, Frame(
        (220, 120, 24, 26, (byte)255),
        (257, 122, 24, 26, (byte)255)), 250, settings);
    Require(result.Candidates.Count == 1, $"expected one merged candidate, got {result.Candidates.Count}");
}

void PaddingClipsAboveBand()
{
    using MotionBboxEngine engine = new();
    MotionBboxSettings settings = Settings() with
    {
        BottomBandMode = BottomBandMode.Manual,
        ManualBottomBandPixels = 60,
        PaddingFactor = 2.0
    };
    Process(engine, Frame(), 0, settings);
    MotionBboxResult result = Process(engine, Frame((250, 265, 50, 28, (byte)255)), 250, settings);
    Require(result.Candidates.Count == 1, "expected candidate immediately above the band");
    MotionCandidate candidate = result.Candidates[0];
    double usableHeight = (Height - 60) / (double)Height;
    Require(candidate.Y + candidate.Height <= usableHeight + 1e-9, "padding extended into the excluded band");
}

MotionBboxResult Process(MotionBboxEngine engine, byte[] pixels, int milliseconds, MotionBboxSettings settings, long generation = 1)
{
    return engine.ProcessBgra(pixels, Width, Height, Stride, TimeSpan.FromMilliseconds(milliseconds).Ticks, "synthetic", generation, settings);
}

MotionBboxSettings Settings() => new()
{
    AnalysisWidth = Width,
    Algorithm = MotionBboxAlgorithm.FrameDifference,
    DifferenceThreshold = 18,
    BottomBandMode = BottomBandMode.None,
    PaddingFactor = 1.0,
    HoldMilliseconds = 0
};

byte[] SolidFrame(byte value)
{
    byte[] pixels = GC.AllocateUninitializedArray<byte>(Stride * Height);
    Array.Fill(pixels, value);
    for (int index = 3; index < pixels.Length; index += 4)
    {
        pixels[index] = 255;
    }
    return pixels;
}

byte[] Frame(params (int X, int Y, int Width, int Height, byte Value)[] rectangles)
{
    byte[] pixels = SolidFrame(0);
    foreach ((int x, int y, int width, int height, byte value) in rectangles)
    {
        for (int row = Math.Max(0, y); row < Math.Min(Height, y + height); row++)
        {
            for (int column = Math.Max(0, x); column < Math.Min(Width, x + width); column++)
            {
                int offset = row * Stride + column * 4;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
            }
        }
    }
    return pixels;
}

void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

void DrawOutline(byte[] pixels, int x, int y, int width, int height, int thickness, byte value)
{
    for (int row = y; row < y + height; row++)
    {
        for (int column = x; column < x + width; column++)
        {
            bool edge = row < y + thickness || row >= y + height - thickness ||
                        column < x + thickness || column >= x + width - thickness;
            if (!edge)
            {
                continue;
            }
            int offset = row * Stride + column * 4;
            pixels[offset] = value;
            pixels[offset + 1] = value;
            pixels[offset + 2] = value;
        }
    }
}
