namespace LearningLanguageBot.Shared.Constants;

public static class Languages
{
    public const string Russian = "ru";
    public const string English = "en";

    public static readonly Dictionary<string, string> DisplayNames = new()
    {
        [Russian] = "Русский",
        [English] = "English"
    };

    public static readonly Dictionary<string, string> Flags = new()
    {
        [Russian] = "🇷🇺",
        [English] = "🇬🇧"
    };

    public static bool IsCyrillic(string text)
    {
        return text.Any(c => c >= 'а' && c <= 'я' || c >= 'А' && c <= 'Я' || c == 'ё' || c == 'Ё');
    }

    public static string DetectLanguage(string text)
    {
        return IsCyrillic(text) ? Russian : English;
    }
}
