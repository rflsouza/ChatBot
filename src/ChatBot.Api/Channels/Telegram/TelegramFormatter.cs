using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ChatBot.Api.Channels.Telegram;

/// <summary>
/// Converte o Markdown que os LLMs geram para o subconjunto de HTML aceito pelo Telegram
/// (b, i, code, pre, a) e divide textos maiores que o limite de 4096 caracteres por mensagem.
/// Se o Telegram rejeitar o HTML, o processor reenvia como texto puro.
/// </summary>
public static partial class TelegramFormatter
{
    /// <summary>Limite do Telegram é 4096; deixamos folga para as tags HTML.</summary>
    public const int ChunkSize = 3500;

    public static IEnumerable<string> Split(string text, int maxLength = ChunkSize)
    {
        text = text.Trim();
        while (text.Length > maxLength)
        {
            var cut = text.LastIndexOf("\n\n", maxLength, StringComparison.Ordinal);
            if (cut < maxLength / 2) cut = text.LastIndexOf('\n', maxLength);
            if (cut < maxLength / 2) cut = text.LastIndexOf(' ', maxLength);
            if (cut <= 0) cut = maxLength;

            yield return text[..cut].TrimEnd();
            text = text[cut..].TrimStart();
        }

        if (text.Length > 0)
        {
            yield return text;
        }
    }

    public static string ToHtml(string markdown)
    {
        var placeholders = new List<string>();
        string Hold(string html)
        {
            placeholders.Add(html);
            return $"\u0000{placeholders.Count - 1}\u0000";
        }

        // 1. Blocos de código e código inline saem do texto antes de qualquer outra regra.
        var text = CodeBlock().Replace(markdown, m =>
        {
            var lang = m.Groups[1].Value;
            var code = WebUtility.HtmlEncode(m.Groups[2].Value.TrimEnd('\n'));
            return Hold(lang.Length > 0
                ? $"<pre><code class=\"language-{lang}\">{code}</code></pre>"
                : $"<pre>{code}</pre>");
        });
        text = InlineCode().Replace(text, m => Hold($"<code>{WebUtility.HtmlEncode(m.Groups[1].Value)}</code>"));

        // 2. Escapa o resto e aplica as marcações.
        text = WebUtility.HtmlEncode(text);
        text = Heading().Replace(text, "<b>$1</b>");
        text = Bullet().Replace(text, "$1• ");
        text = Bold().Replace(text, "<b>$1</b>");
        text = BoldUnderscore().Replace(text, "<b>$1</b>");
        text = Italic().Replace(text, "<i>$1</i>");
        text = Link().Replace(text, "<a href=\"$2\">$1</a>");

        // 3. Devolve os trechos de código.
        var result = new StringBuilder(text);
        for (var i = 0; i < placeholders.Count; i++)
        {
            result.Replace($"\u0000{i}\u0000", placeholders[i]);
        }

        return result.ToString();
    }

    [GeneratedRegex(@"```([\w+#-]*)[^\S\n]*\n?([\s\S]*?)```")]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"`([^`\n]+)`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"^#{1,6}\s+(.+?)\s*#*$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(\s*)[-*+]\s+", RegexOptions.Multiline)]
    private static partial Regex Bullet();

    [GeneratedRegex(@"\*\*(?=\S)(.+?)(?<=\S)\*\*")]
    private static partial Regex Bold();

    [GeneratedRegex(@"(?<!\w)__(?=\S)(.+?)(?<=\S)__(?!\w)")]
    private static partial Regex BoldUnderscore();

    [GeneratedRegex(@"(?<![\*\w])\*(?=\S)([^*\n]+?)(?<=\S)\*(?![\*\w])")]
    private static partial Regex Italic();

    [GeneratedRegex(@"\[([^\]\n]+)\]\((https?://[^\s)]+)\)")]
    private static partial Regex Link();
}
