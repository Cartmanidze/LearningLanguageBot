namespace LearningLanguageBot.Infrastructure.Constants;

public static class CallbackData
{
    // Onboarding
    public const string LangEnglish = "lang:en";
    public const string ModeReveal = "mode:reveal";
    public const string ModeTyping = "mode:typing";
    public const string Goal10 = "goal:10";
    public const string Goal20 = "goal:20";
    public const string Goal30 = "goal:30";
    public const string Goal50 = "goal:50";
    public const string RemindersOk = "reminders:ok";
    public const string RemindersCustom = "reminders:custom";

    // Card actions
    public const string CardEdit = "card:edit:";
    public const string CardEditTranslation = "cardedit:translation:";
    public const string CardEditExamples = "cardedit:examples:";
    public const string CardDelete = "card:delete:";
    public const string CardDeleteConfirm = "carddelconfirm:";
    public const string CardDeleteCancel = "carddelcancel:";

    // Review actions
    public const string ReviewReveal = "review:reveal:";
    public const string ReviewKnew = "review:knew:";
    public const string ReviewDidNotKnow = "review:didnotknow:";
    public const string ReviewCountAsKnew = "review:countas:knew:";      // Typing mode: count partial as correct
    public const string ReviewCountAsDidNotKnow = "review:countas:not:"; // Typing mode: count partial as wrong
    public const string ReviewSkipCard = "review:skip:";                  // Skip current card

    // Learn
    public const string LearnStart = "learn:start";
    public const string LearnSkip = "learn:skip";

    public static string ParseCardId(string data, string prefix) =>
        data.StartsWith(prefix) ? data[prefix.Length..] : string.Empty;
}
