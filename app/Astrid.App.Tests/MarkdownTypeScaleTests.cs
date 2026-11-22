using Astrid.App.ViewModels;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// Markdown headings and body text take the register's compact type steps (task
/// <c>6fbecd8e</c>, register DS24).
/// </summary>
/// <remarks>
/// The steps are read from the vendored <c>tokens.resolved.json</c>, not typed here, so a
/// re-vendor that moves a step fails this until <see cref="MarkdownTypeScale"/> follows. The
/// generated <c>AstridTokens.xaml</c> carries no type scale yet (astrid-core's Windows emitter
/// writes none), which is why the numbers live in code at all.
/// </remarks>
public sealed class MarkdownTypeScaleTests
{
    private static (double Size, int Weight) Step(string name)
    {
        var step = DesignTokenTests.Tokens().GetProperty("type").GetProperty("compact").GetProperty(name);
        return (step.GetProperty("size").GetDouble(), step.GetProperty("weight").GetInt32());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Task6fbecd8e_a_heading_takes_the_compact_markdown_step(int level)
    {
        Assert.Equal(Step($"markdown-h{level}"), MarkdownTypeScale.Heading(level));
    }

    [Fact]
    public void Task6fbecd8e_a_heading_past_h4_stays_at_h4()
    {
        Assert.Equal(Step("markdown-h4"), MarkdownTypeScale.Heading(5));
        Assert.Equal(Step("markdown-h4"), MarkdownTypeScale.Heading(6));
    }

    [Fact]
    public void Task6fbecd8e_body_text_takes_the_compact_detail_body_step()
    {
        Assert.Equal(Step("detail-body"), MarkdownTypeScale.Body);
    }

    [Fact]
    public void Task6fbecd8e_no_heading_is_smaller_than_the_body_it_introduces()
    {
        for (var level = 1; level <= 6; level++)
        {
            Assert.True(MarkdownTypeScale.Heading(level).Size >= MarkdownTypeScale.Body.Size,
                $"h{level} is smaller than the body");
        }
    }

    [Fact]
    public void Task6fbecd8e_the_renderer_draws_headings_and_paragraphs_from_the_scale()
    {
        var source = File.ReadAllText(Path.Combine(DesignTokenTests.RepoRoot(), "app", "Astrid.App",
            "MarkdownRenderer.cs"));
        Assert.Contains("MarkdownTypeScale.Heading(block.Level)", source);
        Assert.Contains("MarkdownTypeScale.Body", source);
        Assert.DoesNotContain("HeadingSize", source);
    }
}
