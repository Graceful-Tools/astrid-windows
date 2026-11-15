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

    /// <param name="core">The core every page sends its commands to.</param>
    /// <param name="passkeys">
    /// Windows Hello, for the one page that needs the platform (task e61a3e60). Optional, and a
    /// ceremony that cannot run by default: a test gets a predictable Account page without an
    /// authenticator, and a machine whose Hello the shell could not reach says so rather than
    /// spending a challenge it cannot answer.
    /// </param>
    public SettingsViewModel(IAstridCore core, IPasskeyCeremony? passkeys = null)
    {
        _session = new SettingsSession(core);
        Account = new AccountSettingsViewModel(_session, passkeys);
        Tasks = new TaskSettingsViewModel(_session);
        Contacts = new ContactsSettingsViewModel(_session);
        Reminders = new ReminderSettingsViewModel(_session);
        Appearance = new AppearanceSettingsViewModel(_session);
        Agents = new AgentSettingsViewModel(_session);
        Connections = new ConnectionsViewModel(_session);
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

    /// <summary>The Connections page: what can act as the account, and the OAuth clients made by hand.</summary>
    public ConnectionsViewModel Connections { get; }

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

    /// <summary>
    /// Read the account from the cache alone (task ffcc6abf).
    /// </summary>
    /// <remarks>
    /// What the sidebar's footer entry needs: web's pattern names the signed-in user there, and
    /// the window has to say who that is without waiting for the screen the entry leads to. The
    /// cache read only — the catch-up in <see cref="LoadAsync"/> is a round trip, and this one
    /// happens before the first paint.
    /// </remarks>
    public Task LoadAccountAsync(CancellationToken cancellationToken = default) =>
        _session.ReadAsync(Commands.Settings(), cancellationToken);

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

    /// <summary>
    /// Where <b>Report an Issue</b> goes: this deployment's own feedback list, or the web when it
    /// names none (task 68771137).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The decision is the core's and arrives as an <c>Option&lt;String&gt;</c> —
    /// <c>brand.feedbackListId</c> from the cached capabilities, which the core already trims and
    /// answers as nothing when it is blank. Nothing here repeats that test, and nothing here holds
    /// a list id: iOS's bug (task <c>a4352b30</c>) was Astrid's own production id compiled into
    /// the client, and a "sensible fallback" in this shell would be the same bug in C#.
    /// </para>
    /// <para>
    /// Since task <c>6fc92134</c> there are two kinds of door, so this answers a decision rather
    /// than a URL. In-app needs two things to be true, and only the core can say either: the
    /// deployment names a list, and <c>publicList</c> opens it for this reader with
    /// <c>canAddTask</c>. A <c>copy_only</c> feedback list means a stranger may not write in it,
    /// and landing somebody on a screen where the report cannot be filed is worse than the
    /// browser — so that falls back, as does a list the core cannot open at all.
    /// </para>
    /// <para>
    /// A core that cannot answer — a pin that predates the command, a cache never filled, no
    /// network — is the same as a deployment naming none. The row is a door, and the web is always
    /// a door; a row that does nothing when clicked is the worse answer.
    /// </para>
    /// </remarks>
    /// <param name="serverOrigin">The deployment this window is signed in to.</param>
    public async Task<FeedbackDestination> FeedbackDestinationAsync(
        string serverOrigin, CancellationToken cancellationToken = default)
    {
        var named = await _session.Core.CallAsync(Commands.FeedbackListId(), cancellationToken);
        var listId = named.Ok ? named.ValueAs<string>() : null;
        var origin = serverOrigin.TrimEnd('/');
        if (listId is null)
        {
            return FeedbackDestination.OnTheWeb($"{origin}/help");
        }

        var opened = await _session.Core.CallAsync(Commands.PublicList(listId), cancellationToken);
        var canAddTask = opened.Ok && opened.Read<OpenedPublicList>()?.CanAddTask == true;
        return canAddTask
            ? FeedbackDestination.InApp(listId)
            : FeedbackDestination.OnTheWeb($"{origin}/lists/{Uri.EscapeDataString(listId)}");
    }
}

/// <summary>
/// Where <b>Report an Issue</b> goes (task 6fc92134): the feedback list on this screen, or an
/// address in the browser. Exactly one of the two is set.
/// </summary>
/// <param name="ListId">The list to open in-app, or null.</param>
/// <param name="WebUrl">The address to hand to the browser, or null.</param>
public sealed record FeedbackDestination(string? ListId, string? WebUrl)
{
    public static FeedbackDestination InApp(string listId) => new(listId, null);

    public static FeedbackDestination OnTheWeb(string url) => new(null, url);

    public bool IsInApp => ListId is not null;
}
