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

        OpenAIClient openAIClient;
        if (provider.UseEntraId)
        {
            // OPENAI001: o construtor com AuthenticationPolicy é marcado como [Experimental] no SDK OpenAI,
            // mas é o caminho recomendado pela Microsoft para Entra ID no endpoint /openai/v1 do Azure.
            // Em produção prefira uma credencial específica (ex.: ManagedIdentityCredential).
#pragma warning disable OPENAI001
            openAIClient = new OpenAIClient(
                new BearerTokenPolicy(new DefaultAzureCredential(), "https://ai.azure.com/.default"), clientOptions);
#pragma warning restore OPENAI001
        }
        else
        {
            if (string.IsNullOrWhiteSpace(provider.ApiKey))
            {
                throw new InvalidOperationException(
                    "Configure AI:Provider:ApiKey (ex.: dotnet user-secrets set \"AI:Provider:ApiKey\" \"<chave>\") ou AI:Provider:UseEntraId=true.");
            }

            openAIClient = new OpenAIClient(new ApiKeyCredential(provider.ApiKey), clientOptions);
        }

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
