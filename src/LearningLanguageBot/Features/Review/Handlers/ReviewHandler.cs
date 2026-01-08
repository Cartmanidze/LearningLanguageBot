using LearningLanguageBot.Features.Cards.Services;
using LearningLanguageBot.Features.Onboarding.Services;
using LearningLanguageBot.Features.Review.Services;
using LearningLanguageBot.Infrastructure.Constants;
using LearningLanguageBot.Infrastructure.Database.Models;
using LearningLanguageBot.Infrastructure.State;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace LearningLanguageBot.Features.Review.Handlers;

public class ReviewHandler
{
    private readonly ITelegramBotClient _bot;
    private readonly CardService _cardService;
    private readonly ReviewService _reviewService;
    private readonly UserService _userService;
    private readonly ConversationStateManager _stateManager;
    private readonly MemoryHintService _memoryHintService;

    public ReviewHandler(
        ITelegramBotClient bot,
        CardService cardService,
        ReviewService reviewService,
        UserService userService,
        ConversationStateManager stateManager,
        MemoryHintService memoryHintService)
    {
        _bot = bot;
        _cardService = cardService;
        _reviewService = reviewService;
        _userService = userService;
        _stateManager = stateManager;
        _memoryHintService = memoryHintService;
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
            UserId = userId,
            CardIds = cards.Select(c => c.Id).ToList()
        };
        state.Touch();

