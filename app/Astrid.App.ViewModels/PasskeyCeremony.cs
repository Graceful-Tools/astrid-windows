using System.Text.Json;

namespace Astrid.App.ViewModels;

/// <summary>
/// The platform half of registering a passkey: Windows Hello, between the core's two commands
/// (task e61a3e60).
/// </summary>
/// <remarks>
/// <para>
/// The core asks the server for the registration options and verifies what comes back, so the only
/// thing left that a view model cannot do is the ceremony itself — it needs a window, the user's
/// face or PIN, and <c>webauthn.dll</c>. That lives in the shell, behind this interface, for the
/// same reason <see cref="IDevicePreferences"/> does: a view model that called the platform
/// directly could not be tested, and abandoned, failed and signed are the three endings worth
/// testing.
/// </para>
/// <para>
/// The options are handed over as the server wrote them. The challenge, the relying party, the
/// algorithms and <c>excludeCredentials</c> are all its choices, and a shell that rewrote any of
/// them would be signing something the server will not verify.
/// </para>
/// </remarks>
public interface IPasskeyCeremony
{
    /// <summary>Whether this machine can run the ceremony at all.</summary>
    /// <remarks>
    /// Asked before the challenge is, because a challenge is stored against the session that asked
    /// for it: spending one on a machine that cannot answer it is a wasted ceremony.
    /// </remarks>
    bool IsAvailable { get; }

    /// <summary>
    /// Run the ceremony over the server's options and answer what the authenticator signed.
    /// </summary>
    Task<PasskeyCeremonyResult> SignAsync(
        JsonElement options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// How a ceremony ended: signed, abandoned, or failed with something to say.
/// </summary>
/// <remarks>
/// Abandoned is deliberately not a failure. Somebody who closes the Windows Hello prompt has not
/// hit a problem, and a line of red beside their own name saying so would be the app inventing one.
/// </remarks>
/// <param name="Attestation">
/// What the authenticator signed, in the WebAuthn spec's shape, passed on unread — neither the
/// shell nor the core looks inside it. Null when nothing was signed.
/// </param>
/// <param name="Message">Why it did not happen, when that is worth saying.</param>
public sealed record PasskeyCeremonyResult(
    IReadOnlyDictionary<string, object?>? Attestation,
    string? Message)
{
    /// <summary>The authenticator signed; this is its answer.</summary>
    public static PasskeyCeremonyResult Signed(IReadOnlyDictionary<string, object?> attestation) =>
        new(attestation, null);

    /// <summary>It did not work, and here is the sentence for it.</summary>
    public static PasskeyCeremonyResult Failed(string message) => new(null, message);

    /// <summary>Somebody closed the prompt. Nothing to add, nothing to report.</summary>
    public static readonly PasskeyCeremonyResult Abandoned = new(null, null);
}

/// <summary>
/// No Windows Hello. The default, so a test or a tool needs no platform to run — and the one
/// honest answer for a machine whose authenticator the shell could not reach.
/// </summary>
public sealed class NoPasskeyCeremony : IPasskeyCeremony
{
    public static readonly NoPasskeyCeremony Instance = new();

    public bool IsAvailable => false;

    public Task<PasskeyCeremonyResult> SignAsync(
        JsonElement options,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PasskeyCeremonyResult.Abandoned);
}
