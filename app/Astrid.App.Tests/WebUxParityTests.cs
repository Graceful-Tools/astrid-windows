using System.Xml.Linq;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// Three cross-client UX decisions Jon took on 2026-10-12, where he picked the web pattern every
/// time (task ffcc6abf): the default task order, where the user menu goes, and what the
/// task-detail overflow menu holds.
/// </summary>
/// <remarks>
/// <para>
/// The first of the three needed no change and has no test here: order is not the shell's to
/// decide. <c>TaskListViewModel</c> asks one <c>rowsForList</c> command, and
/// <c>astrid_core::filters</c> answers it — <c>sort_by_setting</c> sinks completed tasks in every
/// value-based order and the splice puts a parent's subtasks under it, pinned there against web by
/// <c>aitd495_a_list_with_no_sort_is_auto_completed_last_as_on_web</c>. A copy of that rule here
/// would be a second place for it to drift.
/// </para>
/// <para>
/// The other two are shape, not behaviour: which element is where, in which order. So they are
/// read off the XAML, the way <see cref="LocalisationTests"/> reads it — a view-model test cannot
/// see the order of a MenuFlyout's children, and the UI smoke suite that could is ten minutes and
/// does not run on every gate.
/// </para>
/// </remarks>
public sealed class WebUxParityTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XElement View(string file) =>
        XDocument.Load(Path.Combine(LocalisationTests.AppDirectory(), "Views", file)).Root!;

    /// <summary>What to call an element in an assertion: its name, else its uid, else its tag.</summary>
    private static string Key(XElement element) =>
        (string?)element.Attribute(Xaml + "Name")
        ?? (string?)element.Attribute(Xaml + "Uid")
        ?? element.Name.LocalName;

    /// <summary>
    /// The overflow menu holds web's items in web's order (AWTD-1181, where Jon chose both web
    /// patterns).
    /// </summary>
    /// <remarks>
    /// Web's <c>TaskActionMenu</c> reads Copy, Share, Copy task id, then Status, Won't do and
    /// Delete behind separators. Windows had the same set but in another order — its copy-ish
    /// items ran Copy link, Copy task id, Share, Copy to a list — and web's "Copy" is this pane's
    /// "Copy to a list…", since web's <c>onCopy</c> is <c>handleCopyTask(taskId, targetListId)</c>.
    /// One item is Windows' own and sits at the end of the group it belongs to: there is no
    /// address bar to copy a URL out of. Close was a second until task 77d17dc8 moved it to the
    /// header's <c>✕</c>, where the Mac keeps it.
    /// </remarks>
    [Fact]
    public void The_task_detail_overflow_menu_is_in_webs_order_task_ffcc6abf()
    {
        var button = View("TaskDetailPane.xaml")
            .Descendants()
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "TaskActionsButton");
        var flyout = button.Descendants()
            .Single(element => element.Name.LocalName == "MenuFlyout");

        Assert.Equal(
            [
                "CopyTaskItem",   // web's Copy
                "ShareItem",      // web's Share
                "CopyTaskIdItem", // web's Copy task id
                "CopyLinkItem",   // Windows' own, after the group web shares
                "MenuFlyoutSeparator",
                "StatusSubMenu",
                "MenuFlyoutSeparator",
                "WontDoItem",
                "MenuFlyoutSeparator",
                "delete",
                // Close was a last item here, Windows' own because a pane closes and a web route
                // does not. It is the header's `✕` since task 77d17dc8, which follows the Mac.
            ],
            flyout.Elements().Select(Key).ToArray());
    }

    /// <summary>
    /// The account entry is the sidebar's footer and says who is signed in (AWTD-1179, where Jon
    /// chose web-desktop).
    /// </summary>
    /// <remarks>
    /// Web's <c>LeftSidebar</c> ends in a footer row carrying the signed-in user's avatar, their
    /// name and a gear, which opens Settings. Windows had a bare person glyph in the list header
    /// instead, at the far end of the window from web's, and it never said whose account it was.
    /// </remarks>
    [Fact]
    public void The_account_entry_is_the_sidebars_footer_and_names_the_user_task_ffcc6abf()
    {
        var sidebar = View("SidebarView.xaml");
        var entry = sidebar.Descendants()
            .Single(element => (string?)element.Attribute("AutomationProperties.AutomationId")
                               == "AccountButton");

        Assert.Equal("account_and_settings", (string?)entry.Attribute(Xaml + "Uid"));
        // It leads to Settings, which is the whole of the decision.
        Assert.NotNull((string?)entry.Attribute("Click"));
        Assert.Contains("Shell.ShowSettings(true)", SidebarCodeBehind(), StringComparison.Ordinal);
        // The name, from the account the shell reads at startup.
        Assert.Contains(
            entry.Descendants().Select(element => (string?)element.Attribute("Text")),
            text => text?.Contains("Settings.Account.DisplayName", StringComparison.Ordinal) == true);

        // And it is no longer in the list header, where web has nothing of the kind.
        Assert.DoesNotContain(
            View("ListHeaderView.xaml").Descendants()
                .Select(element => (string?)element.Attribute(Xaml + "Uid")),
            uid => uid == "account_and_settings");
    }

    /// <summary>
    /// The bell is in the sidebar's header row, before the hide button, as web's is (task
    /// 96759883).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Web's <c>LeftSidebar</c> carries <c>&lt;NotificationBell align="start" /&gt;</c> in its own
    /// header row, immediately left of search and the sidebar toggle (AWTD-1357 — whose comment
    /// records that the list header "renders nothing on desktop, so there was no bell there at
    /// all"). Windows had it at the far right of the list header, the opposite end of the window,
    /// and it was the last thing left in that cluster: the account button beside it had already
    /// gone to the sidebar's foot for the same reason in task <c>ffcc6abf</c>, the test above.
    /// </para>
    /// <para>
    /// So it goes into the sidebar's header row, immediately left of the hide button — web's
    /// order, bell before toggle. Not the title bar's brand header, which is where web's sidebar
    /// *header* lives on Windows (task <c>20dcc235</c>): the ask says the left side panel, that
    /// control is deliberately view-model-free and speaks to the shell through an event, and this
    /// one already carries <c>Shell</c> and already hosts a flyout of this shape.
    /// </para>
    /// <para>
    /// The bell going with the sidebar into the 48px rail is web's behaviour too, not a loss:
    /// <c>LeftSidebar.tsx</c>'s collapsed rail renders only the toggle.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_bell_is_the_sidebars_header_row_before_the_hide_button_task_96759883()
    {
        var bell = View("SidebarView.xaml")
            .Descendants()
            .Single(element => (string?)element.Attribute(Xaml + "Uid") == "notifications_button");

        Assert.Equal("Button", bell.Name.LocalName);

        // The sidebar's header row — the top one — shared with the way to hide the sidebar.
        var row = bell.Parent!;
        Assert.Equal("0", (string?)row.Attribute("Grid.Row"));
        Assert.Contains(
            row.Elements().Select(element => (string?)element.Attribute(Xaml + "Uid")),
            uid => uid == "hide_sidebar");
        // Web's order: the bell comes first, the toggle last.
        Assert.Equal("notifications_button", (string?)row.Elements().First().Attribute(Xaml + "Uid"));

        // The badge travelled with it: how many are unread, and whether to draw the count at all.
        var bindings = bell.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();
        Assert.Contains(bindings, value => value.Contains("Notifications.HasUnread", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains("Notifications.UnreadText", StringComparison.Ordinal));

        // Anchored at the window's left edge now, so the flyout has to open rightwards —
        // web says the same thing with align="start".
        var flyout = bell.Descendants().Single(element => element.Name.LocalName == "Flyout");
        Assert.Equal("BottomEdgeAlignedLeft", (string?)flyout.Attribute("Placement"));

        // And what the flyout does is the sidebar's to handle, all three of them.
        var sidebar = SidebarCodeBehind();
        Assert.Contains("Shell.Notifications.RefreshAsync()", sidebar, StringComparison.Ordinal);
        Assert.Contains("Shell.Notifications.MarkAllReadAsync()", sidebar, StringComparison.Ordinal);
        Assert.Contains("Shell.Notifications.MarkReadAsync(", sidebar, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing of the bell is left in the list header, where web has none.
    /// </summary>
    /// <remarks>
    /// Separate from the test above because the two can fail for different reasons: a bell drawn
    /// in both places looks right in the sidebar and is a second badge nobody maintains.
    /// </remarks>
    [Fact]
    public void The_list_header_keeps_no_bell_of_its_own_task_96759883()
    {
        Assert.DoesNotContain(
            View("ListHeaderView.xaml").Descendants()
                .Select(element => (string?)element.Attribute(Xaml + "Uid")),
            uid => uid?.StartsWith("notifications", StringComparison.Ordinal) == true);

        Assert.DoesNotContain(
            "Notifications",
            File.ReadAllText(Path.Combine(
                LocalisationTests.AppDirectory(), "Views", "ListHeaderView.xaml.cs")));
    }

    private static string SidebarCodeBehind() =>
        File.ReadAllText(Path.Combine(LocalisationTests.AppDirectory(), "Views", "SidebarView.xaml.cs"));
}
