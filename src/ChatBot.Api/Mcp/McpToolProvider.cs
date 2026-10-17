using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace ChatBot.Api.Mcp;

/// <summary>
/// Mantém uma conexão com cada servidor MCP configurado e a lista de ferramentas de cada um.
/// <list type="bullet">
/// <item>Conecta em segundo plano na inicialização (a Api sobe mesmo se um MCP estiver fora).</item>
/// <item>Tenta de novo os que falharam e recarrega as ferramentas a cada <c>Mcp:RefreshIntervalSeconds</c>.</item>
/// <item>As ferramentas entram nos agentes a cada execução (via <see cref="McpContextProvider"/>), então um MCP
///       que conecta depois passa a valer sem reiniciar.</item>
/// </list>
/// </summary>
public sealed partial class McpToolProvider(
    McpOptions options,
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory,
    ILogger<McpToolProvider> logger) : BackgroundService
{
    public const string HttpClientName = "mcp";

    private readonly ConcurrentDictionary<string, ServerState> _servers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Servidores habilitados na configuração.</summary>
    public IEnumerable<string> ConfiguredServers =>
        options.Servers.Where(s => s.Value.Enabled).Select(s => s.Key);

    /// <summary>
    /// Ferramentas dos servidores indicados (null = todos os habilitados).
    /// Nomes são ajustados para o padrão aceito pelos LLMs e desduplicados entre servidores.
    /// </summary>
    public IList<AITool> GetTools(IReadOnlyCollection<string>? servers)
    {
        var result = new List<AITool>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, state) in _servers.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (servers is not null && !servers.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var tool in state.Tools)
            {
                var toolName = tool.Name;
                if (!usedNames.Add(toolName))
                {
                    // Conflito com ferramenta de outro servidor: prefixa com o nome do servidor.
                    toolName = ToolName($"{SanitizeName(name)}_{tool.ProtocolTool.Name}");
                    if (!usedNames.Add(toolName))
                    {
                        continue;
                    }
                }

                result.Add(toolName == tool.Name ? tool : tool.WithName(toolName));
            }
        }

        return result;
    }

    /// <summary>Instruções que os servidores MCP publicam (se houver).</summary>
    public string? GetInstructions(IReadOnlyCollection<string>? servers)
    {
        var parts = _servers
            .Where(s => servers is null || servers.Contains(s.Key, StringComparer.OrdinalIgnoreCase))
            .Where(s => !string.IsNullOrWhiteSpace(s.Value.Instructions))
            .Select(s => $"[MCP {s.Key}] {Truncate(s.Value.Instructions!, 2000)}")
            .ToList();

        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    /// <summary>Situação de cada servidor (para o endpoint de diagnóstico). Nunca expõe headers/segredos.</summary>
    public IReadOnlyList<McpServerStatus> GetStatus() =>
        options.Servers
            .OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            .Select(s =>
            {
                _servers.TryGetValue(s.Key, out var state);
                return new McpServerStatus(
                    s.Key,
                    s.Value.Type,
                    s.Value.Enabled ? state?.Status ?? "connecting" : "disabled",
                    state?.ServerName,
                    state?.Error,
                    state?.UpdatedAt,
                    state?.Tools.Select(t => t.Name).ToList() ?? []);
            })
            .ToList();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = options.Servers.Where(s => s.Value.Enabled).ToList();
        if (enabled.Count == 0)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(30, options.RefreshIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.WhenAll(enabled.Select(s => RefreshAsync(s.Key, s.Value, stoppingToken)));

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        foreach (var state in _servers.Values)
        {
            await DisposeClientAsync(state.Client);
        }
    }

    private async Task RefreshAsync(string name, McpServerOptions server, CancellationToken stoppingToken)
    {
        _servers.TryGetValue(name, out var current);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, options.ConnectTimeoutSeconds)));

        McpClient? client = current?.Client;
        try
        {
            client ??= await McpClient.CreateAsync(CreateTransport(name, server), loggerFactory: loggerFactory,
                cancellationToken: timeout.Token);

            var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token))
                .Where(t => server.AllowedTools.Count == 0
                            || server.AllowedTools.Contains(t.ProtocolTool.Name, StringComparer.OrdinalIgnoreCase))
                .Select(t => t.WithName(ToolName(string.IsNullOrWhiteSpace(server.ToolPrefix)
                    ? t.ProtocolTool.Name
                    : $"{server.ToolPrefix}_{t.ProtocolTool.Name}")))
                .ToList();

            var isNew = current?.Status != "connected";
            _servers[name] = new ServerState(client, tools, "connected", client.ServerInfo?.Name,
                client.ServerInstructions, null, DateTime.UtcNow);

            if (isNew || current!.Tools.Count != tools.Count)
            {
                logger.LogInformation("MCP {Server}: conectado ({ServerName}), {Count} ferramenta(s): {Tools}",
                    name, client.ServerInfo?.Name, tools.Count, string.Join(", ", tools.Select(t => t.Name)));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // Descarta a conexão; na próxima rodada conecta do zero.
            await DisposeClientAsync(client);
            _servers[name] = new ServerState(null, [], "error", current?.ServerName, null,
                ex is OperationCanceledException ? $"timeout ({options.ConnectTimeoutSeconds} s)" : ex.Message,
                DateTime.UtcNow);

            if (current?.Status != "error")
            {
                logger.LogWarning("MCP {Server}: falha ao conectar/listar ferramentas ({Message}). Nova tentativa em {Seconds} s.",
                    name, ex.Message, Math.Max(30, options.RefreshIntervalSeconds));
            }
        }
    }

    private IClientTransport CreateTransport(string name, McpServerOptions server)
    {
        switch (server.Type.Trim().ToLowerInvariant())
        {
            case "stdio":
                if (string.IsNullOrWhiteSpace(server.Command))
                {
                    throw new InvalidOperationException($"Mcp:Servers:{name}:Command é obrigatório para type=stdio.");
                }

                return new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = name,
                    Command = server.Command,
                    Arguments = server.Args,
                    EnvironmentVariables = server.Env.ToDictionary(e => e.Key, e => (string?)e.Value),
                }, loggerFactory);

            case "http" or "streamable-http" or "streamablehttp" or "sse":
                if (!Uri.TryCreate(server.Url, UriKind.Absolute, out var url))
                {
                    throw new InvalidOperationException($"Mcp:Servers:{name}:Url inválida.");
                }

                return new HttpClientTransport(new HttpClientTransportOptions
                {
                    Name = name,
                    Endpoint = url,
                    TransportMode = server.Type.Trim().ToLowerInvariant() switch
                    {
                        "sse" => HttpTransportMode.Sse,
                        "http" => HttpTransportMode.AutoDetect,
                        _ => HttpTransportMode.StreamableHttp,
                    },
                    AdditionalHeaders = server.Headers.Count > 0 ? new Dictionary<string, string>(server.Headers) : null,
                    ConnectionTimeout = TimeSpan.FromSeconds(Math.Max(5, options.ConnectTimeoutSeconds)),
                }, httpClientFactory.CreateClient(HttpClientName), loggerFactory);

            default:
                throw new InvalidOperationException(
                    $"Mcp:Servers:{name}:Type \"{server.Type}\" não suportado. Use http, streamable-http, sse ou stdio.");
        }
    }

    private static async Task DisposeClientAsync(McpClient? client)
    {
        if (client is null) return;
        try { await client.DisposeAsync(); }
        catch { /* conexão já quebrada */ }
    }

    /// <summary>LLMs aceitam nomes de ferramenta com [a-zA-Z0-9_-], até 64 caracteres.</summary>
    private static string ToolName(string name)
    {
        var sanitized = SanitizeName(name);
        return sanitized.Length <= 64 ? sanitized : sanitized[..64];
    }

    private static string SanitizeName(string name) => InvalidToolChars().Replace(name, "_");

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    [GeneratedRegex("[^a-zA-Z0-9_-]")]
    private static partial Regex InvalidToolChars();

    private sealed record ServerState(
        McpClient? Client,
        IReadOnlyList<McpClientTool> Tools,
        string Status,
        string? ServerName,
        string? Instructions,
        string? Error,
        DateTime UpdatedAt);
}

public sealed record McpServerStatus(
    string Name,
    string Type,
    string Status,
    string? ServerName,
    string? Error,
    DateTime? UpdatedAt,
    IReadOnlyList<string> Tools);
