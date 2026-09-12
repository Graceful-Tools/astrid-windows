using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// The Integrations page's own setting: how Google lists get linked. (Copilot is drawn on the same
/// page and read from the Agent Hub, so it lives with <see cref="AgentSettingsViewModel"/>.)
/// </summary>
/// <remarks>
/// Account-wide, not per list: the modes make counterparts for every list on both sides, so the
/// choice lives beside the account rather than in one list's settings.
/// </remarks>
public sealed class IntegrationsSettingsViewModel : ObservableObject
{
    private readonly SettingsSession _session;
    private string _googleSyncMode = "manual";

    public IntegrationsSettingsViewModel(SettingsSession session)
    {
        _session = session;
    }

    /// <summary>
    /// How Google lists get linked: <c>manual</c>, or one of the three all-lists modes.
    /// </summary>
    public string GoogleSyncMode
    {
        get => _googleSyncMode;
        private set => Set(ref _googleSyncMode, value);
    }

    /// <summary>Read back how this account links Google lists.</summary>
    /// <remarks>
    /// Read rather than remembered: the choice belongs to the account, so a machine that assumed
    /// its own last answer would show the wrong one to somebody who changed it elsewhere.
    /// </remarks>
    public async Task LoadGoogleSyncModeAsync(CancellationToken cancellationToken = default)
    {
        var response = await _session.Core.CallAsync(Commands.GoogleSyncMode(), cancellationToken);
        if (!response.Ok)
        {
            return;
        }
        if (response.Value.TryGetProperty("mode", out var mode) && mode.GetString() is { } value)
        {
            GoogleSyncMode = value;
        }
    }

    /// <summary>Choose how Google lists get linked.</summary>
    public async Task<bool> SetGoogleSyncModeAsync(string mode,
        CancellationToken cancellationToken = default)
    {
        var response = await _session.Core.CallAsync(
            Commands.SetGoogleSyncMode(mode), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.IsStillPending
                ? "Changing how lists link needs a connection."
                : response.Error?.Message;
            return false;
        }
        await LoadGoogleSyncModeAsync(cancellationToken);
        return true;
    }
}
