using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
// Alias: o namespace OpenAI.Chat inteiro conflita com tipos do Microsoft.Extensions.AI (ChatMessage, ChatFinishReason...).
using StreamingChatCompletionUpdate = OpenAI.Chat.StreamingChatCompletionUpdate;

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
            int textChars = 0, reasoningChars = 0, toolCalls = 0, chunks = 0;
            ChatFinishReason? finishReason = null;
            string? modelId = null;
            ProviderError? providerError = null;
            string? lastRawChunk = null;

            await foreach (var update in base.GetStreamingResponseAsync(messageList, options, cancellationToken))
            {
                chunks++;
                if (update.RawRepresentation is StreamingChatCompletionUpdate raw)
                {
                    lastRawChunk = ReadRaw(raw);
                    providerError ??= TryReadError(lastRawChunk);
                }

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
                "LLM {Model} terminou SEM texto (tentativa {Attempt}/{Max}). finish_reason={FinishReason}, raciocínio={ReasoningChars} chars, " +
                "chunks={Chunks}, tempo={Elapsed} ms, erro do provedor={ProviderError}, último chunk={RawChunk}",
                modelId, attempt, retriesOnEmpty + 1, reason, reasoningChars, chunks, stopwatch.ElapsedMilliseconds,
                providerError?.ToString() ?? "(nenhum)", Truncate(lastRawChunk, 1000) ?? "(nenhum)");

            // Limite de uso (429) não melhora repetindo na hora.
            var isRateLimit = providerError?.Code is "429" || providerError?.Message?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) is true;

            if (attempt > retriesOnEmpty || isRateLimit)
            {
                if (providerError is not null)
                {
                    yield return Diagnostic(modelId,
                        $"{DiagnosticPrefix} O provedor retornou um erro para o modelo `{modelId}`: {providerError}. " +
                        (isRateLimit
                            ? "É limite de uso — comum em modelos gratuitos (por minuto/dia). Aguarde ou troque de modelo."
                            : "Tente de novo ou troque de modelo."));
                    yield break;
                }

                var hint = reason == "length"
                    ? "o modelo atingiu o limite de tokens (provavelmente gastou tudo \"pensando\")"
                    : reasoningChars > 0
                        ? "o modelo só gerou raciocínio, sem resposta final"
                        : "o provedor devolveu uma resposta vazia (comum em modelos gratuitos sobrecarregados)";

                yield return Diagnostic(modelId,
                    $"{DiagnosticPrefix} O modelo `{modelId}` não devolveu texto após {attempt} tentativa(s): {hint}. " +
                    $"finish_reason={reason}, raciocínio={reasoningChars} caracteres, {stopwatch.ElapsedMilliseconds} ms. " +
                    "Tente de novo ou troque de modelo.");
                yield break;
            }
        }
    }

    /// <summary>MessageId próprio => vira uma mensagem separada no histórico (e é filtrada do contexto do LLM).</summary>
    private static ChatResponseUpdate Diagnostic(string? modelId, string text) =>
        new(ChatRole.Assistant, text)
        {
            MessageId = $"diagnostic-{Guid.NewGuid():N}",
            ModelId = modelId,
        };

    /// <summary>JSON do chunk como veio do provedor (o SDK preserva campos desconhecidos, como "error").</summary>
    private static string? ReadRaw(StreamingChatCompletionUpdate raw)
    {
        try
        {
            return ModelReaderWriter.Write(raw).ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>OpenRouter e outros enviam erros no meio do stream: {"error": {"code": 429, "message": "..."}}.</summary>
    private static ProviderError? TryReadError(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("error", out var error))
            {
                return null;
            }

            if (error.ValueKind == JsonValueKind.String)
            {
                return new ProviderError(null, error.GetString());
            }

            string? code = error.TryGetProperty("code", out var c) ? c.ToString() : null;
            string? message = error.TryGetProperty("message", out var m) ? m.ToString() : error.GetRawText();

            // OpenRouter detalha o erro do provedor final em metadata.raw.
            if (error.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("raw", out var detail))
            {
                message += $" ({Truncate(detail.ToString(), 300)})";
            }

            return new ProviderError(code, message);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Truncate(string? text, int max) =>
        text is null || text.Length <= max ? text : text[..max] + "…";

    private sealed record ProviderError(string? Code, string? Message)
    {
        public override string ToString() => Code is null ? Message ?? "" : $"[{Code}] {Message}";
    }
}
