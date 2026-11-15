using System.Text.Json;
using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;
using static Astrid.App.Tests.SettingsFixtures;

namespace Astrid.App.Tests;

/// <summary>The Account page: the profile, verification, passkeys, the numbers, deletion.</summary>
public sealed class AccountSettingsViewModelTests
{
    /// <summary>
    /// Passkeys are listed as the server has them, a rename or revoke reloads the list, an empty
    /// name is refused without asking, and a server without the route is worded rather than shown
    /// as nothing (task 19fd9289).
    /// </summary>
    [Fact]
    public async Task Passkeys_are_listed_renamed_revoked_and_a_missing_route_is_worded_task_19fd9289()
    {
        var core = new FakeCore()
            .AnswerOk("passkeys", new { passkeys = new[]
            {
                new { id = "k1", name = (string?)"MacBook", createdAt = "2026-08-01T00:00:00Z", isSynced = true },
                new { id = "k2", name = (string?)null, createdAt = "2026-07-01T00:00:00Z", isSynced = false },
            } })
            .AnswerOk("renamePasskey")
            .AnswerOk("passkeys", new { passkeys = new[] { new { id = "k1", name = "Laptop", createdAt = "2026-08-01T00:00:00Z", isSynced = true } } })
            .AnswerOk("revokePasskey")
            .AnswerOk("passkeys", new { passkeys = Array.Empty<object>() })
            .AnswerFailure("passkeys", AstridFailureKind.Refused, "This server does not offer passkeys to apps yet; manage them on the web.");
        var view = new SettingsViewModel(core).Account;
        Assert.False(view.HasNoPasskeys, "not asked yet is not none");

        Assert.True(await view.LoadPasskeysAsync());
        Assert.Equal(2, view.Passkeys.Count);
        Assert.Equal("MacBook", view.Passkeys[0].Label);
        Assert.Equal("Passkey", view.Passkeys[1].Label);
        Assert.True(view.Passkeys[0].IsSynced);

        Assert.False(await view.RenamePasskeyAsync("k1", "   "));
        Assert.DoesNotContain(core.SentKinds(), kind => kind == "renamePasskey");

        Assert.True(await view.RenamePasskeyAsync("k1", " Laptop "));
        Assert.Single(view.Passkeys);
        Assert.Equal("Laptop", view.Passkeys[0].Label);
        Assert.Contains(core.Sent, json => json.Contains("\"kind\":\"renamePasskey\"") && json.Contains("\"name\":\"Laptop\""));

        Assert.True(await view.RevokePasskeyAsync("k1"));
        Assert.Empty(view.Passkeys);
        Assert.True(view.HasNoPasskeys);

        Assert.False(await view.LoadPasskeysAsync());
        Assert.True(view.HasPasskeysUnavailable);
        Assert.Contains("does not offer passkeys", view.PasskeysUnavailable);
        Assert.False(view.HasNoPasskeys);
    }

    /// <summary>
    /// The account's own state reads off the user the core answers with: verification as a key
    /// for the shell to word, the pending address, the dates as days, and the id (task 19fd9289).
    /// </summary>
    [Fact]
    public async Task The_account_page_reads_verification_and_dates_off_the_user_task_19fd9289()
    {
        var core = new FakeCore()
            .AnswerOk("settings", AccountOf(new
            {
                id = "me", name = "Jon", email = "jon@x.io", image = "https://blob.test/me.png",
                verified = false, hasPendingChange = true, pendingEmail = "new@x.io",
                createdAt = "2026-01-02T03:04:05Z", updatedAt = "2026-09-01T00:00:00Z",
            }))
            .AnswerOk("refreshSettings", AccountOf(new
            {
                id = "me", name = "Jon", email = "jon@x.io", image = (string?)null,
                verified = true, verifiedViaOAuth = true, hasPendingChange = false,
                createdAt = "2026-01-02T03:04:05Z", updatedAt = "2026-09-01T00:00:00Z",
            }))
            .AnswerOk("profileStats", new { completed = 1, inspired = 2, supported = 3 });
        var settings = new SettingsViewModel(core);
        var view = settings.Account;

        // The cache first: not verified, with an address waiting.
        await settings.LoadAsync();
        // After the refresh: verified through the provider, nothing waiting, no photo.
        Assert.True(view.IsVerified);
        Assert.Equal("account.verified_via_provider", view.VerificationKey);
        Assert.False(view.HasPendingEmail);
        Assert.Null(view.PhotoUrl);
        Assert.Equal("me", view.AccountId);
        Assert.NotEqual(string.Empty, view.CreatedOn);
        Assert.Equal("Jon", view.NameDraft);
        Assert.False(view.CanSaveName, "the name on the account is not a change");

        var cached = new SettingsViewModel(new FakeCore().AnswerOk("settings", AccountOf(new
        {
            id = "me", name = "Jon", email = "jon@x.io",
            verified = false, hasPendingChange = true, pendingEmail = "new@x.io",
        })));
        await cached.LoadAsync();
        Assert.False(cached.Account.IsVerified);
        Assert.Equal("account.not_verified", cached.Account.VerificationKey);
        Assert.Equal("new@x.io", cached.Account.PendingEmail);
        Assert.True(cached.Account.HasPendingEmail);
    }

