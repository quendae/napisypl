namespace NapisyPL.Core.ContextResolution;

/// <summary>
/// Turns a pitch track into the <see cref="SpeakerGenderObservation"/> shape the
/// existing aggregator, eligibility rules and turn resolvers already consume.
///
/// The two "probabilities" keep their original meaning:
///   male + female  = how much voiced speech the sample actually contains,
///   winner / sum   = how confident the pitch is about the direction.
/// That is the same contract the audio-tagging classifier nominally had, except
/// the numbers now carry real information instead of sitting in the noise floor.
/// </summary>
public static class VoiceGenderPitchMapper
{
    /// <summary>
    /// Log-midpoint between typical male (~120 Hz) and female (~210 Hz) speaking
    /// pitch. Deciding in the log domain keeps the boundary perceptually centred.
    /// </summary>
    public const double BoundaryHz = 160;

    /// <summary>
    /// Logistic steepness in octaves. At 120 Hz / 210 Hz this yields ~0.92
    /// directional confidence; around 180 Hz it stays near 0.73, i.e. below every
    /// downstream gate, which is the correct answer for a genuinely ambiguous voice.
    /// </summary>
    private const double Steepness = 6.0;

    /// <summary>
    /// Below this share of voiced frames the sample is music, effects or room
    /// tone rather than a speaking voice.
    /// </summary>
    public const double MinimumVoicedFraction = 0.20;

    private const int MinimumVoicedFrames = 8;

    public static SpeakerGenderObservation ToObservation(PitchTrack track, double durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (durationSeconds <= 0 ||
            !double.IsFinite(durationSeconds) ||
            track.VoicedFrameCount < MinimumVoicedFrames ||
            track.VoicedFraction < MinimumVoicedFraction ||
            track.MedianF0Hz <= 0)
        {
            return new SpeakerGenderObservation(0, 0, Math.Max(0, durationSeconds));
        }

        var femaleShare = FemaleProbability(track.MedianF0Hz);
        var evidence = Math.Clamp(track.VoicedFraction, 0, 1);

        return new SpeakerGenderObservation(
            evidence * (1 - femaleShare),
            evidence * femaleShare,
            durationSeconds);
    }

    /// <summary>
    /// Logistic over log2(f0 / boundary), so the decision is symmetric in octaves.
    /// </summary>
    public static double FemaleProbability(double medianF0Hz)
    {
        if (medianF0Hz <= 0 || !double.IsFinite(medianF0Hz))
            return 0.5;

        var octaves = Math.Log2(medianF0Hz / BoundaryHz);
        return 1 / (1 + Math.Exp(-Steepness * octaves));
    }
}
