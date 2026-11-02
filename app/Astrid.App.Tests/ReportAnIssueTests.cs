using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// Report an Issue goes to the deployment's own feedback list, or to the web — never to Astrid's
/// list (task 68771137), and in-app rather than through a browser when the reader may actually
/// write there (task 6fc92134).
/// </summary>
/// <remarks>
/// <para>
/// The bug being fixed here is iOS's: <c>Constants.swift:151</c> carried Astrid's production
/// Bugs &amp; Feedback list id, so a white-label build sent a partner's users to Astrid's list,
/// filing bugs onto someone else's board and reading other people's (task <c>a4352b30</c>). The
/// cure is that the id is brand config, read at runtime out of <c>brand.feedbackListId</c> on
/// <c>/api/v1/capabilities</c>, and that **the client carries no id of its own** — which is the
/// half a shell can get wrong by adding "a sensible default".
/// </para>
/// <para>
/// So these tests assert both branches and the absence. The core answers
/// <c>Option&lt;String&gt;</c> and nothing here re-derives what <c>None</c> means: the core's rule
/// is that absent means this deployment has no in-app feedback list, and the shell's whole job is
/// to render that answer as a destination.
/// </para>
/// <para>
/// Since task <c>6fc92134</c> the answer is a <see cref="FeedbackDestination"/> rather than a URL,
/// because there are now two kinds of door and only the core can say which one this deployment
/// has. Every guarantee below is the one <c>68771137</c> wrote; what changed is that the in-app
/// branch exists, and that reaching it costs a second question — <c>publicList</c>, for whether
/// the reader may write in the list at all.
/// </para>
/// </remarks>
public sealed class ReportAnIssueTests
{
    private const string Origin = "https://acme.example";

    /// <summary>Astrid's own list id, which must appear nowhere in this shell.</summary>
    private const string AstridsOwnList = "6afe098f-e163-46f7-ac4b-4f879a9314eb";

    /// <summary>What <c>publicList</c> answers for a feedback list of each public type.</summary>
    private static object Opened(string id, string publicListType) => new
    {
        list = new { id, name = "Bugs and Feedback", ownerId = "them", privacy = "PUBLIC", publicListType },
        role = "viewer",
        canAddTask = publicListType == "collaborative",
        canComment = publicListType == "collaborative",
    };

    /// <summary>
    /// A deployment that names a feedback list opens that list — its own, not Astrid's — and opens
    /// it in-app, because the reader may file in it (task 6fc92134).
    /// </summary>
    [Fact]
    public async Task A_deployment_that_names_a_feedback_list_opens_its_own_task_68771137()
    {
        var core = new FakeCore()
            .AnswerOk("feedbackListId", "list-acme-feedback")
            .AnswerOk("publicList", Opened("list-acme-feedback", "collaborative"));
        var settings = new SettingsViewModel(core);

        var destination = await settings.FeedbackDestinationAsync(Origin);

        Assert.Equal("list-acme-feedback", destination.ListId);
        Assert.Null(destination.WebUrl);
        Assert.True(destination.IsInApp);
        Assert.Equal(["feedbackListId", "publicList"], core.SentKinds());
    }

    /// <summary>
    /// A deployment that names none opens the web, which is what Android already does — and is
    /// the answer on every deployment running today, since web's half of
    /// <c>brand.feedbackListId</c> is not deployed yet.
    /// </summary>
    /// <remarks>
    /// The assertion that matters is the second one. A shell that fell back to a built-in id would
    /// pass the first test and still be the bug.
    /// </remarks>
    [Fact]
    public async Task A_deployment_that_names_no_feedback_list_opens_the_web_task_68771137()
    {
        var core = new FakeCore().AnswerOk("feedbackListId");
        var settings = new SettingsViewModel(core);

        var destination = await settings.FeedbackDestinationAsync(Origin);

        Assert.Equal($"{Origin}/help", destination.WebUrl);
        Assert.Null(destination.ListId);
        Assert.False(destination.IsInApp);
        Assert.DoesNotContain(AstridsOwnList, destination.WebUrl!, StringComparison.Ordinal);
    }

