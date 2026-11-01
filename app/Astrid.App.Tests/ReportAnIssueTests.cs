using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// Report an Issue goes to the deployment's own feedback list, or to the web — never to Astrid's
/// list (task 68771137).
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
/// </remarks>
public sealed class ReportAnIssueTests
{
    private const string Origin = "https://acme.example";

    /// <summary>Astrid's own list id, which must appear nowhere in this shell.</summary>
    private const string AstridsOwnList = "6afe098f-e163-46f7-ac4b-4f879a9314eb";

    /// <summary>
    /// A deployment that names a feedback list opens that list — its own, not Astrid's.
    /// </summary>
    [Fact]
    public async Task A_deployment_that_names_a_feedback_list_opens_its_own_task_68771137()
    {
        var core = new FakeCore().AnswerOk("feedbackListId", "list-acme-feedback");
        var settings = new SettingsViewModel(core);

        var destination = await settings.FeedbackDestinationAsync(Origin);

        Assert.Equal($"{Origin}/lists/list-acme-feedback", destination);
        Assert.Equal(["feedbackListId"], core.SentKinds());
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

        Assert.Equal($"{Origin}/help", destination);
        Assert.DoesNotContain(AstridsOwnList, destination, StringComparison.Ordinal);
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

        Assert.Equal($"{Origin}/help", await settings.FeedbackDestinationAsync(Origin));
    }

    /// <summary>A trailing slash on the origin does not become a double slash in the door.</summary>
    [Fact]
    public async Task The_origin_is_joined_once_task_68771137()
    {
        var core = new FakeCore().AnswerOk("feedbackListId", "list-acme-feedback");
        var settings = new SettingsViewModel(core);

        Assert.Equal(
            $"{Origin}/lists/list-acme-feedback",
            await settings.FeedbackDestinationAsync($"{Origin}/"));
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
}
