using System.Collections.ObjectModel;
using System.Text.Json;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// The Account page: who is signed in, the profile, verification, the passkeys, the profile
/// numbers, and deleting the account.
/// </summary>
/// <remarks>
/// A change is written through immediately rather than behind a Save button, except the name,
/// which is one thing and goes as one write. There is no draft state to lose otherwise, and a
/// settings screen with a Save button is one where somebody flips a toggle, closes the window, and
/// finds nothing changed.
/// </remarks>
public sealed class AccountSettingsViewModel : ObservableObject
{
    private readonly SettingsSession _session;
    private UserSummary? _user;
    private ProfileStats _stats = new();
    private string _nameDraft = string.Empty;
    private string _deleteConfirmation = string.Empty;
    private bool _verificationSent;
    private bool _isDeleting;
    private string? _passkeysUnavailable;
    private bool _passkeysLoaded;

    public AccountSettingsViewModel(SettingsSession session)
    {
        _session = session;
        _session.AccountRead += account => User = account.User;
    }

    public UserSummary? User
    {
        get => _user;
        private set
        {
            if (Set(ref _user, value))
            {
                Raise(nameof(DisplayName));
                Raise(nameof(Email));
                Raise(nameof(PhotoUrl));
                Raise(nameof(IsVerified));
                Raise(nameof(VerificationKey));
                Raise(nameof(PendingEmail));
                Raise(nameof(HasPendingEmail));
                Raise(nameof(CreatedOn));
                Raise(nameof(UpdatedOn));
                Raise(nameof(AccountId));
                // The name box follows the account — what is typed there is a draft of this, and
                // an edit somebody made on another client should show up here, not be fought.
                NameDraft = value?.Name ?? string.Empty;
            }
        }
    }

    public string DisplayName => User?.DisplayName ?? string.Empty;

    public string Email => User?.Email ?? string.Empty;

    /// <summary>The profile photo's address, when there is one (task 19fd9289).</summary>
    public string? PhotoUrl => string.IsNullOrEmpty(User?.Image) ? null : User.Image;

    /// <summary>Verified outright, or through the provider that signed the account in.</summary>
    public bool IsVerified => User?.Verified == true;

    /// <summary>
    /// The word for the verification state, as a key: verified, verified through a provider, or
    /// not verified. The shell turns it into text.
    /// </summary>
    public string VerificationKey => User?.Verified switch
    {
        true when User.VerifiedViaOAuth == true => "account.verified_via_provider",
        true => "account.verified",
        _ => "account.not_verified",
    };

    /// <summary>A change of address waiting to be confirmed, when there is one.</summary>
    public string? PendingEmail =>
        User?.HasPendingChange == true && !string.IsNullOrEmpty(User.PendingEmail)
            ? User.PendingEmail
            : null;

    public bool HasPendingEmail => PendingEmail is not null;

    /// <summary>When the account was made, as a day in the reader's format.</summary>
    public string CreatedOn => Day(User?.CreatedAt);

    /// <summary>When the account was last changed, likewise.</summary>
    public string UpdatedOn => Day(User?.UpdatedAt);

    public string AccountId => User?.Id ?? string.Empty;

    /// <summary>
    /// The display name as it is being typed. Saved on the button, not on every keystroke: a name
    /// is one thing, and a server that sees "J", "Jo", "Jon" is a server writing three names.
    /// </summary>
    public string NameDraft
    {
        get => _nameDraft;
        set
        {
            if (Set(ref _nameDraft, value))
            {
                Raise(nameof(CanSaveName));
            }
        }
    }

    /// <summary>There is a name to save, and it is not the one the account already has.</summary>
    public bool CanSaveName =>
        !string.IsNullOrWhiteSpace(NameDraft) && NameDraft.Trim() != (User?.Name ?? string.Empty);

    /// <summary>The verification email went out on this visit, so the page can say so.</summary>
    public bool VerificationSent
    {
        get => _verificationSent;
        private set => Set(ref _verificationSent, value);
    }

    /// <summary>
    /// What the server makes somebody type before it deletes their account — the web's phrase,
    /// character for character. Typing it enables the button; the core checks it again.
    /// </summary>
    public const string DeleteConfirmationPhrase = "DELETE MY ACCOUNT";

    /// <summary>What has been typed into the deletion box.</summary>
    public string DeleteConfirmation
    {
        get => _deleteConfirmation;
        set
        {
            if (Set(ref _deleteConfirmation, value))
            {
                Raise(nameof(CanDeleteAccount));
            }
        }
    }

    public bool CanDeleteAccount => DeleteConfirmation == DeleteConfirmationPhrase && !IsDeleting;

    public bool IsDeleting
    {
        get => _isDeleting;
        private set
        {
            if (Set(ref _isDeleting, value))
            {
                Raise(nameof(CanDeleteAccount));
            }
        }
    }

