using ChatBot.Api.Agents;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;

namespace ChatBot.Api.Channels.Web;

/// <summary>
/// Canal Web: expõe cada agente via protocolo AG-UI (SSE com eventos tipados: texto, tool calls, estado...).
/// O frontend Blazor consome com o <c>AGUIChatClient</c>.
/// </summary>
public static class WebChannelEndpoints
{
    public const string AgentsRoutePrefix = "/channels/web/agents";

    public static IServiceCollection AddWebChannel(this IServiceCollection services)
    {
        services.AddAGUIServer();
        return services;
    }

    public static IEndpointRouteBuilder MapWebChannel(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<AIOptions>();

        // Catálogo de agentes para o seletor de modelo do frontend.
        endpoints.MapGet("/api/agents", () => options.Agents.Select(a => new AgentInfo(
                a.Id,
                a.DisplayName ?? a.Id,
                a.Description,
                a.Model,
                a.Id == options.DefaultAgent)))
            .WithName("ListAgents")
            .WithTags("Agents");

        // Um endpoint AG-UI por agente: POST /channels/web/agents/{id}
        // O navegador envia só a NOVA mensagem + threadId. O MapAGUIServer usa o session store registrado
        // para o agente (EfAgentSessionStore) para achar a sessão pelo threadId, e o EfChatHistoryProvider
        // carrega/grava o histórico no SQLite.
        foreach (var agent in options.Agents)
        {
            endpoints.MapAGUIServer(agent.Id, $"{AgentsRoutePrefix}/{agent.Id}")
                .WithTags("Web channel (AG-UI)");
            // Em produção: .RequireAuthorization();
        }

        return endpoints;
    }

    public sealed record AgentInfo(string Id, string DisplayName, string? Description, string Model, bool IsDefault);
}
