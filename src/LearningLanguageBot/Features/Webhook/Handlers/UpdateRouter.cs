using LearningLanguageBot.Features.Cards.Handlers;
using LearningLanguageBot.Features.Import.Handlers;
using LearningLanguageBot.Features.Onboarding.Handlers;
using LearningLanguageBot.Features.Review.Handlers;
using LearningLanguageBot.Features.Settings.Handlers;
using LearningLanguageBot.Infrastructure.Constants;
using LearningLanguageBot.Infrastructure.State;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace LearningLanguageBot.Features.Webhook.Handlers;

public class UpdateRouter
{
    private readonly ITelegramBotClient _bot;
    private readonly OnboardingHandler _onboardingHandler;
    private readonly CardCreationHandler _cardCreationHandler;
    private readonly CardBrowserHandler _cardBrowserHandler;
    private readonly ReviewHandler _reviewHandler;
    private readonly SettingsHandler _settingsHandler;
    private readonly ImportHandler _importHandler;
    private readonly ConversationStateManager _stateManager;
    private readonly ILogger<UpdateRouter> _logger;

    public UpdateRouter(
        ITelegramBotClient bot,
        OnboardingHandler onboardingHandler,
        CardCreationHandler cardCreationHandler,
        CardBrowserHandler cardBrowserHandler,
        ReviewHandler reviewHandler,
        SettingsHandler settingsHandler,
        ImportHandler importHandler,
        ConversationStateManager stateManager,
        ILogger<UpdateRouter> logger)
    {
        _bot = bot;
        _onboardingHandler = onboardingHandler;
        _cardCreationHandler = cardCreationHandler;
        _cardBrowserHandler = cardBrowserHandler;
        _reviewHandler = reviewHandler;
        _settingsHandler = settingsHandler;
        _importHandler = importHandler;
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
                case "/settings":
                    await _settingsHandler.HandleSettingsCommandAsync(message, ct);
                    return;
                case "/cards":
                    await _cardBrowserHandler.HandleCardsCommandAsync(message, ct);
                    return;
                case "/import":
                    await _importHandler.HandleImportCommandAsync(message, ct);
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
            // Handle text input during custom reminders step
            if (state.OnboardingStep == OnboardingStep.CustomReminders)
            {
                await _onboardingHandler.HandleCustomRemindersTextAsync(message, state, ct);
                return;
            }
            // During onboarding, ignore other text messages
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

        // Handle custom time input in Settings
        if (state.Mode == ConversationMode.Settings)
        {
            await _settingsHandler.HandleTimeTextInputAsync(message, state, ct);
            return;
        }

        // Handle search query input in card browser
        if (state.Mode == ConversationMode.BrowsingCards &&
            state.CardBrowser?.WaitingForSearchQuery == true)
        {
            await _cardBrowserHandler.HandleSearchTextAsync(message, state, ct);
            return;
        }

        // Handle import mode
        if (state.Mode == ConversationMode.Importing)
        {
            // Handle file upload
            if (message.Document != null)
            {
                await _importHandler.HandleImportFileAsync(message, state, ct);
                return;
            }
            // Handle text/URL input
            if (!string.IsNullOrWhiteSpace(text))
            {
                await _importHandler.HandleImportTextAsync(message, state, ct);
                return;
            }
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

        // Settings callbacks
        if (data.StartsWith("settings:"))
        {
            await _settingsHandler.HandleSettingsCallbackAsync(callback, ct);
            return;
        }

        // Card browser callbacks
        if (data.StartsWith("cards:"))
        {
            await _cardBrowserHandler.HandleBrowserCallbackAsync(callback, ct);
            return;
        }

        // Import callbacks
        if (data.StartsWith("import:"))
        {
            await _importHandler.HandleImportCallbackAsync(callback, ct);
            return;
        }

        // Help menu callbacks
        if (data.StartsWith("help:"))
        {
            await HandleHelpCallbackAsync(callback, data, ct);
            return;
        }

        await _bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
    }

    private async Task HandleHelpCallbackAsync(CallbackQuery callback, string data, CancellationToken ct)
    {
        var chatId = callback.Message!.Chat.Id;
        var userId = callback.From.Id;

        // Create a fake message to reuse existing handlers
        var fakeMessage = new Message
        {
            Chat = callback.Message.Chat,
            From = callback.From,
            Date = DateTime.UtcNow
        };

        switch (data)
        {
            case "help:learn":
                await _reviewHandler.HandleLearnCommandAsync(fakeMessage, ct);
                break;
            case "help:cards":
                await _cardBrowserHandler.HandleCardsCommandAsync(fakeMessage, ct);
                break;
            case "help:import":
                await _importHandler.HandleImportCommandAsync(fakeMessage, ct);
                break;
            case "help:settings":
                await _settingsHandler.HandleSettingsCommandAsync(fakeMessage, ct);
                break;
        }

        await _bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
    }

    private async Task SendHelpAsync(Message message, CancellationToken ct)
    {
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📚 Учить", "help:learn"),
                InlineKeyboardButton.WithCallbackData("🗂 Карточки", "help:cards")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📥 Импорт", "help:import"),
                InlineKeyboardButton.WithCallbackData("⚙️ Настройки", "help:settings")
            }
        });

        await _bot.SendMessage(
            message.Chat.Id,
            "🤖 *Команды бота:*\n\n" +
            "📚 /learn — начать повторение\n" +
            "🗂 /cards — мои карточки и поиск\n" +
            "📥 /import — импорт слов\n" +
            "⚙️ /settings — настройки\n" +
            "❓ /help — эта справка\n\n" +
            "💡 Просто отправь слово — я создам карточку!",
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }
}
