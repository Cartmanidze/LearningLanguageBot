using Hangfire;
using Hangfire.PostgreSql;
using LearningLanguageBot.Features.Cards.Handlers;
using LearningLanguageBot.Features.Cards.Services;
using LearningLanguageBot.Features.Onboarding.Handlers;
using LearningLanguageBot.Features.Onboarding.Services;
using LearningLanguageBot.Features.Reminders.Services;
using LearningLanguageBot.Features.Review.Handlers;
using LearningLanguageBot.Features.Review.Services;
using LearningLanguageBot.Features.Settings.Handlers;
using LearningLanguageBot.Features.Webhook.Handlers;
using LearningLanguageBot.Infrastructure.Database;
using LearningLanguageBot.Infrastructure.State;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Configure Serilog
    builder.Services.AddSerilog();

    // Configuration
    var telegramToken = builder.Configuration["Telegram:BotToken"]
        ?? throw new InvalidOperationException("Telegram:BotToken is not configured");
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionString is not configured");

    // Database - use NpgsqlDataSourceBuilder to enable dynamic JSON for List<TimeOnly>
    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
    dataSourceBuilder.EnableDynamicJson();
    var dataSource = dataSourceBuilder.Build();

    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(dataSource));

    // Telegram Bot
    builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(telegramToken));

    // OpenRouter (LLM)
    builder.Services.Configure<OpenRouterOptions>(builder.Configuration.GetSection(OpenRouterOptions.SectionName));
    builder.Services.AddHttpClient<OpenRouterClient>();
    builder.Services.AddScoped<ITranslationService, TranslationService>();

    // State
    builder.Services.AddSingleton<ConversationStateManager>();

    // Features: Onboarding
    builder.Services.AddScoped<UserService>();
    builder.Services.AddScoped<OnboardingHandler>();

    // Features: Cards
    builder.Services.AddScoped<CardService>();
    builder.Services.AddScoped<CardCreationHandler>();
    builder.Services.AddScoped<CardBrowserHandler>();

    // Features: Review
    builder.Services.AddScoped<ReviewService>();
    builder.Services.AddScoped<ReviewHandler>();

    // Features: Settings
    builder.Services.AddScoped<SettingsHandler>();

    // Features: Reminders
    builder.Services.AddScoped<ReminderJob>();

    // Features: Webhook
    builder.Services.AddScoped<UpdateRouter>();

    // Hangfire
    builder.Services.AddHangfire(config =>
        config.UsePostgreSqlStorage(options =>
            options.UseNpgsqlConnection(connectionString)));
    builder.Services.AddHangfireServer();

    // Hosted service for bot polling
    builder.Services.AddHostedService<BotPollingService>();

    var host = builder.Build();

    // Apply migrations and configure recurring jobs
    using (var scope = host.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        // Configure recurring jobs using IRecurringJobManager
        var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
        recurringJobManager.AddOrUpdate<ReminderBackgroundService>(
            "send-reminders",
            service => service.SendRemindersAsync(CancellationToken.None),
            "* * * * *"); // Every minute
    }

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

public class BotPollingService : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BotPollingService> _logger;

    public BotPollingService(
        ITelegramBotClient bot,
        IServiceProvider serviceProvider,
        ILogger<BotPollingService> logger)
    {
        _bot = bot;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var me = await _bot.GetMe(stoppingToken);
        _logger.LogInformation("Bot started: @{Username}", me.Username);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery],
            DropPendingUpdates = true
        };

        await _bot.ReceiveAsync(
            updateHandler: async (bot, update, ct) =>
            {
                using var scope = _serviceProvider.CreateScope();
                var router = scope.ServiceProvider.GetRequiredService<UpdateRouter>();
                await router.HandleUpdateAsync(update, ct);
            },
            errorHandler: async (bot, exception, ct) =>
            {
                _logger.LogError(exception, "Polling error");
                await Task.CompletedTask;
            },
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);
    }
}

public class ReminderBackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ReminderBackgroundService> _logger;

    public ReminderBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<ReminderBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task SendRemindersAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var reminderJob = scope.ServiceProvider.GetRequiredService<ReminderJob>();
        var bot = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        var reviewHandler = scope.ServiceProvider.GetRequiredService<ReviewHandler>();

        var users = await reminderJob.GetUsersForReminderAsync(ct);

        foreach (var user in users)
        {
            try
            {
                var dueCount = await reminderJob.GetDueCardsCountAsync(user.TelegramId, ct);
                if (dueCount == 0) continue;

                await reviewHandler.StartReviewSessionFromPushAsync(user.TelegramId, user.TelegramId, ct);
                _logger.LogInformation("Sent reminder to user {UserId}", user.TelegramId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send reminder to user {UserId}", user.TelegramId);

                // Mark user as inactive if bot is blocked
                if (ex.Message.Contains("blocked") || ex.Message.Contains("Forbidden"))
                {
                    await reminderJob.MarkUserInactiveAsync(user.TelegramId, ct);
                }
            }
        }
    }
}
