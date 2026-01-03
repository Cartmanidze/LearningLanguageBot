using LearningLanguageBot.Features.Onboarding.Services;
using LearningLanguageBot.Infrastructure.Database.Models;
using LearningLanguageBot.Infrastructure.State;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

using User = LearningLanguageBot.Infrastructure.Database.Models.User;

namespace LearningLanguageBot.Features.Settings.Handlers;

public class SettingsHandler
{
    private readonly ITelegramBotClient _bot;
    private readonly UserService _userService;
    private readonly ConversationStateManager _stateManager;

    public SettingsHandler(
        ITelegramBotClient bot,
        UserService userService,
        ConversationStateManager stateManager)
    {
        _bot = bot;
        _userService = userService;
        _stateManager = stateManager;
    }

    public async Task HandleSettingsCommandAsync(Message message, CancellationToken ct)
    {
        var userId = message.From!.Id;
        var user = await _userService.GetUserAsync(userId, ct);

        if (user == null)
        {
            await _bot.SendMessage(
                message.Chat.Id,
                "Сначала запусти /start для настройки бота.",
                cancellationToken: ct);
            return;
        }

        await ShowSettingsMenuAsync(message.Chat.Id, null, user, ct);
    }

    public async Task HandleSettingsCallbackAsync(CallbackQuery callback, CancellationToken ct)
    {
        var userId = callback.From.Id;
        var data = callback.Data ?? string.Empty;
        var user = await _userService.GetUserAsync(userId, ct);
        var state = _stateManager.GetOrCreate(userId);

        if (user == null)
        {
            await _bot.AnswerCallbackQuery(callback.Id, "Ошибка", cancellationToken: ct);
            return;
        }

        // Main menu
        if (data == "settings:main")
        {
            state.Mode = ConversationMode.Normal;
            state.SelectedReminderTimes = [];
            await ShowSettingsMenuAsync(callback.Message!.Chat.Id, callback.Message.MessageId, user, ct);
        }
        // Change review mode
        else if (data == "settings:mode")
        {
            await ShowModeSettingsAsync(callback, user, ct);
        }
        else if (data == "settings:mode:typing")
        {
            await _userService.UpdateUserSettingsAsync(userId, reviewMode: ReviewMode.Typing, ct: ct);
            user = await _userService.GetUserAsync(userId, ct);
            await ShowSettingsMenuAsync(callback.Message!.Chat.Id, callback.Message.MessageId, user!, ct);
        }
        else if (data == "settings:mode:reveal")
        {
            await _userService.UpdateUserSettingsAsync(userId, reviewMode: ReviewMode.Reveal, ct: ct);
            user = await _userService.GetUserAsync(userId, ct);
            await ShowSettingsMenuAsync(callback.Message!.Chat.Id, callback.Message.MessageId, user!, ct);
        }
        // Change daily goal
        else if (data == "settings:goal")
        {
            await ShowGoalSettingsAsync(callback, ct);
        }
        else if (data.StartsWith("settings:goal:"))
        {
            var goal = int.Parse(data.Replace("settings:goal:", ""));
            await _userService.UpdateUserSettingsAsync(userId, dailyGoal: goal, ct: ct);
            user = await _userService.GetUserAsync(userId, ct);
            await ShowSettingsMenuAsync(callback.Message!.Chat.Id, callback.Message.MessageId, user!, ct);
        }
        // Change reminder times
        else if (data == "settings:time")
        {
            state.Mode = ConversationMode.Settings;
            state.SelectedReminderTimes = user.ReminderTimes.ToList();
            await ShowTimeSettingsAsync(callback.Message!.Chat.Id, callback.Message.MessageId, state, ct);
        }
        // Change timezone
        else if (data == "settings:timezone")
        {
            await ShowTimezoneSettingsAsync(callback, user, ct);
        }
        else if (data.StartsWith("settings:tz:"))
        {
            var tz = data.Replace("settings:tz:", "");
            await _userService.UpdateUserSettingsAsync(userId, timeZone: tz, ct: ct);
            user = await _userService.GetUserAsync(userId, ct);
            await ShowSettingsMenuAsync(callback.Message!.Chat.Id, callback.Message.MessageId, user!, ct);
        }
        else if (data == "settings:time:9" || data == "settings:time:14" || data == "settings:time:20")
        {
            var hour = int.Parse(data.Replace("settings:time:", ""));
            var time = new TimeOnly(hour, 0);

            if (state.SelectedReminderTimes.Contains(time))
                state.SelectedReminderTimes.Remove(time);
            else
                state.SelectedReminderTimes.Add(time);

            await ShowTimeSettingsAsync(callback.Message!.Chat.Id, callback.Message.MessageId, state, ct);
        }
        else if (data == "settings:time:all")
        {
            state.SelectedReminderTimes = [new TimeOnly(9, 0), new TimeOnly(14, 0), new TimeOnly(20, 0)];
            await ShowTimeSettingsAsync(callback.Message!.Chat.Id, callback.Message.MessageId, state, ct);
        }
        else if (data == "settings:time:done")
        {
            if (state.SelectedReminderTimes.Count == 0)
            {
                await _bot.AnswerCallbackQuery(callback.Id, "Выбери хотя бы одно время!", showAlert: true, cancellationToken: ct);
                return;
            }

            var times = state.SelectedReminderTimes.OrderBy(t => t).ToList();
            await _userService.UpdateUserSettingsAsync(userId, reminderTimes: times, ct: ct);
            state.Mode = ConversationMode.Normal;
            state.SelectedReminderTimes = [];
            user = await _userService.GetUserAsync(userId, ct);
            await ShowSettingsMenuAsync(callback.Message!.Chat.Id, callback.Message.MessageId, user!, ct);
        }

        await _bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
    }

