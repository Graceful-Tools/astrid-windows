using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;

namespace Astrid.App.UITests;

/// <summary>
/// A few canned routes on loopback, for the surfaces the app cannot draw without a server
/// (task 68b00422).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <see cref="AstridApp.Launch"/> writes a session cookie and, until now,
/// had nothing behind it, so every read that left the device came back <em>the session is not
/// valid</em>. Most of the app does not care — the sidebar, the single-list board, the task rows
/// and every filter are projections over the cache — which is why the suite got this far without
/// one. The surfaces that do care were simply untestable: the project board's swimlanes are
/// <c>GET /api/v1/projects/{id}/board-rows</c>, and the core falls back to its cache only for a
/// <em>retryable</em> failure, treating a refusal as an error on purpose ("the cache is for a
/// missing network, not for a server that said no"). Seeding the cached rows does not help, since
/// the read goes out first and fails the same way.
/// </para>
/// <para>
/// <b>What it is not.</b> Not a fake Astrid. It answers the handful of paths a test names and
/// 404s everything else, loudly enough to see in the test output — a stub that invented plausible
/// answers for unnamed routes would let a test pass against behaviour nobody wrote down. It holds
/// no state and has no notion of a session: the app's cookie is never checked, because what is
/// being tested is the shell, not the server's auth.
/// </para>
/// <para>
/// <b>http, on loopback.</b> <c>ServerSelection.ParseOrigin</c> accepts exactly that and nothing
/// else — an https origin anywhere, http only when it is the loopback address — so no production
/// path has to be relaxed for this to work.
/// </para>
/// </remarks>
public sealed class StubServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<string, (int Status, string Body)> _routes =
        new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _unanswered = new();
    private readonly CancellationTokenSource _stopping = new();

    private StubServer(int port)
    {
        Origin = $"http://127.0.0.1:{port}";
        // The prefix needs the trailing slash; the host must be the literal loopback address and
        // not "localhost", or HttpListener wants an ACL on this machine.
        _listener.Prefixes.Add($"{Origin}/");
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    /// <summary>What <c>ASTRID_SERVER_URL</c> is set to, which is also the cache file's name.</summary>
    public string Origin { get; }

    /// <summary>
    /// Start one on a free port.
    /// </summary>
    /// <remarks>
    /// The port is taken by binding a socket to 0 and reading back what the OS gave, then letting
    /// it go — the usual race, and the usual answer to it, which is that nothing else on a
    /// developer's machine is racing for ports in the half-millisecond between.
    /// </remarks>
    public static StubServer Start()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return new StubServer(port);
    }

    /// <summary>
    /// Answer <paramref name="path"/> with this JSON. The path is the whole thing after the
    /// origin, query string included, exactly as the core asks for it.
    /// </summary>
    public StubServer Route(string path, string json, int status = 200)
    {
        _routes[path] = (status, json);
        return this;
    }

    /// <summary>
    /// Every path that was asked for and had no route, in the order they arrived.
    /// </summary>
    /// <remarks>
    /// A test that is not getting what it expects reads this first: the core asks for exactly the
    /// URL it means, and a route registered under a path that is off by a query parameter looks
    /// from the outside like a server that is not running at all.
    /// </remarks>
    public IReadOnlyList<string> Unanswered() => _unanswered.ToArray();

    private async Task ServeAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                return; // Stopped.
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            var path = context.Request.Url?.PathAndQuery ?? string.Empty;
            var (status, body) = _routes.TryGetValue(path, out var canned)
                ? canned
                : (404, """{"error":"no route in the stub for this path"}""");
            if (status == 404 && !_routes.ContainsKey(path))
            {
                _unanswered.Enqueue(path);
                Debug.WriteLine($"StubServer: no route for {path}");
            }

            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            try
            {
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
            catch (HttpListenerException)
            {
                // The app went away mid-answer, which is what Dispose looks like from here.
            }
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
        }
        _stopping.Dispose();
    }
}
