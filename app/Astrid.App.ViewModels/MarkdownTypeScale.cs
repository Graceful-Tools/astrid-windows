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
/// The generated <c>design/AstridTokens.xaml</c> carries these steps since ADEV-217 (task
/// <c>737b31a8</c>), so the renderer draws the dictionary's values through <see cref="BodyKeys"/> and
/// <see cref="HeadingKeys"/>. The numbers stay as the fallback for a renderer used before the
/// application's resources exist, and because this assembly cannot reach WinUI resources at all;
/// <c>MarkdownTypeScaleTests</c> holds both the numbers and the keys to the vendored dictionary.
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

    /// <summary>The generated keys holding <see cref="Body"/>.</summary>
    public static (string Size, string Weight) BodyKeys { get; } =
        ("AstridTypeCompactDetailBodySize", "AstridTypeCompactDetailBodyWeight");

    /// <summary>The generated keys holding <see cref="Heading"/> at this level.</summary>
    public static (string Size, string Weight) HeadingKeys(int level)
    {
        var step = Math.Clamp(level, 1, 4);
        return ($"AstridTypeCompactMarkdownH{step}Size", $"AstridTypeCompactMarkdownH{step}Weight");
    }
}
