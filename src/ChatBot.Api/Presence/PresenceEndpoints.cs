using ChatBot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ChatBot.Api.Presence;

public static class PresenceEndpoints
{
    public static IEndpointRouteBuilder MapPresenceEndpoints(this IEndpointRouteBuilder endpoints, bool isDevelopment)
    {
        var group = endpoints.MapGroup("/api/presence/web").WithTags("Presence");

        // Chamado pelo ChatBot.Web: lista completa dos circuitos abertos naquela instância.
        group.MapPut("/{instanceId}", (string instanceId, List<WebClientPresence> clients, WebPresenceTracker tracker) =>
            {
                if (clients.Count > 10_000)
                {
                    return Results.BadRequest("Lista grande demais.");
                }

                tracker.Report(instanceId, clients);
                return Results.NoContent();
            })
            .ExcludeFromDescription();

        // Chamado pelo ChatBot.Web ao desligar.
        group.MapDelete("/{instanceId}", (string instanceId, WebPresenceTracker tracker) =>
            {
                tracker.Remove(instanceId);
                return Results.NoContent();
            })
            .ExcludeFromDescription();

        if (isDevelopment)
        {
            // ⚠️ Somente em Development: quem está com o chat web aberto agora, com os dados da conversa.
            group.MapGet("/", async (WebPresenceTracker tracker, IDbContextFactory<ChatBotDbContext> dbFactory, CancellationToken ct) =>
            {
                var clients = tracker.GetClients();
                var threadIds = clients.Select(x => x.Client.ThreadId).OfType<string>().Distinct().ToList();

                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var conversations = await db.Conversations.AsNoTracking()
                    .Where(c => c.SessionKey != null && threadIds.Contains(c.SessionKey))
                    .Select(c => new { c.Id, c.AgentId, c.SessionKey, c.Title, MessageCount = c.Messages.Count })
                    .ToListAsync(ct);

                return clients
                    .Select(x =>
                    {
                        var conversation = conversations.FirstOrDefault(c =>
                            c.AgentId == x.Client.AgentId && c.SessionKey == x.Client.ThreadId);

                        return new OnlineWebClientDto(
                            x.Client.ClientId,
                            x.InstanceId,
                            x.Client.IsConnected ? "connected" : "reconnecting",
                            x.Client.AgentId,
                            x.Client.ThreadId,
                            x.Client.ConnectedAt,
                            x.Client.LastActivityAt,
                            conversation?.Id,
                            conversation?.Title,
                            conversation?.MessageCount ?? 0);
                    })
                    .OrderByDescending(x => x.LastActivityAt)
                    .ToList();
            });
        }

        return endpoints;
    }

    /// <param name="ConversationId">null = o usuário abriu a página mas ainda não enviou mensagem.</param>
    public sealed record OnlineWebClientDto(
        string ClientId,
        string WebInstance,
        string Status,
        string? AgentId,
        string? ThreadId,
        DateTime ConnectedAt,
        DateTime LastActivityAt,
        Guid? ConversationId,
        string? Title,
        int MessageCount);
}
