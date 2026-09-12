using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// The account flyout: nine pages, one view model each, and what they share.
/// </summary>
/// <remarks>
/// <para>
/// This used to be one class of nine screens. It is now a façade over one view model per page —
/// the same nine the flyout's XAML is split into under <c>Views/Settings/</c> — and holds only
/// what the flyout itself draws: the pages, whether the account is being caught up, the one error
/// line, and whether the session has expired. Each page binds to its own view model
/// (<c>Shell.Settings.Account.DisplayName</c>, <c>Shell.Settings.Reminders.PushEnabled</c>).
/// </para>
/// <para>
/// Drawn from the cache and caught up afterwards, like everything else here. The settings
/// themselves are the server's and are shared with every client, which is why nothing about their
/// meaning is decided in any of these classes; and a change is written through immediately rather
/// than behind a Save button, because a settings screen with a Save button is one where somebody
/// flips a toggle, closes the window, and finds nothing changed.
/// </para>
/// </remarks>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsSession _session;
    private bool _isLoading;

    public SettingsViewModel(IAstridCore core)
    {
        _session = new SettingsSession(core);
        Account = new AccountSettingsViewModel(_session);
        Tasks = new TaskSettingsViewModel(_session);
        Contacts = new ContactsSettingsViewModel(_session);
        Reminders = new ReminderSettingsViewModel(_session);
        Appearance = new AppearanceSettingsViewModel(_session);
        Agents = new AgentSettingsViewModel(_session);
        ApiAccess = new ApiAccessViewModel(_session);
        Integrations = new IntegrationsSettingsViewModel(_session);
        Data = new DataSettingsViewModel(_session);
        // The error line and the sign-in flag are the session's; the flyout binds to them here.
        _session.PropertyChanged += (_, changed) => Raise(changed.PropertyName);
    }

    /// <summary>The Account page: the profile, verification, passkeys, deleting the account.</summary>
    public AccountSettingsViewModel Account { get; }

    /// <summary>The task settings: the email defaults, the layout, subtasks, smart parsing.</summary>
    public TaskSettingsViewModel Tasks { get; }

    /// <summary>The Contacts page.</summary>
    public ContactsSettingsViewModel Contacts { get; }

    /// <summary>The Reminders page.</summary>
    public ReminderSettingsViewModel Reminders { get; }

    /// <summary>The Appearance page's own two settings: the theme and the quick-add chord.</summary>
    public AppearanceSettingsViewModel Appearance { get; }

    /// <summary>The AI agents page: the hub, the credentials, Copilot, the webhook, custom agents.</summary>
    public AgentSettingsViewModel Agents { get; }

    /// <summary>The API access page.</summary>
    public ApiAccessViewModel ApiAccess { get; }

    /// <summary>The Integrations page's own setting: how Google lists get linked.</summary>
    public IntegrationsSettingsViewModel Integrations { get; }

    /// <summary>The Your data page.</summary>
    public DataSettingsViewModel Data { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    /// <summary>What the last failed operation on any page said; null once one succeeds.</summary>
    public string? ErrorMessage => _session.ErrorMessage;

    /// <summary>The session has expired; see <see cref="SettingsSession.NeedsSignIn"/>.</summary>
    public bool NeedsSignIn => _session.NeedsSignIn;

    /// <summary>Read the account from the cache, then catch it up.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _session.ReadAsync(Commands.Settings(), cancellationToken);
        IsLoading = true;
        try
        {
            await _session.ReadAsync(Commands.RefreshSettings(), cancellationToken);
            // After the account, because it needs to know who is signed in — and quietly, because
            // three numbers missing is not worth a message beside somebody's own name.
            await Account.LoadStatsAsync(cancellationToken);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
