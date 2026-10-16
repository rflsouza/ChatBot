namespace ChatBot.Api.Data;

/// <summary>Uma conversa (de qualquer canal) com um agente.</summary>
public sealed class Conversation
{
    public Guid Id { get; set; }

    /// <summary>Agente que atende a conversa (ex.: "assistant").</summary>
    public required string AgentId { get; set; }

    /// <summary>Canal de origem: web, rest, telegram...</summary>
    public string Channel { get; set; } = "web";

    /// <summary>
    /// Chave usada pelo <c>AgentSessionStore</c> para achar a sessão ativa:
    /// threadId do AG-UI (web) ou "canal:id" (ex.: "telegram:123456").
    /// Fica <c>null</c> quando a conversa é encerrada ("nova conversa"), preservando o histórico.
    /// </summary>
    public string? SessionKey { get; set; }

    /// <summary>Usuário dono da conversa (preenchido quando houver autenticação).</summary>
    public string? UserId { get; set; }

    /// <summary>Primeira mensagem do usuário, resumida — útil para listar conversas.</summary>
    public string? Title { get; set; }

    /// <summary><c>AgentSession</c> serializada (JSON). Contém o estado dos providers, não as mensagens.</summary>
    public string? SessionState { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<ConversationMessage> Messages { get; set; } = [];
}

/// <summary>Cada mensagem trocada: o que o usuário enviou, o que o agente respondeu, chamadas de ferramenta...</summary>
public sealed class ConversationMessage
{
    public long Id { get; set; }

    public Guid ConversationId { get; set; }
    public Conversation? Conversation { get; set; }

    /// <summary>Quem escreveu a mensagem.</summary>
    public MessageAuthor Author { get; set; }

    /// <summary>Nome do autor quando houver (ex.: nome do agente).</summary>
    public string? AuthorName { get; set; }

    /// <summary>Texto puro da mensagem (para leitura/consulta). Pode ser vazio em chamadas de ferramenta.</summary>
    public string? Text { get; set; }

    /// <summary><c>ChatMessage</c> completa em JSON (inclui tool calls/resultados) — é o que alimenta a memória do agente.</summary>
    public required string Payload { get; set; }

    public string? MessageId { get; set; }

    public DateTime CreatedAt { get; set; }
}

public enum MessageAuthor
{
    User,
    Assistant,
    System,
    Tool,
}
