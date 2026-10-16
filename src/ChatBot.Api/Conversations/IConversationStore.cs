using System.Collections.Concurrent;
using System.Text.Json;

namespace ChatBot.Api.Conversations;

/// <summary>
/// Persiste o estado serializado da <c>AgentSession</c> (histórico etc.).
/// Troque a implementação por Redis, Cosmos DB, PostgreSQL... sem tocar nos canais.
/// </summary>
public interface IConversationStore
{
    ValueTask<JsonElement?> GetAsync(ConversationKey key, CancellationToken cancellationToken = default);
    ValueTask SaveAsync(ConversationKey key, JsonElement state, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(ConversationKey key, CancellationToken cancellationToken = default);
}

/// <summary>Implementação em memória (desenvolvimento). Perde os dados ao reiniciar.</summary>
public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<string, JsonElement> _sessions = new();

    public ValueTask<JsonElement?> GetAsync(ConversationKey key, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<JsonElement?>(_sessions.TryGetValue(key.ToString(), out var state) ? state : null);

    public ValueTask SaveAsync(ConversationKey key, JsonElement state, CancellationToken cancellationToken = default)
    {
        _sessions[key.ToString()] = state.Clone();
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(ConversationKey key, CancellationToken cancellationToken = default)
    {
        _sessions.TryRemove(key.ToString(), out _);
        return ValueTask.CompletedTask;
    }
}
