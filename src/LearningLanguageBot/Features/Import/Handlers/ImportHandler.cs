using LearningLanguageBot.Features.Cards.Services;
using LearningLanguageBot.Features.Import.Services;
using LearningLanguageBot.Features.Onboarding.Services;
using LearningLanguageBot.Infrastructure.State;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace LearningLanguageBot.Features.Import.Handlers;

public class ImportHandler
{
    private readonly ITelegramBotClient _bot;
    private readonly WordExtractorService _wordExtractor;
    private readonly ContentFetcherService _contentFetcher;
    private readonly CardService _cardService;
    private readonly UserService _userService;
    private readonly ConversationStateManager _stateManager;
    private readonly ILogger<ImportHandler> _logger;

    private const int DefaultWordsToExtract = 10;

    public ImportHandler(
        ITelegramBotClient bot,
        WordExtractorService wordExtractor,
        ContentFetcherService contentFetcher,
        CardService cardService,
        UserService userService,
        ConversationStateManager stateManager,
        ILogger<ImportHandler> logger)
    {
        _bot = bot;
        _wordExtractor = wordExtractor;
        _contentFetcher = contentFetcher;
        _cardService = cardService;
        _userService = userService;
        _stateManager = stateManager;
        _logger = logger;
    }

    public async Task HandleImportCommandAsync(Message message, CancellationToken ct)
    {
        var state = _stateManager.GetOrCreate(message.From!.Id);
        state.Mode = ConversationMode.Importing;
        state.ImportState = new ImportState();

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🔗 Ссылка", "import:url"),
                InlineKeyboardButton.WithCallbackData("📝 Текст", "import:text")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📄 Файл", "import:file"),
                InlineKeyboardButton.WithCallbackData("🎵 Песня", "import:song")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("❌ Отмена", "import:cancel")
            }
        });

        await _bot.SendMessage(
            message.Chat.Id,
            "📥 Импорт слов\n\n" +
            "Выбери источник:\n\n" +
            "🔗 Ссылка — статья, новость\n" +
            "📝 Текст — вставь текст сюда\n" +
            "📄 Файл — отправь .txt файл\n" +
            "🎵 Песня — ссылка на текст песни",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    public async Task HandleImportCallbackAsync(CallbackQuery callback, CancellationToken ct)
    {
        var userId = callback.From.Id;
        var data = callback.Data ?? string.Empty;
        var state = _stateManager.GetOrCreate(userId);
        var importState = state.ImportState ?? new ImportState();

        if (data == "import:cancel")
        {
            state.Mode = ConversationMode.Normal;
            state.ImportState = null;
            await _bot.EditMessageText(
                callback.Message!.Chat.Id,
                callback.Message.MessageId,
                "❌ Импорт отменён",
                replyMarkup: null,
                cancellationToken: ct);
        }
        else if (data == "import:url")
        {
            importState.Source = ImportSource.Url;
            importState.WaitingForInput = true;
            state.ImportState = importState;

            await _bot.EditMessageText(
                callback.Message!.Chat.Id,
                callback.Message.MessageId,
                "🔗 Отправь ссылку на статью:",
                replyMarkup: CancelKeyboard(),
                cancellationToken: ct);
        }
        else if (data == "import:text")
        {
            importState.Source = ImportSource.Text;
            importState.WaitingForInput = true;
            state.ImportState = importState;

            await _bot.EditMessageText(
                callback.Message!.Chat.Id,
                callback.Message.MessageId,
                "📝 Отправь текст для извлечения слов:",
                replyMarkup: CancelKeyboard(),
                cancellationToken: ct);
        }
        else if (data == "import:file")
        {
            importState.Source = ImportSource.File;
            importState.WaitingForInput = true;
            state.ImportState = importState;

            await _bot.EditMessageText(
                callback.Message!.Chat.Id,
                callback.Message.MessageId,
                "📄 Отправь текстовый файл (.txt):",
                replyMarkup: CancelKeyboard(),
                cancellationToken: ct);
        }
        else if (data == "import:song")
        {
            importState.Source = ImportSource.Song;
            importState.WaitingForInput = true;
            state.ImportState = importState;

            await _bot.EditMessageText(
                callback.Message!.Chat.Id,
                callback.Message.MessageId,
                "🎵 Отправь ссылку на текст песни:\n\n" +
                "Поддерживаются: Genius, AZLyrics, Lyrics.com",
                replyMarkup: CancelKeyboard(),
                cancellationToken: ct);
        }
        else if (data == "import:confirm")
        {
            await CreateCardsFromExtractedWordsAsync(callback, state, ct);
        }
        else if (data == "import:more")
        {
            await ExtractMoreWordsAsync(callback, state, ct);
        }
        else if (data.StartsWith("import:remove:"))
        {
            var indexStr = data.Replace("import:remove:", "");
            if (int.TryParse(indexStr, out var index) && importState.ExtractedWords != null)
            {
                if (index >= 0 && index < importState.ExtractedWords.Count)
                {
                    importState.ExtractedWords.RemoveAt(index);
                    await ShowExtractedWordsAsync(callback.Message!.Chat.Id, callback.Message.MessageId, state, ct);
                }
            }
        }

        await _bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
    }

    public async Task HandleImportTextAsync(Message message, UserState state, CancellationToken ct)
    {
        var importState = state.ImportState;
        if (importState == null || !importState.WaitingForInput)
        {
            state.Mode = ConversationMode.Normal;
            return;
        }

        var text = message.Text ?? string.Empty;
        importState.WaitingForInput = false;

        // Handle URL input
        if (importState.Source is ImportSource.Url or ImportSource.Song)
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out _))
            {
                await _bot.SendMessage(
                    message.Chat.Id,
                    "❌ Некорректная ссылка. Попробуй ещё раз:",
                    replyMarkup: CancelKeyboard(),
                    cancellationToken: ct);
                importState.WaitingForInput = true;
                return;
            }

            var loadingMsg = await _bot.SendMessage(
                message.Chat.Id,
                "⏳ Загружаю контент...",
                cancellationToken: ct);

            var content = await _contentFetcher.FetchContentAsync(text, ct);
            if (!content.Success)
            {
                await _bot.EditMessageText(
                    message.Chat.Id,
                    loadingMsg.MessageId,
                    $"❌ {content.Error}",
                    cancellationToken: ct);
                state.Mode = ConversationMode.Normal;
                state.ImportState = null;
                return;
            }

            importState.SourceText = content.Text;
            importState.SourceTitle = content.Title;

            await _bot.EditMessageText(
                message.Chat.Id,
                loadingMsg.MessageId,
                $"✅ Загружено: {content.Title}\n\n⏳ Извлекаю слова...",
                cancellationToken: ct);

            await ExtractAndShowWordsAsync(message.Chat.Id, loadingMsg.MessageId, state, ct);
        }
        // Handle plain text input
        else if (importState.Source == ImportSource.Text)
        {
            if (text.Length < 50)
            {
                await _bot.SendMessage(
                    message.Chat.Id,
                    "❌ Текст слишком короткий. Отправь минимум 50 символов:",
                    replyMarkup: CancelKeyboard(),
                    cancellationToken: ct);
                importState.WaitingForInput = true;
                return;
            }

            importState.SourceText = text;
            importState.SourceTitle = "Текст";

            var loadingMsg = await _bot.SendMessage(
                message.Chat.Id,
                "⏳ Извлекаю слова...",
                cancellationToken: ct);

            await ExtractAndShowWordsAsync(message.Chat.Id, loadingMsg.MessageId, state, ct);
        }
    }

    public async Task HandleImportFileAsync(Message message, UserState state, CancellationToken ct)
    {
        var importState = state.ImportState;
        if (importState == null || importState.Source != ImportSource.File)
        {
            return;
        }

        var document = message.Document;
        if (document == null)
        {
            await _bot.SendMessage(
                message.Chat.Id,
                "❌ Отправь текстовый файл (.txt)",
                cancellationToken: ct);
            return;
        }

        if (!document.FileName?.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) == true)
        {
            await _bot.SendMessage(
                message.Chat.Id,
                "❌ Поддерживаются только .txt файлы",
                cancellationToken: ct);
            return;
        }

        var loadingMsg = await _bot.SendMessage(
            message.Chat.Id,
            "⏳ Загружаю файл...",
            cancellationToken: ct);

        try
        {
            var file = await _bot.GetFile(document.FileId, ct);
            using var stream = new MemoryStream();
            await _bot.DownloadFile(file.FilePath!, stream, ct);
            stream.Position = 0;

            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync(ct);

            if (text.Length < 50)
            {
                await _bot.EditMessageText(
                    message.Chat.Id,
                    loadingMsg.MessageId,
                    "❌ Файл слишком короткий",
                    cancellationToken: ct);
                state.Mode = ConversationMode.Normal;
                state.ImportState = null;
                return;
            }

            importState.SourceText = text;
            importState.SourceTitle = document.FileName ?? "Файл";
            importState.WaitingForInput = false;

            await _bot.EditMessageText(
                message.Chat.Id,
                loadingMsg.MessageId,
                $"✅ Файл загружен\n\n⏳ Извлекаю слова...",
                cancellationToken: ct);

            await ExtractAndShowWordsAsync(message.Chat.Id, loadingMsg.MessageId, state, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process file");
            await _bot.EditMessageText(
                message.Chat.Id,
                loadingMsg.MessageId,
                "❌ Ошибка при обработке файла",
                cancellationToken: ct);
            state.Mode = ConversationMode.Normal;
            state.ImportState = null;
        }
    }

    private async Task ExtractAndShowWordsAsync(long chatId, int messageId, UserState state, CancellationToken ct)
    {
        var importState = state.ImportState!;

        var words = await _wordExtractor.ExtractWordsAsync(
            importState.SourceText!,
            "intermediate",
            DefaultWordsToExtract,
            ct);

        if (words.Count == 0)
        {
            await _bot.EditMessageText(
                chatId,
                messageId,
                "❌ Не удалось извлечь слова. Попробуй другой текст.",
                cancellationToken: ct);
            state.Mode = ConversationMode.Normal;
            state.ImportState = null;
            return;
        }

        importState.ExtractedWords = words.Select(w => new ExtractedWordState
        {
            Word = w.Word,
            Context = w.Context
        }).ToList();
        await ShowExtractedWordsAsync(chatId, messageId, state, ct);
    }

    private async Task ShowExtractedWordsAsync(long chatId, int messageId, UserState state, CancellationToken ct)
    {
        var importState = state.ImportState!;
        var words = importState.ExtractedWords!;

        var wordsList = string.Join("\n", words.Select((w, i) =>
            $"{i + 1}. **{w.Word}**\n   _{w.Context}_"));

        var text = $"📚 Найдено {words.Count} слов:\n\n{wordsList}";

        var buttons = new List<List<InlineKeyboardButton>>();

        // Remove buttons (2 per row)
        for (int i = 0; i < words.Count; i += 2)
        {
            var row = new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData($"❌ {i + 1}", $"import:remove:{i}")
            };
            if (i + 1 < words.Count)
            {
                row.Add(InlineKeyboardButton.WithCallbackData($"❌ {i + 2}", $"import:remove:{i + 1}"));
            }
            buttons.Add(row);
        }

        // Action buttons
        buttons.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("➕ Ещё слова", "import:more"),
            InlineKeyboardButton.WithCallbackData("✅ Создать карточки", "import:confirm")
        });

        buttons.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("❌ Отмена", "import:cancel")
        });

        await _bot.EditMessageText(
            chatId,
            messageId,
            text,
            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task ExtractMoreWordsAsync(CallbackQuery callback, UserState state, CancellationToken ct)
    {
        var importState = state.ImportState!;

        await _bot.EditMessageText(
            callback.Message!.Chat.Id,
            callback.Message.MessageId,
            "⏳ Извлекаю дополнительные слова...",
            replyMarkup: null,
            cancellationToken: ct);

        var moreWords = await _wordExtractor.ExtractWordsAsync(
            importState.SourceText!,
            "intermediate",
            5,
            ct);

        // Add only new words
        var existingWords = importState.ExtractedWords!.Select(w => w.Word.ToLower()).ToHashSet();
        var newWords = moreWords
            .Where(w => !existingWords.Contains(w.Word.ToLower()))
            .Select(w => new ExtractedWordState { Word = w.Word, Context = w.Context })
            .ToList();

        importState.ExtractedWords!.AddRange(newWords);

        await ShowExtractedWordsAsync(callback.Message.Chat.Id, callback.Message.MessageId, state, ct);
    }

    private async Task CreateCardsFromExtractedWordsAsync(CallbackQuery callback, UserState state, CancellationToken ct)
    {
        var userId = callback.From.Id;
        var importState = state.ImportState!;
        var words = importState.ExtractedWords!;

        await _bot.EditMessageText(
            callback.Message!.Chat.Id,
            callback.Message.MessageId,
            $"⏳ Создаю {words.Count} карточек...",
            replyMarkup: null,
            cancellationToken: ct);

        var created = 0;
        var duplicates = 0;

        foreach (var word in words)
        {
            try
            {
                var (card, isDuplicate) = await _cardService.CreateCardFromTextAsync(userId, word.Word, ct);
                if (isDuplicate)
                    duplicates++;
                else if (card != null)
                    created++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create card for word: {Word}", word.Word);
            }
        }

        state.Mode = ConversationMode.Normal;
        state.ImportState = null;

        var resultText = $"✅ Импорт завершён!\n\n" +
                        $"📚 Создано карточек: {created}\n" +
                        (duplicates > 0 ? $"♻️ Дубликатов: {duplicates}" : "");

        await _bot.EditMessageText(
            callback.Message.Chat.Id,
            callback.Message.MessageId,
            resultText,
            cancellationToken: ct);
    }

    private static InlineKeyboardMarkup CancelKeyboard() =>
        new(new[] { new[] { InlineKeyboardButton.WithCallbackData("❌ Отмена", "import:cancel") } });
}
