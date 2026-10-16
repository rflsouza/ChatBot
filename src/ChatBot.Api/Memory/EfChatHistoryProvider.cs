using System.Text.Json;
using ChatBot.Api.Data;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace ChatBot.Api.Memory;

/// <summary>
/// Memória (histórico) dos agentes persistida com EF Core na tabela <c>Messages</c>.
/// <para>
/// O Agent Framework chama <see cref="ProvideChatHistoryAsync"/> antes de cada execução (para montar o contexto
/// enviado ao LLM) e <see cref="StoreChatHistoryAsync"/> depois (com as mensagens do usuário e as respostas).
/// A mesma tabela serve, portanto, de <b>memória</b> do agente e de <b>registro</b> de tudo que foi conversado.
/// </para>
/// A sessão (<see cref="AgentSession"/>) guarda apenas o <see cref="State.ConversationId"/>.
/// </summary>
public sealed class EfChatHistoryProvider : ChatHistoryProvider
{
    // O Agent Framework serializa o estado com options.GetTypeInfo(...), que exige um TypeInfoResolver
    // explícito (um JsonSerializerOptions "cru" falha com "TypeInfoResolver of type '<null>'").
    private static readonly JsonSerializerOptions StateJsonOptions = MemoryJsonContext.Default.Options;

    private readonly IDbContextFactory<ChatBotDbContext> _dbFactory;
    private readonly ProviderSessionState<State> _sessionState;
    private IReadOnlyList<string>? _stateKeys;

    public EfChatHistoryProvider(IDbContextFactory<ChatBotDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
        _sessionState = new ProviderSessionState<State>(
            _ => new State(Guid.NewGuid()),          // nova sessão => nova conversa
            nameof(EfChatHistoryProvider),
            StateJsonOptions);
    }

    /// <summary>Limite de mensagens enviadas ao LLM como contexto (null = todas). Mensagens antigas continuam no banco.</summary>
    public int? MaxMessagesInContext { get; init; }

    public override IReadOnlyList<string> StateKeys => _stateKeys ??= [_sessionState.StateKey];

    /// <summary>Id da conversa associada à sessão (cria um, se a sessão for nova).</summary>
    public Guid GetConversationId(AgentSession? session) => _sessionState.GetOrInitializeState(session).ConversationId;

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        var conversationId = GetConversationId(context.Session);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Messages.AsNoTracking().Where(m => m.ConversationId == conversationId);
        List<string> payloads;
        if (MaxMessagesInContext is { } max)
        {
            payloads = await query.OrderByDescending(m => m.Id).Take(max).Select(m => m.Payload).ToListAsync(cancellationToken);
            payloads.Reverse();
        }
        else
        {
            payloads = await query.OrderBy(m => m.Id).Select(m => m.Payload).ToListAsync(cancellationToken);
        }

        var messages = payloads
            .Select(p => JsonSerializer.Deserialize<ChatMessage>(p, AIJsonUtilities.DefaultOptions))
            .OfType<ChatMessage>()
            .ToList();

        // Ao cortar o histórico, não podemos começar no meio de uma chamada de ferramenta
        // (o LLM rejeita um resultado de tool sem a chamada correspondente): começa na 1ª mensagem do usuário.
        if (MaxMessagesInContext is not null)
        {
            var firstUser = messages.FindIndex(m => m.Role == ChatRole.User);
            if (firstUser > 0)
            {
                messages.RemoveRange(0, firstUser);
            }
        }

        return messages;
    }

    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        var newMessages = context.RequestMessages.Concat(context.ResponseMessages ?? []).ToList();
        if (newMessages.Count == 0)
        {
            return;
        }

        var conversationId = GetConversationId(context.Session);
        var now = DateTime.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var conversation = await db.Conversations.FindAsync([conversationId], cancellationToken);
        if (conversation is null)
        {
            conversation = new Conversation
            {
                Id = conversationId,
                AgentId = context.Agent.Name ?? context.Agent.Id,
                CreatedAt = now,
            };
            db.Conversations.Add(conversation);
        }

        conversation.Title ??= Truncate(newMessages.FirstOrDefault(m => m.Role == ChatRole.User)?.Text, 200);
        conversation.UpdatedAt = now;

        foreach (var message in newMessages)
        {
            db.Messages.Add(new ConversationMessage
            {
                ConversationId = conversationId,
                Author = ToAuthor(message.Role),
                AuthorName = message.AuthorName,
                Text = message.Text,
                Payload = JsonSerializer.Serialize(message, AIJsonUtilities.DefaultOptions),
                MessageId = message.MessageId,
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static MessageAuthor ToAuthor(ChatRole role) =>
        role == ChatRole.User ? MessageAuthor.User
        : role == ChatRole.Assistant ? MessageAuthor.Assistant
        : role == ChatRole.System ? MessageAuthor.System
        : MessageAuthor.Tool;

    private static string? Truncate(string? text, int max) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>Estado guardado dentro da <see cref="AgentSession"/>.</summary>
    public sealed record State(Guid ConversationId);
}

/// <summary>Metadados JSON gerados em tempo de compilação para o estado da sessão.</summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[System.Text.Json.Serialization.JsonSerializable(typeof(EfChatHistoryProvider.State))]
internal sealed partial class MemoryJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
