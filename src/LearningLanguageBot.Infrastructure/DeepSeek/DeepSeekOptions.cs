namespace LearningLanguageBot.Infrastructure.DeepSeek;

public class DeepSeekOptions
{
    public const string SectionName = "DeepSeek";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-chat";
    public int MaxTokens { get; set; } = 1000;
    public double Temperature { get; set; } = 0.3;
}
