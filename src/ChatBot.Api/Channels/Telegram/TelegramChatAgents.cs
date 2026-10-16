using System.Collections.Concurrent;
using ChatBot.Api.Agents;
using ChatBot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ChatBot.Api.Channels.Telegram;

/// <summary>
/// Qual agente atende cada chat do Telegram (comando /modelo).
/// A escolha fica em memória; depois de reiniciar, vale o agente da conversa ativa mais recente do chat
/// (tabela Conversations) ou, se não houver, o agente padrão.
/// </summary>
public sealed class TelegramChatAgents(
    IDbContextFactory<ChatBotDbContext> dbFactory,
    AIOptions aiOptions,
    TelegramOptions telegramOptions)
{
    private readonly ConcurrentDictionary<long, string> _choices = new();

    public string DefaultAgentId =>
        IsValid(telegramOptions.DefaultAgent) ? telegramOptions.DefaultAgent! : aiOptions.DefaultAgent;

    public bool IsValid(string? agentId) =>
        !string.IsNullOrWhiteSpace(agentId) && aiOptions.Agents.Any(a => a.Id == agentId);

    public async Task<string> GetAsync(long chatId, CancellationToken cancellationToken = default)
    {
        if (_choices.TryGetValue(chatId, out var chosen))
        {
            return chosen;
        }

        var sessionKey = TelegramUpdateProcessor.SessionKeyOf(chatId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var lastAgent = await db.Conversations.AsNoTracking()
            .Where(c => c.SessionKey == sessionKey)
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => c.AgentId)
            .FirstOrDefaultAsync(cancellationToken);

        var agentId = IsValid(lastAgent) ? lastAgent! : DefaultAgentId;
        return _choices.GetOrAdd(chatId, agentId);
    }

    public void Set(long chatId, string agentId) => _choices[chatId] = agentId;
}
