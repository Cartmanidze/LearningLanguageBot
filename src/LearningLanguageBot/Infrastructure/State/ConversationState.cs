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
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;

    public void Touch() => LastActivity = DateTime.UtcNow;
}

public enum ConversationMode
{
    Normal,
    Onboarding,
    EditingCard,
    Reviewing
}

public enum OnboardingStep
{
    ChooseLanguage,
    ChooseMode,
    ChooseGoal,
    ChooseReminders,
    Completed
}

public enum EditAction
{
    Translation,
    Examples
}

public class ReviewSession
{
    public List<Guid> CardIds { get; set; } = [];
    public int CurrentIndex { get; set; }
    public int KnewCount { get; set; }
    public int DidNotKnowCount { get; set; }
    public bool ShowingAnswer { get; set; }

    public Guid CurrentCardId => CardIds.ElementAtOrDefault(CurrentIndex);
    public bool IsComplete => CurrentIndex >= CardIds.Count;
    public int TotalCards => CardIds.Count;
}
