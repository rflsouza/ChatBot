namespace ChatBot.Api.Mcp;

public static class McpExtensions
{
    /// <summary>Registra o catálogo de servidores MCP (seção "Mcp"). Sem servidores configurados, não faz nada.</summary>
    public static WebApplicationBuilder AddMcpServers(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(McpOptions.SectionName).Get<McpOptions>() ?? new McpOptions();

        builder.Services.AddSingleton(options);
        builder.Services.AddTransient<McpHttpDiagnosticsHandler>();
        builder.Services.AddHttpClient(McpToolProvider.HttpClientName, http =>
            {
                // Streams SSE ficam abertos; o timeout de cada chamada é controlado pelo SDK MCP.
                http.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddHttpMessageHandler<McpHttpDiagnosticsHandler>();
        builder.Services.AddSingleton<McpToolProvider>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<McpToolProvider>());

        return builder;
    }

    /// <summary>GET /api/mcp/servers — situação de cada servidor e suas ferramentas (somente Development).</summary>
    public static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder endpoints, bool isDevelopment)
    {
        if (isDevelopment)
        {
            endpoints.MapGet("/api/mcp/servers", (McpToolProvider mcp) => mcp.GetStatus())
                .WithTags("MCP");
        }

        return endpoints;
    }
}
