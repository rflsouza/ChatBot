using System.Text.Json;
using ChatBot.Api.Data;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;

namespace ChatBot.Api.Memory;

/// <summary>
/// Persiste a <see cref="AgentSession"/> na tabela <c>Conversations</c>, encontrando-a por (agente, chave).
/// <list type="bullet">
/// <item>Canal Web (AG-UI): a chave é o <c>threadId</c> enviado pelo navegador.</item>
/// <item>Canais de texto: a chave é "canal:id" (ex.: "telegram:123456"), montada pelo <c>ConversationService</c>.</item>
/// </list>
/// </summary>
public sealed class EfAgentSessionStore(
    IDbContextFactory<ChatBotDbContext> dbFactory,
    EfChatHistoryProvider history) : AgentSessionStore
{
    public override async ValueTask<AgentSession?> GetSessionAsync(
        AIAgent agent, AgentSessionStoreKey key, CancellationToken cancellationToken = default)
    {
        var agentId = AgentIdOf(agent);
        var sessionKey = ToStorageKey(key);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var state = await db.Conversations.AsNoTracking()
            .Where(c => c.AgentId == agentId && c.SessionKey == sessionKey)
            .Select(c => c.SessionState)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrEmpty(state))
        {
            return null; // o framework cria uma sessão nova (e o EfChatHistoryProvider, uma conversa nova)
        }

        using var document = JsonDocument.Parse(state);
        return await agent.DeserializeSessionAsync(document.RootElement.Clone(), cancellationToken: cancellationToken);
    }

    public override async ValueTask SaveSessionAsync(
        AIAgent agent, AgentSessionStoreKey key, AgentSession session, CancellationToken cancellationToken = default)
    {
        var conversationId = history.GetConversationId(session);
        var serialized = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
        var now = DateTime.UtcNow;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var conversation = await db.Conversations.FindAsync([conversationId], cancellationToken);
        if (conversation is null)
        {
            conversation = new Conversation { Id = conversationId, AgentId = AgentIdOf(agent), CreatedAt = now };
            db.Conversations.Add(conversation);
        }

        conversation.SessionKey = ToStorageKey(key);
        conversation.Channel = ChannelOf(key);
        conversation.SessionState = serialized.GetRawText();
        conversation.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Encerra a sessão ativa (próxima mensagem começa uma conversa nova) sem apagar o histórico.
    /// </summary>
    public async Task DetachAsync(AIAgent agent, AgentSessionStoreKey key, CancellationToken cancellationToken = default)
    {
        var agentId = AgentIdOf(agent);
        var sessionKey = ToStorageKey(key);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Conversations
            .Where(c => c.AgentId == agentId && c.SessionKey == sessionKey)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.SessionKey, (string?)null)
                .SetProperty(c => c.UpdatedAt, DateTime.UtcNow), cancellationToken);
    }

    /// <summary>
    /// "sessionId" ou, se houver partições de isolamento (ex.: usuário autenticado), "k1=v1;k2=v2|sessionId".
    /// </summary>
    public static string ToStorageKey(AgentSessionStoreKey key) =>
        key.Partitions is { Count: > 0 } partitions
            ? string.Join(';', partitions.Select(p => $"{p.Key}={p.Value}")) + "|" + key.SessionId
            : key.SessionId;

    private static string ChannelOf(AgentSessionStoreKey key)
    {
        var separator = key.SessionId.IndexOf(':');
        return separator > 0 ? key.SessionId[..separator] : "web";
    }

    private static string AgentIdOf(AIAgent agent) => agent.Name ?? agent.Id;
}
