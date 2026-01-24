using FSRS.Core.Enums;
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
            state.ActiveReview.AnswerStartTime = DateTime.UtcNow;

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
    /// Automatically determines FSRS rating based on match result and response time.
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
        var responseTime = DateTime.UtcNow - (session.AnswerStartTime ?? DateTime.UtcNow);

        session.WaitingForTypedAnswer = false;

        // Auto-determine rating based on match result and response time
        var rating = DetermineRating(matchResult, responseTime);

        switch (matchResult)
        {
            case MatchResult.Exact:
                await HandleExactMatchAsync(message.Chat.Id, card, session, userId, state, rating, ct);
                break;

            case MatchResult.Partial:
                await HandlePartialMatchAsync(message.Chat.Id, card, session, ct);
                break;

            case MatchResult.Wrong:
                await HandleWrongAnswerAsync(message.Chat.Id, card, session, userId, state, ct);
                break;
        }
    }

    /// <summary>
    /// Determines FSRS rating based on match result and response time.
    /// </summary>
    private static Rating DetermineRating(MatchResult match, TimeSpan responseTime)
    {
        return match switch
        {
            MatchResult.Exact when responseTime < TimeSpan.FromSeconds(5) => Rating.Easy,
            MatchResult.Exact => Rating.Good,
            MatchResult.Partial => Rating.Hard,
            MatchResult.Wrong => Rating.Again,
            _ => Rating.Again
        };
    }

    private async Task HandleExactMatchAsync(long chatId, Card card, ReviewSession session, long userId, UserState state, Rating rating, CancellationToken ct)
    {
        await _reviewService.ProcessReviewAsync(session.CurrentCardId, rating, ct);
        await _userService.IncrementTodayReviewedAsync(userId, ct);
        await _reviewService.UpdateStatsAfterReviewAsync(userId, ct);

        session.KnewCount++;
        session.CurrentIndex++;

        var ratingEmoji = rating == Rating.Easy ? "🎯" : "✓";
        var text = $"{ratingEmoji} Верно!\n\n{card.Front} — {card.Back}";
        if (card.Examples.Count > 0)
        {
            text += $"\n\n• {card.Examples[0].Original}";
        }

        await _bot.SendMessage(chatId, text, cancellationToken: ct);

        await AdvanceToNextCardAsync(chatId, null, state, ct);
    }

    private async Task HandlePartialMatchAsync(long chatId, Card card, ReviewSession session, CancellationToken ct)
    {
        // Show buttons for user to decide: count as Good or Again
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✓ Засчитать (Good)", $"{CallbackData.ReviewCountAsGood}{card.Id}"),
                InlineKeyboardButton.WithCallbackData("✗ Не засчитывать", $"{CallbackData.ReviewCountAsAgain}{card.Id}")
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

        await _reviewService.ProcessReviewAsync(session.CurrentCardId, Rating.Again, ct);
        await _userService.IncrementTodayReviewedAsync(userId, ct);
        await _reviewService.UpdateStatsAfterReviewAsync(userId, ct);

        session.DidNotKnowCount++;
        session.CurrentIndex++;

        // Generate memory hint with phonetic association
        var hint = await _memoryHintService.GetOrGenerateHintAsync(card.Id, ct);
        var text = $"✗ Неверно\n\n*{card.Front}* — {card.Back}\n\n{hint}";

        // Delete loading message
        await _bot.DeleteMessage(chatId, loadingMsg.MessageId, cancellationToken: ct);

        // Send hint
        await _bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, cancellationToken: ct);

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
        else if (data.StartsWith(CallbackData.ReviewAgain))
        {
            await ProcessAnswerAsync(callback, state, Rating.Again, ct);
        }
        else if (data.StartsWith(CallbackData.ReviewHard))
        {
            await ProcessAnswerAsync(callback, state, Rating.Hard, ct);
        }
        else if (data.StartsWith(CallbackData.ReviewGood))
        {
            await ProcessAnswerAsync(callback, state, Rating.Good, ct);
        }
        else if (data.StartsWith(CallbackData.ReviewEasy))
        {
            await ProcessAnswerAsync(callback, state, Rating.Easy, ct);
        }
        else if (data.StartsWith(CallbackData.ReviewCountAsGood))
        {
            await ProcessTypingAnswerAsync(callback, state, Rating.Good, ct);
        }
        else if (data.StartsWith(CallbackData.ReviewCountAsAgain))
        {
            await ProcessTypingAnswerAsync(callback, state, Rating.Again, ct);
        }
        else if (data.StartsWith(CallbackData.ReviewDontRemember))
        {
            await DontRememberCardAsync(callback, state, ct);
        }

        await _bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
    }

    private async Task ProcessTypingAnswerAsync(CallbackQuery callback, UserState state, Rating rating, CancellationToken ct)
    {
        var userId = callback.From.Id;
        var session = state.ActiveReview!;

        await _reviewService.ProcessReviewAsync(session.CurrentCardId, rating, ct);
        await _userService.IncrementTodayReviewedAsync(userId, ct);
        await _reviewService.UpdateStatsAfterReviewAsync(userId, ct);

        if (rating >= Rating.Good)
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
        var chatId = callback.Message!.Chat.Id;

        // Show loading state
        await _bot.EditMessageText(
            chatId,
            callback.Message.MessageId,
            "🔄 Генерирую подсказку для запоминания...",
            replyMarkup: null,
            cancellationToken: ct);

        // Treat as "didn't know" (Again)
        await _reviewService.ProcessReviewAsync(cardId, Rating.Again, ct);
        await _userService.IncrementTodayReviewedAsync(userId, ct);
        await _reviewService.UpdateStatsAfterReviewAsync(userId, ct);

        session.DidNotKnowCount++;
        session.CurrentIndex++;
        session.WaitingForTypedAnswer = false;

        var card = await _cardService.GetCardAsync(cardId, ct);
        if (card != null)
        {
            // Generate memory hint with phonetic association
            var hint = await _memoryHintService.GetOrGenerateHintAsync(cardId, ct);
            var text = $"🤔 Не помню\n\n*{card.Front}* — {card.Back}\n\n{hint}";

            // Delete loading message
            await _bot.DeleteMessage(chatId, callback.Message.MessageId, cancellationToken: ct);

            // Send hint
            await _bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, cancellationToken: ct);
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
            session.AnswerStartTime = DateTime.UtcNow;

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

    /// <summary>
    /// Shows answer with 4 FSRS rating buttons including interval previews.
    /// </summary>
    private async Task ShowAnswerAsync(CallbackQuery callback, UserState state, CancellationToken ct)
    {
        var session = state.ActiveReview!;
        var card = await _cardService.GetCardAsync(session.CurrentCardId, ct);

        if (card == null)
        {
            await AdvanceToNextCardAsync(callback.Message!.Chat.Id, callback.Message.MessageId, state, ct);
            return;
        }

        // Get next intervals for all ratings
        var intervals = await _reviewService.GetNextIntervalsAsync(session.CurrentCardId, ct);

        var text = $"{card.Front} — {card.Back}";
        if (card.Examples.Count > 0)
        {
            text += $"\n\n• {card.Examples[0].Original}";
        }

        // Build 4-button keyboard with interval previews
        // Format: 🔄 Снова (1м) | 😓 Трудно (6м)
        //         👍 Хорошо (1д) | 🎯 Легко (4д)
        var againInterval = FsrsService.FormatInterval(intervals.GetValueOrDefault(Rating.Again, TimeSpan.FromMinutes(1)));
        var hardInterval = FsrsService.FormatInterval(intervals.GetValueOrDefault(Rating.Hard, TimeSpan.FromMinutes(6)));
        var goodInterval = FsrsService.FormatInterval(intervals.GetValueOrDefault(Rating.Good, TimeSpan.FromDays(1)));
        var easyInterval = FsrsService.FormatInterval(intervals.GetValueOrDefault(Rating.Easy, TimeSpan.FromDays(4)));

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData($"🔄 Снова ({againInterval})", $"{CallbackData.ReviewAgain}{card.Id}"),
                InlineKeyboardButton.WithCallbackData($"😓 Трудно ({hardInterval})", $"{CallbackData.ReviewHard}{card.Id}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData($"👍 Хорошо ({goodInterval})", $"{CallbackData.ReviewGood}{card.Id}"),
                InlineKeyboardButton.WithCallbackData($"🎯 Легко ({easyInterval})", $"{CallbackData.ReviewEasy}{card.Id}")
            }
        });

        await _bot.EditMessageText(
            callback.Message!.Chat.Id,
            callback.Message.MessageId,
            text,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task ProcessAnswerAsync(CallbackQuery callback, UserState state, Rating rating, CancellationToken ct)
    {
        var userId = callback.From.Id;
        var session = state.ActiveReview!;

        await _reviewService.ProcessReviewAsync(session.CurrentCardId, rating, ct);
        await _userService.IncrementTodayReviewedAsync(userId, ct);
        await _reviewService.UpdateStatsAfterReviewAsync(userId, ct);

        // Count as "knew" if rating >= Good
        if (rating >= Rating.Good)
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
