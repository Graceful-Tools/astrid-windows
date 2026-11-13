using Astrid.Core.Bindings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Astrid.App;

/// <summary>
/// The mark at the head of a row: the shared checkbox art, in the priority's colour (task 82e4ad32).
/// </summary>
/// <remarks>
/// <para>
/// This used to be sixteen PNGs — <c>check_box{_repeat}{_checked}_{0..3}.png</c> — one picture
/// saying three things at once, which is why the view-models answered with a file name rather than
/// with three properties. The pictures carried their colours with them, so a priority token could
/// not reach the mark on a row: changing <c>priority-low</c> in astrid-core would have changed the
/// picker and left every row at the old blue, which is task 204c9d98's bug coming back the other
/// way round.
/// </para>
/// <para>
/// astrid-core's DS21 made the shared art <c>currentColor</c> instead, and it is vendored here at
/// <c>design/AstridCheckbox.xaml</c>. The frame strokes from <c>{TemplateBinding Foreground}</c>,
/// so this control composes it: the box or the repeat arrows by <see cref="IsRepeating"/>, the tick
/// over the top by <see cref="ShowTick"/>, and the <see cref="Priority"/>'s brush as the colour.
/// The art itself is three styles and no logic, exactly as generated; nothing is drawn by hand here.
/// </para>
/// <para>
/// The colour is resolved through <c>ThemeBrushes</c> rather than read from
/// <c>Application.Current.Resources</c>, which answers from the light dictionary however dark the
/// window is, and re-resolved on <c>ActualThemeChanged</c> because the appearance is applied by
/// setting <c>RequestedTheme</c> on ShellPage's root long after these are built.
/// </para>
/// </remarks>
public sealed partial class PriorityMark : Grid
{
    private readonly ContentControl frame = new();
    private readonly ContentControl tick = new();

    public PriorityMark()
    {
        Children.Add(frame);
        Children.Add(tick);

        // Built before the shell has applied the appearance, and rebuilt when a person changes it.
        ActualThemeChanged += (_, _) => Apply();
        Loaded += (_, _) => Apply();
        Apply();
    }

    /// <summary>The task's priority, 0–3; anything else draws as unmarked.</summary>
    public int Priority
    {
        get => (int)GetValue(PriorityProperty);
        set => SetValue(PriorityProperty, value);
    }

    public static readonly DependencyProperty PriorityProperty = DependencyProperty.Register(
        nameof(Priority), typeof(int), typeof(PriorityMark), new PropertyMetadata(0, OnChanged));

    /// <summary>Whether the frame is the repeat arrows rather than the plain box.</summary>
    public bool IsRepeating
    {
        get => (bool)GetValue(IsRepeatingProperty);
        set => SetValue(IsRepeatingProperty, value);
    }

    public static readonly DependencyProperty IsRepeatingProperty = DependencyProperty.Register(
        nameof(IsRepeating), typeof(bool), typeof(PriorityMark), new PropertyMetadata(false, OnChanged));

    /// <summary>Whether the tick is drawn over the frame.</summary>
    /// <remarks>
    /// Separate from the frame because the repeat arrows are also drawn around somebody else's mark
    /// and around nobody's, where "done" is said by the row's opacity and its struck-through title
    /// rather than by a tick (task fa63a5dd).
    /// </remarks>
    public bool ShowTick
    {
        get => (bool)GetValue(ShowTickProperty);
        set => SetValue(ShowTickProperty, value);
    }

    public static readonly DependencyProperty ShowTickProperty = DependencyProperty.Register(
        nameof(ShowTick), typeof(bool), typeof(PriorityMark), new PropertyMetadata(false, OnChanged));

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs args) =>
        ((PriorityMark)element).Apply();

    private void Apply()
    {
        frame.Style = Style(IsRepeating ? "AstridCheckboxRepeatStyle" : "AstridCheckboxBoxStyle");
        frame.Foreground = ThemeBrushes.For(this, PriorityPalette.Key(Priority));

        tick.Style = Style("AstridCheckboxCheckStyle");
        tick.Visibility = ShowTick ? Visibility.Visible : Visibility.Collapsed;

        static Style? Style(string key) =>
            Application.Current.Resources.TryGetValue(key, out var found) ? found as Style : null;
    }
}
