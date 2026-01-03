namespace LearningLanguageBot.Infrastructure.DeepSeek;

public class DeepSeekOptions
{
    public const string SectionName = "OpenRouter";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string Model { get; set; } = "deepseek/deepseek-v3.2";
    public int MaxTokens { get; set; } = 1000;
    public double Temperature { get; set; } = 0.3;
    public string SiteName { get; set; } = "LearningLanguageBot";
}
