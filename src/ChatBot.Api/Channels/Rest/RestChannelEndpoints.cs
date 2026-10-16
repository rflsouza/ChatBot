using ChatBot.Api.Conversations;

namespace ChatBot.Api.Channels.Rest;

/// <summary>
/// Canal REST genérico, com histórico no servidor. Serve para integrações simples, testes
/// e é exatamente o mesmo fluxo que o canal Telegram vai usar (via <see cref="IConversationService"/>).
/// </summary>
public static class RestChannelEndpoints
{
    private const string Channel = "rest";

    public static IEndpointRouteBuilder MapRestChannel(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/channels/rest/conversations/{conversationId}")
            .WithTags("REST channel");

        group.MapPost("/messages", async (string conversationId, SendMessageRequest request,
            IConversationService conversations, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return Results.BadRequest("Text é obrigatório.");
            }

            var key = new ConversationKey(Channel, conversationId, request.AgentId ?? string.Empty);
            var reply = await conversations.SendAsync(key, request.Text, ct);
            return Results.Ok(new SendMessageResponse(conversationId, reply));
        });

        group.MapDelete("/", async (string conversationId, string? agentId,
            IConversationService conversations, CancellationToken ct) =>
        {
            await conversations.ResetAsync(new ConversationKey(Channel, conversationId, agentId ?? string.Empty), ct);
            return Results.NoContent();
        });

        return endpoints;
    }

    public sealed record SendMessageRequest(string Text, string? AgentId = null);

    public sealed record SendMessageResponse(string ConversationId, string Reply);
}
