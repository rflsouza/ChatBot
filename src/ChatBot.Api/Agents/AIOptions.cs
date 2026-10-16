namespace ChatBot.Api.Agents;

/// <summary>Configuração da seção "AI" do appsettings.</summary>
public sealed class AIOptions
{
    public const string SectionName = "AI";

    public ProviderOptions Provider { get; set; } = new();

    /// <summary>Agente usado quando o canal não especifica um.</summary>
    public string DefaultAgent { get; set; } = "assistant";

    /// <summary>
    /// Máximo de mensagens do histórico enviadas ao LLM em cada turno (null = todas).
    /// Todas continuam salvas no banco; isso só limita o contexto (custo/tokens).
    /// </summary>
    public int? MaxMessagesInContext { get; set; }

    public List<AgentDefinition> Agents { get; set; } = [];
}

/// <summary>
/// Qualquer endpoint compatível com a API da OpenAI:
/// OpenAI, Azure OpenAI / Microsoft Foundry (endpoint /openai/v1), GitHub Models, Ollama, LM Studio...
/// </summary>
public sealed class ProviderOptions
{
    public string Endpoint { get; set; } = "https://api.openai.com/v1";

    /// <summary>Chave de API. Deixe vazio e use <see cref="UseEntraId"/> para Azure com identidade gerenciada.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Autentica via Microsoft Entra ID (DefaultAzureCredential) em vez de chave.</summary>
    public bool UseEntraId { get; set; }
}

/// <summary>Um agente = modelo + instruções + ferramentas. Cada um vira um endpoint/opção no chat.</summary>
public sealed class AgentDefinition
{
    public required string Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public required string Model { get; set; }
    public string Instructions { get; set; } = "Você é um assistente prestativo. Responda em português do Brasil.";
    public bool EnableTools { get; set; } = true;
}
