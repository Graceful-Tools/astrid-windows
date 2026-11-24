using System.IO;
using System.Threading;
using System.Windows.Automation;
using Xunit;

namespace Astrid.App.UITests;

/// <summary>
/// "Ask for a decision" from a task's details, with traffic lights (task b0f521c6).
/// </summary>
/// <remarks>
/// The task's own test: compose a question, two options, one criterion and a light, then post.
/// The markdown is <c>DecisionDraftTests</c>'s; this is the half only the running app can show —
/// that the "+" menu opens the composer, the lights are there to press, the preview is drawn, and
/// Post is offered once the draft is whole. It saves a picture of the filled-in composer.
/// </remarks>
[Collection("ui")]
public sealed class DecisionComposerTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public DecisionComposerTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [Fact]
    public void A_decision_is_composed_with_a_light_and_posted_task_b0f521c6()
    {
        using var app = AstridApp.Launch(signedIn: true, seed: Seeds.ListWithEveryPriority("light"));
        Select(app, Seeds.PriorityListName);
        Select(app, Seeds.PriorityTitles[0]);

        app.InvokeById("CommentAddButton");
        app.Invoke("Ask for a decision");

        Set(app, "DecisionQuestion", "Where?");
        Set(app, "DecisionOption0", "Lisbon");
        Set(app, "DecisionOption1", "Cabin");
        app.InvokeById("DecisionAddCriterion");
        Set(app, "DecisionCriterion0", "Cost");
        // The light's name is its word from the core's key: ++ is "Decisive".
        app.Invoke("Lisbon, Cost: Decisive");
        Thread.Sleep(600);

        using var shot = Capture.Window(new IntPtr(app.Window.Current.NativeWindowHandle));
        var png = shot.SavePng(Path.Combine(AppContext.BaseDirectory, "decision-composer-b0f521c6.png"));
        _output.WriteLine($"picture: {png}");

        var post = app.Require("Post");
        Assert.True(post.Current.IsEnabled, $"Post is not offered for a whole draft. Picture: {png}");
        ((InvokePattern)post.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Thread.Sleep(1500);

        Assert.Null(app.FindById("DecisionQuestion", 500));
        Assert.False(File.Exists(app.CrashLogPath), "the app wrote a crash log");
    }

    private static void Set(AstridApp app, string automationId, string text)
    {
        var box = app.FindById(automationId)
            ?? throw new InvalidOperationException($"no '{automationId}'. Saw: {string.Join(", ", app.Names())}");
        ((ValuePattern)box.GetCurrentPattern(ValuePattern.Pattern)).SetValue(text);
        Thread.Sleep(400);
    }

    private static void Select(AstridApp app, string name)
    {
        var walker = TreeWalker.ControlViewWalker;
        var element = app.Require(name);
        while (element is not null && element.Current.ControlType != ControlType.ListItem)
        {
            element = walker.GetParent(element);
        }
        var row = element ?? throw new InvalidOperationException($"'{name}' is not in a row");
        ((SelectionItemPattern)row.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Thread.Sleep(800);
    }
}
