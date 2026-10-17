using Microsoft.Agents.AI;

namespace ChatBot.Api.Mcp;

/// <summary>
/// <see cref="AIContextProvider"/> do Agent Framework: a cada execução do agente acrescenta as ferramentas
/// (e instruções) dos servidores MCP permitidos para ele. Como é avaliado por execução, um MCP que conecta
/// ou muda de ferramentas depois do startup passa a valer imediatamente.
/// </summary>
public sealed class McpContextProvider(McpToolProvider mcp, IReadOnlyCollection<string>? servers) : AIContextProvider
{
    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var tools = mcp.GetTools(servers);
        return ValueTask.FromResult(new AIContext
        {
            Tools = tools.Count > 0 ? tools : null,
            Instructions = tools.Count > 0 ? mcp.GetInstructions(servers) : null,
        });
    }
}
