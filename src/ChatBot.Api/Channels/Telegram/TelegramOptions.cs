namespace ChatBot.Api.Channels.Telegram;

/// <summary>Configuração da seção "Channels:Telegram".</summary>
public sealed class TelegramOptions
{
    public const string SectionName = "Channels:Telegram";
    public const string WebhookPath = "/channels/telegram/webhook";

    public bool Enabled { get; set; }

    /// <summary>Token do @BotFather. Guarde em user-secrets / variável de ambiente.</summary>
    public string? BotToken { get; set; }

    /// <summary>Polling (desenvolvimento, sem URL pública) ou Webhook (produção).</summary>
    public TelegramMode Mode { get; set; } = TelegramMode.Polling;

    /// <summary>URL pública HTTPS completa do webhook, ex.: https://meu-host/channels/telegram/webhook.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Segredo enviado pelo Telegram no header X-Telegram-Bot-Api-Secret-Token (obrigatório no modo Webhook).</summary>
    public string? SecretToken { get; set; }

    /// <summary>Agente usado em chats novos. Vazio = AI:DefaultAgent.</summary>
    public string? DefaultAgent { get; set; }

    /// <summary>Se preenchido, só esses ids de usuário do Telegram podem usar o bot. Vazio = todos.</summary>
    public long[] AllowedUserIds { get; set; } = [];

    /// <summary>Quantas mensagens são processadas em paralelo (chats diferentes).</summary>
    public int MaxConcurrency { get; set; } = 8;

    /// <summary>Ignora mensagens recebidas enquanto a Api estava parada.</summary>
    public bool DropPendingUpdates { get; set; }
}

public enum TelegramMode
{
    Polling,
    Webhook,
}
