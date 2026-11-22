namespace Astrid.App.ViewModels;

/// <summary>
/// The type steps a rendered description, comment or chat message draws in (task
/// <c>6fbecd8e</c>, register DS24).
/// </summary>
/// <remarks>
/// <para>
/// DS24: a Markdown heading is <c>type.&lt;density&gt;.markdown-h1</c> to <c>-h4</c>, all
/// semibold, and never smaller than the density's body text. Windows is a pointer client, so it
/// takes the compact steps — the Mac's sizes — over <c>type.compact.detail-body</c>.
/// </para>
/// <para>
/// The numbers are written here only because the generated <c>design/AstridTokens.xaml</c> has no
/// type scale yet: astrid-core's Windows emitter writes none. <c>MarkdownTypeScaleTests</c> holds
/// every one to the vendored <c>tokens.resolved.json</c>, so a re-vendor that moves a step fails
/// until this follows. It lives outside the WinUI assembly so that test can reach it.
/// </para>
/// </remarks>
public static class MarkdownTypeScale
{
    /// <summary><c>type.compact.detail-body</c>: a paragraph.</summary>
    public static (double Size, int Weight) Body { get; } = (13, 400);

    /// <summary>
    /// <c>type.compact.markdown-h&lt;level&gt;</c>. Markdown has six levels and the register four;
    /// h5 and h6 stay at h4, since a heading stops shrinking at h3.
    /// </summary>
    public static (double Size, int Weight) Heading(int level) => level switch
    {
        <= 1 => (17, 600),
        2 => (15, 600),
        _ => (13, 600),
    };
}
