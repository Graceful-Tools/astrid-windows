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