        await ShowCurrentCardAsync(callback.Message!.Chat.Id, callback.Message.MessageId, state, user.ReviewMode, ct);
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
            UserId = userId,
            CardIds = cards.Select(c => c.Id).ToList()
        };
        state.Touch();

        var card = cards[0];

        if (user.ReviewMode == ReviewMode.Typing)
        {
            // Typing mode: wait for user input
            state.ActiveReview.WaitingForTypedAnswer = true;

            var dontRememberKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🤔 Не помню", $"{CallbackData.ReviewDontRemember}{card.Id}") }
            });

            var msg = await _bot.SendMessage(
                chatId,
                $"⏰ Время повторения! ({cards.Count} карточек ждут)\n\n" +
                $"📖 Карточка 1/{cards.Count}\n\n" +
                $"{card.Front}\n\n" +
                "Напиши перевод:",
                replyMarkup: dontRememberKeyboard,
                cancellationToken: ct);

            state.ActiveReview.LastMessageId = msg.MessageId;
        }
        else
        {
            // Reveal mode: show reveal button
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("👁 Показать перевод", $"{CallbackData.ReviewReveal}{card.Id}") }
            });

            var msg = await _bot.SendMessage(
                chatId,
                $"⏰ Время повторения! ({cards.Count} карточек ждут)\n\n" +
                $"📖 Карточка 1/{cards.Count}\n\n" +
                $"{card.Front}",
                replyMarkup: keyboard,
                cancellationToken: ct);

            state.ActiveReview.LastMessageId = msg.MessageId;
        }
    }

    /// <summary>
    /// Handles typed answer during Typing mode review.
    /// </summary>
    public async Task HandleTypedAnswerAsync(Message message, UserState state, CancellationToken ct)
    {
        var userId = message.From!.Id;
        var session = state.ActiveReview;

        if (session == null || !session.WaitingForTypedAnswer)
            return;

        var card = await _cardService.GetCardAsync(session.CurrentCardId, ct);
        if (card == null)
        {
            await AdvanceToNextCardAsync(message.Chat.Id, null, state, ct);
            return;
        }

        var userAnswer = message.Text ?? string.Empty;
        var matchResult = AnswerMatcher.Compare(userAnswer, card.Back);

        session.WaitingForTypedAnswer = false;

        switch (matchResult)
        {
            case MatchResult.Exact:
                await HandleExactMatchAsync(message.Chat.Id, card, session, userId, state, ct);
                break;

            case MatchResult.Partial:
                await HandlePartialMatchAsync(message.Chat.Id, card, session, ct);
                break;

            case MatchResult.Wrong:
                await HandleWrongAnswerAsync(message.Chat.Id, card, session, userId, state, ct);
                break;
        }
    }

    private async Task HandleExactMatchAsync(long chatId, Card card, ReviewSession session, long userId, UserState state, CancellationToken ct)
    {
        await _reviewService.ProcessReviewAsync(session.CurrentCardId, knew: true, ct);
        await _userService.IncrementTodayReviewedAsync(userId, ct);
        await _reviewService.UpdateStatsAfterReviewAsync(userId, knew: true, ct);

        session.KnewCount++;
        session.CurrentIndex++;

        var text = $"✓ Верно!\n\n{card.Front} — {card.Back}";
        if (card.Examples.Count > 0)
        {
            text += $"\n\n• {card.Examples[0].Original}";
        }

        await _bot.SendMessage(chatId, text, cancellationToken: ct);

        await AdvanceToNextCardAsync(chatId, null, state, ct);
    }

    private async Task HandlePartialMatchAsync(long chatId, Card card, ReviewSession session, CancellationToken ct)
    {
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✓ Засчитать", $"{CallbackData.ReviewCountAsKnew}{card.Id}"),
                InlineKeyboardButton.WithCallbackData("✗ Не засчитывать", $"{CallbackData.ReviewCountAsDidNotKnow}{card.Id}")
            }
        });

        var text = $"≈ Почти!\n\n{card.Front} — {card.Back}";
        if (card.Examples.Count > 0)
        {
            text += $"\n\n• {card.Examples[0].Original}";
        }

        var msg = await _bot.SendMessage(chatId, text, replyMarkup: keyboard, cancellationToken: ct);
        session.LastMessageId = msg.MessageId;
    }

    private async Task HandleWrongAnswerAsync(long chatId, Card card, ReviewSession session, long userId, UserState state, CancellationToken ct)
    {
        // Show loading message
        var loadingMsg = await _bot.SendMessage(chatId, "🔄 Генерирую подсказку для запоминания...", cancellationToken: ct);

        await _reviewService.ProcessReviewAsync(session.CurrentCardId, knew: false, ct);
        await _userService.IncrementTodayReviewedAsync(userId, ct);
        await _reviewService.UpdateStatsAfterReviewAsync(userId, knew: false, ct);

        session.DidNotKnowCount++;
        session.CurrentIndex++;

        // Generate memory hint
        var hint = await _memoryHintService.GetOrGenerateHintAsync(card.Id, ct);

        var text = $"✗ Неверно\n\n**{card.Front}** — {card.Back}\n\n{hint}";

        await _bot.EditMessageText(chatId, loadingMsg.MessageId, text, parseMode: ParseMode.Markdown, cancellationToken: ct);

        // Delay so user can read the hint
        await Task.Delay(3000, ct);

        await AdvanceToNextCardAsync(chatId, null, state, ct);
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
        else if (data.StartsWith(CallbackData.ReviewCountAsKnew))
        {
            await ProcessTypingAnswerAsync(callback, state, knew: true, ct);
        }
        else if (data.StartsWith(CallbackData.ReviewCountAsDidNotKnow))
        {
            await ProcessTypingAnswerAsync(callback, state, knew: false, ct);
        }
        else if (data.StartsWith(CallbackData.ReviewDontRemember))
        {
            await DontRememberCardAsync(callback, state, ct);
        }

        await _bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
    }

    private async Task ProcessTypingAnswerAsync(CallbackQuery callback, UserState state, bool knew, CancellationToken ct)
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

        // Remove buttons from previous message
        await _bot.EditMessageReplyMarkup(
            callback.Message!.Chat.Id,
            callback.Message.MessageId,
            replyMarkup: null,
            cancellationToken: ct);

        await AdvanceToNextCardAsync(callback.Message.Chat.Id, null, state, ct);
    }

    private async Task DontRememberCardAsync(CallbackQuery callback, UserState state, CancellationToken ct)
    {
        var userId = callback.From.Id;
        var session = state.ActiveReview!;
        var cardId = session.CurrentCardId;

        // Show loading state
        await _bot.EditMessageText(
            callback.Message!.Chat.Id,
            callback.Message.MessageId,
            "🔄 Генерирую подсказку для запоминания...",
            replyMarkup: null,
            cancellationToken: ct);

        // Treat as "didn't know"
        await _reviewService.ProcessReviewAsync(cardId, knew: false, ct);
        await _userService.IncrementTodayReviewedAsync(userId, ct);
        await _reviewService.UpdateStatsAfterReviewAsync(userId, knew: false, ct);

        session.DidNotKnowCount++;
        session.CurrentIndex++;
        session.WaitingForTypedAnswer = false;

        var card = await _cardService.GetCardAsync(cardId, ct);
        if (card != null)
        {
            // Generate or get cached memory hint
            var hint = await _memoryHintService.GetOrGenerateHintAsync(cardId, ct);

            var text = $"🤔 Не помню\n\n**{card.Front}** — {card.Back}\n\n{hint}";

            await _bot.EditMessageText(
                callback.Message.Chat.Id,
                callback.Message.MessageId,
                text,
                parseMode: ParseMode.Markdown,
                replyMarkup: null,
                cancellationToken: ct);
        }

        // Small delay so user can read the hint
        await Task.Delay(3000, ct);

        await AdvanceToNextCardAsync(callback.Message.Chat.Id, null, state, ct);
    }

    private async Task ShowCurrentCardAsync(long chatId, int? messageId, UserState state, ReviewMode reviewMode, CancellationToken ct)
    {
        var session = state.ActiveReview!;
        var card = await _cardService.GetCardAsync(session.CurrentCardId, ct);

        if (card == null)
        {
            await AdvanceToNextCardAsync(chatId, messageId, state, ct);
            return;
        }

        if (reviewMode == ReviewMode.Typing)
        {
            // Typing mode: ask for input
            session.WaitingForTypedAnswer = true;

            var dontRememberKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🤔 Не помню", $"{CallbackData.ReviewDontRemember}{card.Id}") }
            });

            var text = $"📖 Карточка {session.CurrentIndex + 1}/{session.TotalCards}\n\n{card.Front}\n\nНапиши перевод:";

            if (messageId.HasValue)
            {
                await _bot.EditMessageText(chatId, messageId.Value, text, replyMarkup: dontRememberKeyboard, cancellationToken: ct);
                session.LastMessageId = messageId.Value;
            }
            else
            {
                var msg = await _bot.SendMessage(chatId, text, replyMarkup: dontRememberKeyboard, cancellationToken: ct);
                session.LastMessageId = msg.MessageId;
            }
        }
        else
        {
            // Reveal mode: show reveal button
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("👁 Показать перевод", $"{CallbackData.ReviewReveal}{card.Id}") }
            });

            var text = $"📖 Карточка {session.CurrentIndex + 1}/{session.TotalCards}\n\n{card.Front}";

            if (messageId.HasValue)
            {
                await _bot.EditMessageText(chatId, messageId.Value, text, replyMarkup: keyboard, cancellationToken: ct);
                session.LastMessageId = messageId.Value;
            }
            else
            {
                var msg = await _bot.SendMessage(chatId, text, replyMarkup: keyboard, cancellationToken: ct);
                session.LastMessageId = msg.MessageId;
            }
        }
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

    private async Task AdvanceToNextCardAsync(long chatId, int? messageId, UserState state, CancellationToken ct)
    {
        var session = state.ActiveReview!;

        if (session.IsComplete)
        {
            await FinishSessionAsync(chatId, messageId, state, ct);
            return;
        }

        var user = await _userService.GetUserAsync(session.UserId, ct);
        var reviewMode = user?.ReviewMode ?? ReviewMode.Reveal;

        await ShowCurrentCardAsync(chatId, messageId, state, reviewMode, ct);
    }

    private async Task FinishSessionAsync(long chatId, int? messageId, UserState state, CancellationToken ct)
    {
        var session = state.ActiveReview!;

        state.Mode = ConversationMode.Normal;
        state.ActiveReview = null;

        var text = $"🎉 Сессия завершена!\n\n" +
                   $"✓ Знал: {session.KnewCount}\n" +
                   $"✗ Повторить: {session.DidNotKnowCount}";

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
