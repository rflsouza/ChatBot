using System.Net.Http.Json;
using AGUI.Client;
using Microsoft.Extensions.AI;

namespace ChatBot.Web.Services;

public sealed record AgentInfo(string Id, string DisplayName, string? Description, string Model, bool IsDefault);

public sealed record StoredMessage(string Author, string? AuthorName, string Text, DateTime CreatedAt);

/// <summary>Acesso ao backend: catálogo de agentes, histórico salvo e IChatClient AG-UI por agente.</summary>
public sealed class ChatBotApiClient(IHttpClientFactory httpClientFactory)
{
    public const string HttpClientName = "chatbot-api";

    public async Task<IReadOnlyList<AgentInfo>> GetAgentsAsync(CancellationToken cancellationToken = default)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);
        return await http.GetFromJsonAsync<List<AgentInfo>>("api/agents", cancellationToken) ?? [];
    }

    /// <summary>Mensagens já salvas no banco para a conversa (agente + threadId).</summary>
    public async Task<IReadOnlyList<StoredMessage>> GetMessagesAsync(string agentId, string threadId, CancellationToken cancellationToken = default)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);
        var url = $"api/conversations/{Uri.EscapeDataString(agentId)}/{Uri.EscapeDataString(threadId)}/messages";
        return await http.GetFromJsonAsync<List<StoredMessage>>(url, cancellationToken) ?? [];
    }

    /// <summary>
    /// Cria um <see cref="IChatClient"/> que fala AG-UI com o agente escolhido.
    /// Para o restante do app é só um IChatClient comum (streaming, tool calls, etc.).
    /// </summary>
    public IChatClient CreateChatClient(string agentId)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);
        return new AGUIChatClient(new(http, $"channels/web/agents/{Uri.EscapeDataString(agentId)}"));
    }
}