    /// <summary>
    /// The profile is saved on the button: a changed name goes as one write and the screen
    /// redraws from the answer; a photo goes by path for the core to upload (task 19fd9289).
    /// </summary>
    [Fact]
    public async Task Saving_the_name_or_a_photo_goes_through_one_profile_command_task_19fd9289()
    {
        var core = new FakeCore()
            .AnswerOk("settings", AccountOf(new { id = "me", name = "Jon", email = "jon@x.io" }))
            .AnswerOk("updateProfile", AccountOf(new { id = "me", name = "Jon P", email = "jon@x.io" }))
            .AnswerOk("updateProfile", AccountOf(new
            {
                id = "me", name = "Jon P", email = "jon@x.io", image = "https://blob.test/me.png",
            }));
        var settings = new SettingsViewModel(core);
        var view = settings.Account;
        await settings.LoadAsync();

        view.NameDraft = " Jon P ";
        Assert.True(view.CanSaveName);
        Assert.True(await view.SaveNameAsync());
        Assert.Equal("Jon P", view.DisplayName);
        Assert.False(view.CanSaveName, "saved, so nothing left to save");
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"updateProfile\"") && json.Contains("\"name\":\"Jon P\"")
            && !json.Contains("photoPath"));

        Assert.True(await view.SetPhotoAsync(@"C:\Pictures\me.png"));
        Assert.Equal("https://blob.test/me.png", view.PhotoUrl);
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"updateProfile\"") && json.Contains("me.png")
            && !json.Contains("\"name\""));
    }

    /// <summary>
    /// Resend says so when it went, and says why when it did not; deleting is gated on the exact
    /// phrase and reports a refusal rather than pretending (task 19fd9289).
    /// </summary>
    [Fact]
    public async Task Resend_and_delete_report_what_happened_task_19fd9289()
    {
        var core = new FakeCore()
            .AnswerOk("settings", AccountOf(new { id = "me", name = "Jon", email = "jon@x.io", verified = false }))
            .AnswerOk("resendVerification", new { message = "Verification email sent" })
            .AnswerFailure("deleteAccount", AstridFailureKind.Refused, "Account authentication method not found")
            .AnswerOk("deleteAccount");
        var settings = new SettingsViewModel(core);
        var view = settings.Account;
        await settings.LoadAsync();

        Assert.True(await view.ResendVerificationAsync());
        Assert.True(view.VerificationSent);

        view.DeleteConfirmation = "delete my account";
        Assert.False(view.CanDeleteAccount, "the phrase is exact, as the server's is");
        Assert.False(await view.DeleteAccountAsync());
        Assert.DoesNotContain(core.SentKinds(), kind => kind == "deleteAccount");

        view.DeleteConfirmation = AccountSettingsViewModel.DeleteConfirmationPhrase;
        Assert.True(view.CanDeleteAccount);
        Assert.False(await view.DeleteAccountAsync());
        Assert.Equal("Account authentication method not found", settings.ErrorMessage);

        Assert.True(await view.DeleteAccountAsync());
        Assert.Null(settings.ErrorMessage);
        Assert.Equal(string.Empty, view.DeleteConfirmation);
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"deleteAccount\"") && json.Contains("\"confirmation\":\"DELETE MY ACCOUNT\""));
    }

    /// <summary>
    /// Task e61a3e60: the address does not change when <c>changeEmail</c> returns — the server mails
    /// the new address and swaps on the link — so the page keeps showing the old one and says a
    /// confirmation was sent. A 409 arrives as a sentence and belongs on the error line.
    /// </summary>
    [Fact]
    public async Task Changing_the_email_asks_for_verification_and_keeps_the_old_address_task_e61a3e60()
    {
        var core = new FakeCore()
            .AnswerOk("settings", AccountOf(new { id = "me", name = "Jon", email = "jon@x.io", verified = true }))
            .AnswerOk("refreshSettings", AccountOf(new { id = "me", name = "Jon", email = "jon@x.io", verified = true }))
            .AnswerOk("changeEmail", new
            {
                verificationRequired = true,
                message = "Account updated successfully. Please check your email to verify the new address.",
            })
            .AnswerFailure("changeEmail", AstridFailureKind.Refused, "Email address is already in use");
        var settings = new SettingsViewModel(core);
        var view = settings.Account;
        await settings.LoadAsync();

        Assert.False(view.CanChangeEmail, "nothing typed is nothing to change");
        view.EmailDraft = "   ";
        Assert.False(view.CanChangeEmail);
        view.EmailDraft = " jon@x.io ";
        Assert.False(view.CanChangeEmail, "the address the account already has is not a change");

        view.EmailDraft = " grace@example.com ";
        Assert.True(view.CanChangeEmail);
        Assert.True(await view.ChangeEmailAsync());

        Assert.Equal("jon@x.io", view.Email);
        Assert.True(view.EmailChangeAsked);
        Assert.Contains("verify the new address", view.EmailChangeMessage);
        Assert.Null(settings.ErrorMessage);
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"changeEmail\"") && json.Contains("\"email\":\"grace@example.com\""));

        // A 409 is a sentence from the core, and the page stops claiming it asked for anything.
        view.EmailDraft = "taken@example.com";
        Assert.False(await view.ChangeEmailAsync());
        Assert.Equal("Email address is already in use", settings.ErrorMessage);
        Assert.False(view.EmailChangeAsked);
        Assert.Equal("jon@x.io", view.Email);

        Assert.Equal(2, core.SentKinds().Count(kind => kind == "changeEmail"));
    }

    /// <summary>
    /// Task e61a3e60: registering a passkey is two commands around one Windows Hello ceremony. The
    /// server's options go to the platform unchanged, the <c>sessionId</c> comes back with the
    /// attestation, and the list is reloaded from the server afterwards.
    /// </summary>
    [Fact]
    public async Task Adding_a_passkey_is_one_ceremony_between_the_two_commands_task_e61a3e60()
    {
        var core = new FakeCore()
            .AnswerOk("passkeyRegistrationOptions", new
            {
                options = new { challenge = "Y2hhbGxlbmdl", rp = new { id = "astrid.cc" } },
                sessionId = "s1",
            })
            .AnswerOk("registerPasskey")
            .AnswerOk("passkeys", new { passkeys = new[]
            {
                new { id = "k1", name = (string?)"Surface", createdAt = "2026-10-22T00:00:00Z", isSynced = false },
            } });
        var hello = new FakeCeremony().Answer(PasskeyCeremonyResult.Signed(new Dictionary<string, object?>
        {
            ["id"] = "new",
            ["rawId"] = "new",
            ["type"] = "public-key",
        }));
        var settings = new SettingsViewModel(core, hello);
        var view = settings.Account;

        Assert.True(await view.AddPasskeyAsync("Surface"));

        Assert.Equal("Y2hhbGxlbmdl", Assert.Single(hello.Challenges));
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"registerPasskey\"") && json.Contains("\"sessionId\":\"s1\"")
            && json.Contains("\"rawId\":\"new\"") && json.Contains("\"name\":\"Surface\""));
        Assert.Equal(
            new[] { "passkeyRegistrationOptions", "registerPasskey", "passkeys" },
            core.SentKinds());
        Assert.Single(view.Passkeys);
        Assert.Null(settings.ErrorMessage);
        Assert.False(view.IsAddingPasskey);
    }

    /// <summary>
    /// Task e61a3e60: an abandoned ceremony is ordinary — the list simply does not gain a row, and
    /// nothing is said. A ceremony that failed says why, and a machine without Windows Hello says
    /// so before any command goes out.
    /// </summary>
    [Fact]
    public async Task An_abandoned_or_refused_passkey_ceremony_adds_nothing_task_e61a3e60()
    {
        var core = new FakeCore()
            .AnswerOk("passkeyRegistrationOptions", new { options = new { challenge = "c" }, sessionId = "s1" })
            .AnswerOk("passkeyRegistrationOptions", new { options = new { challenge = "c" }, sessionId = "s2" })
            .AnswerFailure("passkeyRegistrationOptions", AstridFailureKind.Offline, "no network");
        var hello = new FakeCeremony()
            .Answer(PasskeyCeremonyResult.Abandoned)
            .Answer(PasskeyCeremonyResult.Failed("Windows Hello could not create a passkey."));
        var settings = new SettingsViewModel(core, hello);
        var view = settings.Account;

        Assert.False(await view.AddPasskeyAsync());
        Assert.Empty(view.Passkeys);
        // Cancelling is not an error to report.
        Assert.Null(settings.ErrorMessage);
        Assert.DoesNotContain(core.SentKinds(), kind => kind == "registerPasskey");

        Assert.False(await view.AddPasskeyAsync());
        Assert.Equal("Windows Hello could not create a passkey.", settings.ErrorMessage);
        Assert.DoesNotContain(core.SentKinds(), kind => kind == "registerPasskey");

        // Offline: the challenge is the server's, so there is nothing to ask Windows Hello for.
        Assert.False(await view.AddPasskeyAsync());
        Assert.NotNull(settings.ErrorMessage);
        Assert.Equal(2, hello.Challenges.Count);

        // No Windows Hello on this machine: said before a challenge is asked for and spent.
        var bare = new FakeCore();
        var without = new SettingsViewModel(bare, new FakeCeremony { IsAvailable = false });
        Assert.False(await without.Account.AddPasskeyAsync());
        Assert.NotNull(without.ErrorMessage);
        Assert.Empty(bare.SentKinds());
    }

    /// <summary>
    /// Task e61a3e60: the server names the new key by finding the account's newest authenticator, so
    /// two ceremonies at once would name the wrong one. The second press is refused, not queued.
    /// </summary>
    [Fact]
    public async Task Only_one_passkey_registration_runs_at_a_time_task_e61a3e60()
    {
        var core = new FakeCore()
            .AnswerOk("passkeyRegistrationOptions", new { options = new { challenge = "c" }, sessionId = "s1" })
            .AnswerOk("registerPasskey")
            .AnswerOk("passkeys", new { passkeys = Array.Empty<object>() })
            .Hold("passkeyRegistrationOptions");
        var hello = new FakeCeremony().Answer(PasskeyCeremonyResult.Signed(
            new Dictionary<string, object?> { ["rawId"] = "new" }));
        var settings = new SettingsViewModel(core, hello);
        var view = settings.Account;

        var first = view.AddPasskeyAsync();
        Assert.True(view.IsAddingPasskey);
        Assert.False(await view.AddPasskeyAsync(), "one at a time");
        Assert.Single(core.SentKinds());

        core.Release("passkeyRegistrationOptions");
        Assert.True(await first);
        Assert.False(view.IsAddingPasskey);
        Assert.Single(hello.Challenges);
    }

    /// <summary>A Windows Hello that answers from a script, so the ceremony's three ends are tests.</summary>
    private sealed class FakeCeremony : IPasskeyCeremony
    {
        private readonly Queue<PasskeyCeremonyResult> _answers = new();

        /// <summary>The challenge each ceremony was handed, so "unchanged" is checkable.</summary>
        public List<string> Challenges { get; } = [];

        public bool IsAvailable { get; init; } = true;

        public FakeCeremony Answer(PasskeyCeremonyResult result)
        {
            _answers.Enqueue(result);
            return this;
        }

        public Task<PasskeyCeremonyResult> SignAsync(JsonElement options, CancellationToken cancellationToken = default)
        {
            Challenges.Add(options.TryGetProperty("challenge", out var challenge)
                ? challenge.GetString() ?? string.Empty
                : string.Empty);
            return Task.FromResult(_answers.Count > 0
                ? _answers.Dequeue()
                : PasskeyCeremonyResult.Abandoned);
        }
    }

    /// <summary>
    /// The numbers come from the server, and a screen without them is not a broken screen — three
    /// missing statistics are not worth a message beside somebody's own name.
    /// </summary>
    [Fact]
    public async Task The_profile_numbers_are_loaded_but_never_insisted_on()
    {
        var core = new FakeCore()
            .AnswerOk("settings", Account())
            .AnswerOk("refreshSettings", Account())
            .AnswerFailure("profileStats", AstridFailureKind.Offline, "no network");
        var settings = new SettingsViewModel(core);

        await settings.LoadAsync();

        Assert.Null(settings.ErrorMessage);
        Assert.Equal(0, settings.Account.Stats.Completed);
    }
}
