using System.ComponentModel;
using System.Runtime.CompilerServices;
using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Review;

/// <summary>One question on the review screen, and the answer the person gave it.</summary>
public sealed class GenderReviewItem(GenderReviewCandidate candidate) : INotifyPropertyChanged
{
    private bool? _accepted;

    public GenderReviewCandidate Candidate { get; } = candidate;

    public int CueId => Candidate.CueId;
    public string English => Candidate.English;
    public string Current => Candidate.Current;
    public string Proposed => Candidate.Proposed;
    public string Reason => Candidate.Reason;
    public string Timecode => $"{Candidate.Start:hh\\:mm\\:ss} · kwestia {Candidate.CueId}";

    /// <summary>null while the question is open, true to take the proposal, false to keep the line.</summary>
    public bool? Accepted
    {
        get => _accepted;
        set
        {
            if (_accepted == value)
                return;
            _accepted = value;
            Raise(nameof(Accepted));
            Raise(nameof(IsAnswered));
            Raise(nameof(TookProposal));
            Raise(nameof(KeptCurrent));
        }
    }

    public bool IsAnswered => _accepted.HasValue;
    public bool TookProposal => _accepted == true;
    public bool KeptCurrent => _accepted == false;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
