using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ChatBot.Api.Channels.Telegram;

/// <summary>Registro do canal Telegram. Desligado por padrão (Channels:Telegram:Enabled).</summary>
public static class TelegramChannelExtensions
{
    public static WebApplicationBuilder AddTelegramChannel(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(TelegramOptions.SectionName).Get<TelegramOptions>() ?? new TelegramOptions();
        if (!options.Enabled)
        {
            return builder;
        }

        Validate(options);

        builder.Services.AddSingleton(options);
        builder.Services.AddHttpClient(nameof(TelegramBotClient));
        builder.Services.AddSingleton<ITelegramBotClient>(sp => new TelegramBotClient(
            options.BotToken!,
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(TelegramBotClient))));

        builder.Services.AddSingleton<TelegramUpdateQueue>();
        builder.Services.AddSingleton<TelegramChatAgents>();
        builder.Services.AddSingleton<TelegramUpdateProcessor>();
        builder.Services.AddHostedService<TelegramBotService>();

        return builder;
    }

    /// <summary>Mapeia o webhook (somente quando o canal está ligado no modo Webhook).</summary>
    public static IEndpointRouteBuilder MapTelegramChannel(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetService<TelegramOptions>();
        if (options is not { Mode: TelegramMode.Webhook })
        {
            return endpoints;
        }

        var expectedSecret = Encoding.UTF8.GetBytes(options.SecretToken!);

        endpoints.MapPost(TelegramOptions.WebhookPath, async (HttpRequest request, TelegramUpdateQueue queue, CancellationToken ct) =>
            {
                // Só o Telegram conhece o segredo configurado no SetWebhook.
                var received = Encoding.UTF8.GetBytes(request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString());
                if (!CryptographicOperations.FixedTimeEquals(received, expectedSecret))
                {
                    return Results.Unauthorized();
                }

                // O Telegram usa snake_case: desserializa com as opções da própria biblioteca,
                // sem mexer no JSON global da Api (usado pelo AG-UI).
                var update = await JsonSerializer.DeserializeAsync<Update>(request.Body, JsonBotAPI.Options, ct);
                if (update is not null)
                {
                    queue.Enqueue(update);
                }

                // Responde na hora; o processamento é feito em segundo plano (evita reenvios do Telegram).
                return Results.Ok();
            })
            .ExcludeFromDescription();

        return endpoints;
    }

    private static void Validate(TelegramOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BotToken))
        {
            throw new InvalidOperationException(
                "Channels:Telegram:BotToken não configurado. Use: dotnet user-secrets set \"Channels:Telegram:BotToken\" \"<token do BotFather>\"");
        }

        if (options.Mode == TelegramMode.Webhook)
        {
            if (!Uri.TryCreate(options.WebhookUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException(
                    $"Channels:Telegram:WebhookUrl deve ser uma URL HTTPS pública, ex.: https://meu-host{TelegramOptions.WebhookPath}");
            }

            if (string.IsNullOrWhiteSpace(options.SecretToken)
                || options.SecretToken.Length > 256
                || !options.SecretToken.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            {
                throw new InvalidOperationException(
                    "Channels:Telegram:SecretToken é obrigatório no modo Webhook (1-256 caracteres: A-Z, a-z, 0-9, _ e -).");
            }
        }
    }
}
