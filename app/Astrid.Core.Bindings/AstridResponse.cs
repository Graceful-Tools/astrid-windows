using System.Text.Json;
using System.Text.Json.Serialization;

namespace Astrid.Core.Bindings;

/// <summary>
/// What a command answered.
/// </summary>
/// <remarks>
/// The value is left as a <see cref="JsonElement"/> rather than deserialised into a type per
/// command. The core owns those shapes and adds to them; a C# mirror of each would be a second
/// place to update and a compile error every time the core learned a field. The typed readers on
/// <see cref="Astrid.Core.Bindings.Models"/> pull out what a screen actually needs.
/// </remarks>
public sealed class AstridResponse
{
    private AstridResponse(bool ok, JsonElement value, AstridFailure? error)
    {
        Ok = ok;
        Value = value;
        Error = error;
    }

    public bool Ok { get; }

    public JsonElement Value { get; }

    public AstridFailure? Error { get; }

    /// <summary>Whether the user has to sign in again.</summary>
    public bool NeedsSignIn => Error?.Kind == AstridFailureKind.Unauthorized;

    /// <summary>
    /// Whether the work is still going to happen.
    /// </summary>
    /// <remarks>
    /// An offline write is in the Outbox and will go when the network does. Showing it as an error
    /// is how a working offline app comes to look broken — which is why this is a property and not
    /// something each call site works out from a message.
    /// </remarks>
    public bool IsStillPending => Error?.Kind == AstridFailureKind.Offline;

    public static AstridResponse Parse(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return Failed(AstridFailureKind.Cache, "the core answered with nothing");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var ok = root.TryGetProperty("ok", out var okElement) && okElement.GetBoolean();

            var value = root.TryGetProperty("value", out var valueElement)
                ? valueElement.Clone()
                : default;

            AstridFailure? error = null;
            if (root.TryGetProperty("error", out var errorElement))
            {
                error = AstridFailure.Read(errorElement);
            }

            return new AstridResponse(ok, value, error);
        }
        catch (JsonException exception)
        {
            // The core always answers with JSON, so this is a bug rather than a condition. It
            // still has to become a value: throwing here would surface as an unhandled exception
            // on a pool thread, which takes the process with it.
            return Failed(AstridFailureKind.Cache, exception.Message);
        }
    }

    private static AstridResponse Failed(AstridFailureKind kind, string message) =>
        new(false, default, new AstridFailure(kind, message, null, null));

    /// <summary>Read the value as <typeparamref name="T"/>, or <c>null</c> if it is not there.</summary>
    public T? ValueAs<T>() where T : class =>
        Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? null
            : Value.Deserialize<T>(CommandJson.Options);
}

/// <summary>Why a command did not work. Mirrors <c>astrid_core::app::FailureKind</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AstridFailureKind>))]
public enum AstridFailureKind
{
    /// <summary>Something this build of the core cannot read, or an operation it refuses.</summary>
    BadRequest,

    /// <summary>The session is gone. Sign in again; retrying will not help.</summary>
    Unauthorized,

    /// <summary>The server considered it and said no.</summary>
    Refused,

    /// <summary>It has not reached the server yet. The Outbox has it.</summary>
    Offline,

    /// <summary>The thing being acted on is not here.</summary>
    NotFound,

    /// <summary>The cache could not be read or written.</summary>
    Cache,
}

/// <summary>The failure a command reported.</summary>
public sealed record AstridFailure(
    AstridFailureKind Kind,
    string Message,
    int? Status,
    string? Id)
{
    internal static AstridFailure Read(JsonElement element)
    {
        var kind = element.TryGetProperty("kind", out var kindElement)
            ? kindElement.Deserialize<AstridFailureKind>(CommandJson.Options)
            : AstridFailureKind.Cache;
        var message = element.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString() ?? string.Empty
            : string.Empty;
        var status = element.TryGetProperty("status", out var statusElement)
            ? statusElement.GetInt32()
            : (int?)null;
        var id = element.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        return new AstridFailure(kind, message, status, id);
    }
}

/// <summary>The cache changed underneath the app.</summary>
/// <remarks>
/// It names exactly what moved so the shell refreshes exactly that. Redrawing everything on each
/// notification is worse than not subscribing: the stream can deliver several a second while
/// somebody else is working in the same list.
/// </remarks>
/// <param name="Change">What kind of thing moved: <c>task</c>, <c>list</c>, <c>synced</c>, …</param>
/// <param name="Id">The one thing that moved, when the change names one.</param>
/// <param name="TaskIds">
/// For a <c>synced</c> change, every task a background pass brought in, changed or removed.
/// Empty when the pass could not say — which means "refresh what is on screen", not "nothing".
/// </param>
public sealed record ChangeNotification(string Change, string? Id, IReadOnlyList<string>? TaskIds = null)
{
    public static ChangeNotification Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var change = root.TryGetProperty("change", out var kind) ? kind.GetString() : null;
            var id = ReadFirst(root, "id", "taskId", "channelId");
            var taskIds = root.TryGetProperty("taskIds", out var ids) && ids.ValueKind == JsonValueKind.Array
                ? ids.EnumerateArray()
                    .Where(element => element.ValueKind == JsonValueKind.String)
                    .Select(element => element.GetString()!)
                    .ToList()
                : null;
            return new ChangeNotification(change ?? "unknown", id, taskIds);
        }
        catch (JsonException)
        {
            return new ChangeNotification("unknown", null);
        }
    }

    /// <summary>Whether this change touches the task with <paramref name="taskId"/>.</summary>
    /// <remarks>
    /// A change that names nothing is taken to touch everything: the alternative — treating
    /// "could not say" as "did not" — leaves an open task stale after an external pass.
    /// </remarks>
    public bool Touches(string? taskId)
    {
        if (Id is not null)
        {
            return Id == taskId;
        }
        return TaskIds is null || TaskIds.Count == 0 || (taskId is not null && TaskIds.Contains(taskId));
    }

    private static string? ReadFirst(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }
        }
        return null;
    }
}

/// <summary>How commands and values are serialised.</summary>
/// <remarks>
/// camelCase, matching the core's <c>rename_all</c>. Stated once, here, because a call site that
/// serialises with the default naming sends <c>ListId</c> and gets "bad request" back with nothing
/// to say why.
/// </remarks>
public static class CommandJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
