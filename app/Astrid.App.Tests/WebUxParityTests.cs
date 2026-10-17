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
    /// Two items are Windows' own and sit at the end of the group they belong to: there is no
    /// address bar to copy a URL out of, and no route to leave for Close.
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
                "MenuFlyoutSeparator",
                "close",          // Windows' own: a pane closes, a web route does not
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

    private static string SidebarCodeBehind() =>
        File.ReadAllText(Path.Combine(LocalisationTests.AppDirectory(), "Views", "SidebarView.xaml.cs"));
}
