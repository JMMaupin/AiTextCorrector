using System.Globalization;
using System.Linq;
using System.Text;

namespace AiTextCorrector.Services;

public class TextTransformService
{
    public string ToPlainUnicodeText(string? text)
    {
        return (text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Trim();
    }

    public string ToAsciiPlainText(string? text)
    {
        var normalized = ToPlainUnicodeText(text).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return new string(builder.ToString().Where(ch => ch <= 127).ToArray())
            .Normalize(NormalizationForm.FormC);
    }
}
