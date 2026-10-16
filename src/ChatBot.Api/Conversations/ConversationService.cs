using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ChatBot.Api.Agents;
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

    Task ResetAsync(ConversationKey key, CancellationToken cancellationToken = default);
}

public sealed class ConversationService(
    IServiceProvider services,
    IConversationStore store,
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
        key = key with { AgentId = agent.Name! };
        var gate = Locks.GetOrAdd(key.ToString(), _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);
        try
        {
            var stored = await store.GetAsync(key, cancellationToken);
            AgentSession session = stored is { } state
                ? await agent.DeserializeSessionAsync(state, cancellationToken: cancellationToken)
                : await agent.CreateSessionAsync(cancellationToken);

            await foreach (var update in agent.RunStreamingAsync(userMessage, session, cancellationToken: cancellationToken))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    yield return update.Text;
                }
            }

            var serialized = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
            await store.SaveAsync(key, serialized, cancellationToken);
            logger.LogDebug("Conversa {Key} atualizada.", key);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ResetAsync(ConversationKey key, CancellationToken cancellationToken = default)
        => await store.DeleteAsync(key with { AgentId = ResolveAgent(key.AgentId).Name! }, cancellationToken);

    private AIAgent ResolveAgent(string agentId)
    {
        var id = string.IsNullOrWhiteSpace(agentId) ? options.DefaultAgent : agentId;
        return services.GetKeyedService<AIAgent>(id)
            ?? throw new KeyNotFoundException($"Agente '{id}' não encontrado.");
    }
}
