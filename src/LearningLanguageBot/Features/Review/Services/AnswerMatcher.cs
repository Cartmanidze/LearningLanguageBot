namespace LearningLanguageBot.Features.Review.Services;

/// <summary>
/// Compares user's typed answer with correct translation.
/// </summary>
public static class AnswerMatcher
{
    /// <summary>
    /// Compares user answer with correct answer(s).
    /// </summary>
    /// <param name="userAnswer">User's typed answer</param>
    /// <param name="correctAnswer">Correct answer (may contain alternatives separated by comma)</param>
    /// <returns>Match result: Exact, Partial, or Wrong</returns>
    public static MatchResult Compare(string userAnswer, string correctAnswer)
    {
        var normalizedUser = Normalize(userAnswer);

        if (string.IsNullOrWhiteSpace(normalizedUser))
            return MatchResult.Wrong;

        // Split correct answer by comma to get alternatives
        var alternatives = correctAnswer
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        // Check exact match with any alternative
        if (alternatives.Any(alt => alt == normalizedUser))
            return MatchResult.Exact;

        // Check if user answer contains any alternative or vice versa
        if (alternatives.Any(alt =>
            alt.Contains(normalizedUser) || normalizedUser.Contains(alt)))
            return MatchResult.Partial;

        // Check Levenshtein distance for typos
        foreach (var alt in alternatives)
        {
            var distance = LevenshteinDistance(normalizedUser, alt);
            var maxLen = Math.Max(normalizedUser.Length, alt.Length);
            var similarity = 1.0 - (double)distance / maxLen;

            // 80%+ similarity = exact (minor typo)
            if (similarity >= 0.8)
                return MatchResult.Exact;

            // 60%+ similarity = partial
            if (similarity >= 0.6)
                return MatchResult.Partial;
        }

        return MatchResult.Wrong;
    }

    private static string Normalize(string input)
    {
        return input
            .ToLowerInvariant()
            .Trim()
            .Replace("ё", "е"); // Normalize Russian ё
    }

    private static int LevenshteinDistance(string s1, string s2)
    {
        var m = s1.Length;
        var n = s2.Length;
        var dp = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++) dp[i, 0] = i;
        for (var j = 0; j <= n; j++) dp[0, j] = j;

        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[m, n];
    }
}

public enum MatchResult
{
    Exact,   // Correct answer
    Partial, // Close enough - let user decide
    Wrong    // Incorrect
}
