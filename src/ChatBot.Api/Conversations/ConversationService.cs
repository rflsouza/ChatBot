using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ChatBot.Api.Agents;
using ChatBot.Api.Memory;
using Microsoft.Agents.AI;

namespace ChatBot.Api.Conversations;

/// <summary>
/// Porta de entrada única para canais de texto (REST, Telegram, WhatsApp, Teams...).
/// O servidor guarda o histórico: o canal só envia a nova mensagem + um id de conversa.
/// </summary>
public interface IConversationService
{
    IReadOnlyList<AgentDefinition> Agents { get; }

    Task<string> SendAsync(ConversationKey key, string userMessage, CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> SendStreamingAsync(ConversationKey key, string userMessage, CancellationToken cancellationToken = default);

    /// <summary>Encerra a conversa atual; a próxima mensagem inicia outra. O histórico antigo fica no banco.</summary>
    Task ResetAsync(ConversationKey key, CancellationToken cancellationToken = default);
}

public sealed class ConversationService(
    IServiceProvider services,
    EfAgentSessionStore sessions,
    AIOptions options,
    ILogger<ConversationService> logger) : IConversationService
{
    // Serializa mensagens de uma mesma conversa (ex.: usuário mandando várias mensagens seguidas no Telegram).
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    public IReadOnlyList<AgentDefinition> Agents => options.Agents;

    public async Task<string> SendAsync(ConversationKey key, string userMessage, CancellationToken cancellationToken = default)
    {
        var text = new System.Text.StringBuilder();
        await foreach (var chunk in SendStreamingAsync(key, userMessage, cancellationToken))
        {
            text.Append(chunk);
        }
        return text.ToString();
    }

    public async IAsyncEnumerable<string> SendStreamingAsync(
        ConversationKey key,
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var agent = ResolveAgent(key.AgentId);
        var storeKey = ToStoreKey(key);
        var gate = Locks.GetOrAdd($"{agent.Name}|{storeKey.SessionId}", _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);
        try
        {
            // Sessão vem do SQLite (EfAgentSessionStore); histórico é carregado pelo EfChatHistoryProvider.
            AgentSession session = await sessions.GetOrCreateSessionAsync(agent, storeKey, cancellationToken);

            await foreach (var update in agent.RunStreamingAsync(userMessage, session, cancellationToken: cancellationToken))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    yield return update.Text;
                }
            }

            await sessions.SaveSessionAsync(agent, storeKey, session, cancellationToken);
            logger.LogDebug("Conversa {Key} atualizada.", storeKey.SessionId);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ResetAsync(ConversationKey key, CancellationToken cancellationToken = default)
        => await sessions.DetachAsync(ResolveAgent(key.AgentId), ToStoreKey(key), cancellationToken);

    /// <summary>"canal:id" — ex.: "telegram:123456". O prefixo vira a coluna Channel da conversa.</summary>
    private static AgentSessionStoreKey ToStoreKey(ConversationKey key) => new($"{key.Channel}:{key.ConversationId}");

    private AIAgent ResolveAgent(string agentId)
    {
        var id = string.IsNullOrWhiteSpace(agentId) ? options.DefaultAgent : agentId;
        return services.GetKeyedService<AIAgent>(id)
            ?? throw new KeyNotFoundException($"Agente '{id}' não encontrado.");
    }
}
