using ChatBot.Api.Data;
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
            group.MapGet("/", async (string? channel, int? take,
                IDbContextFactory<ChatBotDbContext> dbFactory, CancellationToken ct) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var query = db.Conversations.AsNoTracking();
                if (!string.IsNullOrWhiteSpace(channel))
                {
                    query = query.Where(c => c.Channel == channel);
                }

                return await query
                    .OrderByDescending(c => c.UpdatedAt)
                    .Take(Math.Clamp(take ?? 50, 1, 500))
                    .Select(c => new ConversationDto(c.Id, c.AgentId, c.Channel, c.SessionKey, c.UserId, c.Title,
                        c.Messages.Count, c.CreatedAt, c.UpdatedAt))
                    .ToListAsync(ct);
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
        string? Title, int MessageCount, DateTime CreatedAt, DateTime UpdatedAt);
}
