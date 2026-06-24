using System.Threading;
using System.Windows.Forms;

namespace AiTextCorrector.Services;

public class ClipboardService
{
    public string TryReadText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public void WritePlainText(string? text)
    {
        try
        {
            Clipboard.Clear();
            Clipboard.SetText(text ?? string.Empty, TextDataFormat.UnicodeText);
        }
        catch
        {
        }
    }

    public void SimulatePaste()
    {
        SendKeys.SendWait("^v");
        Thread.Sleep(60);
    }
}
