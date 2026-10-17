using ChatBot.Api.Agents.Tools;
using ChatBot.Api.Data;
using ChatBot.Api.Mcp;
using ChatBot.Api.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;

namespace ChatBot.Api.Agents;

/// <summary>
/// Núcleo independente de canal: registra cada agente configurado como <see cref="AIAgent"/>
/// keyed no DI (Microsoft Agent Framework Hosting). Web, REST, Telegram etc. apenas consomem esses agentes.
/// </summary>
public static class AgentRegistration
{
    public static WebApplicationBuilder AddChatAgents(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(AIOptions.SectionName).Get<AIOptions>() ?? new AIOptions();
        if (options.Agents.Count == 0)
        {
            throw new InvalidOperationException("Nenhum agente configurado em AI:Agents.");
        }

        builder.Services.AddSingleton(options);

        // Memória (EF Core): histórico na tabela Messages, sessão na tabela Conversations.
        builder.Services.AddSingleton(sp => new EfChatHistoryProvider(
            sp.GetRequiredService<IDbContextFactory<ChatBotDbContext>>())
        {
            MaxMessagesInContext = options.MaxMessagesInContext,
        });
        builder.Services.AddSingleton<EfAgentSessionStore>();

        foreach (var definition in options.Agents)
        {
            builder
                .AddAIAgent(definition.Id, (services, name) =>
                {
                    IChatClient chatClient = ChatClientFactory.Create(options.Provider, definition.Model, services);

                    return chatClient.AsAIAgent(new ChatClientAgentOptions
                    {
                        Name = name,
                        Description = definition.Description,
                        ChatHistoryProvider = services.GetRequiredService<EfChatHistoryProvider>(),
                        // Ferramentas MCP entram a cada execução (servidor que conecta depois passa a valer).
                        AIContextProviders = definition.EnableTools && UsesMcp(definition)
                            ? new AIContextProvider[] { new McpContextProvider(services.GetRequiredService<McpToolProvider>(), McpServersOf(definition)) }
                            : null,
                        ChatOptions = new ChatOptions
                        {
                            Instructions = definition.Instructions,
                            Tools = definition.EnableTools ? BuiltInTools.All() : null,
                        },
                    }, services: services);
                })
                // Sessões persistidas no SQLite. Isolamento por usuário fica a cargo do MapAGUIServer
                // (ativado automaticamente quando houver um AgentIsolationKeyProvider/autenticação).
                .WithSessionStore((services, _) => services.GetRequiredService<EfAgentSessionStore>(), withIsolation: false);
        }

        return builder;
    }

    /// <summary>null ou ["*"] = todos os servidores MCP; [] = nenhum.</summary>
    private static IReadOnlyCollection<string>? McpServersOf(AgentDefinition definition) =>
        definition.McpServers is null || definition.McpServers.Contains("*") ? null : definition.McpServers;

    private static bool UsesMcp(AgentDefinition definition) => definition.McpServers is not { Count: 0 };
}
