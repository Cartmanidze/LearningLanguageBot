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
        state.Mode = ConversationMode.Normal;
        state.OnboardingStep = OnboardingStep.Completed;

        await _bot.EditMessageText(
            callback.Message!.Chat.Id,
            callback.Message.MessageId,
            "Готово! 🎉\n\n" +
            "Теперь просто отправь мне слово или фразу — \n" +
            "я создам карточку с переводом и примерами.\n\n" +
            "Или отправь текст/файл — извлеку новые слова.\n\n" +
            "Попробуй прямо сейчас 👇",
            cancellationToken: ct);
    }
}