    private static string Day(string? instant) =>
        DateTimeOffset.TryParse(instant, null, System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed.ToLocalTime().ToString("d")
            : string.Empty;

    /// <summary>What this account has finished, inspired and supported.</summary>
    /// <remarks>
    /// Fetched rather than counted here: they are about the whole account across every device, and
    /// a client counting its own cache would answer with whatever it happens to have synced.
    /// </remarks>
    public ProfileStats Stats
    {
        get => _stats;
        private set => Set(ref _stats, value);
    }

    /// <summary>
    /// Fetch the three numbers. Quietly: three numbers missing is not worth a message beside
    /// somebody's own name.
    /// </summary>
    public async Task LoadStatsAsync(CancellationToken cancellationToken = default)
    {
        var stats = await _session.Core.CallAsync(Commands.ProfileStats(), cancellationToken);
        if (stats.Ok && stats.Read<ProfileStats>() is { } read)
        {
            Stats = read;
        }
    }

    // ── Passkeys (task 19fd9289) ─────────────────────────────────────────────────────────────

    /// <summary>The passkeys the account signs in with, newest first.</summary>
    public ObservableCollection<PasskeySummary> Passkeys { get; } = [];

    /// <summary>
    /// Why the list is not there, when it is not: offline, or a server that does not offer passkeys
    /// to apps. Null when the list stands.
    /// </summary>
    public string? PasskeysUnavailable
    {
        get => _passkeysUnavailable;
        private set
        {
            if (Set(ref _passkeysUnavailable, value))
            {
                Raise(nameof(HasPasskeysUnavailable));
            }
        }
    }

    public bool HasPasskeysUnavailable => PasskeysUnavailable is not null;

    public bool PasskeysLoaded
    {
        get => _passkeysLoaded;
        private set
        {
            if (Set(ref _passkeysLoaded, value))
            {
                Raise(nameof(HasNoPasskeys));
            }
        }
    }

    /// <summary>Asked, answered, and none: the "no passkeys yet" line.</summary>
    public bool HasNoPasskeys => PasskeysLoaded && PasskeysUnavailable is null && Passkeys.Count == 0;

    /// <summary>Load the passkeys. Online-only; a refusal is worded, not hidden.</summary>
    public async Task<bool> LoadPasskeysAsync(CancellationToken cancellationToken = default)
    {
        var response = await _session.Core.CallAsync(Commands.Passkeys(), cancellationToken);
        if (!response.Ok)
        {
            PasskeysUnavailable = response.IsStillPending
                ? "Passkeys need a connection."
                : response.Error?.Message ?? "Passkeys could not be loaded.";
            PasskeysLoaded = true;
            Raise(nameof(HasNoPasskeys));
            return false;
        }
        PasskeysUnavailable = null;
        Passkeys.Clear();
        if (response.Value.TryGetProperty("passkeys", out var keys))
        {
            foreach (var key in keys.EnumerateArray())
            {
                if (key.Deserialize<PasskeySummary>(CommandJson.Options) is { } row)
                {
                    Passkeys.Add(row);
                }
            }
        }
        PasskeysLoaded = true;
        Raise(nameof(HasNoPasskeys));
        return true;
    }

    /// <summary>Rename one, then show the list as the server now has it.</summary>
    public async Task<bool> RenamePasskeyAsync(string id, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }
        var response = await _session.Core.CallAsync(Commands.RenamePasskey(id, name.Trim()), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.Error?.Message;
            return false;
        }
        _session.ErrorMessage = null;
        return await LoadPasskeysAsync(cancellationToken);
    }

    /// <summary>Revoke one, then show the list as the server now has it.</summary>
    public async Task<bool> RevokePasskeyAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _session.Core.CallAsync(Commands.RevokePasskey(id), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.Error?.Message;
            return false;
        }
        _session.ErrorMessage = null;
        return await LoadPasskeysAsync(cancellationToken);
    }

    // ── The profile (task 19fd9289) ──────────────────────────────────────────────────────────

    /// <summary>Save the display name as typed.</summary>
    public async Task<bool> SaveNameAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSaveName)
        {
            return false;
        }
        return _session.Read(await _session.Core.CallAsync(
            Commands.UpdateProfile(NameDraft.Trim(), null), cancellationToken));
    }

    /// <summary>Put a picture from this machine on the profile.</summary>
    public async Task<bool> SetPhotoAsync(string path, CancellationToken cancellationToken = default) =>
        _session.Read(await _session.Core.CallAsync(Commands.UpdateProfile(null, path), cancellationToken));

    /// <summary>Ask for the verification email again.</summary>
    public async Task<bool> ResendVerificationAsync(CancellationToken cancellationToken = default)
    {
        var response = await _session.Core.CallAsync(Commands.ResendVerification(), cancellationToken);
        if (!response.Ok)
        {
            VerificationSent = false;
            _session.ErrorMessage = response.IsStillPending
                ? "Sending the email needs a connection."
                : response.Error?.Message;
            return false;
        }
        _session.ErrorMessage = null;
        VerificationSent = true;
        return true;
    }

    /// <summary>
    /// Delete the account for good. True when it is gone — the caller then shows the door,
    /// because the core has already signed out.
    /// </summary>
    public async Task<bool> DeleteAccountAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDeleteAccount)
        {
            return false;
        }
        IsDeleting = true;
        try
        {
            var response = await _session.Core.CallAsync(
                Commands.DeleteAccount(DeleteConfirmation), cancellationToken);
            if (!response.Ok)
            {
                _session.ErrorMessage = response.IsStillPending
                    ? "Deleting the account needs a connection."
                    : response.Error?.Message;
                return false;
            }
            _session.ErrorMessage = null;
            DeleteConfirmation = string.Empty;
            return true;
        }
        finally
        {
            IsDeleting = false;
        }
    }
}
