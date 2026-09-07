using System.Text.Json;
using Astrid.Core.Bindings;

namespace Astrid.App.Tests;

/// <summary>
/// A core that answers from a script.
/// </summary>
/// <remarks>
/// The cases worth testing in a view model are the ones a real server will not produce on request:
/// a 401 in the middle of an edit, a list that comes back empty, a write that goes to the Outbox
/// because the network is gone. This makes each of them an ordinary test.
/// </remarks>
internal sealed class FakeCore : IAstridCore
{
    private readonly Dictionary<string, Queue<string>> _answers = new(StringComparer.Ordinal);
    private string? _fallback;

    public event Action<ChangeNotification>? Changed;

    /// <summary>Every command that was sent, in order, as JSON.</summary>
    public List<string> Sent { get; } = [];

    /// <summary>Queue an answer for the next command of this kind.</summary>
    public FakeCore Answer(string kind, object body)
    {
        var json = body as string ?? JsonSerializer.Serialize(body, CommandJson.Options);
        if (!_answers.TryGetValue(kind, out var queue))
        {
            queue = new Queue<string>();
            _answers[kind] = queue;
        }
        queue.Enqueue(json);
        return this;
    }

    /// <summary>Queue a successful answer carrying <paramref name="value"/>.</summary>
    public FakeCore AnswerOk(string kind, object? value = null)
    {
        var payload = value is null
            ? "{\"ok\":true}"
            : $"{{\"ok\":true,\"value\":{JsonSerializer.Serialize(value, CommandJson.Options)}}}";
        return Answer(kind, payload);
    }

    /// <summary>Queue a failure.</summary>
    public FakeCore AnswerFailure(string kind, AstridFailureKind failure, string message = "no")
    {
        var kindName = JsonSerializer.Serialize(failure, CommandJson.Options);
        return Answer(kind, $"{{\"ok\":false,\"error\":{{\"kind\":{kindName},\"message\":\"{message}\"}}}}");
    }

    /// <summary>What to answer when nothing is queued for a kind.</summary>
    public FakeCore Fallback(string json)
    {
        _fallback = json;
        return this;
    }

    /// <summary>Deliver a change notification, as the live stream would.</summary>
    public void Notify(string change, string? id = null) =>
        Changed?.Invoke(new ChangeNotification(change, id));

    public Task<AstridResponse> CallAsync(object command, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(command, CommandJson.Options);
        Sent.Add(json);

        using var document = JsonDocument.Parse(json);
        var kind = document.RootElement.GetProperty("kind").GetString() ?? string.Empty;

        if (_answers.TryGetValue(kind, out var queue) && queue.Count > 0)
        {
            return Task.FromResult(AstridResponse.Parse(queue.Dequeue()));
        }

        // An unscripted command is a bug in the test, and it has to read as one. Answering "ok"
        // with nothing makes a test pass while proving nothing at all.
        return Task.FromResult(AstridResponse.Parse(
            _fallback ?? $"{{\"ok\":false,\"error\":{{\"kind\":\"badRequest\",\"message\":\"nothing scripted for {kind}\"}}}}"));
    }

    /// <summary>The commands that were sent, by kind, in order.</summary>
    public IReadOnlyList<string> SentKinds() => Sent
        .Select(json =>
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("kind").GetString() ?? string.Empty;
        })
        .ToList();
}
