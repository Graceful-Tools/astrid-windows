using System.Text;
using System.Text.Json;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The design budget: the hand-written colours, sizes and strings in the shell are counted, the
/// count is committed, and the gate fails when one rises (task <c>d7e381a7</c>).
/// </summary>
/// <remarks>
/// <para>
/// Jon asked for the reduction of unique strings and inline styles to be audited with a before and
/// an after, and for the <c>fixall</c> and <c>fixstuff</c> loops not to regress it. A report alone
/// would not do that — nobody reads a report on the way past, and an autonomous run reads nothing
/// it is not made to. A number in the gate is what a loop cannot walk past, so the budget lives in
/// <c>design-metrics.json</c> beside this file and <c>npm run predeploy</c> checks it.
/// </para>
/// <para>
/// It ratchets both ways. A rise fails, which is the point. A fall ALSO fails, asking for the
/// budget to be lowered — because a budget left above where the code actually is quietly hands back
/// the room that was just won, and the next drift spends it without ever failing.
/// </para>
/// <para>
/// The two XAML files the count exempts are the ones the design system is written IN: the literals
/// in <c>App.xaml</c> and <c>ShellResources.xaml</c> are the tokens, not drift from them. Those are
/// held to their own rules by <see cref="DesignTokenTests"/> and
/// <see cref="DesignRadiusScaleTests"/>, and XAML strings by <see cref="LocalisationTests"/>, which
/// already holds untranslatable literals at zero — so <c>code.strings</c> counts the half of
/// docs/ASTRID.md rule 10 that no <c>x:Uid</c> can reach: the strings built in C#.
/// </para>
/// </remarks>
public sealed class DesignMetricsTests
{
    /// <summary>
    /// Set <c>ASTRID_DESIGN_METRICS_OUT</c> to a path and the counts are written there as a budget
    /// instead of being checked, which is how <c>scripts/report-design-metrics.ps1</c> both reports
    /// and updates: it writes a temporary budget, prints it beside the committed one, and copies it
    /// over only when asked. One counter, one output format, no second implementation to drift.
    /// </summary>
    private const string OutVariable = "ASTRID_DESIGN_METRICS_OUT";

    private static string? Destination() => Environment.GetEnvironmentVariable(OutVariable);

    private static string BudgetPath() =>
        Path.Combine(
            Directory.GetParent(LocalisationTests.AppDirectory())!.FullName,
            "Astrid.App.Tests",
            "design-metrics.json");

    private static IReadOnlyDictionary<string, int> Budget() => Budget(BudgetPath());

    private static IReadOnlyDictionary<string, int> Budget(string path)
    {
        Assert.True(File.Exists(path), $"the committed budget is missing: {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("counts")
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetInt32(), StringComparer.Ordinal);
    }

    private static void Write(string path, IReadOnlyDictionary<string, int> counts)
    {
        var json = new StringBuilder()
            .AppendLine("{")
            .AppendLine("  \"_\": \"The committed design budget (task d7e381a7). A count must not rise; "
                + "when one falls, lower it with scripts/report-design-metrics.ps1 -Update.\",")
            .AppendLine("  \"counts\": {");
        for (var index = 0; index < DesignMetrics.Names.Length; index++)
        {
            var name = DesignMetrics.Names[index];
            var comma = index == DesignMetrics.Names.Length - 1 ? string.Empty : ",";
            json.AppendLine($"    \"{name}\": {counts[name]}{comma}");
        }
        json.AppendLine("  }").AppendLine("}");
        File.WriteAllText(path, json.ToString());
    }

    /// <summary>
    /// Every count is exactly its budget: a rise is drift, a fall is a budget to lower.
    /// </summary>
    [Fact]
    public void The_shell_stays_inside_its_design_budget_task_d7e381a7()
    {
        var sites = DesignMetrics.Sites(LocalisationTests.AppDirectory());
        var counts = sites.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal);

        if (Destination() is { Length: > 0 } destination)
        {
            Write(destination, counts);
            return;
        }

        var budget = Budget();
        var risen = new List<string>();
        var fallen = new List<string>();

        foreach (var name in DesignMetrics.Names)
        {
            Assert.True(budget.ContainsKey(name), $"the budget does not mention the metric '{name}'");
            if (counts[name] > budget[name])
            {
                var examples = sites[name].Take(6);
                risen.Add($"{name}: {budget[name]} budgeted, {counts[name]} found\n      "
                    + string.Join("\n      ", examples));
            }
            else if (counts[name] < budget[name])
            {
                fallen.Add($"{name}: {budget[name]} budgeted, {counts[name]} found");
            }
        }

        Assert.True(risen.Count == 0,
            "hand-written design values rose — use the tokens in App.xaml / ShellResources.xaml, an "
            + "x:Uid and Resources.resw for a string, and the radius scale for a corner:\n    "
            + string.Join("\n    ", risen));
        Assert.True(fallen.Count == 0,
            "the budget is above where the shell now is — lower it so the room just won is kept:\n    "
            + string.Join("\n    ", fallen)
            + "\n    powershell -File scripts/report-design-metrics.ps1 -Update");
    }

    /// <summary>
    /// What <c>-Update</c> writes is what the gate reads back.
    /// </summary>
    /// <remarks>
    /// The writer and the reader are a pair and nothing else exercises the writer: the report
    /// script drives it, and a script cannot run inside the gate. It had a real bug on the way in —
    /// the header comment was emitted across two lines, and a newline inside a JSON string is not
    /// JSON, so every budget it wrote would have failed to parse the moment anyone used
    /// <c>-Update</c>. A round trip is the cheap test that catches that whole class.
    /// </remarks>
    [Fact]
    public void An_updated_budget_parses_back_to_the_counts_it_recorded_task_d7e381a7()
    {
        var counts = DesignMetrics.Names
            .Select((name, index) => (name, index))
            .ToDictionary(pair => pair.name, pair => pair.index * 7, StringComparer.Ordinal);
        var path = Path.Combine(Path.GetTempPath(), $"astrid-design-metrics-{Guid.NewGuid():N}.json");

        try
        {
            Write(path, counts);
            Assert.Equal(counts, Budget(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The budget names these metrics and no others, so a metric cannot be retired by deleting its
    /// line.
    /// </summary>
    [Fact]
    public void The_budget_names_exactly_the_metrics_task_d7e381a7()
    {
        if (Destination() is { Length: > 0 })
        {
            return;
        }

        Assert.Equal(
            DesignMetrics.Names.OrderBy(name => name, StringComparer.Ordinal),
            Budget().Keys.OrderBy(name => name, StringComparer.Ordinal));
    }
}
