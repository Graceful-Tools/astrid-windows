using System.Runtime.InteropServices;
using System.Text.Json;

namespace Astrid.Core.Bindings;

/// <summary>
/// The core, as something C# can hold: send a command, await an answer.
/// </summary>
/// <remarks>
/// <para>
/// This is the only type the rest of the app uses. It has no pointers in its surface and every
/// call is awaitable, so a view model never blocks the UI thread on a command that might need the
/// network.
/// </para>
/// <para>
/// <b>The callback lifetime problem, and how it is solved here.</b> The core calls back on a pool
/// thread with a pointer we gave it. If the delegate is collected, or the pointer freed, before
/// that happens, the process dies with no stack worth reading. So each in-flight call owns a
/// <see cref="GCHandle"/> to its own completion, and the delegate itself is a static field that
/// lives as long as the type — never a lambda handed to the marshaller and forgotten.
/// </para>
/// </remarks>
public sealed class AstridClient : IAstridCore, IDisposable
{
    /// <summary>
    /// One delegate for every call, held for the life of the type.
    /// </summary>
    /// <remarks>
    /// A per-call delegate would be collected the moment the call returned, which is before the
    /// answer arrives. Keeping one static instance removes the whole class of bug rather than
    /// managing it.
    /// </remarks>
    private static readonly NativeMethods.AstridCallback CompletionCallback = OnCompletion;

    private static readonly NativeMethods.AstridCallback ChangeCallback = OnChange;

    private static readonly IntPtr CompletionCallbackPointer =
        Marshal.GetFunctionPointerForDelegate(CompletionCallback);

    private static readonly IntPtr ChangeCallbackPointer =
        Marshal.GetFunctionPointerForDelegate(ChangeCallback);

    private IntPtr _handle;
    private GCHandle _self;
    private bool _disposed;

    private AstridClient(IntPtr handle)
    {
        _handle = handle;
    }

    /// <summary>Raised when the cache changes underneath the app.</summary>
    /// <remarks>
    /// Arrives on a pool thread. A subscriber that touches the UI has to marshal — which is the
    /// shell's job, and is why this is an event rather than something that pretends to be safe.
    /// </remarks>
    public event Action<ChangeNotification>? Changed;

    /// <summary>
    /// This build's binding version and the revision of the core inside it —
    /// <c>0.1.0 (core a8591a93c022)</c>.
    /// </summary>
    public static string Version => NativeMethods.BorrowString(NativeMethods.Version());

    /// <summary>
    /// Answer one of the core's pure rules, synchronously, without a started client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stateless half of the core waits on nothing — repeating rollover, permissions, filters,
    /// smart parsing, the row projections — so asking it needs no handle, no runtime and no
    /// callback. Static for that reason: a caller that only wants arithmetic should not have to
    /// start a client, and a disposed one is no reason to refuse the question (task ab249b76).
    /// </para>
    /// <para>
    /// The request is the core's <c>Rule</c> vocabulary, tagged by <c>kind</c>; the answer is the
    /// same <c>{"ok":…}</c> envelope a command answers with, never null and never empty.
    /// </para>
    /// </remarks>
    public static string Rule(string requestJson) =>
        NativeMethods.TakeString(NativeMethods.Rule(requestJson));

    /// <summary>
    /// What a list's defaults editor may offer, with <paramref name="defaults"/> ticked — the
    /// core's <c>listDefaultPicks</c> rule (task 92830d81).
    /// </summary>
    /// <remarks>
    /// The shell held four tables for this. Asked through the stateless door rather than through a
    /// command because drawing a picker waits on nothing: no cache, no clock, no handle.
    /// </remarks>
    public static ListDefaultPicks ListDefaultPicks(ListDefaults defaults)
    {
        var request = JsonSerializer.Serialize(new ListDefaultPicksRequest
        {
            Priority = defaults.Priority,
            Repeating = defaults.Repeating,
            DueDate = defaults.DueDate,
            DueTime = defaults.DueTime,
        });
        return AstridResponse.Parse(Rule(request)).ValueAs<ListDefaultPicks>() ?? new ListDefaultPicks();
    }

    /// <summary>
    /// Which CLDR category <paramref name="count"/> selects in <paramref name="locale"/> — the row
    /// a plural key's word is read from (task b656efce).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The catalog holds the words and this picks the row, which is the split astrid-core's
    /// <c>strings/README.md</c> describes. The shell used to decide with <c>count == 1</c> and a
    /// <c>% 100</c> suffix rule of its own: right in English, right in German, and wrong in every
    /// language with more than two forms — which is the whole reason the catalog pluralises.
    /// </para>
    /// <para>
    /// <paramref name="form"/> is not inferable from the number. English's <em>cardinal</em> 3 is
    /// <c>other</c> ("3 weeks") while its <em>ordinal</em> 3 is <c>few</c> ("3rd"): genuinely
    /// different CLDR rules, and the caller already knows which question it is asking.
    /// </para>
    /// <para>
    /// A locale this core has never met answers <c>other</c>, the one category every language has,
    /// so a newer Windows language setting costs a less precise word rather than an exception. A
    /// refusal falls back the same way, because a repeat summary is not worth throwing over.
    /// </para>
    /// </remarks>
    public static string PluralCategory(string locale, CountForm form, long count)
    {
        var request = JsonSerializer.Serialize(new PluralCategoryRequest
        {
            Locale = locale,
            Form = form == CountForm.Ordinal ? "ordinal" : "cardinal",
            Count = count,
        });
        var answer = AstridResponse.Parse(Rule(request));
        return answer.Ok ? answer.ValueAs<string>() ?? "other" : "other";
    }

