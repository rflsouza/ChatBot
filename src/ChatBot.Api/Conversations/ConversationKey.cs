namespace ChatBot.Api.Conversations;

/// <summary>
/// Identifica uma conversa de forma independente de canal.
/// Ex.: ("telegram", "123456789"), ("rest", "abc"), ("whatsapp", "+55...").
/// </summary>
public readonly record struct ConversationKey(string Channel, string ConversationId, string AgentId)
{
    public override string ToString() => $"{Channel}:{AgentId}:{ConversationId}";
}
