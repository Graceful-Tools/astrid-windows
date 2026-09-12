using System.Diagnostics;
using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Astrid.App.Views.Settings;

/// <summary>
/// The Your data page: export the account as JSON or CSV.
/// </summary>
public sealed partial class DataSection : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(DataSection),
        new PropertyMetadata(null));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before the control loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public DataSection()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Write everything this account has to a file the person chooses.
    /// </summary>
    /// <remarks>
    /// The save dialog is the shell's job and the writing is the core's: the bytes never cross the
    /// boundary, because an export is somebody's entire history and a JSON round trip of it would
    /// be work for its own sake.
    /// </remarks>
    private async void OnExportAccount(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is not string format)
        {
            return;
        }
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedFileName = $"astrid-export-{DateTime.Now:yyyy-MM-dd}",
        };
        picker.FileTypeChoices.Add(
            format == "csv" ? "Comma-separated values" : "JSON",
            new List<string> { format == "csv" ? ".csv" : ".json" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            await Shell.Settings.ExportAsync(format, file.Path);
        }
    }
}
