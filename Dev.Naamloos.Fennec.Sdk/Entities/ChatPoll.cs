using System.Collections.ObjectModel;

namespace Dev.Naamloos.Fennec.Sdk.Entities;

public sealed class ChatPoll : ObservableModel
{
    private bool _hasVoted;

    public ChatPoll(string question, IEnumerable<ChatPollAnswer> answers, ulong maxSelections, bool isClosed)
    {
        Question = question;
        Answers = new ObservableCollection<ChatPollAnswer>(answers);
        MaxSelections = maxSelections;
        IsClosed = isClosed;
        foreach (var answer in Answers)
        {
            answer.PropertyChanged += (_, _) => UpdateHasVoted();
        }

        UpdateHasVoted();
    }

    public string Question { get; }

    public ObservableCollection<ChatPollAnswer> Answers { get; }

    public ulong MaxSelections { get; }

    public bool IsClosed { get; }

    public bool HasVoted { get => _hasVoted; private set => Set(ref _hasVoted, value); }

    public string[] Select(string answerId)
    {
        var answer = Answers.FirstOrDefault(x => x.Id == answerId);
        if (answer is null || IsClosed)
        {
            return [];
        }

        if (MaxSelections == 1)
        {
            foreach (var selected in Answers.Where(x => x.IsSelected && x != answer))
            {
                selected.IsSelected = false;
                selected.VoteCount = Math.Max(0, selected.VoteCount - 1);
            }
        }
        else if (!answer.IsSelected && (ulong)Answers.Count(x => x.IsSelected) == MaxSelections)
        {
            return Answers.Where(x => x.IsSelected).Select(x => x.Id).ToArray();
        }

        if (!answer.IsSelected)
        {
            answer.IsSelected = true;
            answer.VoteCount++;
        }

        return Answers.Where(x => x.IsSelected).Select(x => x.Id).ToArray();
    }

    public IReadOnlyDictionary<string, (int VoteCount, bool IsSelected)> Snapshot() =>
        Answers.ToDictionary(x => x.Id, x => (x.VoteCount, x.IsSelected));

    public void Restore(IReadOnlyDictionary<string, (int VoteCount, bool IsSelected)> snapshot)
    {
        foreach (var answer in Answers)
        {
            if (snapshot.TryGetValue(answer.Id, out var state))
            {
                answer.VoteCount = state.VoteCount;
                answer.IsSelected = state.IsSelected;
            }
        }
    }

    private void UpdateHasVoted() => HasVoted = Answers.Any(x => x.IsSelected);
}

public sealed class ChatPollAnswer : ObservableModel
{
    private int _voteCount;
    private bool _isSelected;

    public ChatPollAnswer(string id, string text, int voteCount, bool isSelected)
    {
        Id = id;
        Text = text;
        _voteCount = voteCount;
        _isSelected = isSelected;
    }

    public string Id { get; }

    public string Text { get; }

    public int VoteCount
    {
        get => _voteCount;
        set
        {
            if (Set(ref _voteCount, value)) Raise(nameof(DisplayText));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value)) Raise(nameof(DisplayText));
        }
    }

    public string DisplayText => $"{(IsSelected ? "✓ " : string.Empty)}{Text} · {VoteCount} {(VoteCount == 1 ? "vote" : "votes")}";
}

public sealed record ChatPollVote(ChatTimelineItem Item, string AnswerId);
