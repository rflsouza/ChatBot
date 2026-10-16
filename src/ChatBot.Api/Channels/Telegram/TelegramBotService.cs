using System.Threading.Channels;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ChatBot.Api.Channels.Telegram;

/// <summary>Fila entre quem recebe os updates (polling ou webhook) e quem processa.</summary>
public sealed class TelegramUpdateQueue
{
    private readonly Channel<Update> _channel = Channel.CreateUnbounded<Update>(new UnboundedChannelOptions { SingleReader = true });

    public bool Enqueue(Update update) => _channel.Writer.TryWrite(update);

    public IAsyncEnumerable<Update> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Inicializa o bot (GetMe, comandos, webhook ou polling) e processa a fila de updates em paralelo
/// (até <see cref="TelegramOptions.MaxConcurrency"/> chats ao mesmo tempo; mensagens do mesmo chat são serializadas
/// pelo ConversationService). Falhas aqui não derrubam a Api: o chat web continua funcionando.
/// </summary>
public sealed class TelegramBotService(
    ITelegramBotClient bot,
    TelegramUpdateQueue queue,
    TelegramUpdateProcessor processor,
    TelegramOptions options,
    ILogger<TelegramBotService> logger) : BackgroundService
{
    private static readonly UpdateType[] AllowedUpdates = [UpdateType.Message];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await InitializeAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Telegram: falha ao inicializar o bot (token correto? acesso à internet?). Canal Telegram desativado.");
            return;
        }

        Task polling = Task.CompletedTask;
        if (options.Mode == TelegramMode.Polling)
        {
            polling = bot.ReceiveAsync(
                (_, update, _) =>
                {
                    queue.Enqueue(update);
                    return Task.CompletedTask;
                },
                async (_, exception, ct) =>
                {
                    logger.LogWarning(exception, "Telegram: erro no polling; tentando novamente.");
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                },
                new ReceiverOptions { AllowedUpdates = AllowedUpdates, DropPendingUpdates = options.DropPendingUpdates },
                stoppingToken);
        }

        using var gate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrency));
        try
        {
            await foreach (var update in queue.ReadAllAsync(stoppingToken))
            {
                await gate.WaitAsync(stoppingToken);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await processor.HandleAsync(update, stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogError(ex, "Telegram: erro ao processar o update {UpdateId}.", update.Id);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        try
        {
            await polling;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var me = await bot.GetMe(cancellationToken);
        processor.BotUsername = me.Username;

        await bot.SetMyCommands(
        [
            new BotCommand { Command = "novo", Description = "Começar uma conversa nova" },
            new BotCommand { Command = "modelo", Description = "Ver ou trocar o modelo" },
            new BotCommand { Command = "ajuda", Description = "Mostrar a ajuda" },
        ], cancellationToken: cancellationToken);

        if (options.Mode == TelegramMode.Webhook)
        {
            await bot.SetWebhook(
                options.WebhookUrl!,
                allowedUpdates: AllowedUpdates,
                dropPendingUpdates: options.DropPendingUpdates,
                secretToken: options.SecretToken,
                cancellationToken: cancellationToken);
            logger.LogInformation("Telegram: bot @{Username} recebendo via webhook em {Url}.", me.Username, options.WebhookUrl);
        }
        else
        {
            // Polling não funciona com webhook registrado.
            await bot.DeleteWebhook(cancellationToken: cancellationToken);
            logger.LogInformation("Telegram: bot @{Username} recebendo via polling.", me.Username);
        }
    }
}