    private sealed record PluralCategoryRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("kind")]
        public string Kind { get; init; } = "pluralCategory";

        [System.Text.Json.Serialization.JsonPropertyName("locale")]
        public string Locale { get; init; } = "en";

        [System.Text.Json.Serialization.JsonPropertyName("form")]
        public string Form { get; init; } = "cardinal";

        [System.Text.Json.Serialization.JsonPropertyName("count")]
        public long Count { get; init; }
    }

    private sealed record ListDefaultPicksRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("kind")]
        public string Kind { get; init; } = "listDefaultPicks";

        [System.Text.Json.Serialization.JsonPropertyName("priority")]
        public int Priority { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("repeating")]
        public string? Repeating { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("dueDate")]
        public string? DueDate { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("dueTime")]
        public string? DueTime { get; init; }
    }

    /// <summary>
    /// Where a task callout opens from the row it belongs to, and where its arrow points — the
    /// core's <c>calloutPlacement</c> rule (task eb2684bb, the Windows half of 011080d4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A multi-column screen has to pick a side and aim an arrow before it can draw a callout, and
    /// both are arithmetic every client would otherwise repeat. The Mac, the iPad and the Android
    /// tablet ask this same rule; this shell asked nothing at all, because its board spliced detail
    /// into the column as another card instead of opening a callout.
    /// </para>
    /// <para>
    /// Logical pixels in the window's space, y increasing downward. Divide by the scale factor
    /// before asking — the rule does not take one, deliberately, because a rule that took a scale
    /// factor is a rule that can get it wrong.
    /// </para>
    /// <para>
    /// The callout's SIZE is the caller's. How big a detail pane wants to be is a design decision
    /// per platform and per density, so the shell measures and then asks. What comes back is that
    /// same size, placed.
    /// </para>
    /// </remarks>
    public static CalloutPlacement CalloutPlacement(
        CalloutRect anchor, CalloutRect viewport, double width, double height)
    {
        var request = JsonSerializer.Serialize(new CalloutPlacementRequest
        {
            Anchor = anchor,
            Viewport = viewport,
            Width = width,
            Height = height,
        });
        var answer = AstridResponse.Parse(Rule(request));
        // A refusal would mean this build's core does not know the rule, which the pin makes
        // impossible — but a callout is not worth throwing over, so an unplaced one falls back to
        // the anchor's own top-left rather than leaving the reader with no detail at all.
        return answer.ValueAs<CalloutPlacement>()
            ?? new CalloutPlacement
            {
                Side = "right",
                Left = anchor.Left + anchor.Width,
                Top = anchor.Top,
                Width = width,
                Height = height,
                ArrowOffset = height / 2,
            };
    }

    private sealed record CalloutPlacementRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("kind")]
        public string Kind { get; init; } = "calloutPlacement";

        [System.Text.Json.Serialization.JsonPropertyName("anchor")]
        public CalloutRect Anchor { get; init; } = new(0, 0, 0, 0);

        [System.Text.Json.Serialization.JsonPropertyName("viewport")]
        public CalloutRect Viewport { get; init; } = new(0, 0, 0, 0);

        [System.Text.Json.Serialization.JsonPropertyName("width")]
        public double Width { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("height")]
        public double Height { get; init; }
    }

    /// <summary>
    /// Start the core against a cache at <paramref name="cachePath"/>.
    /// </summary>
    /// <exception cref="AstridStartupException">
    /// The core could not start, with the reason it gave. Thrown rather than returned because
    /// there is nothing the app can do without it, and a null client that fails on every later call
    /// reports the problem in the wrong place.
    /// </exception>
    public static AstridClient Start(string cachePath, string baseUrl, string? urlScheme = null)
    {
        var config = BuildStartConfig(cachePath, baseUrl, urlScheme);

        var handle = NativeMethods.Start(config, out var error);
        if (handle == IntPtr.Zero)
        {
            throw new AstridStartupException(NativeMethods.TakeString(error));
        }

        var client = new AstridClient(handle);
        client._self = GCHandle.Alloc(client, GCHandleType.Normal);
        return client;
    }

    /// <summary>
    /// The JSON the core is started from — what the shell has to tell it before anything else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A key the shell has nothing to say about is LEFT OUT rather than sent null or empty, because
    /// the core's <c>Config</c> gives each one a default and absence is how a shell asks for it.
    /// For <paramref name="urlScheme"/> that default is <c>astrid</c>, so a build that does not
    /// name a scheme behaves exactly as every client did before the field existed.
    /// </para>
    /// <para>
    /// <paramref name="baseUrl"/> is the exception, and it is required (task <c>7e68a733</c>).
    /// The core's default for it is Astrid's own server, which is the one answer a white-label
    /// build must never give: a partner that left it out sent its sync, its Outbox and its live
    /// stream to astrid.cc while every call the shell made went to the partner's server. There is
    /// deliberately no way to ask for the core's default here, and astrid-core is removing it from
    /// its own side as well — a core built with <c>require-base-url</c> refuses a start without
    /// one, with <c>CONFIG_BASE_URL_REQUIRED</c>. Pass
    /// <c>ServerSelection.Origin(…)</c>, which answers the brand's origin when nothing was chosen;
    /// never <c>ServerSelection.Parse(…)</c>, which answers null for exactly that case.
    /// </para>
    /// <para>
    /// Separate from <see cref="Start"/> so the contract can be asserted without a running core:
    /// the core ignores a key it does not know, which makes a wrong or missing one invisible at the
    /// boundary (task 8cbed184).
    /// </para>
    /// </remarks>
    public static string BuildStartConfig(string cachePath, string baseUrl, string? urlScheme) =>
        JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["cachePath"] = cachePath,
            ["baseUrl"] = baseUrl,
            ["urlScheme"] = urlScheme,
        }.Where(entry => entry.Value is not null).ToDictionary(entry => entry.Key, entry => entry.Value));

    /// <summary>
    /// Send a command and await the answer.
    /// </summary>
    /// <remarks>
    /// Never blocks: the core answers on a pool thread and this completes the task from there.
    /// </remarks>
    public Task<AstridResponse> CallAsync(object command, CancellationToken cancellationToken = default)
        => CallAsync(JsonSerializer.Serialize(command, CommandJson.Options), cancellationToken);

    /// <summary>Send a command already serialised.</summary>
    public Task<AstridResponse> CallAsync(string requestJson, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var completion = new TaskCompletionSource<AstridResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = GCHandle.Alloc(completion, GCHandleType.Normal);

        // Cancellation abandons the wait, not the work. The core has no notion of cancelling a
        // command — a write is already journalled by the time this returns, and "cancelling" it
        // would mean the user's change existed for a moment and then did not.
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        }

        NativeMethods.Call(_handle, requestJson, CompletionCallbackPointer, GCHandle.ToIntPtr(pending));
        return completion.Task;
    }

    /// <summary>
    /// Send a command and wait. For a first paint, before there is a UI to keep responsive.
    /// </summary>
    public AstridResponse CallBlocking(string requestJson)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return AstridResponse.Parse(NativeMethods.TakeString(NativeMethods.CallBlocking(_handle, requestJson)));
    }

    /// <summary>Start receiving <see cref="Changed"/> notifications.</summary>
    public void Subscribe()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeMethods.Subscribe(_handle, ChangeCallbackPointer, GCHandle.ToIntPtr(_self));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        // Stop first: after it returns the core starts no more callbacks, so the handle below can
        // be released without a race. Doing it the other way round is the crash this ordering
        // exists to prevent.
        var handle = _handle;
        _handle = IntPtr.Zero;
        NativeMethods.Stop(handle);

        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    private static void OnCompletion(IntPtr userData, IntPtr json)
    {
        if (userData == IntPtr.Zero)
        {
            return;
        }

        var pending = GCHandle.FromIntPtr(userData);
        try
        {
            if (pending.Target is TaskCompletionSource<AstridResponse> completion)
            {
                completion.TrySetResult(AstridResponse.Parse(NativeMethods.BorrowString(json)));
            }
        }
        catch (Exception error)
        {
            // A managed exception must not unwind into Rust. Anything thrown here becomes the
            // task's failure, where the caller can see it.
            if (pending.Target is TaskCompletionSource<AstridResponse> completion)
            {
                completion.TrySetException(error);
            }
        }
        finally
        {
            pending.Free();
        }
    }

    private static void OnChange(IntPtr userData, IntPtr json)
    {
        if (userData == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (GCHandle.FromIntPtr(userData).Target is AstridClient client)
            {
                var notification = ChangeNotification.Parse(NativeMethods.BorrowString(json));
                client.Changed?.Invoke(notification);
            }
        }
        catch
        {
            // A subscriber that throws is the shell's bug, and swallowing it here is the only way
            // to keep it from becoming undefined behaviour in the core. It is logged by whoever
            // subscribed; there is nothing useful this frame can do with it.
        }
    }
}

/// <summary>The core would not start.</summary>
public sealed class AstridStartupException(string message) : Exception(message);