    /// <summary>
    /// And a core that cannot answer at all still opens somewhere.
    /// </summary>
    /// <remarks>
    /// A build whose pin predates <c>feedbackListId</c> answers "bad request", and a first run
    /// that has never reached the server answers from an empty cache. Neither is a reason for a
    /// row to do nothing when it is clicked — the row is a door, and the web is always a door.
    /// </remarks>
    [Fact]
    public async Task A_core_that_cannot_answer_still_opens_the_web_task_68771137()
    {
        var core = new FakeCore().AnswerFailure("feedbackListId", AstridFailureKind.BadRequest);
        var settings = new SettingsViewModel(core);

        Assert.Equal($"{Origin}/help", (await settings.FeedbackDestinationAsync(Origin)).WebUrl);
    }

    /// <summary>A trailing slash on the origin does not become a double slash in the door.</summary>
    /// <remarks>
    /// Asked of a <c>copy_only</c> list, since that is the branch that still builds a URL now that
    /// a writable list is opened in-app.
    /// </remarks>
    [Fact]
    public async Task The_origin_is_joined_once_task_68771137()
    {
        var core = new FakeCore()
            .AnswerOk("feedbackListId", "list-acme-feedback")
            .AnswerOk("publicList", Opened("list-acme-feedback", "copy_only"));
        var settings = new SettingsViewModel(core);

        Assert.Equal(
            $"{Origin}/lists/list-acme-feedback",
            (await settings.FeedbackDestinationAsync($"{Origin}/")).WebUrl);
    }

    /// <summary>
    /// A feedback list nobody but its members may write in sends the reader to the web, rather
    /// than in-app to a screen where the report cannot be filed.
    /// </summary>
    /// <remarks>
    /// The rule is <c>canAddTask</c>, and it comes <em>from the core</em> with the list — it
    /// follows <c>publicListType</c>, it is pinned by <c>contracts/fixtures/permissions.json</c>,
    /// and re-deriving it here would be business logic in <c>app/</c>. A door that lands somewhere
    /// the person may not write is worse than the browser, which is why this branch exists at all.
    /// </remarks>
    [Fact]
    public async Task A_feedback_list_the_reader_may_not_write_in_opens_the_web_task_6fc92134()
    {
        var core = new FakeCore()
            .AnswerOk("feedbackListId", "list-acme-feedback")
            .AnswerOk("publicList", Opened("list-acme-feedback", "copy_only"));
        var settings = new SettingsViewModel(core);

        var destination = await settings.FeedbackDestinationAsync(Origin);

        Assert.Equal($"{Origin}/lists/list-acme-feedback", destination.WebUrl);
        Assert.Null(destination.ListId);
    }

    /// <summary>
    /// A feedback list the core cannot open — offline, or a pin that predates
    /// <c>publicList</c> — is still a door, and the door is the web.
    /// </summary>
    /// <remarks>
    /// The same judgement as <c>68771137</c>'s third test, one layer along: the row is a door, and
    /// a row that does nothing when it is clicked is the worse answer. It goes to the list rather
    /// than to <c>/help</c>, because the id is known and only the reading of it failed.
    /// </remarks>
    [Fact]
    public async Task A_feedback_list_that_cannot_be_opened_still_opens_the_web_task_6fc92134()
    {
        var core = new FakeCore()
            .AnswerOk("feedbackListId", "list-acme-feedback")
            .AnswerFailure("publicList", AstridFailureKind.Offline);
        var settings = new SettingsViewModel(core);

        var destination = await settings.FeedbackDestinationAsync(Origin);

        Assert.Equal($"{Origin}/lists/list-acme-feedback", destination.WebUrl);
        Assert.Null(destination.ListId);
    }

