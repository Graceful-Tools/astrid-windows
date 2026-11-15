using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Astrid.App.ViewModels;

namespace Astrid.App;

/// <summary>
/// Registering a passkey with Windows Hello (task e61a3e60): the ceremony between the core's
/// <c>passkeyRegistrationOptions</c> and <c>registerPasskey</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the one piece of the passkey story that cannot live in the core or in a view model. It
/// needs <c>webauthn.dll</c>, a window to parent the prompt to, and the user's face, fingerprint or
/// PIN. Everything around it — asking the server for the challenge, verifying what came back,
/// naming the key — is the core's.
/// </para>
/// <para>
/// <b>The server's options are obeyed, not interpreted.</b> The challenge, the relying party, the
/// algorithms, the timeout, <c>excludeCredentials</c> and the attestation preference are all read
/// straight out of the JSON the server sent and handed to Windows as they are. The one thing this
/// file writes itself is <c>clientDataJSON</c>, because WebAuthn gives that job to the client: it
/// carries the challenge back verbatim and names the origin, which the server checks against its
/// own. The origin is this build's server, so a build pointed at a staging host registers against
/// that host.
/// </para>
/// <para>
/// <b>Nothing here throws.</b> A missing DLL on an older Windows, a cancelled prompt and a refused
/// authenticator all come back as an ordinary <see cref="PasskeyCeremonyResult"/>, because a
/// settings page that crashed would be a far worse answer than one that says it could not.
/// </para>
/// </remarks>
internal sealed class WindowsHelloPasskeys : IPasskeyCeremony
{
    /// <summary>What WebAuthn calls the only credential type there is.</summary>
    private const string PublicKey = "public-key";

    private const string Sha256 = "SHA-256";

    /// <summary>
    /// The options struct version this asks for. Version 3 is the first that carries
    /// <c>pExcludeCredentialList</c>, which is the field the server's own exclusions need, and it is
    /// understood by every Windows that has <c>webauthn.dll</c> at all.
    /// </summary>
    private const uint MakeCredentialOptionsVersion = 3;

    /// <summary>The user closed the prompt, or let it time out. Not a failure worth a sentence.</summary>
    private static readonly int[] Abandoned =
    [
        unchecked((int)0x800704C7), // HRESULT_FROM_WIN32(ERROR_CANCELLED)
        unchecked((int)0x80090036), // NTE_USER_CANCELLED
        unchecked((int)0x800704C6), // HRESULT_FROM_WIN32(ERROR_TIMEOUT) as Hello reports it
    ];

    private readonly Func<IntPtr> _window;

    private WindowsHelloPasskeys(Func<IntPtr> window)
    {
        _window = window;
    }

    /// <summary>
    /// The ceremony for this app's window, or a ceremony that cannot run when this Windows has no
    /// WebAuthn API at all.
    /// </summary>
    /// <param name="window">
    /// Asked for when a prompt is about to open, not now: the shell builds its view models while
    /// the window is still being constructed, so the handle does not exist yet at this point.
    /// </param>
    public static IPasskeyCeremony ForThisWindow(Func<IntPtr> window)
    {
        try
        {
            return WebAuthNGetApiVersionNumber() > 0
                ? new WindowsHelloPasskeys(window)
                : NoPasskeyCeremony.Instance;
        }
        catch (Exception)
        {
            // No webauthn.dll beside this Windows. Older than the API, which is an honest "no".
            return NoPasskeyCeremony.Instance;
        }
    }

    public bool IsAvailable => true;

    /// <summary>
    /// Run the ceremony. On a thread of its own: <c>WebAuthNAuthenticatorMakeCredential</c> shows a
    /// modal prompt and does not return until somebody answers it, and the UI thread is the one
    /// thread that must stay free to draw it.
    /// </summary>
    public Task<PasskeyCeremonyResult> SignAsync(
        JsonElement options,
        CancellationToken cancellationToken = default)
    {
        // Parsed here, on the caller's thread, so a malformed answer is reported before a prompt
        // appears in front of somebody.
        Request request;
        try
        {
            request = Request.From(options);
        }
        catch (Exception error)
        {
            return Task.FromResult(PasskeyCeremonyResult.Failed(
                Strings.Get("passkeys.hello_options", error.Message)));
        }

        var window = _window();
        if (window == IntPtr.Zero)
        {
            return Task.FromResult(PasskeyCeremonyResult.Failed(
                Strings.Get("passkeys.hello_failed", "no window")));
        }
        return Task.Run(() => Sign(window, request), cancellationToken);
    }

