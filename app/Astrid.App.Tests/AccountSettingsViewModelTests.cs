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
