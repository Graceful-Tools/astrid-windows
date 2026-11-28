using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using Xunit;

namespace Astrid.App.UITests;

/// <summary>
/// An empty board column draws DS36's empty state on screen (task <c>e0692268</c>): the art beside
/// a speech bubble holding the column's message.
/// </summary>
/// <remarks>
/// <c>BoardEmptyColumnTests</c> holds the bubble's numbers to the vendored tokens; this is the
/// check that the template actually renders — that the message reaches UI Automation with a size
/// on screen — and it saves the capture the task asks for, which is the by-eye record of the art,
/// the bubble and its tail. The seed's board has no cards in Ready, Doing or Waiting.
/// </remarks>
[Collection("ui")]
public sealed class BoardEmptyColumnUITests
{
    /// <summary>
    /// What an empty column says today: the generic <c>board.empty.custom</c>, because the core's
    /// board answer at the current pin does not send each column's key (task 4ac294ab bound the
    /// key; the core half is filed). When it does, this becomes Ready's own "Nothing queued up!…"
    /// and the test should assert Doing's and Waiting's own messages too.
    /// </summary>
    private const string Message = "Nothing here yet! Drag a task in to move it along.";

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public BoardEmptyColumnUITests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [Fact]
    public void An_empty_column_is_the_art_beside_a_speech_bubble_task_e0692268_4ac294ab()
    {
        using var app = AstridApp.Launch(signedIn: true, seed: Seeds.BoardWithADoneCard);
        Thread.Sleep(1200);
        BoardCardMarkTests.OpenTheBoard(app);
        Thread.Sleep(700);

        using var shot = Capture.Window(new IntPtr(app.Window.Current.NativeWindowHandle));
        var saved = shot.SavePng(Path.Combine(Path.GetTempPath(), "astrid-uitests", "board-empty-column-4ac294ab.png"));
        _output.WriteLine($"capture: {saved}");

        var messages = app.Window.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, Message))
            .Cast<AutomationElement>()
            .ToList();
        Assert.True(messages.Count > 0, "no empty column drew the DS36 message");

        // Doing and Waiting are past the window's edge at the harness's size and report an empty
        // rectangle; Ready is on screen.
        var onScreen = messages.Select(message => message.Current.BoundingRectangle)
            .Where(bounds => !bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
            .ToList();
        _output.WriteLine($"messages {messages.Count}, on screen {onScreen.Count}: {string.Join(" ", onScreen)}");
        Assert.True(onScreen.Count > 0, "no empty column's message has a size on screen");

    }
}
