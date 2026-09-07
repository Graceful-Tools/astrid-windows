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

    /// <summary>The version of the core this build is talking to.</summary>
    public static string Version => NativeMethods.BorrowString(NativeMethods.Version());

    /// <summary>
    /// Start the core against a cache at <paramref name="cachePath"/>.
    /// </summary>
    /// <exception cref="AstridStartupException">
    /// The core could not start, with the reason it gave. Thrown rather than returned because
    /// there is nothing the app can do without it, and a null client that fails on every later call
    /// reports the problem in the wrong place.
    /// </exception>
    public static AstridClient Start(string cachePath, string? baseUrl = null)
    {
        var config = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["cachePath"] = cachePath,
            ["baseUrl"] = baseUrl,
        }.Where(entry => entry.Value is not null).ToDictionary(entry => entry.Key, entry => entry.Value));

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
