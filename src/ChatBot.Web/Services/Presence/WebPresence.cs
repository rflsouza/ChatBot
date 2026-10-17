using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace ChatBot.Web.Services.Presence;

/// <summary>
/// Estado de UM navegador conectado (um circuito Blazor Server = uma conexão SignalR).
/// Scoped: o Blazor cria um escopo de DI por circuito, então cada aba tem a sua instância.
/// </summary>
public sealed class CircuitPresence(WebPresenceRegistry registry)
{
    /// <summary>Id próprio e aleatório (não expomos o id interno do circuito).</summary>
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; private set; } = DateTime.UtcNow;
    public string? AgentId { get; private set; }
    public string? ThreadId { get; private set; }
    public bool IsConnected { get; private set; } = true;

    /// <summary>Chamado pela página de chat quando abre/troca de conversa.</summary>
    public void SetConversation(string agentId, string threadId)
    {
        AgentId = agentId;
        ThreadId = threadId;
        Touch();
    }

    /// <summary>Chamado quando o usuário envia uma mensagem.</summary>
    public void Touch()
    {
        LastActivityAt = DateTime.UtcNow;
        registry.NotifyChanged();
    }

    internal void SetConnected(bool connected)
    {
        IsConnected = connected;
        registry.NotifyChanged();
    }
}

/// <summary>Todos os circuitos vivos nesta instância do ChatBot.Web.</summary>
public sealed class WebPresenceRegistry
{
    private readonly ConcurrentDictionary<string, CircuitPresence> _clients = new();
    private readonly SemaphoreSlim _changed = new(0, 1);

    public int Count => _clients.Count;

    public void Add(CircuitPresence presence)
    {
        _clients[presence.Id] = presence;
        NotifyChanged();
    }

    public void Remove(CircuitPresence presence)
    {
        _clients.TryRemove(presence.Id, out _);
        NotifyChanged();
    }

    public List<WebClientPresenceDto> Snapshot() =>
        _clients.Values
            .Select(p => new WebClientPresenceDto(p.Id, p.AgentId, p.ThreadId, p.ConnectedAt, p.LastActivityAt, p.IsConnected))
            .ToList();

    public void NotifyChanged()
    {
        if (_changed.CurrentCount > 0) return;
        try { _changed.Release(); }
        catch (SemaphoreFullException) { }
    }

    /// <summary>Espera uma mudança (true) ou o tempo limite (false), o que vier primeiro.</summary>
    public Task<bool> WaitForChangeAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _changed.WaitAsync(timeout, cancellationToken);
}

/// <summary>Mesmo formato do <c>WebClientPresence</c> da Api.</summary>
public sealed record WebClientPresenceDto(
    string ClientId,
    string? AgentId,
    string? ThreadId,
    DateTime ConnectedAt,
    DateTime LastActivityAt,
    bool IsConnected);

/// <summary>
/// Ganchos do Blazor Server para o ciclo de vida da conexão SignalR:
/// abriu → registra; caiu → "reconectando"; voltou → conectado; fechou → remove.
/// Ao fechar a aba, o Blazor mantém o circuito por ~3 min (DisconnectedCircuitRetentionPeriod) antes de fechá-lo.
/// </summary>
public sealed class PresenceCircuitHandler(CircuitPresence presence, WebPresenceRegistry registry) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        registry.Add(presence);
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        presence.SetConnected(true);
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        presence.SetConnected(false);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        registry.Remove(presence);
        return Task.CompletedTask;
    }
}
