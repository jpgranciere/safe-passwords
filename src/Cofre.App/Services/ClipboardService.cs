using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Cofre.App.Services;

/// <summary>
/// Copies secrets to the clipboard, keeps them out of Windows clipboard history / cloud sync, and wipes them
/// after a delay (only if the clipboard still holds what we put there).
/// </summary>
internal sealed class ClipboardService
{
    private readonly DispatcherTimer _timer = new();
    private string? _ownedText;

    public ClipboardService()
    {
        _timer.Tick += (_, _) => ClearIfOwned();
    }

    public void CopySensitive(string text, int clearAfterSeconds)
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(BitConverter.GetBytes(1)));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));

        Retry(() => Clipboard.SetDataObject(data, copy: true));
        _ownedText = text;

        _timer.Stop();
        if (clearAfterSeconds > 0)
        {
            _timer.Interval = TimeSpan.FromSeconds(clearAfterSeconds);
            _timer.Start();
        }
    }

    public void ClearIfOwned()
    {
        _timer.Stop();
        if (_ownedText is null) return;

        try
        {
            if (Clipboard.ContainsText() && Clipboard.GetText() == _ownedText)
                Retry(Clipboard.Clear);
        }
        catch (COMException)
        {
            // The clipboard is held by another process; we'll leave it alone rather than block.
        }
        finally
        {
            _ownedText = null;
        }
    }

    private static void Retry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (COMException) when (attempt < 5)
            {
                Thread.Sleep(25); // CLIPBRD_E_CANT_OPEN: someone else has the clipboard open right now
            }
        }
    }
}