    public async Task HandleTimeTextInputAsync(Message message, UserState state, CancellationToken ct)
    {
        var userId = message.From!.Id;
        var text = message.Text ?? string.Empty;
        var times = ParseReminderTimes(text);

        if (times.Count == 0)
        {
            await _bot.SendMessage(
                message.Chat.Id,
                "❌ Не удалось распознать время.\n\nВведи в формате: 9:00, 14:00, 20:00",
                cancellationToken: ct);
            return;
        }

        await _userService.UpdateUserSettingsAsync(userId, reminderTimes: times, ct: ct);
        state.Mode = ConversationMode.Normal;
        state.SelectedReminderTimes = [];

        var timesStr = string.Join(", ", times.Select(t => t.ToString("HH:mm")));

        var user = await _userService.GetUserAsync(userId, ct);
        await _bot.SendMessage(
            message.Chat.Id,
            $"✓ Напоминания обновлены: {timesStr}",
            cancellationToken: ct);

        await ShowSettingsMenuAsync(message.Chat.Id, null, user!, ct);
    }

    private async Task ShowSettingsMenuAsync(long chatId, int? messageId, User user, CancellationToken ct)
    {
        var modeName = user.ReviewMode == ReviewMode.Typing ? "Печатать" : "Вспоминать";
        var timesStr = string.Join(", ", user.ReminderTimes.OrderBy(t => t).Select(t => t.ToString("HH:mm")));
        var tzDisplay = GetTimezoneDisplayName(user.TimeZone);

        var text = "⚙️ Настройки\n\n" +
                   $"📝 Режим: {modeName}\n" +
                   $"🎯 Цель: {user.DailyGoal} карточек/день\n" +
                   $"⏰ Напоминания: {timesStr}\n" +
                   $"🌍 Часовой пояс: {tzDisplay}";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📝 Режим", "settings:mode"),
                InlineKeyboardButton.WithCallbackData("🎯 Цель", "settings:goal")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⏰ Время", "settings:time"),
                InlineKeyboardButton.WithCallbackData("🌍 Пояс", "settings:timezone")
            }
        });

        if (messageId.HasValue)
        {
            await _bot.EditMessageText(chatId, messageId.Value, text, replyMarkup: keyboard, cancellationToken: ct);
        }
        else
        {
            await _bot.SendMessage(chatId, text, replyMarkup: keyboard, cancellationToken: ct);
        }
    }

    private async Task ShowModeSettingsAsync(CallbackQuery callback, User user, CancellationToken ct)
    {
        var currentMode = user.ReviewMode == ReviewMode.Typing ? "📝 Печатать" : "👁 Вспоминать";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📝 Печатать", "settings:mode:typing"),
                InlineKeyboardButton.WithCallbackData("👁 Вспоминать", "settings:mode:reveal")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⬅️ Назад", "settings:main")
            }
        });

        await _bot.EditMessageText(
            callback.Message!.Chat.Id,
            callback.Message.MessageId,
            $"Текущий режим: {currentMode}\n\n" +
            "📝 Печатать — пишешь перевод сам\n" +
            "👁 Вспоминать — смотришь и оцениваешь",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task ShowGoalSettingsAsync(CallbackQuery callback, CancellationToken ct)
    {
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("10", "settings:goal:10"),
                InlineKeyboardButton.WithCallbackData("20", "settings:goal:20"),
                InlineKeyboardButton.WithCallbackData("30", "settings:goal:30"),
                InlineKeyboardButton.WithCallbackData("50", "settings:goal:50")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⬅️ Назад", "settings:main")
            }
        });

        await _bot.EditMessageText(
            callback.Message!.Chat.Id,
            callback.Message.MessageId,
            "Сколько карточек в день?",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task ShowTimeSettingsAsync(long chatId, int messageId, UserState state, CancellationToken ct)
    {
        var selected = state.SelectedReminderTimes;
        string Check(TimeOnly time) => selected.Contains(time) ? "✓ " : "";

        var selectedText = selected.Count > 0
            ? $"\n\nВыбрано: {string.Join(", ", selected.OrderBy(t => t).Select(t => t.ToString("HH:mm")))}"
            : "";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData($"{Check(new TimeOnly(9, 0))}🌅 9:00", "settings:time:9"),
                InlineKeyboardButton.WithCallbackData($"{Check(new TimeOnly(14, 0))}🌞 14:00", "settings:time:14"),
                InlineKeyboardButton.WithCallbackData($"{Check(new TimeOnly(20, 0))}🌙 20:00", "settings:time:20")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📅 Все три", "settings:time:all"),
                InlineKeyboardButton.WithCallbackData("✅ Сохранить", "settings:time:done")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⬅️ Назад", "settings:main")
            }
        });

        await _bot.EditMessageText(
            chatId,
            messageId,
            "Выбери время напоминаний:" + selectedText + "\n\nИли напиши своё время: 8:30, 13:00",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private List<TimeOnly> ParseReminderTimes(string input)
    {
        var times = new List<TimeOnly>();
        var parts = input.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (TimeOnly.TryParse(trimmed, out var time))
            {
                times.Add(time);
            }
            else if (int.TryParse(trimmed, out var hour) && hour >= 0 && hour <= 23)
            {
                times.Add(new TimeOnly(hour, 0));
            }
        }

        return times.Distinct().OrderBy(t => t).ToList();
    }

    private async Task ShowTimezoneSettingsAsync(CallbackQuery callback, User user, CancellationToken ct)
    {
        string Check(string tz) => user.TimeZone == tz ? "✓ " : "";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            // Row 1: Europe
            new[]
            {
                InlineKeyboardButton.WithCallbackData($"{Check("Europe/London")}Лондон", "settings:tz:Europe/London"),
                InlineKeyboardButton.WithCallbackData($"{Check("Europe/Berlin")}Берлин", "settings:tz:Europe/Berlin"),
                InlineKeyboardButton.WithCallbackData($"{Check("Europe/Kiev")}Киев", "settings:tz:Europe/Kiev")
            },
            // Row 2: Russia West
            new[]
            {
                InlineKeyboardButton.WithCallbackData($"{Check("Europe/Kaliningrad")}Калининград", "settings:tz:Europe/Kaliningrad"),
                InlineKeyboardButton.WithCallbackData($"{Check("Europe/Moscow")}Москва", "settings:tz:Europe/Moscow"),
                InlineKeyboardButton.WithCallbackData($"{Check("Europe/Samara")}Самара", "settings:tz:Europe/Samara")
            },
            // Row 3: Russia East
            new[]
            {
                InlineKeyboardButton.WithCallbackData($"{Check("Asia/Yekaterinburg")}Екб", "settings:tz:Asia/Yekaterinburg"),
                InlineKeyboardButton.WithCallbackData($"{Check("Asia/Omsk")}Омск", "settings:tz:Asia/Omsk"),
                InlineKeyboardButton.WithCallbackData($"{Check("Asia/Novosibirsk")}Нск", "settings:tz:Asia/Novosibirsk")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData($"{Check("Asia/Vladivostok")}Владивосток", "settings:tz:Asia/Vladivostok")
            },
            // Row 4: Asia
            new[]
            {
                InlineKeyboardButton.WithCallbackData($"{Check("Asia/Dubai")}Дубай", "settings:tz:Asia/Dubai"),
                InlineKeyboardButton.WithCallbackData($"{Check("Asia/Bangkok")}Бангкок", "settings:tz:Asia/Bangkok"),
                InlineKeyboardButton.WithCallbackData($"{Check("Asia/Singapore")}Сингапур", "settings:tz:Asia/Singapore")
            },
            // Row 5: Americas
            new[]
            {
                InlineKeyboardButton.WithCallbackData($"{Check("America/New_York")}Нью-Йорк", "settings:tz:America/New_York"),
                InlineKeyboardButton.WithCallbackData($"{Check("America/Los_Angeles")}Лос-Анджелес", "settings:tz:America/Los_Angeles")
            },
            // Back
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⬅️ Назад", "settings:main")
            }
        });

        await _bot.EditMessageText(
            callback.Message!.Chat.Id,
            callback.Message.MessageId,
            $"Текущий: {GetTimezoneDisplayName(user.TimeZone)}\n\n" +
            "Выбери часовой пояс:",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private static string GetTimezoneDisplayName(string tzId) => tzId switch
    {
        "Europe/London" => "Лондон (UTC+0)",
        "Europe/Berlin" => "Берлин (UTC+1)",
        "Europe/Kiev" => "Киев (UTC+2)",
        "Europe/Kaliningrad" => "Калининград (UTC+2)",
        "Europe/Moscow" => "Москва (UTC+3)",
        "Europe/Samara" => "Самара (UTC+4)",
        "Asia/Yekaterinburg" => "Екатеринбург (UTC+5)",
        "Asia/Omsk" => "Омск (UTC+6)",
        "Asia/Novosibirsk" => "Новосибирск (UTC+7)",
        "Asia/Vladivostok" => "Владивосток (UTC+10)",
        "Asia/Dubai" => "Дубай (UTC+4)",
        "Asia/Bangkok" => "Бангкок (UTC+7)",
        "Asia/Singapore" => "Сингапур (UTC+8)",
        "America/New_York" => "Нью-Йорк (UTC-5)",
        "America/Los_Angeles" => "Лос-Анджелес (UTC-8)",
        _ => tzId
    };
}
