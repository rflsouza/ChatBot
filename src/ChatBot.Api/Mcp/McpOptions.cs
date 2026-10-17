namespace ChatBot.Api.Mcp;

/// <summary>
/// Seção "Mcp": catálogo de servidores MCP disponíveis para os agentes.
/// Mesmo formato usado por VS Code / Claude / Kiro ("type", "url", "headers", "command", "args", "env").
/// </summary>
public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    /// <summary>Nome do servidor → configuração. O nome é usado em AI:Agents:n:McpServers.</summary>
    public Dictionary<string, McpServerOptions> Servers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tempo máximo para conectar e listar ferramentas de cada servidor.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 30;

    /// <summary>De quanto em quanto tempo tentar de novo um servidor que falhou (e recarregar a lista de ferramentas).</summary>
    public int RefreshIntervalSeconds { get; set; } = 300;
}

public sealed class McpServerOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// "http" (detecta Streamable HTTP ou SSE), "streamable-http", "sse" ou "stdio" (processo local).
    /// </summary>
    public string Type { get; set; } = "http";

    /// <summary>URL do servidor (http/sse).</summary>
    public string? Url { get; set; }

    /// <summary>Headers enviados em toda chamada (http/sse). Ex.: X-API-KEY, Authorization, X-TENANT-ID.</summary>
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Executável (stdio). Ex.: "npx", "uvx", "dotnet".</summary>
    public string? Command { get; set; }

    /// <summary>Argumentos (stdio).</summary>
    public List<string> Args { get; set; } = [];

    /// <summary>Variáveis de ambiente do processo (stdio).</summary>
    public Dictionary<string, string> Env { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Se preenchido, só essas ferramentas do servidor ficam disponíveis.</summary>
    public List<string> AllowedTools { get; set; } = [];

    /// <summary>Prefixo para o nome das ferramentas (evita conflito entre servidores). Ex.: "xgen" → "xgen_buscar".</summary>
    public string? ToolPrefix { get; set; }
}
