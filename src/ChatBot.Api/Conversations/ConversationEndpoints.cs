using ChatBot.Api.Data;
using ChatBot.Api.Presence;
using Microsoft.EntityFrameworkCore;

namespace ChatBot.Api.Conversations;

/// <summary>Leitura das conversas salvas (para o frontend recarregar o histórico e para consulta).</summary>
public static class ConversationEndpoints
{
    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder endpoints, bool isDevelopment)
    {
        var group = endpoints.MapGroup("/api/conversations").WithTags("Conversations");

        // Mensagens da conversa ativa identificada por (agente, sessionId).
        // Web: sessionId = threadId do AG-UI. Ex.: GET /api/conversations/assistant/3f2c.../messages
        group.MapGet("/{agentId}/{sessionId}/messages", async (string agentId, string sessionId,
            IDbContextFactory<ChatBotDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var conversationId = await db.Conversations.AsNoTracking()
                .Where(c => c.AgentId == agentId && c.SessionKey == sessionId)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);

            if (conversationId is null)
            {
                return Results.Ok(Array.Empty<MessageDto>());
            }

            var messages = await db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == conversationId
                            && (m.Author == MessageAuthor.User || m.Author == MessageAuthor.Assistant)
                            && m.Text != null && m.Text != "")
                .OrderBy(m => m.Id)
                .Select(m => new MessageDto(m.Author.ToString(), m.AuthorName, m.Text!, m.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(messages);
        });

        if (isDevelopment)
        {
            // ⚠️ Somente em Development: lista conversas de TODOS os canais/usuários.
            // ?online=true  => só conversas web abertas agora em algum navegador
            // ?online=false => todas as outras (inclusive de canais sem conexão, como Telegram/REST)
            group.MapGet("/", async (string? channel, bool? online, int? take,
                IDbContextFactory<ChatBotDbContext> dbFactory, WebPresenceTracker presence, CancellationToken ct) =>
            {
                var open = presence.GetOpenConversations();

                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var query = db.Conversations.AsNoTracking();
                if (!string.IsNullOrWhiteSpace(channel))
                {
                    query = query.Where(c => c.Channel == channel);
                }

                if (online == true)
                {
                    var threadIds = open.Select(o => o.ThreadId).Distinct().ToList();
                    query = query.Where(c => c.SessionKey != null && threadIds.Contains(c.SessionKey));
                }

                var rows = await query
                    .OrderByDescending(c => c.UpdatedAt)
                    .Take(Math.Clamp(take ?? 50, 1, 500))
                    .Select(c => new
                    {
                        c.Id, c.AgentId, c.Channel, c.SessionKey, c.UserId, c.Title,
                        MessageCount = c.Messages.Count, c.CreatedAt, c.UpdatedAt,
                    })
                    .ToListAsync(ct);

                return rows
                    .Select(c => new ConversationDto(c.Id, c.AgentId, c.Channel, c.SessionKey, c.UserId, c.Title,
                        c.MessageCount, c.CreatedAt, c.UpdatedAt,
                        // Só o canal web tem "conexão"; nos outros canais não se aplica (null).
                        IsConnected: c.Channel == "web"
                            ? c.SessionKey is not null && open.Contains((c.AgentId, c.SessionKey))
                            : null))
                    .Where(c => online is null || (c.IsConnected ?? false) == online)
                    .ToList();
            });

            // ⚠️ Somente em Development: todas as mensagens de uma conversa, incluindo ferramentas.
            group.MapGet("/{conversationId:guid}", async (Guid conversationId,
                IDbContextFactory<ChatBotDbContext> dbFactory, CancellationToken ct) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                return await db.Messages.AsNoTracking()
                    .Where(m => m.ConversationId == conversationId)
                    .OrderBy(m => m.Id)
                    .Select(m => new MessageDto(m.Author.ToString(), m.AuthorName, m.Text ?? "", m.CreatedAt))
                    .ToListAsync(ct);
            });
        }

        return endpoints;
    }

    public sealed record MessageDto(string Author, string? AuthorName, string Text, DateTime CreatedAt);

    public sealed record ConversationDto(Guid Id, string AgentId, string Channel, string? SessionKey, string? UserId,
        string? Title, int MessageCount, DateTime CreatedAt, DateTime UpdatedAt, bool? IsConnected);
}
