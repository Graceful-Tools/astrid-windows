using Windows.ApplicationModel.DataTransfer;

namespace Astrid.App.Views;

/// <summary>Put a piece of text on the clipboard, if there is one.</summary>
internal static class ClipboardText
{
    internal static void Copy(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
