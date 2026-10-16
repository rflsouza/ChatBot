namespace ChatBot.Api.Conversations;

/// <summary>
/// Identifica uma conversa de forma independente de canal.
/// Ex.: ("telegram", "123456789", "assistant"), ("rest", "abc", "").
/// AgentId vazio = agente padrão (AI:DefaultAgent).
/// </summary>
public readonly record struct ConversationKey(string Channel, string ConversationId, string AgentId);
