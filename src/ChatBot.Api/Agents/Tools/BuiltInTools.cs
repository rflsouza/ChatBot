using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace ChatBot.Api.Agents.Tools;

/// <summary>Ferramentas (function calling) executadas no backend — valem para todos os canais.</summary>
public static class BuiltInTools
{
    [Description("Retorna a data e hora atuais no fuso horário informado (IANA, ex.: America/Sao_Paulo).")]
    public static string GetCurrentDateTime(
        [Description("Fuso horário IANA. Padrão: America/Sao_Paulo")] string timeZone = "America/Sao_Paulo")
    {
        var tz = TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var found) ? found : TimeZoneInfo.Utc;
        return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).ToString("yyyy-MM-dd HH:mm:ss zzz");
    }

    public static IList<AITool> All() =>
    [
        AIFunctionFactory.Create(GetCurrentDateTime, name: "get_current_datetime"),
    ];
}
