using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>The Your data page: export everything this account has to a file.</summary>
/// <remarks>
/// The save dialog is the shell's job and the writing is the core's: the bytes never cross the
/// boundary, because an export is somebody's entire history and a JSON round trip of it would be
/// work for its own sake.
/// </remarks>
public sealed class DataSettingsViewModel : ObservableObject
{
    private readonly SettingsSession _session;
    private string? _lastExportPath;

    public DataSettingsViewModel(SettingsSession session)
    {
        _session = session;
    }

    /// <summary>Where the last export was written, once one has been.</summary>
    public string? LastExportPath
    {
        get => _lastExportPath;
        private set => Set(ref _lastExportPath, value);
    }

    /// <summary>Write everything this account has to a file.</summary>
    public async Task<bool> ExportAsync(string format, string path,
        CancellationToken cancellationToken = default)
    {
        var response = await _session.Core.CallAsync(
            Commands.ExportAccount(format, path), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.IsStillPending
                ? "An export needs a connection."
                : response.Error?.Message;
            return false;
        }
        _session.ErrorMessage = null;
        LastExportPath = path;
        return true;
    }
}
