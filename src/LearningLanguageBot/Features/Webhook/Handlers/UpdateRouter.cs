using LearningLanguageBot.Features.Cards.Handlers;
using LearningLanguageBot.Features.Onboarding.Handlers;
using LearningLanguageBot.Features.Review.Handlers;
using LearningLanguageBot.Infrastructure.Constants;
using LearningLanguageBot.Infrastructure.State;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace LearningLanguageBot.Features.Webhook.Handlers;

public class UpdateRouter
{
    private readonly ITelegramBotClient _bot;
    private readonly OnboardingHandler _onboardingHandler;
    private readonly CardCreationHandler _cardCreationHandler;
    private readonly ReviewHandler _reviewHandler;
    private readonly ConversationStateManager _stateManager;
    private readonly ILogger<UpdateRouter> _logger;

    public UpdateRouter(
        ITelegramBotClient bot,
        OnboardingHandler onboardingHandler,
        CardCreationHandler cardCreationHandler,
        ReviewHandler reviewHandler,
        ConversationStateManager stateManager,
        ILogger<UpdateRouter> logger)
    {
        _bot = bot;
        _onboardingHandler = onboardingHandler;
        _cardCreationHandler = cardCreationHandler;
        _reviewHandler = reviewHandler;
        _stateManager = stateManager;
        _logger = logger;
    }

    public async Task HandleUpdateAsync(Update update, CancellationToken ct)
    {
        try
        {
            var handler = update.Type switch
            {
                UpdateType.Message => HandleMessageAsync(update.Message!, ct),
                UpdateType.CallbackQuery => HandleCallbackAsync(update.CallbackQuery!, ct),
                _ => Task.CompletedTask
            };

            await handler;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling update {UpdateId}", update.Id);
        }
    }

    private async Task HandleMessageAsync(Message message, CancellationToken ct)
    {
        if (message.From == null) return;

        var text = message.Text ?? string.Empty;

        // Handle commands
        if (text.StartsWith('/'))
        {
            var command = text.Split(' ')[0].ToLowerInvariant();
            switch (command)
            {
                case "/start":
                    await _onboardingHandler.HandleStartAsync(message, ct);
                    return;
                case "/learn":
                    await _reviewHandler.HandleLearnCommandAsync(message, ct);
                    return;
                case "/help":
                    await SendHelpAsync(message, ct);
                    return;
            }
        }

        // Handle regular text based on state
        var state = _stateManager.GetOrCreate(message.From.Id);

        if (state.Mode == ConversationMode.Onboarding)
        {
            // During onboarding, ignore text messages
            return;
        }

        if (state.Mode == ConversationMode.EditingCard)
        {
            await _cardCreationHandler.HandleTextAsync(message, ct);
            return;
        }

        // Handle typed answers during review (Typing mode)
        if (state.Mode == ConversationMode.Reviewing &&
            state.ActiveReview?.WaitingForTypedAnswer == true)
        {
            await _reviewHandler.HandleTypedAnswerAsync(message, state, ct);
            return;
        }

        // Default: create card from text
        if (!string.IsNullOrWhiteSpace(text))
        {
            await _cardCreationHandler.HandleTextAsync(message, ct);
        }
    }

    private async Task HandleCallbackAsync(CallbackQuery callback, CancellationToken ct)
    {
        var data = callback.Data ?? string.Empty;
        var userId = callback.From.Id;
        var state = _stateManager.GetOrCreate(userId);

        // Onboarding callbacks
        if (state.Mode == ConversationMode.Onboarding)
        {
            await _onboardingHandler.HandleCallbackAsync(callback, ct);
            return;
        }

        // Review callbacks
        if (data.StartsWith("review:") || data == CallbackData.LearnStart || data == CallbackData.LearnSkip)
        {
            if (data == CallbackData.LearnStart || data == CallbackData.LearnSkip)
            {
                await _reviewHandler.HandleLearnCallbackAsync(callback, ct);
            }
            else
            {
                await _reviewHandler.HandleReviewCallbackAsync(callback, ct);
            }
            return;
        }

        // Card edit callbacks
        if (data.StartsWith("card:") || data.StartsWith("cardedit:") || data.StartsWith("carddelconfirm:") || data.StartsWith("carddelcancel:"))
        {
            await _cardCreationHandler.HandleEditCallbackAsync(callback, ct);
            return;
        }

        await _bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
    }

    private async Task SendHelpAsync(Message message, CancellationToken ct)
    {
        await _bot.SendMessage(
            message.Chat.Id,
            "🤖 Команды бота:\n\n" +
            "/start — настроить бота\n" +
            "/learn — начать повторение\n" +
            "/help — эта справка\n\n" +
            "Просто отправь слово или фразу — я создам карточку!",
            cancellationToken: ct);
    }
}
