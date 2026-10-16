using ChatBot.Api.Agents.Tools;
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

        foreach (var definition in options.Agents)
        {
            builder.AddAIAgent(definition.Id, (services, name) =>
            {
                IChatClient chatClient = ChatClientFactory.Create(options.Provider, definition.Model, services);

                return chatClient.AsAIAgent(new ChatClientAgentOptions
                {
                    Name = name,
                    Description = definition.Description,
                    ChatOptions = new ChatOptions
                    {
                        Instructions = definition.Instructions,
                        Tools = definition.EnableTools ? BuiltInTools.All() : null,
                    },
                }, services: services);
            });
        }

        return builder;
    }
}
