namespace ChatBot.Api.Mcp;

/// <summary>
/// Loga cada requisição HTTP feita aos servidores MCP: método, URL, status e os headers enviados
/// (valores sensíveis mascarados). Debug para sucesso; Warning para status de erro.
/// Ative com "Logging:LogLevel:ChatBot.Api.Mcp": "Debug".
/// </summary>
public sealed class McpHttpDiagnosticsHandler(ILogger<McpHttpDiagnosticsHandler> logger) : DelegatingHandler
{
    private static readonly string[] SensitiveMarkers = ["key", "token", "auth", "secret", "password", "cookie"];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        var status = (int)response.StatusCode;
        // GET com 405 é normal no Streamable HTTP: o servidor só não oferece o stream opcional.
        var expected = response.IsSuccessStatusCode || (request.Method == HttpMethod.Get && status == 405);
        var level = expected ? LogLevel.Debug : LogLevel.Warning;

        if (logger.IsEnabled(level))
        {
            logger.Log(level, "MCP HTTP {Method} {Url} -> {Status}. Headers enviados: {Headers}",
                request.Method, request.RequestUri, status, DescribeHeaders(request));
        }

        return response;
    }

    public static string DescribeHeaders(HttpRequestMessage request)
    {
        var headers = request.Headers
            .Where(h => !h.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase)
                        && !h.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
            .Select(h => $"{h.Key}={Mask(h.Key, string.Join(",", h.Value))}")
            .ToList();
        return headers.Count == 0 ? "(nenhum)" : string.Join("; ", headers);
    }

    public static string Mask(string name, string? value)
    {
        if (string.IsNullOrEmpty(value)) return "(VAZIO)";
        if (!SensitiveMarkers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase))) return value;
        return value.Length <= 4 ? $"***({value.Length})" : $"{value[..2]}***({value.Length} chars)";
    }
}
