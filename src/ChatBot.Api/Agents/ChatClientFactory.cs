using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.Identity;
using Microsoft.Extensions.AI;
using OpenAI;

namespace ChatBot.Api.Agents;

/// <summary>Cria um <see cref="IChatClient"/> (Microsoft.Extensions.AI) para um modelo do provedor configurado.</summary>
public static class ChatClientFactory
{
    public static IChatClient Create(ProviderOptions provider, string model, IServiceProvider services)
    {
        var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(provider.Endpoint) };

        OpenAIClient openAIClient = provider.UseEntraId
            // Em produção prefira uma credencial específica (ex.: ManagedIdentityCredential).
            ? new OpenAIClient(new BearerTokenPolicy(new DefaultAzureCredential(), "https://ai.azure.com/.default"), clientOptions)
            : new OpenAIClient(new ApiKeyCredential(provider.ApiKey
                ?? throw new InvalidOperationException("Configure AI:Provider:ApiKey ou AI:Provider:UseEntraId=true.")), clientOptions);

        // Chat Completions = máxima compatibilidade (OpenAI, Azure, GitHub Models, Ollama...).
        return openAIClient
            .GetChatClient(model)
            .AsIChatClient()
            .AsBuilder()
            .UseLogging()
            .UseOpenTelemetry(sourceName: "ChatBot.Api", configure: c => c.EnableSensitiveData = false)
            .Build(services);
    }
}
