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

    // Review actions (FSRS ratings)
    public const string ReviewReveal = "review:reveal:";
    public const string ReviewAgain = "review:again:";   // Rating.Again - не вспомнил
    public const string ReviewHard = "review:hard:";     // Rating.Hard - с трудом
    public const string ReviewGood = "review:good:";     // Rating.Good - нормально
    public const string ReviewEasy = "review:easy:";     // Rating.Easy - легко
    public const string ReviewCountAsGood = "review:countas:good:";      // Typing mode: count partial as correct
    public const string ReviewCountAsAgain = "review:countas:again:";    // Typing mode: count partial as wrong
    public const string ReviewDontRemember = "review:dontremember:";     // Show answer with memory hints

    // Learn
    public const string LearnStart = "learn:start";
    public const string LearnSkip = "learn:skip";

    public static string ParseCardId(string data, string prefix) =>
        data.StartsWith(prefix) ? data[prefix.Length..] : string.Empty;
}
