namespace NapisyPL.Core.ContextResolution;

/// <summary>
/// Result of tracking the fundamental frequency across one audio sample.
/// </summary>
public sealed class PitchTrack
{
    public PitchTrack(IReadOnlyList<double> voicedF0Hz, int totalFrameCount)
    {
        VoicedF0Hz = voicedF0Hz;
        TotalFrameCount = totalFrameCount;
        MedianF0Hz = Median(voicedF0Hz);
    }

    public static PitchTrack Empty { get; } = new([], 0);

    public IReadOnlyList<double> VoicedF0Hz { get; }

    public int TotalFrameCount { get; }

    public int VoicedFrameCount => VoicedF0Hz.Count;

    /// <summary>
    /// Share of analysed frames that carried a periodic (voiced) signal. Speech
    /// sits well above zero; music, noise and silence sit near it, which is what
    /// makes this usable as the "is there actually a voice here" gate.
    /// </summary>
    public double VoicedFraction => TotalFrameCount == 0
        ? 0
        : (double)VoicedFrameCount / TotalFrameCount;

    /// <summary>
    /// Median rather than mean, so a stray octave error cannot drag the estimate.
    /// </summary>
    public double MedianF0Hz { get; }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return 0;

        var ordered = values.ToArray();
        Array.Sort(ordered);
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 1
            ? ordered[middle]
            : (ordered[middle - 1] + ordered[middle]) / 2;
    }
}

/// <summary>
/// Voiced-frame fundamental frequency (F0) estimation using the YIN cumulative
/// mean normalized difference function.
///
/// This replaces reading two AudioSet tag probabilities off a general-purpose
/// audio-tagging model. Pitch is a direct acoustic correlate of speaker sex and
/// costs a fraction of a neural forward pass, so it is both a stronger signal
/// and far cheaper per cue.
/// </summary>
public static class FundamentalFrequencyEstimator
{
    /// <summary>Lowest tracked F0; covers deep male voices.</summary>
    public const double MinimumHz = 60;

    /// <summary>Highest tracked F0; covers high female voices with headroom.</summary>
    public const double MaximumHz = 400;

    // F0 never exceeds 400 Hz, so 8 kHz leaves an order of magnitude of headroom
    // over Nyquist. Decimating first makes the O(frame x lag) search 4x cheaper.
    private const int AnalysisSampleRate = 8000;
    private const double FrameSeconds = 0.045;
    private const double HopSeconds = 0.020;

    // Standard YIN aperiodicity threshold. Below it a frame is considered voiced.
    private const double YinThreshold = 0.15;

    // Frames quieter than this are silence or room tone; tracking them only
    // produces noise.
    private const double MinimumFrameRms = 0.008;

    public static PitchTrack Analyze(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (samples.IsEmpty || sampleRate <= 0)
            return PitchTrack.Empty;

        var (analysis, analysisRate) = Decimate(samples, sampleRate);

        var frameLength = (int)Math.Round(FrameSeconds * analysisRate);
        var hopLength = Math.Max(1, (int)Math.Round(HopSeconds * analysisRate));
        var maximumLag = (int)Math.Ceiling(analysisRate / MinimumHz);
        var minimumLag = Math.Max(2, (int)Math.Floor(analysisRate / MaximumHz));

        // A frame must hold at least two full periods of the lowest tracked
        // pitch, otherwise the difference function has nothing to compare.
        frameLength = Math.Max(frameLength, maximumLag * 2);
        if (analysis.Length < frameLength || minimumLag >= maximumLag)
            return PitchTrack.Empty;

        var voiced = new List<double>();
        var totalFrames = 0;

        var difference = new double[maximumLag + 1];
        var normalized = new double[maximumLag + 1];

        for (var offset = 0; offset + frameLength <= analysis.Length; offset += hopLength)
        {
            totalFrames++;
            var frame = analysis.AsSpan(offset, frameLength);
            var f0 = EstimateFrame(frame, analysisRate, minimumLag, maximumLag, difference, normalized);
            if (f0 > 0)
                voiced.Add(f0);
        }

        return new PitchTrack(voiced, totalFrames);
    }

    private static double EstimateFrame(
        ReadOnlySpan<float> frame,
        int sampleRate,
        int minimumLag,
        int maximumLag,
        double[] difference,
        double[] normalized)
    {
        double sumOfSquares = 0;
        for (var index = 0; index < frame.Length; index++)
            sumOfSquares += (double)frame[index] * frame[index];

        var rms = Math.Sqrt(sumOfSquares / frame.Length);
        if (rms < MinimumFrameRms)
            return 0;

        var window = frame.Length - maximumLag;
        if (window <= 0)
            return 0;

        // Squared difference function over every lag. The cumulative mean below
        // needs all lags from 1, not just the searched range.
        for (var lag = 1; lag <= maximumLag; lag++)
        {
            double sum = 0;
            for (var index = 0; index < window; index++)
            {
                var delta = (double)frame[index] - frame[index + lag];
                sum += delta * delta;
            }

            difference[lag] = sum;
        }

        normalized[0] = 1;
        double running = 0;
        for (var lag = 1; lag <= maximumLag; lag++)
        {
            running += difference[lag];
            normalized[lag] = running > 0
                ? difference[lag] * lag / running
                : 1;
        }

        // First local minimum below the threshold, which is what makes YIN
        // resistant to reporting an octave above the true pitch.
        var chosenLag = -1;
        for (var lag = minimumLag; lag <= maximumLag; lag++)
        {
            if (normalized[lag] >= YinThreshold)
                continue;

            while (lag + 1 <= maximumLag && normalized[lag + 1] < normalized[lag])
                lag++;

            chosenLag = lag;
            break;
        }

        if (chosenLag < 0)
            return 0;

        var refined = ParabolicRefine(normalized, chosenLag, maximumLag);
        if (refined <= 0)
            return 0;

        var frequency = sampleRate / refined;
        return frequency >= MinimumHz && frequency <= MaximumHz ? frequency : 0;
    }

    private static double ParabolicRefine(double[] values, int lag, int maximumLag)
    {
        if (lag <= 0 || lag >= maximumLag)
            return lag;

        var previous = values[lag - 1];
        var current = values[lag];
        var next = values[lag + 1];
        var denominator = previous + next - (2 * current);
        if (Math.Abs(denominator) < 1e-12)
            return lag;

        return lag + ((previous - next) / (2 * denominator));
    }

    /// <summary>
    /// Binomial low-pass then decimate. Cheap, and more than adequate when the
    /// band of interest tops out at 400 Hz.
    /// </summary>
    private static (float[] Samples, int SampleRate) Decimate(ReadOnlySpan<float> samples, int sampleRate)
    {
        var factor = sampleRate / AnalysisSampleRate;
        if (factor < 2)
            return (samples.ToArray(), sampleRate);

        var filtered = new float[samples.Length];
        for (var index = 0; index < samples.Length; index++)
        {
            var minus2 = samples[Math.Max(0, index - 2)];
            var minus1 = samples[Math.Max(0, index - 1)];
            var centre = samples[index];
            var plus1 = samples[Math.Min(samples.Length - 1, index + 1)];
            var plus2 = samples[Math.Min(samples.Length - 1, index + 2)];
            filtered[index] = (minus2 + (4 * minus1) + (6 * centre) + (4 * plus1) + plus2) / 16f;
        }

        var length = filtered.Length / factor;
        var decimated = new float[length];
        for (var index = 0; index < length; index++)
            decimated[index] = filtered[index * factor];

        return (decimated, sampleRate / factor);
    }
}
