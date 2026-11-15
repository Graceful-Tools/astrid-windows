using System.Xml.Linq;
using Astrid.App.ViewModels;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The four places the task detail pane follows the Mac's chrome rather than astrid-web's
/// (task <c>77d17dc8</c>).
/// </summary>
/// <remarks>
/// <para>
/// The pane was built to match astrid-web decision by decision, and four of the five differences
/// Jon's screenshot showed were places where the Mac and web disagree and Windows had sided with
/// web. On 2026-11-01 Jon settled it the other way: <b>the Mac is the reference for this pane's
/// layout and chrome</b>, and Windows diverges from web here deliberately. The four rows are the
/// header, the card grouping, the comments toolbar and the assignee chip.
/// </para>
/// <para>
/// Only the chrome. The <c>Astrid*</c> brushes still track <c>astrid-web/styles/themes/*.css</c>
/// value for value — on colour it is the Mac's <c>Theme.swift</c> that drifted — so nothing here
/// asserts a palette. And nothing here is a licence to drop a feature: the pane keeps priority,
/// list membership, the task-id row, board state, waiting-on blockers, attachments, comments with
/// reply/edit/delete, and the timer, which the Mac's pane does not all show.
/// </para>
/// <para>
/// Read off the XAML, for the reason <see cref="DesignRadiusScaleTests"/> and
/// <see cref="TaskDetailAlignmentTests"/> give: a view-model test cannot see which half of the
/// pane a title lives in, and the UI smoke suite that could is ten minutes and does not run on
/// every gate. The one assertion that is about behaviour rather than placement — that the system
/// filter actually filters — is a view-model test, because a toolbar with a dead toggle would
/// pass every XAML assertion above it.
/// </para>
/// </remarks>
public sealed class MacDetailChromeTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XElement Pane() =>
        XDocument.Load(Path.Combine(LocalisationTests.AppDirectory(), "Views", "TaskDetailPane.xaml")).Root!;

    /// <summary>The pinned header: row 0 of the pane's outer grid.</summary>
    private static XElement Header() =>
        Pane().Descendants().Single(element =>
            element.Name.LocalName == "Border" && (string?)element.Attribute("Grid.Row") == "0");

    /// <summary>The scrolling body: row 1.</summary>
    private static XElement Body() =>
        Pane().Descendants().Single(element =>
            element.Name.LocalName == "ScrollViewer" && (string?)element.Attribute("Grid.Row") == "1");

    private static XElement? Named(XElement scope, string name) =>
        scope.Descendants().FirstOrDefault(element => (string?)element.Attribute(Xaml + "Name") == name);

    /// <summary>
    /// The header is chrome, not the task: <c>✕</c> at the left, a centred "Task Details", and
    /// the task's own title nowhere in it.
    /// </summary>
    /// <remarks>
    /// This is the load-bearing row. A header that holds the task's editable title cannot centre
    /// a fixed string in the way the Mac does, so moving the title out is what lets the other
    /// three rows read as the Mac at all. It also lifts Close out of the <c>⋯</c> menu, where it
    /// was a Windows-only item — <see cref="WebUxParityTests"/> pins the menu it left.
    /// </remarks>
    [Fact]
    public void The_header_is_chrome_with_a_centred_title_and_a_close_at_the_left_task_77d17dc8()
    {
        var header = Header();

        var heading = Named(header, "DetailChromeTitle");
        Assert.NotNull(heading);
        Assert.Equal("Task Details", (string?)heading!.Attribute("Text"));
        Assert.Equal("Center", (string?)heading.Attribute("HorizontalAlignment"));

        var close = Named(header, "DetailCloseButton");
        Assert.NotNull(close);
        Assert.Equal("0", (string?)close!.Attribute("Grid.Column"));

        Assert.Null(Named(header, "DetailTitleBox"));
    }

    /// <summary>
    /// The centred label is the side pane's, so it is bound away on a board card (task 6df01318).
    /// </summary>
    /// <remarks>
    /// Read off the XAML for the same reason as everything else here: the view model deciding
    /// <c>ShowsDetailChromeTitle</c> correctly proves nothing if the label is not wired to it.
    /// </remarks>
    [Fact]
    public void The_centred_title_is_bound_to_the_shells_chrome_title_rule_task_6df01318()
    {
        var heading = Named(Header(), "DetailChromeTitle");
        Assert.NotNull(heading);
        Assert.Contains(
            "Shell.ShowsDetailChromeTitle",
            (string?)heading!.Attribute("Visibility") ?? string.Empty);
    }

    /// <summary>
    /// The title, its mark and the Won't-do chip moved into the body, where the Mac draws them.
    /// </summary>
    [Fact]
    public void The_title_and_its_mark_moved_into_the_body_task_77d17dc8()
    {
        var body = Body();
        Assert.NotNull(Named(body, "DetailTitleBox"));
        Assert.NotNull(Named(body, "DetailTitleMark"));
        Assert.NotNull(Named(body, "WontDoChip"));
    }

    /// <summary>
    /// The body is grouped into rounded cards, as the Mac groups it, and each takes its radius
    /// from the scale rather than restating a number.
    /// </summary>
    /// <remarks>
    /// Two cards, matching the screenshot: everything from the title down through the description
    /// in the first, subtasks in the second. The comments thread stays outside them — on the Mac
    /// it sits on the pane's own surface under its toolbar.
    /// </remarks>
    [Theory]
    [InlineData("DetailFieldsCard")]
    [InlineData("DetailSubtasksCard")]
    public void The_body_is_grouped_into_rounded_cards_task_77d17dc8(string card)
    {
        var found = Named(Body(), card);
        Assert.NotNull(found);
        Assert.Equal("Border", found!.Name.LocalName);
        Assert.Equal("{StaticResource AstridRadiusLarge}", (string?)found.Attribute("CornerRadius"));
        Assert.NotNull((string?)found.Attribute("Background"));
    }

    /// <summary>
    /// The comments toolbar: a count that is shown even at zero, the system filter, and Refresh.
    /// </summary>
    /// <remarks>
    /// The count is the part that changes behaviour as well as looks — the old bare "Comments"
    /// label was hidden when the thread was empty, so an empty thread said nothing at all about
    /// itself. The Mac says <c>Comments (0)</c>.
    /// </remarks>
    [Fact]
    public void The_comments_toolbar_counts_filters_and_refreshes_task_77d17dc8()
    {
        var body = Body();

        var toolbar = Named(body, "CommentsToolbar");
        Assert.NotNull(toolbar);
        Assert.Null((string?)toolbar!.Attribute("Visibility"));

        // The count is a number the view model answers and a converter words, not a sentence the
        // view model built: "Comments ({0})" is a translator's string like any other.
        var count = Named(toolbar, "CommentsCount");
        Assert.NotNull(count);
        var text = (string?)count!.Attribute("Text") ?? string.Empty;
        Assert.Contains("VisibleCommentCount", text);
        Assert.Contains("CommentsHeading", text);

        Assert.NotNull(Named(toolbar, "ShowSystemCommentsButton"));
        Assert.NotNull(Named(toolbar, "RefreshCommentsButton"));

        // The thread draws what the filter leaves, not the whole collection.
        var thread = Named(body, "CommentsThread");
        Assert.NotNull(thread);
        Assert.Contains("VisibleComments", (string?)thread!.Attribute("ItemsSource") ?? string.Empty);
    }

    /// <summary>
    /// The assignee is a bordered chip with a mark and a chevron, not a line of text.
    /// </summary>
    [Fact]
    public void The_assignee_is_a_chip_with_a_chevron_task_77d17dc8()
    {
        var chip = Named(Body(), "AssigneeChip");
        Assert.NotNull(chip);
        Assert.Equal("Border", chip!.Name.LocalName);
        Assert.Equal("{StaticResource AstridRadiusLarge}", (string?)chip.Attribute("CornerRadius"));
        Assert.NotNull((string?)chip.Attribute("BorderBrush"));

        // Segoe's ChevronDown, as the Mac's chip wears one.
        Assert.Contains(
            chip.Descendants(),
            element => (string?)element.Attribute("Text") == "");
    }

    /// <summary>
    /// The toggle the toolbar offers actually filters: a system note is hidden until it is asked
    /// for, and everything else is always there.
    /// </summary>
    [Fact]
    public async Task Show_system_hides_and_reveals_the_servers_own_notes_task_77d17dc8()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", new
            {
                task = new { id = "t1", title = "Plan the trip", description = "", priority = 0, completed = false },
                fieldOrder = new[] { "assignee", "when", "priority", "lists" },
                comments = new object[]
                {
                    new { id = "c0", content = "Thoughts?", createdAt = "2026-11-01T12:00:00Z", isSystem = false },
                    new { id = "c1", content = "Jon moved this to Doing", createdAt = "2026-11-01T12:01:00Z", isSystem = true },
                    new { id = "c2", content = "On it", createdAt = "2026-11-01T12:02:00Z", isSystem = false },
                },
                subtasks = Array.Empty<object>(),
            })
            .AnswerOk("dueDateOptions", new { picks = Array.Empty<object>(), timePicks = Array.Empty<object>() });

        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.Equal(3, view.Comments.Count);
        Assert.False(view.ShowsSystemComments, "the server's own notes are the quiet default");
        Assert.Equal(["c0", "c2"], view.VisibleComments.Select(comment => comment.Id));

        view.ShowsSystemComments = true;
        Assert.Equal(["c0", "c1", "c2"], view.VisibleComments.Select(comment => comment.Id));

        view.ShowsSystemComments = false;
        Assert.Equal(["c0", "c2"], view.VisibleComments.Select(comment => comment.Id));
    }

    /// <summary>
    /// The count the toolbar shows is of the thread it is drawing, so it agrees with what is
    /// under it rather than with what the server sent.
    /// </summary>
    [Fact]
    public async Task The_comment_count_follows_the_filter_task_77d17dc8()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", new
            {
                task = new { id = "t1", title = "Plan the trip", description = "", priority = 0, completed = false },
                fieldOrder = new[] { "assignee", "when", "priority", "lists" },
                comments = new object[]
                {
                    new { id = "c0", content = "Thoughts?", createdAt = "2026-11-01T12:00:00Z", isSystem = false },
                    new { id = "c1", content = "Jon moved this to Doing", createdAt = "2026-11-01T12:01:00Z", isSystem = true },
                },
                subtasks = Array.Empty<object>(),
            })
            .AnswerOk("dueDateOptions", new { picks = Array.Empty<object>(), timePicks = Array.Empty<object>() });

        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.Equal(1, view.VisibleCommentCount);
        view.ShowsSystemComments = true;
        Assert.Equal(2, view.VisibleCommentCount);
    }
}
