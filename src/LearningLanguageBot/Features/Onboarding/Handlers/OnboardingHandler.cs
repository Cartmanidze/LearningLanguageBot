using LearningLanguageBot.Features.Onboarding.Services;
using LearningLanguageBot.Infrastructure.Constants;
using LearningLanguageBot.Infrastructure.Database.Models;
using LearningLanguageBot.Infrastructure.State;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace LearningLanguageBot.Features.Onboarding.Handlers;

public class OnboardingHandler
{
    private readonly ITelegramBotClient _bot;
    private readonly UserService _userService;
    private readonly ConversationStateManager _stateManager;

    public OnboardingHandler(
        ITelegramBotClient bot,
        UserService userService,
        ConversationStateManager stateManager)
    {
        _bot = bot;
        _userService = userService;
        _stateManager = stateManager;
    }

    public async Task HandleStartAsync(Message message, CancellationToken ct)
    {
        var userId = message.From!.Id;
        var user = await _userService.GetOrCreateUserAsync(userId, ct);

        var state = _stateManager.GetOrCreate(userId);
        state.Mode = ConversationMode.Onboarding;
        state.OnboardingStep = OnboardingStep.ChooseLanguage;
        state.Touch();

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("🇬🇧 Английский", CallbackData.LangEnglish) }
        });

        await _bot.SendMessage(
            message.Chat.Id,
            "👋 Привет! Я помогу учить языки через карточки.\n\n" +
            "Какой язык хочешь изучать?",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    public async Task HandleCallbackAsync(CallbackQuery callback, CancellationToken ct)
    {
        var userId = callback.From.Id;
        var state = _stateManager.GetOrCreate(userId);
        var data = callback.Data ?? string.Empty;

        state.Touch();

        switch (state.OnboardingStep)
        {
            case OnboardingStep.ChooseLanguage:
                await HandleLanguageChoiceAsync(callback, state, ct);
                break;
            case OnboardingStep.ChooseMode:
                await HandleModeChoiceAsync(callback, state, ct);
                break;
            case OnboardingStep.ChooseGoal:
                await HandleGoalChoiceAsync(callback, state, ct);
                break;
            case OnboardingStep.ChooseReminders:
                await HandleRemindersChoiceAsync(callback, state, ct);
                break;
            case OnboardingStep.CustomReminders:
                await HandleCustomRemindersCallbackAsync(callback, state, ct);
                break;
        }

        await _bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
    }

    private async Task HandleLanguageChoiceAsync(CallbackQuery callback, UserState state, CancellationToken ct)
    {
        if (callback.Data == CallbackData.LangEnglish)
        {
            await _userService.UpdateUserSettingsAsync(callback.From.Id, targetLanguage: "en", ct: ct);

            state.OnboardingStep = OnboardingStep.ChooseMode;

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📝 Печатать", CallbackData.ModeTyping),
                    InlineKeyboardButton.WithCallbackData("👁 Вспоминать", CallbackData.ModeReveal)
                }
            });

            await _bot.EditMessageText(
                callback.Message!.Chat.Id,
                callback.Message.MessageId,
                "Как будем повторять карточки?\n\n" +
                "📝 Печатать ответ — пишешь перевод сам\n" +
                "👁 Вспоминать — смотришь и оцениваешь",
                replyMarkup: keyboard,
                cancellationToken: ct);
        }
    }

    private async Task HandleModeChoiceAsync(CallbackQuery callback, UserState state, CancellationToken ct)
    {
        var mode = callback.Data switch
        {
            CallbackData.ModeTyping => ReviewMode.Typing,
            CallbackData.ModeReveal => ReviewMode.Reveal,
            _ => ReviewMode.Reveal
        };

        await _userService.UpdateUserSettingsAsync(callback.From.Id, reviewMode: mode, ct: ct);

        state.OnboardingStep = OnboardingStep.ChooseGoal;

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("10", CallbackData.Goal10),
                InlineKeyboardButton.WithCallbackData("20", CallbackData.Goal20),
                InlineKeyboardButton.WithCallbackData("30", CallbackData.Goal30),
                InlineKeyboardButton.WithCallbackData("50", CallbackData.Goal50)
            }
        });

        await _bot.EditMessageText(
            callback.Message!.Chat.Id,
            callback.Message.MessageId,
            "Сколько карточек в день?",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task HandleGoalChoiceAsync(CallbackQuery callback, UserState state, CancellationToken ct)
    {
        var goal = callback.Data switch
        {
            CallbackData.Goal10 => 10,
            CallbackData.Goal20 => 20,
            CallbackData.Goal30 => 30,
            CallbackData.Goal50 => 50,
            _ => 20
        };

        await _userService.UpdateUserSettingsAsync(callback.From.Id, dailyGoal: goal, ct: ct);

        state.OnboardingStep = OnboardingStep.ChooseReminders;

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✓ Ок", CallbackData.RemindersOk),
                InlineKeyboardButton.WithCallbackData("⚙ Настроить", CallbackData.RemindersCustom)
            }
        });

        await _bot.EditMessageText(
            callback.Message!.Chat.Id,
            callback.Message.MessageId,
            "Когда присылать карточки?\n" +
            "По умолчанию: 9:00, 14:00, 20:00",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task HandleRemindersChoiceAsync(CallbackQuery callback, UserState state, CancellationToken ct)
    {
        if (callback.Data == CallbackData.RemindersCustom)
        {
            state.OnboardingStep = OnboardingStep.CustomReminders;
            state.SelectedReminderTimes = [];

            await ShowCustomRemindersMenuAsync(callback.Message!.Chat.Id, callback.Message.MessageId, state, ct);
            return;
        }

        // Default times accepted
        await FinishOnboardingAsync(callback.Message!.Chat.Id, callback.Message.MessageId, state, ct);
    }

    public async Task HandleCustomRemindersCallbackAsync(CallbackQuery callback, UserState state, CancellationToken ct)
    {
        var data = callback.Data ?? string.Empty;

        if (data == "reminder:back")
        {
            state.OnboardingStep = OnboardingStep.ChooseReminders;
            state.SelectedReminderTimes = [];

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✓ Ок", CallbackData.RemindersOk),
                    InlineKeyboardButton.WithCallbackData("⚙ Настроить", CallbackData.RemindersCustom)
                }
            });

            await _bot.EditMessageText(
                callback.Message!.Chat.Id,
                callback.Message.MessageId,
                "Когда присылать карточки?\n" +
                "По умолчанию: 9:00, 14:00, 20:00",
                replyMarkup: keyboard,
                cancellationToken: ct);
            return;
        }

        if (data == "reminder:done")
        {
            if (state.SelectedReminderTimes.Count == 0)
            {
                await _bot.AnswerCallbackQuery(callback.Id, "Выбери хотя бы одно время!", showAlert: true, cancellationToken: ct);
                return;
            }

            var times = state.SelectedReminderTimes.OrderBy(t => t).ToList();
            await _userService.UpdateUserSettingsAsync(callback.From.Id, reminderTimes: times, ct: ct);
            state.SelectedReminderTimes = [];
            await FinishOnboardingAsync(callback.Message!.Chat.Id, callback.Message.MessageId, state, ct);
            return;
        }

        if (data == "reminder:all")
        {
            state.SelectedReminderTimes = [new TimeOnly(9, 0), new TimeOnly(14, 0), new TimeOnly(20, 0)];
            await ShowCustomRemindersMenuAsync(callback.Message!.Chat.Id, callback.Message.MessageId, state, ct);
            return;
        }

        // Toggle time selection
        TimeOnly? timeToToggle = data switch
        {
            "reminder:9" => new TimeOnly(9, 0),
            "reminder:14" => new TimeOnly(14, 0),
            "reminder:20" => new TimeOnly(20, 0),
            _ => null
        };

        if (timeToToggle.HasValue)
        {
            if (state.SelectedReminderTimes.Contains(timeToToggle.Value))
                state.SelectedReminderTimes.Remove(timeToToggle.Value);
            else
                state.SelectedReminderTimes.Add(timeToToggle.Value);

            await ShowCustomRemindersMenuAsync(callback.Message!.Chat.Id, callback.Message.MessageId, state, ct);
        }
    }

    private async Task ShowCustomRemindersMenuAsync(long chatId, int messageId, UserState state, CancellationToken ct)
    {
        var selected = state.SelectedReminderTimes;

        string Check(TimeOnly time) => selected.Contains(time) ? "✓ " : "";

        var selectedText = selected.Count > 0
            ? $"\n\nВыбрано: {string.Join(", ", selected.OrderBy(t => t).Select(t => t.ToString("HH:mm")))}"
            : "";

        await _bot.EditMessageText(
            chatId,
            messageId,
            "Выбери время напоминаний (можно несколько):" + selectedText + "\n\nИли напиши своё время, например: 8:30, 13:00",
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData($"{Check(new TimeOnly(9, 0))}🌅 Утро (9:00)", "reminder:9"),
                    InlineKeyboardButton.WithCallbackData($"{Check(new TimeOnly(14, 0))}🌞 День (14:00)", "reminder:14")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData($"{Check(new TimeOnly(20, 0))}🌙 Вечер (20:00)", "reminder:20"),
                    InlineKeyboardButton.WithCallbackData("📅 Все три", "reminder:all")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("⬅️ Назад", "reminder:back"),
                    InlineKeyboardButton.WithCallbackData("✅ Готово", "reminder:done")
                }
            }),
            cancellationToken: ct);
    }

    public async Task HandleCustomRemindersTextAsync(Message message, UserState state, CancellationToken ct)
    {
        var text = message.Text ?? string.Empty;
        var times = ParseReminderTimes(text);

        if (times.Count == 0)
        {
            await _bot.SendMessage(
                message.Chat.Id,
                "❌ Не удалось распознать время.\n\n" +
                "Введи в формате: 9:00, 14:00, 20:00",
                cancellationToken: ct);
            return;
        }

        await _userService.UpdateUserSettingsAsync(message.From!.Id, reminderTimes: times, ct: ct);

        var timesStr = string.Join(", ", times.Select(t => t.ToString("HH:mm")));
        await _bot.SendMessage(
            message.Chat.Id,
            $"✓ Установлены напоминания: {timesStr}",
            cancellationToken: ct);

        await FinishOnboardingAsync(message.Chat.Id, null, state, ct);
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

    private async Task FinishOnboardingAsync(long chatId, int? messageId, UserState state, CancellationToken ct)
    {
        state.Mode = ConversationMode.Normal;
        state.OnboardingStep = OnboardingStep.Completed;

        var text = "Готово! 🎉\n\n" +
                   "Теперь просто отправь мне слово или фразу — \n" +
                   "я создам карточку с переводом и примерами.\n\n" +
                   "Или отправь текст/файл — извлеку новые слова.\n\n" +
                   "Попробуй прямо сейчас 👇";

        if (messageId.HasValue)
        {
            await _bot.EditMessageText(chatId, messageId.Value, text, replyMarkup: null, cancellationToken: ct);
        }
        else
        {
            await _bot.SendMessage(chatId, text, cancellationToken: ct);
        }
    }
}
