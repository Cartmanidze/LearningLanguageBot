using LearningLanguageBot.Features.Cards.Services;
using LearningLanguageBot.Infrastructure.Constants;
using LearningLanguageBot.Infrastructure.Database.Models;
using LearningLanguageBot.Infrastructure.State;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace LearningLanguageBot.Features.Cards.Handlers;

public class CardCreationHandler
{
    private readonly ITelegramBotClient _bot;
    private readonly CardService _cardService;
    private readonly ConversationStateManager _stateManager;

    public CardCreationHandler(
        ITelegramBotClient bot,
        CardService cardService,
        ConversationStateManager stateManager)
    {
        _bot = bot;
        _cardService = cardService;
        _stateManager = stateManager;
    }

    public async Task HandleTextAsync(Message message, CancellationToken ct)
    {
        var userId = message.From!.Id;
        var text = message.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(text)) return;

        var state = _stateManager.GetOrCreate(userId);

        // Check if we're editing a card
        if (state.Mode == ConversationMode.EditingCard && state.EditingCardId.HasValue)
        {
            await HandleEditInputAsync(message, state, ct);
            return;
        }

        // Create new card
        await CreateCardAsync(message, text, ct);
    }

    private async Task CreateCardAsync(Message message, string text, CancellationToken ct)
    {
        var userId = message.From!.Id;

        var processingMsg = await _bot.SendMessage(
            message.Chat.Id,
            "🔄 Создаю карточку...",
            cancellationToken: ct);

        try
        {
            var (card, isDuplicate) = await _cardService.CreateCardFromTextAsync(userId, text, ct);

            if (card == null)
            {
                await _bot.EditMessageText(
                    message.Chat.Id,
                    processingMsg.MessageId,
                    "❌ Не удалось создать карточку. Попробуй /start для настройки.",
                    cancellationToken: ct);
                return;
            }

            var response = isDuplicate
                ? $"📌 Такая карточка уже есть:\n\n{FormatCard(card)}"
                : $"📝 Карточка создана\n\n{FormatCard(card)}";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("✏️ Редактировать", $"{CallbackData.CardEdit}{card.Id}") }
            });

            await _bot.EditMessageText(
                message.Chat.Id,
                processingMsg.MessageId,
                response,
                replyMarkup: keyboard,
                cancellationToken: ct);
        }
        catch (Exception)
        {
            await _bot.EditMessageText(
                message.Chat.Id,
                processingMsg.MessageId,
                "❌ Ошибка при создании карточки. Попробуй позже.",
                cancellationToken: ct);
        }
    }

    public async Task HandleEditCallbackAsync(CallbackQuery callback, CancellationToken ct)
    {
        var data = callback.Data ?? string.Empty;
        var userId = callback.From.Id;
        var state = _stateManager.GetOrCreate(userId);

        if (data.StartsWith(CallbackData.CardEdit))
        {
            var cardIdStr = CallbackData.ParseCardId(data, CallbackData.CardEdit);
            if (Guid.TryParse(cardIdStr, out var cardId))
            {
                await ShowEditMenuAsync(callback, cardId, ct);
            }
        }
        else if (data.StartsWith(CallbackData.CardEditTranslation))
        {
            var cardIdStr = CallbackData.ParseCardId(data, CallbackData.CardEditTranslation);
            if (Guid.TryParse(cardIdStr, out var cardId))
            {
                state.Mode = ConversationMode.EditingCard;
                state.EditingCardId = cardId;
                state.EditAction = EditAction.Translation;

                var card = await _cardService.GetCardAsync(cardId, ct);
                await _bot.SendMessage(
                    callback.Message!.Chat.Id,
                    $"Текущий перевод: {card?.Back}\n\nОтправь новый перевод:",
                    cancellationToken: ct);
            }
        }
        else if (data.StartsWith(CallbackData.CardDelete))
        {
            var cardIdStr = CallbackData.ParseCardId(data, CallbackData.CardDelete);
            if (Guid.TryParse(cardIdStr, out var cardId))
            {
                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("✓ Да, удалить", $"{CallbackData.CardDeleteConfirm}{cardId}"),
                        InlineKeyboardButton.WithCallbackData("✗ Отмена", $"{CallbackData.CardDeleteCancel}{cardId}")
                    }
                });

                await _bot.EditMessageText(
                    callback.Message!.Chat.Id,
                    callback.Message.MessageId,
                    "Удалить карточку?",
                    replyMarkup: keyboard,
                    cancellationToken: ct);
            }
        }
        else if (data.StartsWith(CallbackData.CardDeleteConfirm))
        {
            var cardIdStr = CallbackData.ParseCardId(data, CallbackData.CardDeleteConfirm);
            if (Guid.TryParse(cardIdStr, out var cardId))
            {
                await _cardService.DeleteCardAsync(cardId, ct);
                await _bot.EditMessageText(
                    callback.Message!.Chat.Id,
                    callback.Message.MessageId,
                    "✓ Карточка удалена",
                    cancellationToken: ct);
            }
        }
        else if (data.StartsWith(CallbackData.CardDeleteCancel))
        {
            var cardIdStr = CallbackData.ParseCardId(data, CallbackData.CardDeleteCancel);
            if (Guid.TryParse(cardIdStr, out var cardId))
            {
                var card = await _cardService.GetCardAsync(cardId, ct);
                if (card != null)
                {
                    var keyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("✏️ Редактировать", $"{CallbackData.CardEdit}{card.Id}") }
                    });

                    await _bot.EditMessageText(
                        callback.Message!.Chat.Id,
                        callback.Message.MessageId,
                        FormatCard(card),
                        replyMarkup: keyboard,
                        cancellationToken: ct);
                }
            }
        }

        await _bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
    }

    private async Task ShowEditMenuAsync(CallbackQuery callback, Guid cardId, CancellationToken ct)
    {
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📝 Перевод", $"{CallbackData.CardEditTranslation}{cardId}"),
                InlineKeyboardButton.WithCallbackData("🗑 Удалить", $"{CallbackData.CardDelete}{cardId}")
            }
        });

        await _bot.EditMessageText(
            callback.Message!.Chat.Id,
            callback.Message.MessageId,
            "Что изменить?",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task HandleEditInputAsync(Message message, UserState state, CancellationToken ct)
    {
        var text = message.Text?.Trim() ?? string.Empty;

        if (state.EditAction == EditAction.Translation && state.EditingCardId.HasValue)
        {
            await _cardService.UpdateCardTranslationAsync(state.EditingCardId.Value, text, ct);

            state.Mode = ConversationMode.Normal;
            state.EditingCardId = null;
            state.EditAction = null;

            await _bot.SendMessage(
                message.Chat.Id,
                "✓ Перевод обновлён",
                cancellationToken: ct);
        }
    }

    private static string FormatCard(Card card)
    {
        var result = $"{card.Front} — {card.Back}";

        if (card.Examples.Count > 0)
        {
            result += "\n\n📚 Примеры:";
            foreach (var ex in card.Examples.Take(3))
            {
                result += $"\n• {ex.Original} — {ex.Translated}";
            }
        }

        return result;
    }
}
