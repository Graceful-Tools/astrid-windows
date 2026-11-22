namespace Astrid.App.ViewModels;

/// <summary>
/// How wide a board column is (astrid-core register DS34, task <c>9fedf44e</c>).
/// </summary>
/// <remarks>
/// DS34's compact layout — the one this pointer shell takes — has the columns SHARE the board's
/// width, none narrower than <c>layout.compact.board-column-min-width</c>. When they do not fit at
/// that minimum each keeps it and the board scrolls sideways. It lives outside the WinUI assembly
/// so <c>BoardColumnLayoutTests</c> can reach it; <c>BoardColumnsPanel</c> is its only caller.
/// </remarks>
public static class BoardColumnLayout
{
    /// <summary>
    /// The width of each of <paramref name="count"/> columns in a board
    /// <paramref name="viewport"/> wide, <paramref name="gap"/> apart.
    /// </summary>
    public static double ColumnWidth(double viewport, int count, double minWidth, double gap)
    {
        if (count <= 0 || !(viewport > 0) || double.IsInfinity(viewport))
        {
            return minWidth;
        }

        var shared = (viewport - gap * (count - 1)) / count;
        return Math.Max(minWidth, shared);
    }
}
