using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace ChatBot.Api.Agents;

/// <summary>
/// Registra um resumo de cada chamada ao LLM (modelo, finish_reason, tamanho do texto/raciocínio e tempo) e trata
/// o caso de "resposta em branco": tenta de novo até <c>retriesOnEmpty</c> vezes e, se continuar vazio,
/// devolve um aviso com o diagnóstico (visível no chat web e no Telegram).
/// </summary>
public sealed class ResponseDiagnosticsChatClient(
    IChatClient innerClient,
    ILogger<ResponseDiagnosticsChatClient> logger,
    int retriesOnEmpty = 1) : DelegatingChatClient(innerClient)
{
    /// <summary>Prefixo dos avisos gerados aqui; essas mensagens não voltam para o LLM como histórico.</summary>
    public const string DiagnosticPrefix = "⚠️ [diagnóstico]";

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageList = messages as IList<ChatMessage> ?? messages.ToList();

        for (var attempt = 1; ; attempt++)
        {
            var stopwatch = Stopwatch.StartNew();
            int textChars = 0, reasoningChars = 0, toolCalls = 0;
            ChatFinishReason? finishReason = null;
            string? modelId = null;

            await foreach (var update in base.GetStreamingResponseAsync(messageList, options, cancellationToken))
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextContent text:
                            textChars += text.Text?.Trim().Length ?? 0;
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

            var reason = finishReason?.Value ?? "(nenhum)";
            if (textChars > 0 || toolCalls > 0)
            {
                logger.LogInformation(
                    "LLM {Model}: finish_reason={FinishReason}, texto={TextChars} chars, raciocínio={ReasoningChars} chars, tools={ToolCalls}, tempo={Elapsed} ms, tentativa={Attempt}",
                    modelId, reason, textChars, reasoningChars, toolCalls, stopwatch.ElapsedMilliseconds, attempt);
                yield break;
            }

            logger.LogWarning(
                "LLM {Model} terminou SEM texto (tentativa {Attempt}/{Max}). finish_reason={FinishReason}, raciocínio={ReasoningChars} chars, tempo={Elapsed} ms.",
                modelId, attempt, retriesOnEmpty + 1, reason, reasoningChars, stopwatch.ElapsedMilliseconds);

            if (attempt > retriesOnEmpty)
            {
                var hint = reason == "length"
                    ? "o modelo atingiu o limite de tokens (provavelmente gastou tudo \"pensando\")"
                    : reasoningChars > 0
                        ? "o modelo só gerou raciocínio, sem resposta final"
                        : "o provedor devolveu uma resposta vazia (comum em modelos gratuitos sobrecarregados)";

                // MessageId próprio => vira uma mensagem separada no histórico (e é filtrada do contexto do LLM).
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                    $"{DiagnosticPrefix} O modelo `{modelId}` não devolveu texto após {attempt} tentativa(s): {hint}. " +
                    $"finish_reason={reason}, raciocínio={reasoningChars} caracteres, {stopwatch.ElapsedMilliseconds} ms. " +
                    "Tente de novo ou troque de modelo.")
                {
                    MessageId = $"diagnostic-{Guid.NewGuid():N}",
                    ModelId = modelId,
                };
                yield break;
            }
        }
    }
}