    private static PasskeyCeremonyResult Sign(IntPtr window, Request request)
    {
        var native = new Arena();
        try
        {
            var relyingParty = new RpEntity
            {
                Version = 1,
                Id = native.Text(request.RelyingPartyId),
                Name = native.Text(request.RelyingPartyName),
                Icon = IntPtr.Zero,
            };
            var user = new UserEntity
            {
                Version = 1,
                IdLength = (uint)request.UserId.Length,
                Id = native.Bytes(request.UserId),
                Name = native.Text(request.UserName),
                Icon = IntPtr.Zero,
                DisplayName = native.Text(request.UserDisplayName),
            };

            var clientDataJson = Encoding.UTF8.GetBytes(request.ClientDataJson);
            var clientData = new ClientData
            {
                Version = 1,
                JsonLength = (uint)clientDataJson.Length,
                Json = native.Bytes(clientDataJson),
                HashAlgorithmId = native.Text(Sha256),
            };

            var parameters = new CoseParameters
            {
                Count = (uint)request.Algorithms.Count,
                Parameters = native.Array(request.Algorithms.Select(algorithm => new CoseParameter
                {
                    Version = 1,
                    CredentialType = native.Text(PublicKey),
                    Algorithm = algorithm,
                })),
            };

            var options = new MakeCredentialOptions
            {
                Version = MakeCredentialOptionsVersion,
                TimeoutMilliseconds = request.TimeoutMilliseconds,
                // The old flat list stays empty: the exclusions go in pExcludeCredentialList
                // below, which is the one Windows reads from version 3 on.
                CredentialList = default,
                Extensions = default,
                AuthenticatorAttachment = request.Attachment,
                RequireResidentKey = request.RequireResidentKey ? 1 : 0,
                UserVerificationRequirement = request.UserVerification,
                AttestationConveyancePreference = request.Attestation,
                Flags = 0,
                CancellationId = IntPtr.Zero,
                ExcludeCredentialList = Exclusions(native, request.Excluded),
            };

            var status = WebAuthNAuthenticatorMakeCredential(
                window, in relyingParty, in user, in parameters, in clientData, in options,
                out var answer);
            if (status != 0)
            {
                return Abandoned.Contains(status)
                    ? PasskeyCeremonyResult.Abandoned
                    : PasskeyCeremonyResult.Failed(
                        Strings.Get("passkeys.hello_failed", ErrorName(status)));
            }

            try
            {
                return PasskeyCeremonyResult.Signed(Credential(answer, request.ClientDataJson));
            }
            finally
            {
                WebAuthNFreeCredentialAttestation(answer);
            }
        }
        catch (Exception error)
        {
            return PasskeyCeremonyResult.Failed(Strings.Get("passkeys.hello_failed", error.Message));
        }
        finally
        {
            native.Dispose();
        }
    }

    /// <summary>
    /// The credentials the server says this account already has on this authenticator, so Windows
    /// can say "you already have one here" rather than quietly making a second.
    /// </summary>
    private static IntPtr Exclusions(Arena native, IReadOnlyList<byte[]> excluded)
    {
        if (excluded.Count == 0)
        {
            return IntPtr.Zero;
        }
        // PWEBAUTHN_CREDENTIAL_EX** — an array of pointers, not an array of structs.
        var pointers = excluded
            .Select(id => native.Struct(new CredentialEx
            {
                Version = 1,
                IdLength = (uint)id.Length,
                Id = native.Bytes(id),
                CredentialType = native.Text(PublicKey),
                Transports = 0,
            }))
            .ToList();
        return native.Struct(new CredentialList
        {
            Count = (uint)pointers.Count,
            Credentials = native.Pointers(pointers),
        });
    }

