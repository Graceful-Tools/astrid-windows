using System.Numerics;
using Astrid.Core.Bindings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

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
        nameof(ShowTick), typeof(bool), typeof(PriorityMark), new PropertyMetadata(false, OnTickChanged));

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs args) =>
        ((PriorityMark)element).Apply();

    /// <summary>
    /// The tick arriving is a task being completed, and that is the one change that is animated
    /// (astrid-core DS16, task 787c7aa5).
    /// </summary>
    /// <remarks>
    /// Only false → true, and only on a mark that is on screen. A row scrolling into view, a list
    /// reloading and the already-done rows of a completed filter all set <see cref="ShowTick"/> to
    /// true as well, and a pop on each of those is a list that twitches rather than a task that was
    /// just finished.
    /// </remarks>
    private static void OnTickChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        var mark = (PriorityMark)element;
        mark.Apply();

        if (args.NewValue is true && args.OldValue is false && mark.IsLoaded)
        {
            mark.Pop();
        }
    }

    /// <summary>
    /// Scales the whole mark to <c>state.complete-pop-scale</c> and lets the token's spring settle
    /// it back (astrid-core DS16).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A spring, not an easing curve. <c>motion.spring</c> is a named entry in the shared tokens
    /// carrying a damping ratio and a period rather than a duration and a cubic-bezier, and DS16
    /// says to read it rather than approximate it. Composition's
    /// <c>SpringVector3NaturalMotionAnimation</c> is that shape exactly — those two numbers and no
    /// others — so the shell runs the token rather than something that looks like it, and there is
    /// nothing for astrid-core's register to record as a substitution.
    /// </para>
    /// <para>
    /// The overshoot comes from where it starts, not from the animation: the scale is set to the
    /// pop and the spring's <c>FinalValue</c> is 1, so it falls back through and settles. Centred on
    /// the mark, or it would grow away from its own corner and shove the title.
    /// </para>
    /// </remarks>
    private void Pop()
    {
        var pop = (float)Number("AstridCompletePopScale", 1);
        var visual = ElementCompositionPreview.GetElementVisual(this);

        visual.CenterPoint = new Vector3((float)(ActualWidth / 2), (float)(ActualHeight / 2), 0);
        visual.Scale = new Vector3(pop, pop, 1);

        var spring = visual.Compositor.CreateSpringVector3Animation();
        spring.DampingRatio = (float)Number("AstridSpringDamping", 1);
        spring.Period = TimeSpan.FromMilliseconds(Number("AstridSpringMs", 0));
        spring.FinalValue = Vector3.One;
        visual.StartAnimation("Scale", spring);
    }

    /// <summary>
    /// One of the design numbers out of the application's resources, with the value that leaves the
    /// mark alone if it is missing.
    /// </summary>
    /// <remarks>
    /// The same route the art itself takes (<see cref="Apply"/>), so the four values of DS5 and DS16
    /// all sit in the two XAML files the design-metrics budget exempts and one test compares them
    /// with the vendored tokens. A fallback rather than a throw: a missing resource should cost the
    /// animation, never the row.
    /// </remarks>
    private static double Number(string key, double fallback) =>
        Application.Current.Resources.TryGetValue(key, out var found) && found is double value
            ? value
            : fallback;

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
