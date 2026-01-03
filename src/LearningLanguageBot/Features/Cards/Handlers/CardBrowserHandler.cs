using LearningLanguageBot.Features.Cards.Services;
using LearningLanguageBot.Infrastructure.State;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace LearningLanguageBot.Features.Cards.Handlers;

public class CardBrowserHandler
{
    private readonly ITelegramBotClient _bot;
    private readonly CardService _cardService;
    private readonly ConversationStateManager _stateManager;
    private const int PageSize = 5;

    public CardBrowserHandler(
        ITelegramBotClient bot,
        CardService cardService,
        ConversationStateManager stateManager)
    {
        _bot = bot;
        _cardService = cardService;
        _stateManager = stateManager;
    }

    public async Task HandleCardsCommandAsync(Message message, CancellationToken ct)
    {
        var userId = message.From!.Id;
        var state = _stateManager.GetOrCreate(userId);

        state.Mode = ConversationMode.BrowsingCards;
        state.CardBrowser = new CardBrowserState
        {
            CurrentPage = 0,
            SearchQuery = null,
            WaitingForSearchQuery = false
        };

        await ShowCardsPageAsync(message.Chat.Id, null, userId, state.CardBrowser, ct);
    }

    public async Task HandleBrowserCallbackAsync(CallbackQuery callback, CancellationToken ct)
    {
        var userId = callback.From.Id;
        var data = callback.Data ?? string.Empty;
        var state = _stateManager.GetOrCreate(userId);
        var browser = state.CardBrowser ?? new CardBrowserState();

        // Navigation
        if (data == "cards:prev")
        {
            browser.CurrentPage = Math.Max(0, browser.CurrentPage - 1);
            await ShowCardsPageAsync(callback.Message!.Chat.Id, callback.Message.MessageId, userId, browser, ct);
        }
        else if (data == "cards:next")
        {
            browser.CurrentPage++;
            await ShowCardsPageAsync(callback.Message!.Chat.Id, callback.Message.MessageId, userId, browser, ct);
        }
        // Search
        else if (data == "cards:search")
        {
            browser.WaitingForSearchQuery = true;
            browser.LastMessageId = callback.Message!.MessageId;

            await _bot.EditMessageText(
                callback.Message.Chat.Id,
                callback.Message.MessageId,
                "Введи слово для поиска:",
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("❌ Отмена", "cards:cancel") }
                }),
                cancellationToken: ct);
        }
        // Clear search
        else if (data == "cards:clear")
        {
            browser.SearchQuery = null;
            browser.CurrentPage = 0;
            await ShowCardsPageAsync(callback.Message!.Chat.Id, callback.Message.MessageId, userId, browser, ct);
        }
        // Cancel search input
        else if (data == "cards:cancel")
        {
            browser.WaitingForSearchQuery = false;
            await ShowCardsPageAsync(callback.Message!.Chat.Id, callback.Message.MessageId, userId, browser, ct);
        }
        // Close browser
        else if (data == "cards:close")
        {
            state.Mode = ConversationMode.Normal;
            state.CardBrowser = null;
            await _bot.DeleteMessage(callback.Message!.Chat.Id, callback.Message.MessageId, ct);
        }
        // View card details
        else if (data.StartsWith("cards:view:"))
        {
            var cardIdStr = data.Replace("cards:view:", "");
            if (Guid.TryParse(cardIdStr, out var cardId))
            {
                await ShowCardDetailsAsync(callback.Message!.Chat.Id, callback.Message.MessageId, cardId, browser, ct);
            }
        }
        // Back to list from card view
        else if (data == "cards:back")
        {
            await ShowCardsPageAsync(callback.Message!.Chat.Id, callback.Message.MessageId, userId, browser, ct);
        }
        // Delete card
        else if (data.StartsWith("cards:delete:"))
        {
            var cardIdStr = data.Replace("cards:delete:", "");
            if (Guid.TryParse(cardIdStr, out var cardId))
            {
                await _cardService.DeleteCardAsync(cardId, ct);
                await ShowCardsPageAsync(callback.Message!.Chat.Id, callback.Message.MessageId, userId, browser, ct);
            }
        }

        await _bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
    }

    public async Task HandleSearchTextAsync(Message message, UserState state, CancellationToken ct)
    {
        var userId = message.From!.Id;
        var browser = state.CardBrowser;

        if (browser == null)
        {
            state.Mode = ConversationMode.Normal;
            return;
        }

        browser.SearchQuery = message.Text?.Trim();
        browser.CurrentPage = 0;
        browser.WaitingForSearchQuery = false;

        // Delete user's search query message
        try
        {
            await _bot.DeleteMessage(message.Chat.Id, message.MessageId, ct);
        }
        catch { /* ignore if can't delete */ }

        // Update the browser message
        if (browser.LastMessageId.HasValue)
        {
            await ShowCardsPageAsync(message.Chat.Id, browser.LastMessageId.Value, userId, browser, ct);
        }
        else
        {
            await ShowCardsPageAsync(message.Chat.Id, null, userId, browser, ct);
        }
    }

    private async Task ShowCardsPageAsync(long chatId, int? messageId, long userId, CardBrowserState browser, CancellationToken ct)
    {
        var (cards, totalCount) = await _cardService.GetUserCardsAsync(
            userId, browser.SearchQuery, browser.CurrentPage, PageSize, ct);

        var totalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
        if (totalPages == 0) totalPages = 1;

        // Adjust page if out of bounds
        if (browser.CurrentPage >= totalPages)
        {
            browser.CurrentPage = Math.Max(0, totalPages - 1);
            (cards, totalCount) = await _cardService.GetUserCardsAsync(
                userId, browser.SearchQuery, browser.CurrentPage, PageSize, ct);
        }

        var searchInfo = !string.IsNullOrEmpty(browser.SearchQuery)
            ? $"Поиск: \"{browser.SearchQuery}\"\n\n"
            : "";

        string text;
        if (totalCount == 0)
        {
            text = searchInfo + (string.IsNullOrEmpty(browser.SearchQuery)
                ? "У тебя пока нет карточек.\nОтправь слово — я создам карточку!"
                : "Карточек не найдено.");
        }
        else
        {
            var cardsList = string.Join("\n", cards.Select((c, i) =>
                $"{browser.CurrentPage * PageSize + i + 1}. {c.Front} — {TruncateText(c.Back, 30)}"));

            text = $"{searchInfo}Твои карточки ({totalCount}):\n\n{cardsList}\n\nСтраница {browser.CurrentPage + 1}/{totalPages}";
        }

        var buttons = new List<List<InlineKeyboardButton>>();

        // Card buttons (one per row)
        foreach (var card in cards)
        {
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData($"{card.Front}", $"cards:view:{card.Id}")
            });
        }

        // Navigation row
        var navRow = new List<InlineKeyboardButton>();
        if (browser.CurrentPage > 0)
            navRow.Add(InlineKeyboardButton.WithCallbackData("◀️", "cards:prev"));
        if (browser.CurrentPage < totalPages - 1)
            navRow.Add(InlineKeyboardButton.WithCallbackData("▶️", "cards:next"));
        if (navRow.Count > 0)
            buttons.Add(navRow);

        // Action row
        var actionRow = new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("🔍 Поиск", "cards:search")
        };
        if (!string.IsNullOrEmpty(browser.SearchQuery))
        {
            actionRow.Add(InlineKeyboardButton.WithCallbackData("❌ Сбросить", "cards:clear"));
        }
        buttons.Add(actionRow);

        // Close row
        buttons.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("✖️ Закрыть", "cards:close")
        });

        var keyboard = new InlineKeyboardMarkup(buttons);

        if (messageId.HasValue)
        {
            await _bot.EditMessageText(chatId, messageId.Value, text, replyMarkup: keyboard, cancellationToken: ct);
        }
        else
        {
            var sent = await _bot.SendMessage(chatId, text, replyMarkup: keyboard, cancellationToken: ct);
            browser.LastMessageId = sent.MessageId;
        }
    }

    private async Task ShowCardDetailsAsync(long chatId, int messageId, Guid cardId, CardBrowserState browser, CancellationToken ct)
    {
        var card = await _cardService.GetCardAsync(cardId, ct);
        if (card == null)
        {
            await ShowCardsPageAsync(chatId, messageId, 0, browser, ct);
            return;
        }

        var examples = card.Examples.Count > 0
            ? "\n\nПримеры:\n" + string.Join("\n", card.Examples.Select(e => $"• {e.Original}\n  {e.Translated}"))
            : "";

        var text = $"📝 {card.Front}\n\n" +
                   $"Перевод: {card.Back}" +
                   examples +
                   $"\n\nСоздана: {card.CreatedAt:dd.MM.yyyy}";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🗑 Удалить", $"cards:delete:{card.Id}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⬅️ Назад", "cards:back")
            }
        });

        await _bot.EditMessageText(chatId, messageId, text, replyMarkup: keyboard, cancellationToken: ct);
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..(maxLength - 3)] + "...";
    }
}
