using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// What the nine settings pages share: the core, the one error line the flyout shows, and the
/// account answer three of them draw from.
/// </summary>
/// <remarks>
/// <para>
/// The account, the reminder settings and the task defaults arrive together — in <c>settings</c>,
/// in <c>refreshSettings</c>, and in every write that answers with the account (<c>updateProfile</c>,
/// <c>updateReminderSettings</c>, <c>updateSmartTaskSettings</c>) — so the answer is read once here
/// and each page takes its slice, rather than three pages each parsing the whole thing and coming
/// to disagree about it.
/// </para>
/// <para>
/// One error line, because the flyout draws one: an operation on any page reports here, and the
/// next that succeeds clears it, exactly as when the pages were one class.
/// </para>
/// </remarks>
public sealed class SettingsSession : ObservableObject
{
    private string? _errorMessage;
    private bool _needsSignIn;

    public SettingsSession(IAstridCore core)
    {
        Core = core;
    }

    public IAstridCore Core { get; }

    /// <summary>What the last failed operation said, on any page; null once one succeeds.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        internal set => Set(ref _errorMessage, value);
    }

    /// <summary>
    /// The session has expired.
    /// </summary>
    /// <remarks>
    /// This screen asks the server for the account, so it notices an expired session as soon as it
    /// opens. "The session is not valid" beside somebody's own name is a worse thing to show than a
    /// sign-in button.
    /// </remarks>
    public bool NeedsSignIn
    {
        get => _needsSignIn;
        private set => Set(ref _needsSignIn, value);
    }

    /// <summary>Raised with every account answer, so each page can take what is its own.</summary>
    public event Action<AccountSettings>? AccountRead;

    /// <summary>Send a command that answers with the account, and share the answer.</summary>
    public async Task<bool> ReadAsync(object command, CancellationToken cancellationToken = default) =>
        Read(await Core.CallAsync(command, cancellationToken));

    /// <summary>Take an account answer: report a failure, or share a success with every page.</summary>
    public bool Read(AstridResponse response)
    {
        if (!response.Ok)
        {
            if (response.NeedsSignIn)
            {
                NeedsSignIn = true;
                ErrorMessage = null;
                return false;
            }
            // Offline is not a failure to report here: what is on screen came from the cache and
            // is still what this account last chose.
            ErrorMessage = response.IsStillPending ? null : response.Error?.Message;
            return false;
        }
        ErrorMessage = null;
        var account = response.Read<AccountSettings>();
        if (account is null)
        {
            return false;
        }
        AccountRead?.Invoke(account);
        return true;
    }
}
