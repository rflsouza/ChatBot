using System.Collections.Concurrent;

namespace ChatBot.Api.Presence;

/// <summary>Um navegador com o chat aberto (um circuito Blazor no ChatBot.Web).</summary>
/// <param name="ClientId">Id aleatório do circuito (não é o id interno do Blazor).</param>
/// <param name="IsConnected">false = conexão caiu e o Blazor está tentando reconectar (o circuito vive ~3 min).</param>
public sealed record WebClientPresence(
    string ClientId,
    string? AgentId,
    string? ThreadId,
    DateTime ConnectedAt,
    DateTime LastActivityAt,
    bool IsConnected);

/// <summary>
/// Quem está com o chat web aberto agora. Cada instância do ChatBot.Web envia periodicamente a lista completa
/// dos seus circuitos; se uma instância parar de enviar (caiu/foi desligada), seus clientes expiram em <see cref="Ttl"/>.
/// Fica em memória: é estado "ao vivo", não histórico.
/// </summary>
public sealed class WebPresenceTracker
{
    /// <summary>O Web envia a cada 30 s (e a cada mudança); 90 s tolera duas falhas seguidas.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(90);

    private readonly ConcurrentDictionary<string, Snapshot> _instances = new();

    public void Report(string instanceId, IReadOnlyList<WebClientPresence> clients) =>
        _instances[instanceId] = new Snapshot(DateTimeOffset.UtcNow, clients);

    public void Remove(string instanceId) => _instances.TryRemove(instanceId, out _);

    public IReadOnlyList<(string InstanceId, WebClientPresence Client)> GetClients()
    {
        var now = DateTimeOffset.UtcNow;
        var result = new List<(string, WebClientPresence)>();

        foreach (var (instanceId, snapshot) in _instances)
        {
            if (now - snapshot.ReceivedAt > Ttl)
            {
                _instances.TryRemove(instanceId, out _);
                continue;
            }

            result.AddRange(snapshot.Clients.Select(c => (instanceId, c)));
        }

        return result;
    }

    /// <summary>(agente, threadId) das conversas web abertas em algum navegador.</summary>
    public HashSet<(string AgentId, string ThreadId)> GetOpenConversations() =>
        GetClients()
            .Where(x => x.Client.AgentId is not null && x.Client.ThreadId is not null)
            .Select(x => (x.Client.AgentId!, x.Client.ThreadId!))
            .ToHashSet();

    private sealed record Snapshot(DateTimeOffset ReceivedAt, IReadOnlyList<WebClientPresence> Clients);
}
