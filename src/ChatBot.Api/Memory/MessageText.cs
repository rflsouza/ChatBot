using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ChatBot.Api.Memory;

/// <summary>
/// Texto gravado na coluna <c>Messages.Text</c>. Mensagens normais usam o próprio texto; chamadas e resultados de
/// ferramentas (que não têm texto) ganham um resumo legível, só para registro interno:
/// <code>
/// Assistant: [functionCall] person_search {"name":"Rafael Souza"}
/// Tool:      [functionResult] person_search succeeded: true (1234 chars)
/// </code>
/// Esses resumos NÃO são enviados ao LLM (a memória usa o Payload) nem exibidos ao usuário (o endpoint de histórico
/// filtra pelos prefixos).
/// </summary>
public static class MessageText
{
    public const string FunctionCallPrefix = "[functionCall]";
    public const string FunctionResultPrefix = "[functionResult]";

    public static string? Describe(ChatMessage message, IReadOnlyDictionary<string, string> callNames)
    {
        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            return message.Text;
        }

        var parts = new List<string>();
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case FunctionCallContent call:
                    parts.Add($"{FunctionCallPrefix} {call.Name} {Truncate(Arguments(call), 300)}");
                    break;

                case FunctionResultContent result:
                    var name = callNames.TryGetValue(result.CallId, out var n) ? n : $"(callId {result.CallId})";
                    var succeeded = result.Exception is null && !IsErrorResult(result.Result);
                    var detail = result.Exception is not null
                        ? $" error: {Truncate(result.Exception.Message, 300)}"
                        : $" ({ResultLength(result.Result)} chars)";
                    parts.Add($"{FunctionResultPrefix} {name} succeeded: {(succeeded ? "true" : "false")}{detail}");
                    break;
            }
        }

        return parts.Count == 0 ? message.Text : string.Join("\n", parts);
    }

    private static string Arguments(FunctionCallContent call)
    {
        if (call.Arguments is not { Count: > 0 } args)
        {
            return "{}";
        }

        try
        {
            return JsonSerializer.Serialize(args, AIJsonUtilities.DefaultOptions);
        }
        catch
        {
            return "{" + string.Join(", ", args.Keys) + "}";
        }
    }

    /// <summary>Resultado de MCP com isError = true (JSON ou objeto CallToolResult).</summary>
    private static bool IsErrorResult(object? result) => result switch
    {
        JsonElement { ValueKind: JsonValueKind.Object } json =>
            (json.TryGetProperty("isError", out var e) || json.TryGetProperty("IsError", out e)) && e.ValueKind == JsonValueKind.True,
        null => false,
        _ => result.GetType().GetProperty("IsError")?.GetValue(result) is true,
    };

    private static int ResultLength(object? result) => result switch
    {
        null => 0,
        string s => s.Length,
        JsonElement json => json.GetRawText().Length,
        _ => SafeSerialize(result).Length,
    };

    private static string SafeSerialize(object value)
    {
        try { return JsonSerializer.Serialize(value, AIJsonUtilities.DefaultOptions); }
        catch { return value.ToString() ?? ""; }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    public static bool IsInternal(string? text) =>
        text is not null && (text.StartsWith(FunctionCallPrefix, StringComparison.Ordinal)
                             || text.StartsWith(FunctionResultPrefix, StringComparison.Ordinal));
}
