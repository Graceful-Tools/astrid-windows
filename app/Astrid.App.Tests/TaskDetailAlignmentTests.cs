using System.Xml.Linq;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The task detail pane's shape, as Jon settled it across the clients in
/// <see href="https://astrid.cc/t/AWTD-1177">AWTD-1177</see> and this repo's half, task
/// <c>439e94a4</c>: the Mac pattern, in the right-hand pane of the three-pane layout.
/// </summary>
/// <remarks>
/// <para>
/// Three of the four terms of that decision are shape rather than behaviour — which number, which
/// element, in which order — so they are read off the XAML the way <see cref="WebUxParityTests"/>
/// and <see cref="LocalisationTests"/> already read it. A view-model test cannot see that a row
/// hand-rolled its own indent, and the UI smoke suite that could is ten minutes and does not run
/// on every gate.
/// </para>
/// <para>
/// The fourth term — the field order — is the core's and is tested there
/// (<c>astrid_rules::rows::detail::list_mode_is_who_date_priority_lists</c>). What is Windows' to
/// get wrong is whether the pane draws the rows in the order it is handed, so that is what
/// <see cref="The_four_contract_fields_are_in_the_cores_order_task_439e94a4"/> asks.
/// </para>
/// </remarks>
public sealed class TaskDetailAlignmentTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The shared icon-column width, by the name the rows reference it under.</summary>
    private const string IconColumnKey = "DetailFieldIconColumn";

    /// <summary>The shared gap between the icon column and the value, likewise.</summary>
    private const string GapKey = "DetailFieldGap";

    private static string PaneText() => File.ReadAllText(PanePath());

    private static string PanePath() =>
        Path.Combine(LocalisationTests.AppDirectory(), "Views", "TaskDetailPane.xaml");

    private static XElement Pane() => XDocument.Load(PanePath()).Root!;

    private static XElement Resources() =>
        XDocument.Load(Path.Combine(LocalisationTests.AppDirectory(), "Views", "ShellResources.xaml")).Root!;

    /// <summary>The value of an <c>x:Double</c> resource, which must be declared exactly once.</summary>
    private static double SharedNumber(string key)
    {
        var declarations = Resources()
            .Descendants()
            .Where(element => element.Name.LocalName == "Double"
                              && (string?)element.Attribute(Xaml + "Key") == key)
            .ToList();
        Assert.Single(declarations);
        return double.Parse(declarations[0].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The alignment arithmetic is stated ONCE and every row references it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The invariant itself is the one web writes down in <c>components/task-detail/
    /// TaskFieldRow.tsx</c>: the icon column is exactly as wide as the title row's leading
    /// control and shares the title row's gap inside the same container padding, so each field
    /// icon lands centred under the checkbox and each value's left edge lands on the title's.
    /// Web's terms are 16 + 32 + 8 = 56px; iOS and Mac state the same invariant with their own
    /// numbers, 16 + 34 + 12 = 62pt. Windows' are web's.
    /// </para>
    /// <para>
    /// Windows had the prose and not the numbers: the sum was written in a comment at the top of
    /// the pane and then <c>&lt;Grid ColumnSpacing="8"&gt;</c> over
    /// <c>&lt;ColumnDefinition Width="32" /&gt;</c> was retyped for all eleven rows, with no 32,
    /// 8 or 56 named anywhere in <c>app/</c>. Each of the other two platforms had to fix exactly
    /// that drift, which is why the task asks for the numbers to be stated once and pinned
    /// rather than for the comment to be rewritten.
    /// </para>
    /// <para>
    /// The container padding is pinned here too, and deliberately: it lives on the header
    /// <c>Border</c> and the body <c>ScrollViewer</c> rather than on the rows, so asserting the
    /// two shared numbers is not enough to keep the columns together — web's own test makes the
    /// same point in the same words.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_detail_field_alignment_numbers_are_stated_once_task_439e94a4()
    {
        var iconColumn = SharedNumber(IconColumnKey);
        var gap = SharedNumber(GapKey);

        // The header's padding and the body's leading padding are the first term of the sum.
        var pane = Pane();
        var header = pane.Descendants()
            .Single(element => element.Name.LocalName == "Border"
                               && (string?)element.Attribute("Grid.Row") == "0");
        var body = pane.Descendants()
            .Single(element => element.Name.LocalName == "ScrollViewer"
                               && (string?)element.Attribute("Grid.Row") == "1");
        var inset = double.Parse(
            ((string)header.Attribute("Padding")!).Split(',')[0],
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(
            inset,
            double.Parse(
                ((string)body.Attribute("Padding")!).Split(',')[0],
                System.Globalization.CultureInfo.InvariantCulture));

        // The whole of the invariant, as one sum, in the terms web states it in.
        Assert.Equal(16d, inset);
        Assert.Equal(32d, iconColumn);
        Assert.Equal(8d, gap);
        Assert.Equal(56d, inset + iconColumn + gap);

        // And no row states any of it for itself.
        var text = PaneText();
        Assert.DoesNotContain("<ColumnDefinition Width=\"32\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ColumnSpacing=\"8\"", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every row that has an icon column takes BOTH numbers from the shared resources.
    /// </summary>
    /// <remarks>
    /// The previous test says the numbers exist and that no row writes one out; this one says the
    /// rows actually reference them. The two together are what stop a row from quietly opting out
    /// — a grid that neither names 32 nor asks for <c>DetailFieldIconColumn</c> would pass the
    /// first test by having no icon column at all, which is a different bug and not this one.
    /// Rows without an icon column, like an attachment's name-and-size line, are not field rows
    /// and are left alone.
    /// </remarks>
    [Fact]
    public void Every_field_row_takes_its_indent_from_the_shared_resources_task_439e94a4()
    {
        var iconSlots = Pane()
            .Descendants()
            .Where(element => element.Name.LocalName == "ColumnDefinition"
                              && ((string?)element.Attribute("MinWidth"))?.Contains(IconColumnKey, StringComparison.Ordinal) == true)
            .ToList();

        // The header, then the ten rows under it.
        Assert.Equal(11, iconSlots.Count);

        foreach (var slot in iconSlots)
        {
            var grid = slot.Parent!.Parent!;
            Assert.Equal("Grid", grid.Name.LocalName);
            Assert.Contains(
                GapKey,
                (string?)grid.Attribute("ColumnSpacing") ?? string.Empty,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The priority row's marker NAMES the field; it does not report the open task's value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The row was bound to the core's <c>priorityGlyph</c>, which is
    /// <c>priority_glyph(task.priority)</c> — so the marker was <c>○</c> on a task with no
    /// priority, which is most tasks, and <c>!!!</c> only on a high one. Both other platforms
    /// make it a constant and say why in nearly the same words: web's <c>PRIORITY_ROW_GLYPH</c>
    /// is <i>"CONSTANT BY DESIGN — it names the field while the picker beside it reports the
    /// value, so it must not follow the task's own priority"</i>, and <c>PriorityGlyph.rowIcon</c>
    /// on iOS and Mac is the same rule. The core already states the constant, as
    /// <c>rows::detail::PRIORITY_ROW_ICON</c>; Windows was reading the wrong one of the two
    /// fields it is sent.
    /// </para>
    /// <para>
    /// Being constant, it is drawn in the muted colour every other field icon wears rather than
    /// in the task's priority colour: a fixed <c>!!!</c> tinted "no priority" reads as a
    /// high-priority mark greyed out. The four swatches beside it carry the colour, which is
    /// where web and the Mac put it too.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_priority_row_marker_names_the_field_and_not_the_value_task_439e94a4()
    {
        Assert.Equal("!!!", new Astrid.App.ViewModels.TaskDetailViewModel(new FakeCore()).PriorityRowMarker);

        var text = PaneText();
        Assert.Contains("Detail.PriorityRowMarker", text, StringComparison.Ordinal);
        // The value-following glyph is gone from the pane, and so is tinting the marker by it.
        Assert.DoesNotContain("Detail.PriorityGlyph", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PriorityBrush", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The four contract fields are drawn in the order the core hands them: Who, Date, Priority,
    /// Lists.
    /// </summary>
    /// <remarks>
    /// Reminder and Repeat are Windows' own rows and sit between Date and Priority; the task id,
    /// board state, waiting-on and description rows come after Lists. Neither is interleaved INTO
    /// the four, which is the whole of the contract — the order of the four relative to each
    /// other, stated once in <c>astrid_rules::rows::detail::LIST_MODE</c> and not re-decided in a
    /// renderer.
    /// </remarks>
    [Fact]
    public void The_four_contract_fields_are_in_the_cores_order_task_439e94a4()
    {
        var uids = Pane()
            .Descendants()
            .Select(element => (string?)element.Attribute(Xaml + "Uid") ?? string.Empty)
            .Where(uid => uid is "assignee_2" or "due_date" or "priority_2" or "lists_2")
            .ToArray();

        Assert.Equal(["assignee_2", "due_date", "priority_2", "lists_2"], uids);
    }

    /// <summary>
    /// The subtasks section says how many of them are done.
    /// </summary>
    /// <remarks>
    /// iPhone, iPad and Mac have shown "Sub tasks 0/2" for a long time and web gained it as
    /// AWTD-1227; Windows had the bare word "Subtasks" over the rows. The count is the part of
    /// the Mac pattern the task names, and the data for it was already here —
    /// <c>SubtaskSummary.Completed</c> is filled from the same <c>taskDetail</c> payload the rows
    /// come from and was simply unread.
    /// </remarks>
    [Fact]
    public void The_subtasks_section_shows_a_completed_total_count_task_439e94a4()
    {
        Assert.Contains("Detail.SubtaskCount", PaneText(), StringComparison.Ordinal);
    }
}