    /// <summary>
    /// What the authenticator signed, in the shape the WebAuthn spec gives a registration response.
    /// </summary>
    /// <remarks>
    /// Assembled rather than forwarded because Windows answers in C structs and the server speaks
    /// the JSON shape a browser would send. Nothing is interpreted on the way: the credential id and
    /// the attestation object go across as the bytes that came back, base64url as the spec requires.
    /// </remarks>
    private static IReadOnlyDictionary<string, object?> Credential(IntPtr answer, string clientDataJson)
    {
        var attestation = Marshal.PtrToStructure<CredentialAttestation>(answer);
        var credentialId = Read(attestation.CredentialId, attestation.CredentialIdLength);
        var attestationObject = Read(attestation.AttestationObject, attestation.AttestationObjectLength);
        var id = Base64Url.Encode(credentialId);

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["rawId"] = id,
            ["type"] = PublicKey,
            // WEBAUTHN_CTAP_TRANSPORT_INTERNAL: the authenticator is this machine's, which is what
            // "platform" means to the server.
            ["authenticatorAttachment"] =
                (attestation.UsedTransport & 0x20) != 0 ? "platform" : "cross-platform",
            ["clientExtensionResults"] = new Dictionary<string, object?>(StringComparer.Ordinal),
            ["response"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["clientDataJSON"] = Base64Url.Encode(Encoding.UTF8.GetBytes(clientDataJson)),
                ["attestationObject"] = Base64Url.Encode(attestationObject),
            },
        };
    }

    private static byte[] Read(IntPtr bytes, uint length)
    {
        if (bytes == IntPtr.Zero || length == 0)
        {
            return [];
        }
        var copy = new byte[length];
        Marshal.Copy(bytes, copy, 0, copy.Length);
        return copy;
    }

    private static string ErrorName(int status)
    {
        try
        {
            return Marshal.PtrToStringUni(WebAuthNGetErrorName(status)) ?? $"0x{status:X8}";
        }
        catch (Exception)
        {
            return $"0x{status:X8}";
        }
    }

    // ── What the server asked for ────────────────────────────────────────────────────────────

    /// <summary>
    /// The server's registration options, read into what Windows needs — and nothing more.
    /// </summary>
    private sealed record Request(
        string RelyingPartyId,
        string RelyingPartyName,
        byte[] UserId,
        string UserName,
        string UserDisplayName,
        IReadOnlyList<int> Algorithms,
        uint TimeoutMilliseconds,
        uint Attachment,
        bool RequireResidentKey,
        uint UserVerification,
        uint Attestation,
        IReadOnlyList<byte[]> Excluded,
        string ClientDataJson)
    {
        public static Request From(JsonElement options)
        {
            var challenge = Text(options, "challenge")
                ?? throw new InvalidOperationException("the options carry no challenge");
            var relyingParty = Child(options, "rp");
            var user = Child(options, "user");
            var selection = Child(options, "authenticatorSelection");

            var algorithms = options.TryGetProperty("pubKeyCredParams", out var parameters)
                && parameters.ValueKind == JsonValueKind.Array
                ? parameters.EnumerateArray()
                    .Where(parameter => parameter.TryGetProperty("alg", out var algorithm)
                                        && algorithm.ValueKind == JsonValueKind.Number)
                    .Select(parameter => parameter.GetProperty("alg").GetInt32())
                    .ToList()
                : [];
            if (algorithms.Count == 0)
            {
                throw new InvalidOperationException("the options name no algorithm");
            }

            var excluded = options.TryGetProperty("excludeCredentials", out var exclusions)
                && exclusions.ValueKind == JsonValueKind.Array
                ? exclusions.EnumerateArray()
                    .Select(credential => Text(credential, "id"))
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Select(id => Base64Url.Decode(id!))
                    .ToList()
                : [];

            return new Request(
                RelyingPartyId: Text(relyingParty, "id") ?? string.Empty,
                RelyingPartyName: Text(relyingParty, "name") ?? Text(relyingParty, "id") ?? string.Empty,
                UserId: Base64Url.Decode(Text(user, "id") ?? string.Empty),
                UserName: Text(user, "name") ?? string.Empty,
                UserDisplayName: Text(user, "displayName") ?? Text(user, "name") ?? string.Empty,
                Algorithms: algorithms,
                TimeoutMilliseconds:
                    options.TryGetProperty("timeout", out var timeout)
                    && timeout.ValueKind == JsonValueKind.Number
                        ? (uint)Math.Clamp(timeout.GetInt64(), 30_000, 600_000)
                        : 120_000,
                Attachment: Text(selection, "authenticatorAttachment") switch
                {
                    "platform" => 1,
                    "cross-platform" => 2,
                    _ => 0,
                },
                // Either spelling: the newer `residentKey` wins where the server sends both, as the
                // spec says, and the older boolean is still what some deployments send.
                RequireResidentKey: Text(selection, "residentKey") is { } resident
                    ? resident is "required"
                    : selection.TryGetProperty("requireResidentKey", out var flag)
                      && flag.ValueKind == JsonValueKind.True,
                UserVerification: Text(selection, "userVerification") switch
                {
                    "required" => 1,
                    "preferred" => 2,
                    "discouraged" => 3,
                    _ => 0,
                },
                Attestation: Text(options, "attestation") switch
                {
                    "none" => 1,
                    "indirect" => 2,
                    "direct" => 3,
                    _ => 0,
                },
                Excluded: excluded,
                // The client's own half of the ceremony, and the server checks every field of it.
                ClientDataJson: JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = "webauthn.create",
                    ["challenge"] = challenge,
                    ["origin"] = App.ServerOrigin.TrimEnd('/'),
                    ["crossOrigin"] = false,
                }));
        }

        private static JsonElement Child(JsonElement parent, string name) =>
            parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var child)
                ? child
                : default;

        private static string? Text(JsonElement parent, string name) =>
            parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    /// <summary>Base64url, which is how WebAuthn writes every byte string it sends over JSON.</summary>
    private static class Base64Url
    {
        public static string Encode(byte[] bytes) => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        public static byte[] Decode(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return [];
            }
            var padded = text.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(padded.PadRight((padded.Length + 3) / 4 * 4, '='));
        }
    }

    /// <summary>
    /// Everything handed to Windows for one call, freed together.
    /// </summary>
    /// <remarks>
    /// WebAuthn's structs are a graph of raw pointers that has to stay alive and unmoved for the
    /// length of a modal prompt, which is exactly what a pinned managed object cannot promise
    /// across a blocking native call. So each piece is copied to unmanaged memory and the whole lot
    /// is released in one place, rather than one <c>try</c>/<c>finally</c> per field.
    /// </remarks>
    private sealed class Arena : IDisposable
    {
        private readonly List<IntPtr> _allocations = [];

        public IntPtr Text(string value)
        {
            var pointer = Marshal.StringToHGlobalUni(value);
            _allocations.Add(pointer);
            return pointer;
        }

        public IntPtr Bytes(byte[] value)
        {
            if (value.Length == 0)
            {
                return IntPtr.Zero;
            }
            var pointer = Marshal.AllocHGlobal(value.Length);
            _allocations.Add(pointer);
            Marshal.Copy(value, 0, pointer, value.Length);
            return pointer;
        }

        public IntPtr Struct<T>(T value) where T : struct
        {
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
            _allocations.Add(pointer);
            Marshal.StructureToPtr(value, pointer, false);
            return pointer;
        }

        /// <summary>A contiguous array of structs, as every <c>c…</c>/<c>p…</c> pair expects.</summary>
        public IntPtr Array<T>(IEnumerable<T> values) where T : struct
        {
            var all = values.ToList();
            if (all.Count == 0)
            {
                return IntPtr.Zero;
            }
            var size = Marshal.SizeOf<T>();
            var pointer = Marshal.AllocHGlobal(size * all.Count);
            _allocations.Add(pointer);
            for (var index = 0; index < all.Count; index++)
            {
                Marshal.StructureToPtr(all[index], pointer + (size * index), false);
            }
            return pointer;
        }

        /// <summary>An array of pointers, which is what <c>ppCredentials</c> is.</summary>
        public IntPtr Pointers(IReadOnlyList<IntPtr> values)
        {
            if (values.Count == 0)
            {
                return IntPtr.Zero;
            }
            var pointer = Marshal.AllocHGlobal(IntPtr.Size * values.Count);
            _allocations.Add(pointer);
            for (var index = 0; index < values.Count; index++)
            {
                Marshal.WriteIntPtr(pointer, IntPtr.Size * index, values[index]);
            }
            return pointer;
        }

        public void Dispose()
        {
            foreach (var pointer in _allocations)
            {
                Marshal.FreeHGlobal(pointer);
            }
            _allocations.Clear();
        }
    }

    // ── webauthn.dll ─────────────────────────────────────────────────────────────────────────
    // The structs below are webauthn.h's, field for field and in its order. Each `dwVersion` is the
    // version of ITS OWN struct, which is 1 for all of them but the options.

    [StructLayout(LayoutKind.Sequential)]
    private struct RpEntity
    {
        public uint Version;
        public IntPtr Id;
        public IntPtr Name;
        public IntPtr Icon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UserEntity
    {
        public uint Version;
        public uint IdLength;
        public IntPtr Id;
        public IntPtr Name;
        public IntPtr Icon;
        public IntPtr DisplayName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ClientData
    {
        public uint Version;
        public uint JsonLength;
        public IntPtr Json;
        public IntPtr HashAlgorithmId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CoseParameter
    {
        public uint Version;
        public IntPtr CredentialType;
        public int Algorithm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CoseParameters
    {
        public uint Count;
        public IntPtr Parameters;
    }

    /// <summary>The pre-version-3 credential list. Left empty; see the options above.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Credentials
    {
        public uint Count;
        public IntPtr Items;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Extensions
    {
        public uint Count;
        public IntPtr Items;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CredentialEx
    {
        public uint Version;
        public uint IdLength;
        public IntPtr Id;
        public IntPtr CredentialType;
        public uint Transports;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CredentialList
    {
        public uint Count;
        public IntPtr Credentials;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MakeCredentialOptions
    {
        public uint Version;
        public uint TimeoutMilliseconds;
        public Credentials CredentialList;
        public Extensions Extensions;
        public uint AuthenticatorAttachment;
        public int RequireResidentKey;
        public uint UserVerificationRequirement;
        public uint AttestationConveyancePreference;
        public uint Flags;
        public IntPtr CancellationId;
        public IntPtr ExcludeCredentialList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CredentialAttestation
    {
        public uint Version;
        public IntPtr FormatType;
        public uint AuthenticatorDataLength;
        public IntPtr AuthenticatorData;
        public uint AttestationLength;
        public IntPtr Attestation;
        public uint AttestationDecodeType;
        public IntPtr AttestationDecode;
        public uint AttestationObjectLength;
        public IntPtr AttestationObject;
        public uint CredentialIdLength;
        public IntPtr CredentialId;
        public Extensions Extensions;
        public uint UsedTransport;
    }

    [DllImport("webauthn.dll", ExactSpelling = true)]
    private static extern int WebAuthNGetApiVersionNumber();

    [DllImport("webauthn.dll", ExactSpelling = true)]
    private static extern int WebAuthNAuthenticatorMakeCredential(
        IntPtr window,
        in RpEntity relyingParty,
        in UserEntity user,
        in CoseParameters parameters,
        in ClientData clientData,
        in MakeCredentialOptions options,
        out IntPtr attestation);

    [DllImport("webauthn.dll", ExactSpelling = true)]
    private static extern void WebAuthNFreeCredentialAttestation(IntPtr attestation);

    [DllImport("webauthn.dll", ExactSpelling = true)]
    private static extern IntPtr WebAuthNGetErrorName(int status);
}
