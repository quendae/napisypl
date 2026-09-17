using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public class FundamentalFrequencyEstimatorTests
{
    private const int SampleRate = 16000;

    /// <summary>
    /// A glottal pulse train is closer to real speech than a pure sine: the
    /// harmonic stack is what YIN actually locks onto.
    /// </summary>
    private static float[] SynthesizeVoice(double f0Hz, double seconds, double amplitude = 0.3)
    {
        var length = (int)(seconds * SampleRate);
        var samples = new float[length];
        for (var index = 0; index < length; index++)
        {
            var time = (double)index / SampleRate;
            double value = 0;
            for (var harmonic = 1; harmonic <= 12; harmonic++)
            {
                if (f0Hz * harmonic >= SampleRate / 2.0)
                    break;
                value += Math.Sin(2 * Math.PI * f0Hz * harmonic * time) / harmonic;
            }

            samples[index] = (float)(amplitude * value);
        }

        return samples;
    }

    private static float[] Silence(double seconds) => new float[(int)(seconds * SampleRate)];

    private static float[] Noise(double seconds, int seed = 7)
    {
        var random = new Random(seed);
        var samples = new float[(int)(seconds * SampleRate)];
        for (var index = 0; index < samples.Length; index++)
            samples[index] = (float)((random.NextDouble() * 2 - 1) * 0.3);
        return samples;
    }

    [Theory]
    [InlineData(100)]
    [InlineData(120)]
    [InlineData(150)]
    [InlineData(200)]
    [InlineData(240)]
    public void Analyze_SynthesizedVoice_RecoversFundamentalWithinTwoPercent(double f0Hz)
    {
        var track = FundamentalFrequencyEstimator.Analyze(SynthesizeVoice(f0Hz, 1.0), SampleRate);

        Assert.True(track.VoicedFraction > 0.8, $"voicedFraction={track.VoicedFraction}");
        Assert.InRange(track.MedianF0Hz, f0Hz * 0.98, f0Hz * 1.02);
    }

    [Fact]
    public void Analyze_Silence_ReportsNoVoicedFrames()
    {
        var track = FundamentalFrequencyEstimator.Analyze(Silence(1.0), SampleRate);

        Assert.Equal(0, track.VoicedFrameCount);
        Assert.Equal(0, track.MedianF0Hz);
    }

    [Fact]
    public void Analyze_WhiteNoise_StaysMostlyUnvoiced()
    {
        var track = FundamentalFrequencyEstimator.Analyze(Noise(1.0), SampleRate);

        Assert.True(
            track.VoicedFraction < VoiceGenderPitchMapper.MinimumVoicedFraction,
            $"voicedFraction={track.VoicedFraction}");
    }

    [Fact]
    public void Analyze_EmptyInput_ReturnsEmptyTrack()
    {
        var track = FundamentalFrequencyEstimator.Analyze([], SampleRate);

        Assert.Equal(0, track.TotalFrameCount);
        Assert.Equal(0, track.VoicedFrameCount);
    }

    [Fact]
    public void Analyze_HalfSpeechHalfSilence_ReportsPartialVoicing()
    {
        var combined = SynthesizeVoice(130, 0.5).Concat(Silence(0.5)).ToArray();

        var track = FundamentalFrequencyEstimator.Analyze(combined, SampleRate);

        Assert.InRange(track.VoicedFraction, 0.3, 0.7);
        Assert.InRange(track.MedianF0Hz, 127, 133);
    }
}
