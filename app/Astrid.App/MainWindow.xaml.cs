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
