using System.Text;
using ChatBot.Api.Agents;
using ChatBot.Api.Conversations;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ChatBot.Api.Channels.Telegram;

/// <summary>
/// Trata cada mensagem recebida do Telegram: comandos (/start, /novo, /modelo, /ajuda) ou pergunta ao agente
/// via <see cref="IConversationService"/> — o mesmo núcleo usado pelos outros canais.
/// </summary>
public sealed class TelegramUpdateProcessor(
    ITelegramBotClient bot,
    IConversationService conversations,
    TelegramChatAgents chatAgents,
    TelegramOptions options,
    AIOptions aiOptions,
    ILogger<TelegramUpdateProcessor> logger)
{
    public const string Channel = "telegram";

    /// <summary>Preenchido na inicialização (GetMe); usado para detectar menções em grupos.</summary>
    public string? BotUsername { get; set; }

    /// <summary>Mesmo formato que o ConversationService grava em Conversations.SessionKey.</summary>
    public static string SessionKeyOf(long chatId) => $"{Channel}:{chatId}";

    public async Task HandleAsync(Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { } message)
        {
            return; // por enquanto só tratamos mensagens novas
        }

        var chatId = message.Chat.Id;
        var isPrivate = message.Chat.Type == ChatType.Private;
        var text = message.Text?.Trim();

        // Em grupos, só responde quando for comando ou quando o bot for mencionado.
        if (!isPrivate)
        {
            if (text is null) return;
            var mention = BotUsername is null ? null : "@" + BotUsername;
            var mentioned = mention is not null && text.Contains(mention, StringComparison.OrdinalIgnoreCase);
            if (!text.StartsWith('/') && !mentioned) return;
            if (mentioned && !text.StartsWith('/')) text = text.Replace(mention!, "", StringComparison.OrdinalIgnoreCase).Trim();
        }

        if (options.AllowedUserIds.Length > 0 && (message.From is null || !options.AllowedUserIds.Contains(message.From.Id)))
        {
            logger.LogWarning("Telegram: usuário {UserId} não autorizado (chat {ChatId}).", message.From?.Id, chatId);
            await SendTextAsync(chatId, "⛔ Você não tem permissão para usar este bot.", message.Id, cancellationToken);
            return;
        }

        if (string.IsNullOrEmpty(text))
        {
            await SendTextAsync(chatId, "Por enquanto eu só entendo mensagens de texto. 🙂", message.Id, cancellationToken);
            return;
        }

        if (text.StartsWith('/'))
        {
            await HandleCommandAsync(message, text, cancellationToken);
            return;
        }

        await AnswerAsync(message, text, cancellationToken);
    }

    private async Task AnswerAsync(Message message, string text, CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        var agentId = await chatAgents.GetAsync(chatId, cancellationToken);
        var key = new ConversationKey(Channel, chatId.ToString(), agentId,
            message.From is { } from ? $"{Channel}:{from.Id}" : null);

        using var typingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var typing = KeepTypingAsync(chatId, typingCts.Token);

        string reply;
        try
        {
            reply = await conversations.SendAsync(key, text, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Telegram: erro ao gerar resposta (chat {ChatId}, agente {AgentId}).", chatId, agentId);
            await StopTypingAsync(typingCts, typing);
            await SendTextAsync(chatId, "⚠️ Desculpe, ocorreu um erro ao gerar a resposta. Tente novamente em instantes.",
                message.Id, cancellationToken);
            return;
        }

        await StopTypingAsync(typingCts, typing);

        if (string.IsNullOrWhiteSpace(reply))
        {
            await SendTextAsync(chatId,
                "⚠️ O modelo terminou sem devolver texto. Tente de novo ou troque de modelo com /modelo.",
                message.Id, cancellationToken);
            return;
        }

        await SendMarkdownAsync(chatId, reply, message.Id, cancellationToken);
    }

    private async Task HandleCommandAsync(Message message, string text, CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var command = parts[0].Split('@')[0].ToLowerInvariant(); // "/novo@MeuBot" -> "/novo"
        var argument = parts.Length > 1 ? parts[1] : null;
        var agentId = await chatAgents.GetAsync(chatId, cancellationToken);

        switch (command)
        {
            case "/start":
                await SendMarkdownAsync(chatId,
                    $"Olá, {message.From?.FirstName ?? "tudo bem"}! 👋\n\n" +
                    $"Sou um assistente de IA. Pode perguntar o que quiser.\n\n" +
                    $"Modelo atual: **{DisplayName(agentId)}**\n\n" + HelpText(),
                    null, cancellationToken);
                break;

            case "/novo":
                await conversations.ResetAsync(new ConversationKey(Channel, chatId.ToString(), agentId), cancellationToken);
                await SendTextAsync(chatId, "🆕 Conversa reiniciada. Pode mandar a próxima pergunta!", message.Id, cancellationToken);
                break;

            case "/modelo":
                if (argument is null)
                {
                    var list = new StringBuilder("Modelos disponíveis:\n\n");
                    foreach (var agent in aiOptions.Agents)
                    {
                        list.Append(agent.Id == agentId ? "✅ " : "▫️ ")
                            .Append($"`{agent.Id}` — {agent.DisplayName ?? agent.Id}");
                        if (!string.IsNullOrWhiteSpace(agent.Description)) list.Append($" ({agent.Description})");
                        list.Append('\n');
                    }
                    list.Append("\nPara trocar: `/modelo <id>`");
                    await SendMarkdownAsync(chatId, list.ToString(), message.Id, cancellationToken);
                }
                else if (chatAgents.IsValid(argument))
                {
                    chatAgents.Set(chatId, argument);
                    await SendMarkdownAsync(chatId,
                        $"Modelo alterado para **{DisplayName(argument)}**. Use /novo para começar uma conversa do zero.",
                        message.Id, cancellationToken);
                }
                else
                {
                    await SendTextAsync(chatId, $"Modelo \"{argument}\" não encontrado. Envie /modelo para ver a lista.",
                        message.Id, cancellationToken);
                }
                break;

            case "/ajuda":
            case "/help":
                await SendMarkdownAsync(chatId, HelpText(), message.Id, cancellationToken);
                break;

            default:
                await SendTextAsync(chatId, "Comando desconhecido. Envie /ajuda para ver os comandos.", message.Id, cancellationToken);
                break;
        }
    }

    private static string HelpText() =>
        "Comandos:\n" +
        "/novo — começa uma conversa nova\n" +
        "/modelo — lista os modelos; `/modelo <id>` troca\n" +
        "/ajuda — mostra esta ajuda";

    private string DisplayName(string agentId) =>
        aiOptions.Agents.FirstOrDefault(a => a.Id == agentId)?.DisplayName ?? agentId;

    /// <summary>Mostra "digitando…" enquanto o LLM gera a resposta (a ação expira em ~5 s no Telegram).</summary>
    private async Task KeepTypingAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await bot.SendChatAction(chatId, ChatAction.Typing, cancellationToken: cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Telegram: falha ao enviar 'digitando' (chat {ChatId}).", chatId);
        }
    }

    private static async Task StopTypingAsync(CancellationTokenSource typingCts, Task typing)
    {
        await typingCts.CancelAsync();
        await typing;
    }

    /// <summary>Envia Markdown convertido para HTML; se o Telegram rejeitar, reenvia como texto puro.</summary>
    private async Task SendMarkdownAsync(long chatId, string markdown, int? replyTo, CancellationToken cancellationToken)
    {
        foreach (var chunk in TelegramFormatter.Split(markdown))
        {
            try
            {
                await bot.SendMessage(chatId, TelegramFormatter.ToHtml(chunk), ParseMode.Html,
                    replyParameters: ReplyTo(replyTo), cancellationToken: cancellationToken);
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 400)
            {
                logger.LogDebug(ex, "Telegram rejeitou o HTML; reenviando como texto puro.");
                await bot.SendMessage(chatId, chunk, replyParameters: ReplyTo(replyTo), cancellationToken: cancellationToken);
            }

            replyTo = null; // só o primeiro pedaço responde (cita) a mensagem do usuário
        }
    }

    private Task SendTextAsync(long chatId, string text, int? replyTo, CancellationToken cancellationToken) =>
        bot.SendMessage(chatId, text, replyParameters: ReplyTo(replyTo), cancellationToken: cancellationToken);

    private static ReplyParameters? ReplyTo(int? messageId) =>
        messageId is { } id ? new ReplyParameters { MessageId = id, AllowSendingWithoutReply = true } : null;
}
