namespace Astrid.Core.Bindings;

/// <summary>
/// The core, as the shell's view models see it.
/// </summary>
/// <remarks>
/// <para>
/// One method, because the boundary has one shape: a command in, a response out. Everything a view
/// model does goes through here.
/// </para>
/// <para>
/// It exists as an interface so a view model can be tested against a scripted core rather than a
/// live one. That is not a small convenience: the cases worth testing in a view model are a 401
/// mid-edit, a list that comes back empty, and a write that goes to the Outbox because the network
/// is gone — none of which a real server produces on request.
/// </para>
/// </remarks>
public interface IAstridCore
{
    /// <summary>Send a command and await the answer. Never blocks the calling thread.</summary>
    Task<AstridResponse> CallAsync(object command, CancellationToken cancellationToken = default);

    /// <summary>Raised when the cache changes underneath the app, on a pool thread.</summary>
    event Action<ChangeNotification>? Changed;
}
