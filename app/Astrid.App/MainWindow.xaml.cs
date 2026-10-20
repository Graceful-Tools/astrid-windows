using Microsoft.UI.Xaml;

namespace Astrid.App;

/// <summary>
/// The application window. A frame around <see cref="ShellPage"/> and nothing more.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Not a literal in the XAML: this is the name in the taskbar and in Alt+Tab, so a partner
        // build has to say theirs (task 8d241526). It is still set — the window has a title even
        // with the strip extended, and an untitled one reads as "Astrid.App" to a screen reader.
        Title = BrandCopy.AppName;
        // The app draws its own title bar (Views/TitleBarView): Windows' white strip with the
        // word "Astrid" in it was the one part of the frame that did not wear the theme. The
        // caption buttons stay Windows' own and follow the theme of the content.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(Shell.TitleBarDragRegion);
        // Backgrounding saves (PRODUCT_CONTRACT.md §6): whatever was being edited when the window
        // lost the foreground is committed, so switching apps never loses a half-typed title.
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                _ = Shell.Shell.Detail.CommitAllAsync();
            }
        };
    }
}
