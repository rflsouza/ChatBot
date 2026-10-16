namespace ChatBot.Api.Conversations;

/// <summary>
/// Identifica uma conversa de forma independente de canal.
/// Ex.: ("telegram", "123456789", "assistant", "telegram:987"), ("rest", "abc", "").
/// AgentId vazio = agente padrão (AI:DefaultAgent).
/// UserId (opcional) é gravado na coluna Conversations.UserId — quem está falando no canal.
/// </summary>
public readonly record struct ConversationKey(string Channel, string ConversationId, string AgentId, string? UserId = null);
