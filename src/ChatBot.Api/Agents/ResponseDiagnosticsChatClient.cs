using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace ChatBot.Api.Agents;

/// <summary>
/// Registra um resumo de cada chamada ao LLM: modelo, finish_reason, tamanho do texto/raciocínio e tempo.
/// Avisa (Warning) quando o modelo termina sem devolver texto nem chamada de ferramenta — o caso de "resposta em branco".
/// </summary>
public sealed class ResponseDiagnosticsChatClient(IChatClient innerClient, ILogger<ResponseDiagnosticsChatClient> logger)
    : DelegatingChatClient(innerClient)
{
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        int textChars = 0, reasoningChars = 0, toolCalls = 0;
        ChatFinishReason? finishReason = null;
        string? modelId = null;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextContent text:
                        textChars += text.Text?.Length ?? 0;
                        break;
                    case TextReasoningContent reasoning:
                        reasoningChars += reasoning.Text?.Length ?? 0;
                        break;
                    case FunctionCallContent:
                        toolCalls++;
                        break;
                }
            }

            finishReason ??= update.FinishReason;
            modelId ??= update.ModelId;
            yield return update;
        }

        if (textChars == 0 && toolCalls == 0)
        {
            logger.LogWarning(
                "LLM {Model} terminou SEM texto. finish_reason={FinishReason}, raciocínio={ReasoningChars} chars, tempo={Elapsed} ms. " +
                "Se finish_reason=length, o modelo gastou o limite de tokens pensando; tente outro modelo.",
                modelId, finishReason?.Value ?? "(nenhum)", reasoningChars, stopwatch.ElapsedMilliseconds);
        }
        else
        {
            logger.LogInformation(
                "LLM {Model}: finish_reason={FinishReason}, texto={TextChars} chars, raciocínio={ReasoningChars} chars, tools={ToolCalls}, tempo={Elapsed} ms",
                modelId, finishReason?.Value ?? "(nenhum)", textChars, reasoningChars, toolCalls, stopwatch.ElapsedMilliseconds);
        }
    }
}
