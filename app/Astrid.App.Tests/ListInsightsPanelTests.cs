using System.Text.RegularExpressions;
using System.Xml.Linq;
using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The Insights panel is actually drawn, and wired (task 48c624ad).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ListInsightsViewModelTests"/> covers what the view model does with the server's
/// answer. This covers the other half, the half a green view-model suite says nothing about: a
/// panel nothing binds to is seventeen passing tests and a blank flyout. It is read off the XAML
/// the way <see cref="WebUxParityTests"/> reads it — the UI smoke suite that could click it is ten
/// minutes and does not run on every gate.
/// </para>
/// <para>
/// The description's "every list except saved filters" needs no gate here: this app has no saved
/// filters, and the flyout is the gear on a real list's header.
/// </para>
/// </remarks>
public sealed class ListInsightsPanelTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Action<Func<Task>> RunInline = work => work().GetAwaiter().GetResult();

    /// <summary>The keys the panel words at runtime, which an <c>x:Uid</c> cannot reach.</summary>
    /// <remarks>
    /// Each is inside a <c>DataTemplate</c> or carries a number, so it goes through a converter and
    /// <c>Strings.Get</c>. The spellings are web's, in <c>lib/i18n/locales/*.json</c> under
    /// <c>listInsights</c>, so the two clients say the same sentence.
    /// </remarks>
    private static readonly string[] RuntimeKeys =
    [
        "listInsights.days7", "listInsights.days30", "listInsights.days90",
        "listInsights.createdTotal", "listInsights.completedTotal",
        "listInsights.sampleSize", "listInsights.profileCounts", "listInsights.activityCounts",
        "listInsights.duration.minutes", "listInsights.duration.hours", "listInsights.duration.days",
    ];

    private static XElement Panel()
    {
        var flyout = XDocument.Load(Path.Combine(
            LocalisationTests.AppDirectory(), "Views", "ListSettingsFlyout.xaml")).Root!;
        return flyout.Descendants()
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "InsightsPanel");
    }

    /// <summary>
    /// Everything the description asks the panel to render is bound to the view model.
    /// </summary>
    /// <remarks>
    /// The list is the description's, in its order: the picker, the two-series chart with its
    /// totals, the median time to complete, the medians per column for a board, and the
    /// contributors. The three states the panel can be in — loading, failed, nothing to show — are
    /// in it too, because an empty window that says nothing reads as a broken panel.
    /// </remarks>
    [Fact]
    public void The_insights_panel_binds_everything_web_renders_task_48c624ad()
    {
        var panel = Panel().ToString();

        string[] wanted =
        [
            "Shell.Insights.WindowChoices",   // the 7 / 30 / 90 picker
            "Shell.Insights.CreatedSeries",   // New
            "Shell.Insights.CompletedSeries", // Completed
            "Shell.Insights.CreatedTotal",
            "Shell.Insights.CompletedTotal",
            "Shell.Insights.TimeToComplete",
            "Shell.Insights.HasLanes",        // only a board has columns
            "Shell.Insights.Lanes",
            "Shell.Insights.LanesNeedHistory",
            "Shell.Insights.Contributors",
            "Shell.Insights.NoActivity",
            "Shell.Insights.IsLoading",
            "Shell.Insights.Failed",
        ];

        var unbound = wanted.Where(path => !panel.Contains(path, StringComparison.Ordinal)).ToList();
        Assert.True(unbound.Count == 0,
            "the Insights panel draws nothing for:\n  " + string.Join("\n  ", unbound));
    }

    /// <summary>
    /// Every converter the panel names is declared, and the picker leads somewhere.
    /// </summary>
    /// <remarks>
    /// A <c>StaticResource</c> with no declaration throws when the page loads, which no build and
    /// no view-model test catches; and a picker whose buttons have no <c>Click</c> is three
    /// decorations.
    /// </remarks>
    [Fact]
    public void The_panels_converters_are_declared_and_the_picker_is_wired_task_48c624ad()
    {
        var panel = Panel();
        var shared = File.ReadAllText(Path.Combine(
            LocalisationTests.AppDirectory(), "Views", "ShellResources.xaml"));

        var undeclared = Regex.Matches(panel.ToString(), @"\{StaticResource (\w+)\}")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Where(key => !shared.Contains($"x:Key=\"{key}\"", StringComparison.Ordinal))
            .ToList();
        Assert.True(undeclared.Count == 0,
            "the Insights panel names resources nothing declares, which throws on load:\n  "
            + string.Join("\n  ", undeclared));

        var picker = panel.Descendants()
            .Single(element => (string?)element.Attribute("ItemsSource") is { } source
                               && source.Contains("WindowChoices", StringComparison.Ordinal));
        Assert.Contains(
            picker.Descendants().Select(element => (string?)element.Attribute("Click")),
            click => click is not null);
        Assert.Contains("SetDaysAsync", File.ReadAllText(Path.Combine(
            LocalisationTests.AppDirectory(), "Views", "ListSettingsFlyout.xaml.cs")),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every key the panel words at runtime has a word, in both languages and in the fallback.
    /// </summary>
    /// <remarks>
    /// Three places because they fail differently, as <c>CorePinFollowsIosTests</c> says: no
    /// resource is a key on screen in a packaged build, no fallback is a key on screen in an
    /// unpackaged one, and a language folder missing one is English on a German page.
    /// </remarks>
    [Fact]
    public void Every_insights_word_is_in_the_resources_and_the_fallback_task_48c624ad()
    {
        var app = LocalisationTests.AppDirectory();
        var fallback = File.ReadAllText(Path.Combine(app, "Strings.cs"));

        var missing = RuntimeKeys
            .SelectMany(key => Directory
                .EnumerateDirectories(Path.Combine(app, "Strings"))
                .Select(folder => Words(folder).ContainsKey(key.Replace('.', '_'))
                    ? null
                    : $"{key} — not in {Path.GetFileName(folder)}")
                .Append(fallback.Contains($"[\"{key}\"]", StringComparison.Ordinal)
                    ? null
                    : $"{key} — not in the fallback table")
                .OfType<string>())
            .ToList();

        Assert.True(missing.Count == 0,
            "the Insights panel has no word for:\n  " + string.Join("\n  ", missing));
    }

    private static Dictionary<string, string> Words(string folder) =>
        XDocument.Load(Path.Combine(folder, "Resources.resw")).Root!.Elements("data")
            .ToDictionary(
                data => data.Attribute("name")!.Value,
                data => data.Element("value")!.Value,
                StringComparer.Ordinal);

    /// <summary>
    /// Opening the Insights section asks for the open list's insights.
    /// </summary>
    /// <remarks>
    /// Through the shell rather than from the flyout's code-behind, as the links and the agent
    /// choices are: which list is open is the shell's fact, and a control that reached for it would
    /// be the second place that knows. On the section rather than on the flyout because it is a
    /// round trip over history this device does not hold, and the gear is usually opened for
    /// something else.
    /// </remarks>
    [Fact]
    public async Task Opening_the_insights_section_asks_for_them_task_48c624ad()
    {
        var core = new FakeCore()
            .AnswerOk("isSignedIn", new { signedIn = true, waitingForCallback = false })
            .AnswerOk("lists", new[] { new { id = "l1", name = "Home", isFavorite = false } })
            .AnswerOk("rowsForList", new { total = 0, offset = 0, rows = Array.Empty<object>() })
            .AnswerOk("outboxStats", new { pending = 0, running = 0, failed = 0, hasUnsentWork = false })
            .AnswerOk("sync", new { fetched = false })
            .AnswerOk("listInsights", new
            {
                window = new { days = 30, timeZone = "UTC", start = "2026-09-23", end = "2026-10-22" },
                series = new[] { new { date = "2026-10-22", created = 2, completed = 3 } },
                totals = new { created = 60, completed = 45 },
                createToComplete = new { medianMs = (long?)111_600_000, sampleSize = 42 },
                lanes = (object?)null,
                contributors = Array.Empty<object>(),
            });
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        await shell.OpenListAsync("l1", "Home");

        await shell.LoadListInsightsAsync();

        Assert.Contains("listInsights", core.SentKinds());
        Assert.True(shell.Insights.HasInsights);
        Assert.Equal(60, shell.Insights.CreatedTotal);
        // And the section is what asks, so the panel fills when it opens rather than never.
        var flyout = File.ReadAllText(Path.Combine(
            LocalisationTests.AppDirectory(), "Views", "ListSettingsFlyout.xaml.cs"));
        Assert.Contains("LoadListInsightsAsync", flyout, StringComparison.Ordinal);
        Assert.Contains("Expanding=\"OnInsightsExpanding\"", Panel().Parent!.ToString(),
            StringComparison.Ordinal);
    }
}
