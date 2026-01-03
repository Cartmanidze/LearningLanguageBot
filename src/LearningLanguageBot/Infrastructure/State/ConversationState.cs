using System.Collections.Concurrent;

namespace LearningLanguageBot.Infrastructure.State;

public class ConversationStateManager
{
    private readonly ConcurrentDictionary<long, UserState> _states = new();
    private readonly TimeSpan _timeout = TimeSpan.FromMinutes(10);

    public UserState GetOrCreate(long userId)
    {
        CleanupExpired();
        return _states.GetOrAdd(userId, _ => new UserState());
    }

    public void Clear(long userId)
    {
        _states.TryRemove(userId, out _);
    }

    private void CleanupExpired()
    {
        var cutoff = DateTime.UtcNow - _timeout;
        var expired = _states.Where(x => x.Value.LastActivity < cutoff).Select(x => x.Key).ToList();
        foreach (var key in expired)
        {
            _states.TryRemove(key, out _);
        }
    }
}

public class UserState
{
    public ConversationMode Mode { get; set; } = ConversationMode.Normal;
    public OnboardingStep OnboardingStep { get; set; }
    public Guid? EditingCardId { get; set; }
    public EditAction? EditAction { get; set; }
    public ReviewSession? ActiveReview { get; set; }
    public List<TimeOnly> SelectedReminderTimes { get; set; } = [];
    public CardBrowserState? CardBrowser { get; set; }
    public ImportState? ImportState { get; set; }
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;

    public void Touch() => LastActivity = DateTime.UtcNow;
}

public class CardBrowserState
{
    public int CurrentPage { get; set; }
    public string? SearchQuery { get; set; }
    public int? LastMessageId { get; set; }
    public bool WaitingForSearchQuery { get; set; }
}

public enum ConversationMode
{
    Normal,
    Onboarding,
    EditingCard,
    Reviewing,
    Settings,
    BrowsingCards,
    Importing
}

public enum OnboardingStep
{
    ChooseLanguage,
    ChooseMode,
    ChooseGoal,
    ChooseReminders,
    CustomReminders,
    Completed
}

public enum EditAction
{
    Translation,
    Examples
}

public class ReviewSession
{
    public long UserId { get; set; }
    public List<Guid> CardIds { get; set; } = [];
    public int CurrentIndex { get; set; }
    public int KnewCount { get; set; }
    public int DidNotKnowCount { get; set; }
    public bool ShowingAnswer { get; set; }
    public bool WaitingForTypedAnswer { get; set; }
    public int? LastMessageId { get; set; }

    public Guid CurrentCardId => CardIds.ElementAtOrDefault(CurrentIndex);
    public bool IsComplete => CurrentIndex >= CardIds.Count;
    public int TotalCards => CardIds.Count;
}

public class ImportState
{
    public ImportSource Source { get; set; }
    public bool WaitingForInput { get; set; }
    public string? SourceText { get; set; }
    public string? SourceTitle { get; set; }
    public List<ExtractedWordState>? ExtractedWords { get; set; }

    // Song search
    public List<SongSearchResultState>? SongSearchResults { get; set; }
    public bool WaitingForSongSelection { get; set; }
}

public class SongSearchResultState
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class ExtractedWordState
{
    public string Word { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
}

public enum ImportSource
{
    Url,
    Text,
    File,
    Song
}
