using LearningLanguageBot.Bot.Services;
using LearningLanguageBot.Bot.State;
using LearningLanguageBot.Core.Entities;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace LearningLanguageBot.Bot.Handlers;

public class ReviewHandler
{
    private readonly ITelegramBotClient _bot;
    private readonly CardService _cardService;
    private readonly ReviewService _reviewService;
    private readonly UserService _userService;
    private readonly ConversationStateManager _stateManager;

    public ReviewHandler(
        ITelegramBotClient bot,
        CardService cardService,
        ReviewService reviewService,
        UserService userService,
        ConversationStateManager stateManager)
    {
        _bot = bot;
        _cardService = cardService;
        _reviewService = reviewService;
        _userService = userService;
        _stateManager = stateManager;
    }

    public async Task HandleLearnCommandAsync(Message message, CancellationToken ct)
    {
        var userId = message.From!.Id;
        var dueCount = await _cardService.GetDueCardsCountAsync(userId, ct);
        var (reviewed, goal) = await _userService.GetTodayProgressAsync(userId, ct);

        if (dueCount == 0)
        {
            await _bot.SendMessage(
                message.Chat.Id,
                "📚 Нет карточек на повторение!\n\nОтправь слово, чтобы создать новую карточку.",
                cancellationToken: ct);
            return;
        }

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("▶️ Начать", CallbackData.LearnStart),
                InlineKeyboardButton.WithCallbackData("⏭ Пропустить сегодня", CallbackData.LearnSkip)
            }
        });

        await _bot.SendMessage(
            message.Chat.Id,
            $"📚 Доступно карточек: {dueCount}\n\n" +
            $"Сегодня пройдено: {reviewed}/{goal}",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    public async Task HandleLearnCallbackAsync(CallbackQuery callback, CancellationToken ct)
    {
        var userId = callback.From.Id;
        var data = callback.Data ?? string.Empty;

        if (data == CallbackData.LearnStart)
        {
            await StartReviewSessionAsync(callback, ct);
        }
        else if (data == CallbackData.LearnSkip)
        {
            await _bot.EditMessageText(
                callback.Message!.Chat.Id,
                callback.Message.MessageId,
                "Ок, отдохни! Напомню позже.",
                cancellationToken: ct);
        }

        await _bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
    }

    public async Task StartReviewSessionAsync(CallbackQuery callback, CancellationToken ct)
    {
        var userId = callback.From.Id;
        var user = await _userService.GetUserAsync(userId, ct);
        if (user == null) return;

        var remaining = user.DailyGoal - user.TodayReviewed;
        var cards = await _cardService.GetCardsForReviewAsync(userId, Math.Max(remaining, 5), ct);

        if (cards.Count == 0)
        {
            await _bot.EditMessageText(
                callback.Message!.Chat.Id,
                callback.Message.MessageId,
                "📚 Нет карточек на повторение!",
                cancellationToken: ct);
            return;
        }

        var state = _stateManager.GetOrCreate(userId);
        state.Mode = ConversationMode.Reviewing;
        state.ActiveReview = new ReviewSession
        {
            CardIds = cards.Select(c => c.Id).ToList()
        };
        state.Touch();

        await ShowCurrentCardAsync(callback.Message!.Chat.Id, callback.Message.MessageId, state, ct);
    }

    public async Task StartReviewSessionFromPushAsync(long chatId, long userId, CancellationToken ct)
    {
        var user = await _userService.GetUserAsync(userId, ct);
        if (user == null) return;

        var remaining = user.DailyGoal - user.TodayReviewed;
        var cards = await _cardService.GetCardsForReviewAsync(userId, Math.Max(remaining, 5), ct);

        if (cards.Count == 0) return;

        var state = _stateManager.GetOrCreate(userId);
        state.Mode = ConversationMode.Reviewing;
        state.ActiveReview = new ReviewSession
        {
            CardIds = cards.Select(c => c.Id).ToList()
        };
        state.Touch();

        var card = cards[0];
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("👁 Показать перевод", $"{CallbackData.ReviewReveal}{card.Id}") }
        });

        await _bot.SendMessage(
            chatId,
            $"⏰ Время повторения! ({cards.Count} карточек ждут)\n\n" +
            $"📖 Карточка 1/{cards.Count}\n\n" +
            $"{card.Front}",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    public async Task HandleReviewCallbackAsync(CallbackQuery callback, CancellationToken ct)
    {
        var userId = callback.From.Id;
        var data = callback.Data ?? string.Empty;
        var state = _stateManager.GetOrCreate(userId);

        if (state.ActiveReview == null)
        {
            await _bot.AnswerCallbackQuery(callback.Id, "Сессия истекла", cancellationToken: ct);
            return;
        }

        if (data.StartsWith(CallbackData.ReviewReveal))
        {
            state.ActiveReview.ShowingAnswer = true;
            await ShowAnswerAsync(callback, state, ct);
        }
        else if (data.StartsWith(CallbackData.ReviewKnew))
        {
            await ProcessAnswerAsync(callback, state, knew: true, ct);
        }
        else if (data.StartsWith(CallbackData.ReviewDidNotKnow))
        {
            await ProcessAnswerAsync(callback, state, knew: false, ct);
        }

        await _bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
    }

    private async Task ShowCurrentCardAsync(long chatId, int messageId, UserState state, CancellationToken ct)
    {
        var session = state.ActiveReview!;
        var card = await _cardService.GetCardAsync(session.CurrentCardId, ct);

        if (card == null)
        {
            await AdvanceToNextCardAsync(chatId, messageId, state, ct);
            return;
        }

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("👁 Показать перевод", $"{CallbackData.ReviewReveal}{card.Id}") }
        });

        await _bot.EditMessageText(
            chatId,
            messageId,
            $"📖 Карточка {session.CurrentIndex + 1}/{session.TotalCards}\n\n{card.Front}",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task ShowAnswerAsync(CallbackQuery callback, UserState state, CancellationToken ct)
    {
        var session = state.ActiveReview!;
        var card = await _cardService.GetCardAsync(session.CurrentCardId, ct);

        if (card == null)
        {
            await AdvanceToNextCardAsync(callback.Message!.Chat.Id, callback.Message.MessageId, state, ct);
            return;
        }

        var text = $"{card.Front} — {card.Back}";
        if (card.Examples.Count > 0)
        {
            text += $"\n\n• {card.Examples[0].Original}";
        }

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✗ Не знал", $"{CallbackData.ReviewDidNotKnow}{card.Id}"),
                InlineKeyboardButton.WithCallbackData("✓ Знал", $"{CallbackData.ReviewKnew}{card.Id}")
            }
        });

        await _bot.EditMessageText(
            callback.Message!.Chat.Id,
            callback.Message.MessageId,
            text,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task ProcessAnswerAsync(CallbackQuery callback, UserState state, bool knew, CancellationToken ct)
    {
        var userId = callback.From.Id;
        var session = state.ActiveReview!;

        await _reviewService.ProcessReviewAsync(session.CurrentCardId, knew, ct);
        await _userService.IncrementTodayReviewedAsync(userId, ct);
        await _reviewService.UpdateStatsAfterReviewAsync(userId, knew, ct);

        if (knew)
            session.KnewCount++;
        else
            session.DidNotKnowCount++;

        session.CurrentIndex++;
        session.ShowingAnswer = false;

        await AdvanceToNextCardAsync(callback.Message!.Chat.Id, callback.Message.MessageId, state, ct);
    }

    private async Task AdvanceToNextCardAsync(long chatId, int messageId, UserState state, CancellationToken ct)
    {
        var session = state.ActiveReview!;

        if (session.IsComplete)
        {
            await FinishSessionAsync(chatId, messageId, state, ct);
            return;
        }

        await ShowCurrentCardAsync(chatId, messageId, state, ct);
    }

    private async Task FinishSessionAsync(long chatId, int messageId, UserState state, CancellationToken ct)
    {
        var session = state.ActiveReview!;

        state.Mode = ConversationMode.Normal;
        state.ActiveReview = null;

        await _bot.EditMessageText(
            chatId,
            messageId,
            $"🎉 Сессия завершена!\n\n" +
            $"✓ Знал: {session.KnewCount}\n" +
            $"✗ Повторить: {session.DidNotKnowCount}",
            cancellationToken: ct);
    }
}