    /// <summary>An id with something in it that a URL would mangle is escaped, as it always was.</summary>
    [Fact]
    public async Task A_list_id_is_escaped_into_the_web_door_task_6fc92134()
    {
        var core = new FakeCore()
            .AnswerOk("feedbackListId", "a list/id")
            .AnswerFailure("publicList", AstridFailureKind.Offline);
        var settings = new SettingsViewModel(core);

        Assert.Equal(
            $"{Origin}/lists/a%20list%2Fid",
            (await settings.FeedbackDestinationAsync(Origin)).WebUrl);
    }

    /// <summary>
    /// The shell holds no feedback list id of its own, in any file.
    /// </summary>
    /// <remarks>
    /// This is the regression that matters most and the one a view-model test cannot catch: the
    /// original bug was a constant, and a constant can be reintroduced as a "fallback" beside a
    /// perfectly correct call to the core.
    /// </remarks>
    [Fact]
    public void No_file_in_the_shell_carries_a_feedback_list_id_task_68771137()
    {
        var app = Path.GetDirectoryName(LocalisationTests.AppDirectory())!;
        var carrying = Directory
            .EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => Path.GetFileName(path) != "ReportAnIssueTests.cs")
            .Where(path => File.ReadAllText(path).Contains(AstridsOwnList, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(carrying.Count == 0,
            "a hardcoded feedback list id is the bug task a4352b30 fixed, in a new language:\n  "
            + string.Join("\n  ", carrying));
    }

    /// <summary>
    /// The hub has the row, and it asks the view model where to go rather than naming a page.
    /// </summary>
    /// <remarks>
    /// Both halves, because they fail differently: no <c>ListViewItem</c> is a feature nobody can
    /// reach however green the view-model tests are, and a literal URL in the code-behind is the
    /// decision moving back into <c>app/</c>, where CLAUDE.md says it does not live.
    /// </remarks>
    [Fact]
    public void The_settings_hub_has_a_report_an_issue_row_task_68771137()
    {
        var app = LocalisationTests.AppDirectory();
        var hub = File.ReadAllText(Path.Combine(app, "Views", "Settings", "SettingsView.xaml"));
        var code = File.ReadAllText(Path.Combine(app, "Views", "Settings", "SettingsView.xaml.cs"));

        Assert.Contains("Tag=\"Feedback\"", hub, StringComparison.Ordinal);
        Assert.Contains("FeedbackDestinationAsync", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// The hub honours both halves of the decision: in-app through the shell, the web through the
    /// launcher (task 6fc92134).
    /// </summary>
    /// <remarks>
    /// A code-behind that read only <c>WebUrl</c> would pass every view-model test above and still
    /// never open the in-app screen — the branch has to be in the file that does the opening.
    /// </remarks>
    [Fact]
    public void The_hub_opens_the_feedback_list_in_app_when_the_decision_says_so_task_6fc92134()
    {
        var code = File.ReadAllText(Path.Combine(
            LocalisationTests.AppDirectory(), "Views", "Settings", "SettingsView.xaml.cs"));

        Assert.Contains("ShowPublicListAsync", code, StringComparison.Ordinal);
        Assert.Contains("LaunchUriAsync", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// The in-app feedback list is read-only: it offers no add box.
    /// </summary>
    /// <remarks>
    /// Not a style choice. Whether the Outbox may hold a write against a list this device has not
    /// cached is still an open question in the core — <c>publicList</c> deliberately does not cache
    /// the list it answers — and an offline bug report is exactly the correlated case, since
    /// something being broken is why the person is filing. So the screen reads, and carries one
    /// honest door to the web for the writing. An add box here would be that question guessed at.
    /// </remarks>
    [Fact]
    public void The_in_app_feedback_list_offers_no_add_box_task_6fc92134()
    {
        var view = File.ReadAllText(Path.Combine(
            LocalisationTests.AppDirectory(), "Views", "PublicListView.xaml"));

        Assert.DoesNotContain("QuickAdd", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreateTask", view, StringComparison.OrdinalIgnoreCase);
        // The door that does the writing, instead.
        Assert.Contains("x:Uid=\"public_list_report_on_web\"", view, StringComparison.Ordinal);
    }
}
