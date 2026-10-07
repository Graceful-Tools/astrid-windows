using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

public sealed class TaskDetailViewModelTests
{
    private static object Detail(string title = "Plan the trip", int priority = 3,
        bool completed = false, string[]? subtasks = null, string[]? comments = null,
        bool isCanceled = false, object? boardState = null, object? blockers = null,
        string? identifier = null, bool showsIdentifier = false,
        bool offersCopyIdentifier = false) => new
        {
            task = new { id = "t1", title, description = "two weeks", priority, completed, identifier },
            isCanceled,
            boardState,
            blockers,
            showsIdentifier,
            offersCopyIdentifier,
            link = "https://astrid.cc/tasks/t1",
            fieldOrder = new[] { "assignee", "when", "priority", "lists" },
            priorityGlyph = "!!!",
            due = new { key = "today" },
            isOverdue = false,
            listChips = new[] { new { id = "l1", name = "Home", color = "#3b82f6" } },
            assignee = (object?)null,
            comments = (comments ?? []).Select((content, index) => new
            {
                id = $"c{index}",
                content,
                createdAt = "2026-09-07T12:00:00Z",
            }).ToArray(),
            subtasks = (subtasks ?? []).Select((sub, index) => new
            {
                id = $"s{index}",
                title = sub,
                completed = false,
                isPending = false,
            }).ToArray(),
        };

    /// <summary>
    /// Typing a sigil fills the popup from the core, the arrows move the lit row round, and a
    /// choice comes back as the text the server will read (task 3271a0c5).
    /// </summary>
    [Fact]
    public async Task The_comment_box_offers_suggestions_and_applies_the_chosen_one_task_3271a0c5()
    {
        var core = OpenedTask()
            .AnswerOk("commentSuggestions", new
            {
                trigger = new { kind = "mention", start = 4, query = "as" },
                items = new[]
                {
                    new { kind = "mention", id = "ai-agent-astrid", label = "Astrid", secondary = "astrid@astrid.cc", isAgent = true, completed = false },
                    new { kind = "mention", id = "dana", label = "Dana", secondary = "dana@x.io", isAgent = false, completed = false },
                },
            })
            .AnswerOk("applyCommentSuggestion", new { text = "hey @[Dana](dana) ", caret = 18 })
            .AnswerOk("commentSuggestions", new { trigger = (object?)null, items = Array.Empty<object>() });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.True(await view.SuggestCommentAsync("hey @as", 7));
        Assert.Equal(2, view.CommentSuggestions.Count);
        Assert.True(view.CommentSuggestions[0].IsAgent);
        Assert.Equal("@", view.CommentSuggestions[0].Sigil);
        Assert.Equal(0, view.SuggestionIndex);

        view.MoveSuggestion(1);
        Assert.Equal(1, view.SuggestionIndex);
        view.MoveSuggestion(1);
        Assert.Equal(0, view.SuggestionIndex); // wraps
        view.MoveSuggestion(-1);
        Assert.Equal(1, view.SuggestionIndex); // wraps the other way

        var applied = await view.ApplyCommentSuggestionAsync(null, "hey @as", 7);
        Assert.NotNull(applied);
        Assert.Equal("hey @[Dana](dana) ", applied!.Text);
        Assert.Equal(18, applied.Caret);
        var sent = core.Sent.First(command => command.Contains("applyCommentSuggestion"));
        Assert.Contains("\"triggerKind\":\"mention\"", sent);
        Assert.Contains("\"id\":\"dana\"", sent);
        Assert.False(view.HasCommentSuggestions, "the popup closes on a choice");

        Assert.False(await view.SuggestCommentAsync("hey @[Dana](dana) ", 18), "nothing at the caret");
        Assert.Empty(view.CommentSuggestions);
    }

    /// <summary>
    /// A reply is posted under the comment its box was opened on, an edit changes the text, and
    /// a delete removes it — each through the core and each followed by a re-read of the thread
    /// (task 97c817dd).
    /// </summary>
    [Fact]
    public async Task Comments_can_be_replied_to_edited_and_deleted_task_97c817dd()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail(comments: ["Thoughts?", "Another"]))
            .AnswerOk("dueDateOptions", NoDuePicks())
            .AnswerOk("refreshComments", CommentRows("Thoughts?", "Another"))
            .AnswerOk("postComment", new { id = "c9" })
            .AnswerOk("taskDetail", Detail(comments: ["Thoughts?", "Yes", "Another"]))
            .AnswerOk("dueDateOptions", NoDuePicks())
            .AnswerOk("editComment")
            .AnswerOk("taskDetail", Detail(comments: ["Thoughts!", "Yes", "Another"]))
            .AnswerOk("dueDateOptions", NoDuePicks())
            .AnswerOk("deleteComment")
            .AnswerOk("taskDetail", Detail(comments: ["Thoughts!", "Another"]))
            .AnswerOk("dueDateOptions", NoDuePicks());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        view.BeginReply("c0");
        Assert.True(view.Comments[0].IsReplying, "the row says a reply box is open under it");
        Assert.False(view.Comments[1].IsReplying);
        Assert.True(await view.SendReplyAsync("Yes"));
        var reply = core.Sent.First(sent => sent.Contains("postComment"));
        Assert.Contains("\"parentCommentId\":\"c0\"", reply);
        Assert.Contains("\"content\":\"Yes\"", reply);
        Assert.Null(view.ReplyingToId);
        Assert.Equal(3, view.Comments.Count);
        Assert.False(view.Comments[0].IsReplying);

        view.BeginEdit("c0");
        Assert.True(view.Comments[0].IsEditing);
        Assert.False(view.Comments[0].ShowsBubble, "the editor takes the bubble's place");
        Assert.True(await view.SaveEditAsync("Thoughts!"));
        Assert.Contains("\"kind\":\"editComment\"", core.Sent.First(sent => sent.Contains("editComment")));
        Assert.Equal("Thoughts!", view.Comments[0].Content);
        Assert.Null(view.EditingCommentId);

        Assert.True(await view.DeleteCommentAsync("c1"));
        Assert.Contains("\"commentId\":\"c1\"", core.Sent.First(sent => sent.Contains("deleteComment")));
        Assert.Equal(2, view.Comments.Count);
    }

    /// <summary>Saving unchanged text is a cancel, not a write; opening one box closes the other.</summary>
    [Fact]
    public async Task An_unchanged_edit_writes_nothing_and_one_box_is_open_at_a_time()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail(comments: ["Thoughts?"]))
            .AnswerOk("dueDateOptions", NoDuePicks())
            .AnswerOk("refreshComments", CommentRows("Thoughts?"));
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        view.BeginEdit("c0");
        Assert.False(await view.SaveEditAsync("Thoughts?"));
        Assert.Null(view.EditingCommentId);
        Assert.DoesNotContain(core.Sent, sent => sent.Contains("editComment"));

        view.BeginEdit("c0");
        view.BeginReply("c0");
        Assert.Null(view.EditingCommentId);
        Assert.Equal("c0", view.ReplyingToId);
        Assert.False(await view.SendReplyAsync("   "), "nothing to say is nothing to post");
    }

    /// <summary>Who may do what: replies on top-level comments, edit and delete on your own.</summary>
    [Fact]
    public void Reply_is_offered_on_top_level_comments_and_edit_only_on_your_own()
    {
        var mine = new CommentSummary { Id = "a", IsMine = true };
        var theirs = new CommentSummary { Id = "b", IsMine = false };
        var myReply = new CommentSummary { Id = "c", IsMine = true, IsReply = true };
        var note = new CommentSummary { Id = "d", IsSystem = true };

        Assert.True(mine.CanReply);
        Assert.True(mine.CanEdit);
        Assert.True(theirs.CanReply);
        Assert.False(theirs.CanEdit);
        Assert.False(myReply.CanReply);
        Assert.True(myReply.CanEdit);
        Assert.False(note.CanReply);
        Assert.False(note.CanEdit);
    }

    /// <summary>
    /// Full screen (task 1927c2e7, PRODUCT_CONTRACT §3) is an escape hatch, not a layout: off
    /// until asked for, and off again for the next task rather than remembered.
    /// </summary>
    [Fact]
    public void Full_screen_is_off_by_default_and_ends_with_the_task_task_1927c2e7()
    {
        var view = new TaskDetailViewModel(new FakeCore());

        Assert.False(view.IsFullScreen);

        view.ToggleFullScreen();
        Assert.True(view.IsFullScreen);

        view.Close();
        Assert.False(view.IsFullScreen, "the next task opens as a pane, not as the last one was left");
    }

    /// <summary>
    /// The description comes rendered, and is shown rendered until somebody clicks it
    /// (task 11cfaf6d). What the blocks MEAN was decided in the core; the view model only says
    /// which of the two — the drawing or the box — is on screen.
    /// </summary>
    [Fact]
    public async Task A_description_arrives_rendered_and_opens_for_editing_on_request_task_11cfaf6d()
    {
        var rendered = new
        {
            task = new { id = "t1", title = "Pushups", description = "##title\n**bold**", priority = 0, completed = false },
            descriptionBlocks = new object[]
            {
                new
                {
                    kind = "paragraph",
                    inlines = new object[]
                    {
                        new { kind = "text", text = "##title", bold = false, italic = false, strike = false, code = false, link = (string?)null },
                        new { kind = "lineBreak" },
                        new { kind = "text", text = "bold", bold = true, italic = false, strike = false, code = false, link = (string?)null },
                    },
                },
            },
            fieldOrder = new[] { "assignee", "when", "priority", "lists" },
            priorityGlyph = "○",
            due = new { key = "none" },
            listChips = Array.Empty<object>(),
            comments = Array.Empty<object>(),
            subtasks = Array.Empty<object>(),
        };
        var core = new FakeCore()
            .AnswerOk("taskDetail", rendered)
            .AnswerOk("dueDateOptions", NoDuePicks())
            .AnswerOk("refreshComments")
            .AnswerOk("updateTask")
            .AnswerOk("taskDetail", rendered)
            .AnswerOk("dueDateOptions", NoDuePicks())
            .AnswerOk("beginEditing", Transition("description"));
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        var block = Assert.Single(view.DescriptionBlocks);
        Assert.Equal("paragraph", block.Kind);
        Assert.Equal(3, block.Inlines.Count);
        Assert.Equal("lineBreak", block.Inlines[1].Kind);
        Assert.True(block.Inlines[2].Bold);
        Assert.True(view.ShowsRenderedDescription);
        Assert.False(view.ShowsDescriptionEditor);
        // The text is still there to edit; the drawing does not replace it.
        Assert.Equal("##title\n**bold**", view.Description);

        await view.BeginEditingAsync(TaskDetailViewModel.DescriptionEditor);
        Assert.True(view.ShowsDescriptionEditor);
        Assert.False(view.ShowsRenderedDescription);

        await view.SaveDescriptionAsync("##title\n**bold**");
        Assert.True(view.ShowsRenderedDescription, "saving goes back to the drawing");
    }

    /// <summary>
    /// The Lists row edits (task d3f3b111): what the task is in, what it could join, and a change
    /// that goes through the core and comes back reflected in both.
    /// </summary>
    [Fact]
    public async Task The_lists_a_task_is_in_can_be_changed_from_the_detail_task_d3f3b111()
    {
        var home = new { id = "l1", name = "Home", color = "#3b82f6" };
        var work = new { id = "l2", name = "Work", color = "#ef4444" };
        var core = OpenedTask()
            .AnswerOk("listPicks", new { selected = new[] { home }, options = new[] { work }, createName = (string?)null })
            .AnswerOk("addTaskToList")
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", NoDuePicks())
            .AnswerOk("listPicks", new { selected = new[] { home, work }, options = Array.Empty<object>(), createName = (string?)null })
            .AnswerOk("removeTaskFromList")
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", NoDuePicks())
            .AnswerOk("listPicks", new { selected = new[] { work }, options = new[] { home }, createName = (string?)null });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.LoadListPicksAsync(string.Empty);
        Assert.Equal("Home", Assert.Single(view.SelectedLists).Name);
        Assert.Equal("Work", Assert.Single(view.ListOptions).Name);
        Assert.False(view.CanCreateList);

        Assert.True(await view.AddToListAsync("l2"));
        Assert.Contains("\"kind\":\"addTaskToList\"", core.Sent.First(sent => sent.Contains("addTaskToList")));
        Assert.Contains("\"listId\":\"l2\"", core.Sent.First(sent => sent.Contains("addTaskToList")));
        Assert.Equal(2, view.SelectedLists.Count);
        Assert.Empty(view.ListOptions);

        Assert.True(await view.RemoveFromListAsync("l1"));
        Assert.Equal("Work", Assert.Single(view.SelectedLists).Name);
    }

    /// <summary>A typed name no list has is offered for creation, and creating goes through the core.</summary>
    [Fact]
    public async Task A_name_no_list_has_can_be_created_from_the_editor()
    {
        var core = OpenedTask()
            .AnswerOk("listPicks", new { selected = Array.Empty<object>(), options = Array.Empty<object>(), createName = "Garden" })
            .AnswerOk("createListForTask", new { list = new { id = "l9", name = "Garden" } })
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", NoDuePicks())
            .AnswerOk("listPicks", new { selected = new[] { new { id = "l9", name = "Garden", color = "#22c55e" } }, options = Array.Empty<object>(), createName = (string?)null });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.LoadListPicksAsync("Garden");
        Assert.True(view.CanCreateList);
        Assert.Equal("Garden", view.CreateListName);

        Assert.True(await view.CreateListAsync());

        var sent = core.Sent.First(command => command.Contains("createListForTask"));
        Assert.Contains("\"name\":\"Garden\"", sent);
        Assert.Equal("Garden", Assert.Single(view.SelectedLists).Name);
        Assert.Equal(string.Empty, view.ListSearch);
        Assert.False(view.CanCreateList);
    }

    /// <summary>Nothing to draw means the box, with its placeholder, as the web shows its prompt.</summary>
    [Fact]
    public async Task An_empty_description_shows_the_editor()
    {
        var view = new TaskDetailViewModel(OpenedTask());

        await view.OpenAsync("t1");

        Assert.Empty(view.DescriptionBlocks);
        Assert.True(view.ShowsDescriptionEditor);
        Assert.False(view.ShowsRenderedDescription);
    }

    /// <summary>The thread as a refresh answers it: the same rows the detail carries.</summary>
    private static object CommentRows(params string[] contents) => contents
        .Select((content, index) => new { id = $"c{index}", content, createdAt = "2026-09-07T12:00:00Z" })
        .ToArray();

    private static object NoDuePicks() =>
        new { isAllDay = true, dates = Array.Empty<object>(), times = Array.Empty<object>() };

    /// <summary>
    /// The menu's closing entry reads "Won't do" on an open task and sends the reason; on a task
    /// closed that way it reads "Reopen" and sends null. What canceled means is the core's
    /// (task 016ce981).
    /// </summary>
    [Fact]
    public async Task Won_t_do_sends_the_reason_and_reopen_clears_it_task_016ce981()
    {
        var core = OpenedTask()
            .AnswerOk("setClosedReason", new { id = "t1", completed = true, closedReason = "canceled" })
            .AnswerOk("taskDetail", Detail(completed: true, isCanceled: true))
            .AnswerOk("setClosedReason", new { id = "t1", completed = false })
            .AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");
        Assert.False(view.IsCanceled);
        Assert.Equal("detail.wont_do", view.WontDoLabelKey);
        Assert.Equal("https://astrid.cc/tasks/t1", view.Link);
        Assert.True(view.CanCopyLink);

        Assert.True(await view.ToggleWontDoAsync());
        Assert.True(view.IsCanceled);
        Assert.True(view.Completed);
        Assert.Equal("detail.reopen", view.WontDoLabelKey);

        Assert.True(await view.ToggleWontDoAsync());
        Assert.False(view.IsCanceled);
        Assert.Equal("detail.wont_do", view.WontDoLabelKey);

        var sent = core.Sent.Where(json => json.Contains("\"kind\":\"setClosedReason\"")).ToList();
        Assert.Equal(2, sent.Count);
        Assert.Contains("\"closedReason\":\"canceled\"", sent[0]);
        // Nulls are not written, so a reopen carries no reason at all; the core reads absence as
        // null, which is what clears it.
        Assert.DoesNotContain("closedReason", sent[1]);
    }

    /// <summary>
    /// The identifier row (task 99da12e0): drawn for a task whose key means something, and the two
    /// answers come apart on a task that has left every board — it stops being drawn and goes on
    /// being copyable, because the id still resolves in links and search.
    /// </summary>
    /// <remarks>
    /// Both flags are read from the core, never derived from the id being present. That is the whole
    /// fix: the shell deciding for itself is what put <c>AWTD-1007</c> beside every title on every
    /// personal list.
    /// </remarks>
    [Fact]
    public async Task The_id_row_follows_the_core_and_copying_outlives_showing_task_99da12e0()
    {
        var onABoard = new FakeCore().AnswerOk("taskDetail",
            Detail(identifier: "AWTD-1007", showsIdentifier: true, offersCopyIdentifier: true));
        var view = new TaskDetailViewModel(onABoard);
        await view.OpenAsync("t1");

        Assert.Equal("AWTD-1007", view.Identifier);
        Assert.True(view.ShowsIdentifier);
        Assert.True(view.OffersCopyIdentifier);

        // Moved off every board: the key no longer describes where the task lives, so the row goes —
        // but the menu still hands the id over.
        var movedOut = new FakeCore().AnswerOk("taskDetail",
            Detail(identifier: "AWTD-1007", showsIdentifier: false, offersCopyIdentifier: true));
        var moved = new TaskDetailViewModel(movedOut);
        await moved.OpenAsync("t1");

        Assert.Equal("AWTD-1007", moved.Identifier);
        Assert.False(moved.ShowsIdentifier);
        Assert.True(moved.OffersCopyIdentifier);

        // An ordinary solo task has neither.
        var solo = new TaskDetailViewModel(new FakeCore().AnswerOk("taskDetail", Detail()));
        await solo.OpenAsync("t1");

        Assert.Null(solo.Identifier);
        Assert.False(solo.ShowsIdentifier);
        Assert.False(solo.OffersCopyIdentifier);
    }

    /// <summary>
    /// The BOARD STATE row (task 5221e43f) arrives with the detail: the board's columns as chips
    /// with the current one lit, or nothing at all for a task the core says has no row. A chip
    /// makes the same move as the menu's Status.
    /// </summary>
    [Fact]
    public async Task A_board_task_shows_its_state_as_chips_and_a_chip_moves_it_task_5221e43f()
    {
        var chips = new
        {
            current = "doing",
            chips = new[]
            {
                new { id = "__virtual_inbox__", name = "Inbox", kind = "inbox", isCurrent = false },
                new { id = "ready", name = "Ready", kind = "status", isCurrent = false },
                new { id = "doing", name = "Doing", kind = "status", isCurrent = true },
            },
        };
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail(boardState: chips))
            .AnswerOk("setTaskStatus", new { id = "t1", statusRole = "ready" })
            .AnswerOk("taskDetail", Detail(boardState: chips));
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        Assert.True(view.ShowsBoardState);
        Assert.Equal(new[] { "Inbox", "Ready", "Doing" }, view.BoardStateChips.Select(chip => chip.Name));
        Assert.True(view.BoardStateChips[2].IsCurrent);

        Assert.True(await view.SetStatusAsync("ready"));
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"setTaskStatus\"") && json.Contains("\"columnId\":\"ready\""));
    }

    /// <summary>
    /// Most tasks are not on a board, and the core says so with no row at all; the pane draws
    /// nothing rather than an empty strip.
    /// </summary>
    [Fact]
    public async Task A_task_the_core_gives_no_board_state_draws_no_row_task_5221e43f()
    {
        var core = new FakeCore().AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        Assert.False(view.ShowsBoardState);
        Assert.Empty(view.BoardStateChips);
    }

    /// <summary>
    /// The WAITING ON row (task 69a840a4) arrives with the detail: every task this one is held
    /// by, in the server's order, with no cap. A blocker the reader may not see is drawn all the
    /// same — it still blocks — and a completed one is still listed, struck through.
    /// </summary>
    [Fact]
    public async Task A_blocked_task_lists_every_task_it_waits_on_task_69a840a4()
    {
        var core = new FakeCore().AnswerOk("taskDetail", Detail(blockers: new
        {
            chips = new object[]
            {
                new { id = "b1", title = "Ship the API", identifier = "AWTD-1002", completed = false, hidden = false },
                new { id = "b2", title = "Sign the build", identifier = (string?)null, completed = true, hidden = false },
                new { id = "b3", hidden = true },
            },
            canEdit = true,
        }));
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        Assert.True(view.ShowsWaitingOn);
        Assert.True(view.CanEditBlockers);
        Assert.False(view.HasNoBlockers);
        // Three chips, not two: the hidden one is counted and drawn.
        Assert.Equal(new[] { "b1", "b2", "b3" }, view.WaitingOnChips.Select(chip => chip.Id));
        // The short id goes before the title, and a task without one is just its title.
        Assert.Equal("AWTD-1002 Ship the API", view.WaitingOnChips[0].Label);
        Assert.Equal("Sign the build", view.WaitingOnChips[1].Label);
        Assert.True(view.WaitingOnChips[1].Completed);
        // A hidden blocker shows neither title nor id.
        Assert.Empty(view.WaitingOnChips[2].Label);
        Assert.False(view.WaitingOnChips[2].IsVisible);
        // The row's one `canEdit` rides on each chip, because a DataTemplate cannot bind past it.
        Assert.All(view.WaitingOnChips, chip => Assert.True(chip.CanEdit));
    }

    /// <summary>
    /// The row shows with nothing on it to anybody who could add a blocker, and not at all to a
    /// reader the core gave no row — the rule is entirely the core's (task 69a840a4).
    /// </summary>
    [Fact]
    public async Task An_empty_waiting_on_row_shows_to_whoever_could_fill_it_task_69a840a4()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail(blockers: new { chips = Array.Empty<object>(), canEdit = true }));
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        Assert.True(view.ShowsWaitingOn);
        Assert.True(view.HasNoBlockers);
        Assert.Empty(view.WaitingOnChips);
    }

    /// <summary>
    /// A reader the core gave chips but no <c>canEdit</c> sees them and cannot lift them: no ✕ on
    /// any chip and no picker. Unlike the board-state row, this one does show read-only
    /// (task 69a840a4).
    /// </summary>
    [Fact]
    public async Task A_read_only_reader_sees_the_blockers_and_cannot_lift_them_task_69a840a4()
    {
        var core = new FakeCore().AnswerOk("taskDetail", Detail(blockers: new
        {
            chips = new[] { new { id = "b1", title = "Ship the API", completed = false, hidden = false } },
            canEdit = false,
        }));
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        Assert.True(view.ShowsWaitingOn);
        Assert.False(view.CanEditBlockers);
        Assert.False(view.WaitingOnChips[0].CanEdit);
    }

    /// <summary>
    /// Most tasks are on no board, and the core says so with no row at all (task 69a840a4).
    /// </summary>
    [Fact]
    public async Task A_task_the_core_gives_no_blockers_draws_no_row_task_69a840a4()
    {
        var core = new FakeCore().AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        Assert.False(view.ShowsWaitingOn);
        Assert.False(view.CanEditBlockers);
        Assert.Empty(view.WaitingOnChips);
    }

    /// <summary>
    /// Adding a blocker sends the core's command and redraws from what it answers — and re-asks
    /// the picker, because the task just chosen is no longer offerable and the picker stays open
    /// between picks (task 69a840a4).
    /// </summary>
    [Fact]
    public async Task Waiting_on_a_task_sends_the_command_and_redraws_from_the_answer_task_69a840a4()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail(blockers: new { chips = Array.Empty<object>(), canEdit = true }))
            .AnswerOk("addTaskBlocker", new
            {
                blockedBy = new[] { new { id = "b1", title = "Ship the API", completed = false, hidden = false } },
                blocks = Array.Empty<object>(),
                dependentIds = Array.Empty<string>(),
            })
            .AnswerOk("taskBlockerCandidates", new { candidates = Array.Empty<object>() });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.True(await view.AddBlockerAsync("b1"));

        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"addTaskBlocker\"")
            && json.Contains("\"blockingTaskId\":\"b1\""));
        Assert.Equal(new[] { "b1" }, view.WaitingOnChips.Select(chip => chip.Id));
        Assert.False(view.HasNoBlockers);
        // The ✕ still rides on the chip after a write, from the row's `canEdit`.
        Assert.True(view.WaitingOnChips[0].CanEdit);
        Assert.Contains(core.Sent, json => json.Contains("\"kind\":\"taskBlockerCandidates\""));
    }

    /// <summary>The ✕ sends the core's command and redraws from the answer (task 69a840a4).</summary>
    [Fact]
    public async Task Removing_a_blocker_sends_the_command_and_redraws_task_69a840a4()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail(blockers: new
            {
                chips = new[] { new { id = "b1", title = "Ship the API", completed = false, hidden = false } },
                canEdit = true,
            }))
            .AnswerOk("removeTaskBlocker", new
            {
                blockedBy = Array.Empty<object>(),
                blocks = Array.Empty<object>(),
                dependentIds = Array.Empty<string>(),
            });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.True(await view.RemoveBlockerAsync("b1"));

        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"removeTaskBlocker\"")
            && json.Contains("\"blockingTaskId\":\"b1\""));
        Assert.Empty(view.WaitingOnChips);
        Assert.True(view.HasNoBlockers);
    }

    /// <summary>
    /// The picker asks the core for its whole answer — what matches, what is offerable and the
    /// two-character threshold are all the core's, because a picker that filtered the page it
    /// happens to have loaded is the bug web fixed as 5df85b9f (task 69a840a4).
    /// </summary>
    [Fact]
    public async Task The_blocker_picker_asks_the_core_and_filters_nothing_itself_task_69a840a4()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail(blockers: new { chips = Array.Empty<object>(), canEdit = true }))
            .AnswerOk("taskBlockerCandidates", new
            {
                candidates = new[]
                {
                    new { id = "b7", title = "Ship the API", identifier = "AWTD-1002", completed = false, hidden = false },
                },
            });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.LoadBlockerCandidatesAsync("shi");

        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"taskBlockerCandidates\"") && json.Contains("\"query\":\"shi\""));
        Assert.Equal("shi", view.BlockerSearch);
        Assert.Equal(new[] { "b7" }, view.BlockerCandidates.Select(pick => pick.Id));
        Assert.Equal("AWTD-1002 Ship the API", view.BlockerCandidates[0].Label);
        Assert.False(view.HasNoCandidates);
    }

    /// <summary>
    /// The refresh is the round trip that tells "a task you cannot see" from "a task this cache
    /// has not got yet". A task with no row does not make it (task 69a840a4).
    /// </summary>
    [Fact]
    public async Task Refreshing_blockers_corrects_the_row_and_skips_a_task_without_one_task_69a840a4()
    {
        var withoutRow = new FakeCore().AnswerOk("taskDetail", Detail());
        var quiet = new TaskDetailViewModel(withoutRow);
        await quiet.OpenAsync("t1");
        await quiet.RefreshBlockersAsync();
        Assert.DoesNotContain(withoutRow.Sent, json => json.Contains("\"kind\":\"taskBlockers\""));

        // Opening does the round trip itself, after the screen is already drawn from the cache.
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail(blockers: new
            {
                chips = new[] { new { id = "b1", hidden = true } },
                canEdit = true,
            }))
            .AnswerOk("taskBlockers", new
            {
                blockedBy = new[] { new { id = "b1", title = "Ship the API", completed = false, hidden = false } },
                blocks = Array.Empty<object>(),
                dependentIds = Array.Empty<string>(),
            });
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        Assert.Contains(core.Sent, json => json.Contains("\"kind\":\"taskBlockers\""));
        // The cache could only say "a task you cannot see"; the server says which it is.
        Assert.Equal("Ship the API", view.WaitingOnChips[0].Label);
        Assert.True(view.WaitingOnChips[0].IsVisible);
        Assert.True(view.WaitingOnChips[0].CanEdit);
    }

    /// <summary>
    /// A refresh that cannot reach the server leaves the cached row alone and says nothing: the
    /// chips on screen are the right thing to be looking at offline, and a banner over a working
    /// screen is noise (task 69a840a4).
    /// </summary>
    [Fact]
    public async Task A_blocker_refresh_that_fails_keeps_the_cached_row_and_is_quiet_task_69a840a4()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail(blockers: new
            {
                chips = new[] { new { id = "b1", title = "Ship the API", completed = false, hidden = false } },
                canEdit = true,
            }))
            .AnswerFailure("taskBlockers", AstridFailureKind.Offline);
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        Assert.Equal(new[] { "b1" }, view.WaitingOnChips.Select(chip => chip.Id));
        Assert.Null(view.ErrorMessage);
    }

    /// <summary>
    /// A pick the core refuses says so in the picker, in this app's words (task 69a840a4).
    /// </summary>
    /// <remarks>
    /// A resource key and not a sentence: the core's message is a developer's line in one
    /// language, and the row's copy is translated in both string folders. The chip does not appear
    /// either — a refused write must not leave the reader looking at a blocker that is not there.
    /// </remarks>
    [Fact]
    public async Task A_refused_pick_says_so_in_the_picker_s_own_words_task_69a840a4()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail(blockers: new { chips = Array.Empty<object>(), canEdit = true }))
            .AnswerFailure("addTaskBlocker", AstridFailureKind.BadRequest, "cannot wait on that");
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.False(await view.AddBlockerAsync("b1"));

        Assert.Equal("detail.waiting_on_error", view.BlockerErrorKey);
        Assert.Empty(view.WaitingOnChips);
    }

    /// <summary>
    /// The refusal goes when the next pick takes, so a picker left open does not keep saying no
    /// about a task the reader has moved on from (task 69a840a4).
    /// </summary>
    [Fact]
    public async Task A_pick_that_takes_clears_the_last_refusal_task_69a840a4()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail(blockers: new { chips = Array.Empty<object>(), canEdit = true }))
            .AnswerFailure("addTaskBlocker", AstridFailureKind.BadRequest, "cannot wait on that")
            .AnswerOk("addTaskBlocker", new
            {
                blockedBy = new[] { new { id = "b2", title = "Ship the API", completed = false, hidden = false } },
                blocks = Array.Empty<object>(),
                dependentIds = Array.Empty<string>(),
            })
            .AnswerOk("taskBlockerCandidates", new { candidates = Array.Empty<object>() });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");
        await view.AddBlockerAsync("b1");
        Assert.NotNull(view.BlockerErrorKey);

        Assert.True(await view.AddBlockerAsync("b2"));

        Assert.Null(view.BlockerErrorKey);
        Assert.Equal(new[] { "b2" }, view.WaitingOnChips.Select(chip => chip.Id));
    }

    /// <summary>
    /// The Status submenu is the board's columns as the core gives them, current one lit, and a
    /// choice is sent by column id — the core makes the move (task 016ce981).
    /// </summary>
    [Fact]
    public async Task The_status_menu_offers_the_board_s_columns_and_a_choice_moves_the_task_task_016ce981()
    {
        var core = OpenedTask()
            .AnswerOk("taskStatusOptions", new
            {
                current = "__virtual_inbox__",
                columns = new[]
                {
                    new { id = "__virtual_inbox__", name = "Inbox", kind = "inbox", isCurrent = true },
                    new { id = "doing", name = "Doing", kind = "status", isCurrent = false },
                    new { id = "__virtual_done__", name = "Done", kind = "done", isCurrent = false },
                },
            })
            .AnswerOk("setTaskStatus", new { id = "t1", statusRole = "doing" })
            .AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.True(await view.LoadStatusChoicesAsync());
        Assert.Equal(new[] { "Inbox", "Doing", "Done" }, view.StatusChoices.Select(choice => choice.Name));
        Assert.True(view.StatusChoices[0].IsCurrent);
        Assert.False(view.StatusChoices[1].IsCurrent);

        Assert.True(await view.SetStatusAsync("doing"));
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"setTaskStatus\"") && json.Contains("\"columnId\":\"doing\""));
    }

    /// <summary>
    /// Copy hands back the copy's id and sends where it goes and whether the comments come; an
    /// offline attempt is reported rather than pretended, because the server makes the copy.
    /// </summary>
    [Fact]
    public async Task Copy_names_the_target_and_the_comments_and_reports_a_copy_that_did_not_happen()
    {
        var core = OpenedTask()
            .AnswerOk("copyTask", new { id = "t-copy", title = "Book flights" })
            .AnswerFailure("copyTask", AstridFailureKind.Offline, "no network");
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.Equal("t-copy", await view.CopyAsync("l2", includeComments: true));
        var sent = core.Sent.First(json => json.Contains("copyTask"));
        Assert.Contains("\"targetListId\":\"l2\"", sent, StringComparison.Ordinal);
        Assert.Contains("\"includeComments\":true", sent, StringComparison.Ordinal);
        Assert.Null(view.ErrorMessage);

        Assert.Null(await view.CopyAsync(null, includeComments: false));
        Assert.Equal("no network", view.ErrorMessage);
    }

    /// <summary>
    /// Share hands back the address the core minted, and a refusal lands in the error line rather
    /// than vanishing (task 016ce981).
    /// </summary>
    [Fact]
    public async Task Share_returns_the_minted_link_and_reports_a_refusal_task_016ce981()
    {
        var core = OpenedTask()
            .AnswerOk("shareTask", new { url = "https://astrid.cc/s/abc123" })
            .AnswerFailure("shareTask", AstridFailureKind.BadRequest,
                "this task has not reached the server yet, so it cannot be shared");
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.Equal("https://astrid.cc/s/abc123", await view.ShareAsync());
        Assert.Null(view.ErrorMessage);

        Assert.Null(await view.ShareAsync());
        Assert.Equal("this task has not reached the server yet, so it cannot be shared", view.ErrorMessage);
    }

    /// <summary>
    /// The detail's buttons change what they show at once and let the core catch up; a refused
    /// write puts the old value back (task cdb30d3d).
    /// </summary>
    [Fact]
    public async Task Priority_completion_and_timer_show_at_once_and_a_refusal_puts_them_back_task_cdb30d3d()
    {
        var core = OpenedTask()
            .Hold("updateTask")
            .AnswerOk("updateTask", new { id = "t1" })
            .AnswerOk("taskDetail", Detail(priority: 2))
            .AnswerFailure("completeTask", AstridFailureKind.Refused, "not yours")
            .AnswerOk("taskDetail", Detail(priority: 2))
            .AnswerFailure("startTimer", AstridFailureKind.Refused, "no timer for you")
            .AnswerOk("taskDetail", Detail(priority: 2));
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");
        Assert.Equal(3, view.Priority);

        var pending = view.SetPriorityAsync(2);
        Assert.Equal(2, view.Priority); // before the core has answered
        core.Release("updateTask");
        Assert.True(await pending);
        Assert.Equal(2, view.Priority);

        Assert.False(await view.SetCompletedAsync(true));
        Assert.False(view.Completed); // refused, so put back
        Assert.Equal("not yours", view.ErrorMessage);

        Assert.False(await view.SetTimingAsync(true));
        Assert.False(view.IsTiming);
    }

    private static FakeCore OpenedTask() => new FakeCore()
        .AnswerOk("taskDetail", Detail())
        .AnswerOk("dueDateOptions", NoDuePicks())
        .AnswerOk("refreshComments");

    /// <summary>
    /// A timer that is running keeps the section on screen; one that has recorded something keeps
    /// a caption, so hiding the section never hides the data.
    /// </summary>
    [Fact]
    public async Task The_timer_says_whether_it_is_running_and_what_it_has_recorded()
    {
        var core = OpenedTask()
            .AnswerOk("startTimer", new { isRunning = true, startedAt = "2026-09-07T09:00:00Z", loggedMinutes = 0 })
            .AnswerOk("stopTimer", new { isRunning = false, loggedMinutes = 65, lastValue = "1h 5m" });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.SetTimingAsync(true);
        Assert.True(view.IsTiming);
        Assert.False(view.HasLoggedTime);

        await view.SetTimingAsync(false);
        Assert.False(view.IsTiming);
        Assert.True(view.HasLoggedTime);
        Assert.Equal(65, view.Timer.LoggedMinutes);
    }

    /// <summary>
    /// The files on a task come from the core, which gathers the task's own and its comments' —
    /// there is no attach-to-task endpoint anywhere.
    /// </summary>
    [Fact]
    public async Task The_files_on_a_task_are_listed_with_their_sizes()
    {
        var core = OpenedTask().AnswerOk("attachments", new
        {
            files = new[]
            {
                new
                {
                    id = "f1",
                    name = "itinerary.pdf",
                    size = 2048,
                    mimeType = "application/pdf",
                    isCached = false,
                    path = "C:/cache/f1.pdf",
                },
            },
        });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.LoadAttachmentsAsync();

        Assert.Single(view.Attachments);
        Assert.Equal("itinerary.pdf", view.Attachments[0].Name);
        Assert.Equal("2 KB", view.Attachments[0].SizeLabel);
        Assert.False(view.Attachments[0].IsCached);
    }

    /// <summary>
    /// A download answers with a path, not with bytes: what somebody does with an attachment is
    /// open it in the program that reads that kind of file.
    /// </summary>
    [Fact]
    public async Task Downloading_answers_with_where_the_file_landed()
    {
        var core = OpenedTask()
            .AnswerOk("downloadAttachment", new { path = "C:/cache/f1.pdf" })
            .AnswerOk("attachments", new { files = Array.Empty<object>() });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        var path = await view.DownloadAsync("f1");

        Assert.Equal("C:/cache/f1.pdf", path);
    }

    /// <summary>
    /// Attaching needs a connection, unlike every other write here — the journal holds JSON, and a
    /// queued photo would be megabytes nobody else could see. So it reports rather than pretends.
    /// </summary>
    [Fact]
    public async Task An_attachment_that_could_not_be_uploaded_is_reported()
    {
        var core = OpenedTask()
            .AnswerFailure("attachFile", AstridFailureKind.Offline, "no network");
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.False(await view.AttachAsync("C:/photos/map.png"));
    }

    /// <summary>
    /// The choices carry the instant each one means, so the shell never subtracts an hour from a
    /// date — the arithmetic that goes wrong across a daylight-saving boundary.
    /// </summary>
    [Fact]
    public async Task The_reminder_picker_writes_the_instant_the_core_worked_out()
    {
        var core = OpenedTask()
            .AnswerOk("reminderOptions", new
            {
                reminderTime = (string?)null,
                picks = new[]
                {
                    new { titleKey = "reminder.none", reminderTime = (string?)null, isSelected = true },
                    new
                    {
                        titleKey = "reminder.hour_before",
                        reminderTime = (string?)"2026-09-20T08:00:00+00:00",
                        isSelected = false,
                    },
                },
            })
            .AnswerOk("updateTask")
            .AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");
        await view.LoadReminderPicksAsync();

        Assert.Equal(2, view.ReminderPicks.Count);
        Assert.False(view.HasReminder);

        await view.SetReminderAsync(view.ReminderPicks[1].ReminderTime);

        var update = core.Sent.First(sent => sent.Contains("updateTask"));
        // The offset's "+" comes back escaped by the JSON writer, so this looks for the instant
        // rather than the exact spelling of the separator.
        Assert.Contains("reminderTime", update);
        Assert.Contains("2026-09-20T08:00:00", update);
    }

    /// <summary>Clearing sends null, because absent means "leave it alone".</summary>
    [Fact]
    public async Task Choosing_no_reminder_clears_it()
    {
        var core = OpenedTask().AnswerOk("updateTask").AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.SetReminderAsync(null);

        Assert.Contains("\"reminderTime\":null", core.Sent.First(sent => sent.Contains("updateTask")));
    }

    /// <summary>
    /// The presets come from the core, marked. A shell that decided which row was selected would
    /// be the fourth place to decide it.
    /// </summary>
    [Fact]
    public async Task The_repeat_picker_is_loaded_when_it_is_opened()
    {
        var core = OpenedTask().AnswerOk("repeatOptions", new
        {
            repeating = "weekly",
            repeatFrom = "DUE_DATE",
            presets = new[]
            {
                new { value = "never", titleKey = "repeating.one_time_only", isSelected = false },
                new { value = "weekly", titleKey = "repeating.weekly", isSelected = true },
            },
            summary = new[] { new { key = "repeating.weekly" }, new { key = "repeat.from_due_date" } },
        });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.LoadRepeatAsync();

        Assert.Equal(2, view.RepeatPresets.Count);
        Assert.True(view.RepeatPresets[1].IsSelected);
        Assert.True(view.RepeatsFromDueDate);
        Assert.True(view.IsRepeating);
    }

    /// <summary>
    /// A preset clears any custom pattern with it: a daily task carrying a leftover custom rule
    /// repeats one way and reads another.
    /// </summary>
    [Fact]
    public async Task Choosing_a_preset_clears_the_custom_pattern()
    {
        var core = OpenedTask().AnswerOk("updateTask").AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.True(await view.SetRepeatAsync("daily"));

        var update = core.Sent.First(sent => sent.Contains("updateTask"));
        Assert.Contains("\"repeating\":\"daily\"", update);
        Assert.Contains("\"repeatingData\":null", update);
    }

    /// <summary>
    /// "Custom" is not a repeat until a pattern has been built. Writing it with nothing behind it
    /// leaves a task repeating on a rule nobody can read.
    /// </summary>
    [Fact]
    public async Task Choosing_custom_writes_nothing_on_its_own()
    {
        var core = OpenedTask();
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.False(await view.SetRepeatAsync("custom"));

        Assert.DoesNotContain("updateTask", core.SentKinds());
    }

    [Fact]
    public async Task A_custom_pattern_is_stored_with_the_preset_that_names_it()
    {
        var core = OpenedTask().AnswerOk("updateTask").AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.SetCustomRepeatAsync("weeks", 2, ["monday", "wednesday"]);

        var update = core.Sent.First(sent => sent.Contains("updateTask"));
        Assert.Contains("\"repeating\":\"custom\"", update);
        Assert.Contains("\"unit\":\"weeks\"", update);
        Assert.Contains("\"interval\":2", update);
        Assert.Contains("monday", update);
    }

    /// <summary>
    /// The fields a pattern does not need are left out. The column is free-form JSON, and a
    /// monthly pattern carrying weekdays from a previous edit means something different to
    /// whichever client reads it next.
    /// </summary>
    [Fact]
    public async Task A_pattern_carries_only_the_fields_its_unit_needs()
    {
        var core = OpenedTask().AnswerOk("updateTask").AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.SetCustomRepeatAsync("months", 1, ["monday"], "never", 10, "2027-01-01T00:00:00Z");

        var update = core.Sent.First(sent => sent.Contains("updateTask"));
        Assert.DoesNotContain("weekdays", update);
        Assert.DoesNotContain("endAfterOccurrences", update);
        Assert.DoesNotContain("endUntilDate", update);
    }

    /// <summary>An interval of zero is not a repeat, it is a loop.</summary>
    [Fact]
    public async Task An_interval_below_one_is_pulled_back_up()
    {
        var core = OpenedTask().AnswerOk("updateTask").AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.SetCustomRepeatAsync("days", 0);

        Assert.Contains("\"interval\":1", core.Sent.First(sent => sent.Contains("updateTask")));
    }

    /// <summary>
    /// The picker is asked for as it opens rather than carried with the task: the answer depends on
    /// the account's agents and on every list the task is on.
    /// </summary>
    [Fact]
    public async Task The_assignee_picker_is_loaded_when_it_is_opened()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", new { isAllDay = true, dates = Array.Empty<object>(), times = Array.Empty<object>() })
            .AnswerOk("refreshComments")
            .AnswerOk("assigneeOptions", new
            {
                assigneeId = (string?)null,
                options = new[]
                {
                    new { userId = (string?)null, name = (string?)null, initials = "", isCurrentUser = false, isAgent = false },
                    new { userId = (string?)"me", name = (string?)"Jon", initials = "JO", isCurrentUser = true, isAgent = false },
                },
            });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.LoadAssigneesAsync();

        Assert.Equal(2, view.Assignees.Count);
        // Unassigned first, and it carries no word — the shell names it from its resources.
        Assert.Null(view.Assignees[0].UserId);
        Assert.Equal("assignee.unassigned", view.Assignees[0].TitleKey);
        Assert.Equal("Jon", view.Assignees[1].TitleKey);
    }

    [Fact]
    public async Task Assigning_writes_the_chosen_person()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", new { isAllDay = true, dates = Array.Empty<object>(), times = Array.Empty<object>() })
            .AnswerOk("refreshComments")
            .AnswerOk("updateTask")
            .AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.True(await view.AssignAsync("u1"));

        var update = core.Sent.First(sent => sent.Contains("updateTask"));
        Assert.Contains("\"assigneeId\":\"u1\"", update);
    }

    /// <summary>
    /// Clearing has to send null rather than leave the field out: absent means "leave alone", and a
    /// picker that cannot express "no one" cannot take a task off somebody.
    /// </summary>
    [Fact]
    public async Task Choosing_no_one_clears_the_assignee_rather_than_saying_nothing()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", new { isAllDay = true, dates = Array.Empty<object>(), times = Array.Empty<object>() })
            .AnswerOk("refreshComments")
            .AnswerOk("updateTask")
            .AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.AssignAsync(null);

        var update = core.Sent.First(sent => sent.Contains("updateTask"));
        Assert.Contains("\"assigneeId\":null", update);
    }

    [Fact]
    public async Task Opening_a_task_fills_the_pane_from_one_command()
    {
        var core = new FakeCore().AnswerOk("taskDetail",
            Detail(subtasks: ["Book flights"], comments: ["asked Sam"]));
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        Assert.True(view.IsOpen);
        Assert.Equal("Plan the trip", view.Title);
        Assert.Equal("two weeks", view.Description);
        Assert.Equal(3, view.Priority);
        Assert.Equal("!!!", view.PriorityGlyph);
        Assert.Equal("today", view.Due.Key);
        Assert.Equal("Home", view.ListChips[0].Name);
        Assert.Equal("Book flights", view.Subtasks[0].Title);
        Assert.Equal("asked Sam", view.Comments[0].Content);
        // The screen and the quick date choices, both cache reads, and then the comment thread
        // from the server — sync does not pull threads, so one is fetched when it is opened.
        Assert.Equal(["taskDetail", "dueDateOptions", "refreshComments"], core.SentKinds());
    }

    /// <summary>
    /// A thread that cannot be fetched leaves the cached one on screen. That is the right thing to
    /// be looking at offline, and an error banner over a working screen is noise.
    /// </summary>
    [Fact]
    public async Task A_comment_refresh_that_fails_leaves_the_cached_thread_alone()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail(comments: ["asked Sam"]))
            .AnswerFailure("refreshComments", AstridFailureKind.Offline, "no network");
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        Assert.Equal("asked Sam", view.Comments[0].Content);
        Assert.Null(view.ErrorMessage);
    }

    /// <summary>And one that succeeds replaces it from the answer, without re-reading the screen.</summary>
    [Fact]
    public async Task A_comment_refresh_updates_the_thread_from_its_own_answer()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail(comments: ["stale"]))
            .AnswerOk("refreshComments", new object[]
            {
                new { id = "c1", content = "fresh", createdAt = "2026-09-07T12:00:00Z" },
            });
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        Assert.Equal("fresh", view.Comments[0].Content);
        Assert.Equal(1, core.SentKinds().Count(kind => kind == "taskDetail"));
    }

    /// <summary>
    /// The order is the core's. Both Apple platforms put Priority first and Who second before it
    /// was written down once, which is what happens when two views each decide for themselves.
    /// </summary>
    [Fact]
    public async Task The_field_order_comes_from_the_core()
    {
        var core = new FakeCore().AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        Assert.Equal(["assignee", "when", "priority", "lists"], view.FieldOrder);
    }

    /// <summary>Completing goes through the command that rolls a repeating task forward.</summary>
    [Fact]
    public async Task Completing_from_the_detail_uses_the_complete_command()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("completeTask")
            .AnswerOk("taskDetail", Detail(completed: true));
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.True(await view.SetCompletedAsync(true));
        Assert.Contains("completeTask", core.SentKinds());
        Assert.DoesNotContain("updateTask", core.SentKinds());
        Assert.True(view.Completed);
    }

    /// <summary>An empty title leaves a row nobody can identify.</summary>
    [Fact]
    public async Task An_empty_title_is_refused_rather_than_saved()
    {
        var core = new FakeCore().AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.False(await view.SaveTitleAsync("   "));
        Assert.DoesNotContain("updateTask", core.SentKinds());
    }

    /// <summary>
    /// A task that has gone — deleted here or by somebody else — closes the pane rather than
    /// leaving a screen showing something that is not there.
    /// </summary>
    [Fact]
    public async Task A_task_that_has_gone_closes_the_pane()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerFailure("taskDetail", AstridFailureKind.NotFound, "no task with id t1");
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.ReloadAsync();

        Assert.False(view.IsOpen);
        Assert.Null(view.TaskId);
    }

    /// <summary>Offline is not an error to show: the edit is in the Outbox.</summary>
    [Fact]
    public async Task An_edit_made_offline_is_not_reported_as_a_failure()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerFailure("updateTask", AstridFailureKind.Offline, "no network");
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.SaveTitleAsync("Plan the other trip");

        Assert.Null(view.ErrorMessage);
    }

    [Fact]
    public async Task Clearing_the_due_date_sends_a_null()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("updateTask")
            .AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.SetDueDateAsync(null, isAllDay: true);

        var sent = core.Sent.Last(json => json.Contains("updateTask", StringComparison.Ordinal));
        Assert.Contains("\"dueDateTime\":null", sent, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reloading swaps rows in place. Clearing and re-adding would collapse everything and lose
    /// the caret in a comment being typed, after every single edit.
    /// </summary>
    [Fact]
    public async Task A_reload_keeps_the_rows_that_did_not_change()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail(comments: ["one", "two"]))
            .AnswerOk("taskDetail", Detail(comments: ["one", "two changed"]));
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");
        var first = view.Comments[0];

        await view.ReloadAsync();

        Assert.Same(first, view.Comments[0]);
        Assert.Equal("two changed", view.Comments[1].Content);
    }

    /// <summary>
    /// The quick choices arrive with the instant each one means, so the shell never computes a
    /// date. Every part of that arithmetic is decided once, in the core, for all three clients.
    /// </summary>
    [Fact]
    public async Task The_due_picks_carry_the_instants_they_mean()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", new
            {
                isAllDay = true,
                dueDateTime = "2026-09-07T00:00:00Z",
                dates = new object[]
                {
                    new { titleKey = "picker.no_due_date", dueDateTime = (string?)null, isSelected = false },
                    new { titleKey = "picker.today", dueDateTime = "2026-09-07T00:00:00Z", isSelected = true },
                },
                times = new object[]
                {
                    new { titleKey = "picker.morning", hour = 9, dueDateTime = "2026-09-07T09:00:00Z", isSelected = false },
                },
            });
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        Assert.Equal("picker.no_due_date", view.DatePicks[0].TitleKey);
        Assert.Null(view.DatePicks[0].DueDateTime);
        Assert.True(view.DatePicks[1].IsSelected);
        Assert.Equal("2026-09-07T09:00:00Z", view.TimePicks[0].DueDateTime);
        Assert.True(view.IsAllDay);
    }

    /// <summary>
    /// Clearing the date is the shell's own control now, and it sends an explicit null.
    /// </summary>
    /// <remarks>
    /// The quick choices used to carry a "No due date" row and this was a pick like any other.
    /// Following iOS, the core's <c>dueDateOptions</c> stopped returning it (task 6ee938cc) — so a
    /// shell that only drew what the core offered had a date it could set and never unset. The
    /// control is the shell's, the write is still the core's.
    /// </remarks>
    [Fact]
    public async Task Clearing_the_date_is_the_shells_own_control_task_6ee938cc()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", new
            {
                isAllDay = true,
                dates = new object[]
                {
                    new { titleKey = "picker.today", dueDateTime = "2026-09-07T00:00:00Z", isSelected = true },
                },
                times = Array.Empty<object>(),
            })
            .AnswerOk("updateTask")
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", new { isAllDay = true, dates = Array.Empty<object>(), times = Array.Empty<object>() });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.DoesNotContain("picker.no_due_date", view.DatePicks.Select(pick => pick.TitleKey));
        Assert.True(await view.ClearDueAsync());

        var sent = core.Sent.Last(json => json.Contains("updateTask", StringComparison.Ordinal));
        Assert.Contains("\"dueDateTime\":null", sent, StringComparison.Ordinal);
    }

    /// <summary>Choosing a time makes the task timed rather than all-day.</summary>
    [Fact]
    public async Task Choosing_a_time_makes_the_task_timed()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", new
            {
                isAllDay = true,
                dates = Array.Empty<object>(),
                times = new object[]
                {
                    new { titleKey = "picker.morning", hour = 9, dueDateTime = "2026-09-07T09:00:00Z", isSelected = false },
                },
            })
            .AnswerOk("updateTask")
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", new { isAllDay = false, dates = Array.Empty<object>(), times = Array.Empty<object>() });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.TakeDuePickAsync(view.TimePicks[0]);

        var sent = core.Sent.Last(json => json.Contains("updateTask", StringComparison.Ordinal));
        Assert.Contains("\"isAllDay\":false", sent, StringComparison.Ordinal);
        Assert.Contains("2026-09-07T09:00:00Z", sent, StringComparison.Ordinal);
    }

    /// <summary>
    /// The detail pane only reloads for the task it is showing. Reloading for every task somebody
    /// else touches would make the open one flicker while a colleague works in the same list.
    /// </summary>
    [Fact]
    public async Task A_notification_for_another_task_leaves_the_open_one_alone()
    {
        var core = new FakeCore()
            .AnswerOk("isSignedIn", new { signedIn = true, waitingForCallback = false })
            .AnswerOk("lists", new object[] { new { id = "l1", name = "Home" } })
            .AnswerOk("rowsForList", new { total = 0, offset = 0, rows = Array.Empty<object>() })
            .AnswerOk("outboxStats", new { hasUnsentWork = false })
            .AnswerOk("sync", new { fetched = false })
            .AnswerOk("taskDetail", Detail());
        using var shell = new ShellViewModel(core, work => work().GetAwaiter().GetResult());
        await shell.StartAsync();
        await shell.OpenTaskAsync("t1");

        core.AnswerOk("rowsForList", new { total = 0, offset = 0, rows = Array.Empty<object>() })
            .AnswerOk("outboxStats", new { hasUnsentWork = false });
        var before = core.SentKinds().Count(kind => kind == "taskDetail");

        core.Notify("task", "somebody-elses-task");

        Assert.Equal(before, core.SentKinds().Count(kind => kind == "taskDetail"));
    }

    /// <summary>
    /// A file posted without a caption used to draw an empty row, and an empty row is what "it did
    /// not attach" looks like. The core says whether there is text; this is the view model
    /// carrying its answer.
    /// </summary>
    [Fact]
    public async Task A_comment_carries_its_files_and_says_whether_it_has_text()
    {
        var core = new FakeCore().AnswerOk("taskDetail", new
        {
            task = new { id = "t1", title = "Plan the trip" },
            fieldOrder = new[] { "assignee" },
            comments = new[]
            {
                new
                {
                    id = "c1",
                    content = "",
                    showsText = false,
                    isPending = false,
                    files = new[]
                    {
                        new
                        {
                            id = "f1",
                            name = "shot.png",
                            size = 2048L,
                            mimeType = "image/png",
                            rendersInline = true,
                        },
                    },
                },
            },
            subtasks = Array.Empty<object>(),
        });
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        var comment = Assert.Single(view.Comments);
        Assert.False(comment.ShowsText);
        Assert.True(comment.HasFiles);
        Assert.Equal("shot.png", comment.Files[0].Name);
        Assert.Equal("2 KB", comment.Files[0].SizeLabel);
        Assert.True(comment.Files[0].RendersInline);
    }

    /// <summary>
    /// A picture whose bytes are in hand is drawn, not described. The core reports the path
    /// without touching the network, so a screenshot posted from this machine appears at once
    /// instead of being fetched back from a server it has not reached yet (AITD-308).
    /// </summary>
    [Fact]
    public void A_picture_with_its_bytes_in_hand_is_drawn()
    {
        var file = new CommentFile
        {
            Id = "f1",
            Name = "shot.png",
            MimeType = "image/png",
            RendersInline = true,
            LocalPath = @"C:\cache\pending1",
        };

        Assert.True(file.ShowsThumbnail);
        Assert.False(file.ShowsChip);
    }

    /// <summary>
    /// Not fetched yet is a chip, not a broken image. It is also exactly what the row looked like
    /// before any of this, so the fallback is the old behaviour rather than a new empty state.
    /// </summary>
    [Fact]
    public void A_picture_whose_bytes_are_not_here_yet_stays_a_chip()
    {
        var file = new CommentFile
        {
            Id = "f1",
            Name = "shot.png",
            MimeType = "image/png",
            RendersInline = true,
        };

        Assert.False(file.ShowsThumbnail);
        Assert.True(file.ShowsChip);
    }

    /// <summary>
    /// A document is a chip whether or not its bytes are here. The core decides what is drawable;
    /// having a path is not a second opinion about it.
    /// </summary>
    [Fact]
    public void A_document_is_a_chip_even_with_its_bytes_in_hand()
    {
        var file = new CommentFile
        {
            Id = "f1",
            Name = "notes.pdf",
            MimeType = "application/pdf",
            RendersInline = false,
            LocalPath = @"C:\cache1",
        };

        Assert.False(file.ShowsThumbnail);
        Assert.True(file.ShowsChip);
    }

    /// <summary>
    /// The Send button and the Return key have to agree about whether there is anything to send —
    /// an offered Send that does nothing when clicked is worse than no Send at all.
    /// </summary>
    [Fact]
    public async Task Send_is_offered_exactly_when_a_comment_would_post()
    {
        var core = new FakeCore().AnswerOk("postComment").AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.False(view.CanSendComment);
        Assert.False(await view.AddCommentAsync(view.CommentDraft));

        view.CommentDraft = "   ";
        Assert.False(view.CanSendComment);
        Assert.False(await view.AddCommentAsync(view.CommentDraft));

        view.CommentDraft = "said something";
        Assert.True(view.CanSendComment);
        Assert.True(await view.AddCommentAsync(view.CommentDraft));
    }

    /// <summary>Reading the clipboard is the window's job; deciding what it meant is not.</summary>
    [Fact]
    public async Task A_paste_asks_the_core_what_it_meant()
    {
        var core = new FakeCore().AnswerOk("clipboardPaste", new
        {
            action = "files",
            files = new[] { @"C:\shots\one.png" },
        });
        var view = new TaskDetailViewModel(core);

        var decided = await view.DecidePasteAsync([@"C:\shots\one.png"], hasImage: true,
            hasText: false);

        Assert.Equal("files", decided.Action);
        Assert.Equal(@"C:\shots\one.png", Assert.Single(decided.Files));
    }

    /// <summary>A core that cannot answer must leave an ordinary paste alone.</summary>
    [Fact]
    public async Task A_paste_the_core_cannot_answer_is_left_to_type()
    {
        var view = new TaskDetailViewModel(new FakeCore());

        var decided = await view.DecidePasteAsync([], hasImage: false, hasText: true);

        Assert.Equal("text", decided.Action);
        Assert.Empty(decided.Files);
    }

    /// <summary>
    /// A task the reader can only copy says so, and its header's control copies it rather than
    /// completing it (task f6bc59e8).
    /// </summary>
    [Fact]
    public async Task A_copy_only_task_is_copied_from_its_header_task_f6bc59e8()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", new
            {
                task = new { id = "t1", title = "Bake bread", description = "", priority = 0, completed = false },
                isCopyOnly = true,
                fieldOrder = Array.Empty<string>(),
                listChips = Array.Empty<object>(),
                comments = Array.Empty<object>(),
                subtasks = Array.Empty<object>(),
            })
            .AnswerOk("copyTask", new { id = "t9", title = "Bake bread" });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.True(view.IsCopyOnly);
        Assert.True(await view.CopyToMineAsync());
        var copy = Assert.Single(core.Sent, json => json.Contains("\"kind\":\"copyTask\""));
        Assert.Contains("\"taskId\":\"t1\"", copy);
        Assert.DoesNotContain("completeTask", core.SentKinds());
    }

    // ── One editing session at a time (PRODUCT_CONTRACT.md §6, task e71ed760) ──────────────
    //
    // The machine is the core's, stepped by command; these script its answers and check that
    // the view model does what each names — commit, revert, or nothing at all.

    private static object Transition(string? active, string? commit = null, string? cancel = null) =>
        new { active, commit, cancel };

    /// <summary>Opening a second editor commits the first: the buffer goes as one write.</summary>
    [Fact]
    public async Task Opening_a_second_editor_commits_the_first_task_e71ed760()
    {
        var core = OpenedTask()
            .AnswerOk("beginEditing", Transition("title"))
            .AnswerOk("beginEditing", Transition("description", commit: "title"))
            .AnswerOk("updateTask")
            .AnswerOk("taskDetail", Detail(title: "Plan the other trip"));
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.BeginEditingAsync(TaskDetailViewModel.TitleEditor);
        Assert.Equal("title", view.ActiveEditor);
        view.Title = "Plan the other trip";
        Assert.DoesNotContain("updateTask", core.SentKinds());

        await view.BeginEditingAsync(TaskDetailViewModel.DescriptionEditor);

        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"updateTask\"") && json.Contains("\"title\":\"Plan the other trip\""));
        Assert.Equal("description", view.ActiveEditor);
        Assert.True(view.IsEditingDescription, "the second editor is open");
        Assert.Equal("Plan the other trip", view.Title);
    }

    /// <summary>Ending the open editor commits it; ending a stale one — already handed off — commits nothing.</summary>
    [Fact]
    public async Task Ending_commits_the_open_editor_and_a_stale_end_commits_nothing_task_e71ed760()
    {
        var core = OpenedTask()
            .AnswerOk("beginEditing", Transition("title"))
            .AnswerOk("endEditing", Transition(null, commit: "title"))
            .AnswerOk("updateTask")
            .AnswerOk("taskDetail", Detail(title: "Plan B"))
            .AnswerOk("beginEditing", Transition("description"))
            .AnswerOk("endEditing", Transition("description"));
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.BeginEditingAsync(TaskDetailViewModel.TitleEditor);
        view.Title = "Plan B";
        await view.EndEditingAsync(TaskDetailViewModel.TitleEditor);
        Assert.Null(view.ActiveEditor);
        Assert.Single(core.SentKinds(), kind => kind == "updateTask");

        await view.BeginEditingAsync(TaskDetailViewModel.DescriptionEditor);
        view.Title = "typed after the hand-off";
        await view.EndEditingAsync(TaskDetailViewModel.TitleEditor);

        Assert.Single(core.SentKinds(), kind => kind == "updateTask");
        Assert.Equal("description", view.ActiveEditor);
    }

    /// <summary>Cancel is the only transition that reverts: the buffer goes back, nothing is written.</summary>
    [Fact]
    public async Task Cancel_reverts_the_buffer_and_writes_nothing_task_e71ed760()
    {
        var core = OpenedTask()
            .AnswerOk("beginEditing", Transition("title"))
            .AnswerOk("cancelEditing", Transition(null, cancel: "title"))
            .AnswerOk("beginEditing", Transition("description"))
            .AnswerOk("cancelEditing", Transition(null, cancel: "description"));
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.BeginEditingAsync(TaskDetailViewModel.TitleEditor);
        view.Title = "Plan B";
        await view.CancelEditingAsync(TaskDetailViewModel.TitleEditor);
        Assert.Equal("Plan the trip", view.Title);
        Assert.Null(view.ActiveEditor);

        await view.BeginEditingAsync(TaskDetailViewModel.DescriptionEditor);
        view.Description = "three weeks";
        await view.CancelEditingAsync(TaskDetailViewModel.DescriptionEditor);
        Assert.Equal("two weeks", view.Description);
        Assert.False(view.IsEditingDescription, "back to the drawing");

        Assert.DoesNotContain("updateTask", core.SentKinds());
    }

    /// <summary>Navigating away saves: opening another task commits what was open, on the task it was open on.</summary>
    [Fact]
    public async Task Opening_another_task_commits_what_was_open_task_e71ed760()
    {
        var core = OpenedTask()
            .AnswerOk("beginEditing", Transition("title"))
            .AnswerOk("commitAllEditing", Transition(null, commit: "title"))
            .AnswerOk("updateTask")
            .AnswerOk("taskDetail", Detail(title: "Plan B"))
            .AnswerOk("taskDetail", new
            {
                task = new { id = "t2", title = "Pack", description = "", priority = 0, completed = false },
                fieldOrder = Array.Empty<string>(),
                listChips = Array.Empty<object>(),
                comments = Array.Empty<object>(),
                subtasks = Array.Empty<object>(),
            });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");
        await view.BeginEditingAsync(TaskDetailViewModel.TitleEditor);
        view.Title = "Plan B";

        await view.OpenAsync("t2");

        var update = Assert.Single(core.Sent, json => json.Contains("\"kind\":\"updateTask\""));
        Assert.Contains("\"t1\"", update);
        Assert.Contains("\"title\":\"Plan B\"", update);
        Assert.Equal("t2", view.TaskId);
        Assert.Equal("Pack", view.Title);
        Assert.Null(view.ActiveEditor);
    }

    /// <summary>Closing the pane commits what was open; closing because the task is gone discards it.</summary>
    [Fact]
    public async Task Closing_commits_and_a_discarding_close_cancels_task_e71ed760()
    {
        var core = OpenedTask()
            .AnswerOk("beginEditing", Transition("title"))
            .AnswerOk("commitAllEditing", Transition(null, commit: "title"))
            .AnswerOk("updateTask")
            .AnswerOk("taskDetail", Detail(title: "Plan B"))
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("beginEditing", Transition("title"))
            .AnswerOk("cancelEditing", Transition(null, cancel: "title"));
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");
        await view.BeginEditingAsync(TaskDetailViewModel.TitleEditor);
        view.Title = "Plan B";

        await view.CloseAsync();
        Assert.False(view.IsOpen);
        Assert.Single(core.SentKinds(), kind => kind == "updateTask");

        await view.OpenAsync("t1");
        await view.BeginEditingAsync(TaskDetailViewModel.TitleEditor);
        view.Title = "never saved";
        view.Close();

        Assert.Null(view.ActiveEditor);
        Assert.Contains("cancelEditing", core.SentKinds());
        Assert.Single(core.SentKinds(), kind => kind == "updateTask");
    }

    /// <summary>
    /// Lists and assignee save when the row is picked, so closing them commits nothing — but
    /// opening one is still a begin, which is what commits an open title.
    /// </summary>
    [Fact]
    public async Task A_selection_editor_commits_nothing_when_it_closes_task_e71ed760()
    {
        var core = OpenedTask()
            .AnswerOk("beginEditing", Transition("lists"))
            .AnswerOk("endEditing", Transition(null, commit: "lists"))
            .AnswerOk("beginEditing", Transition("assignee"))
            .AnswerOk("endEditing", Transition(null, commit: "assignee"));
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.BeginEditingAsync(TaskDetailViewModel.ListsEditor);
        await view.EndEditingAsync(TaskDetailViewModel.ListsEditor);
        await view.BeginEditingAsync(TaskDetailViewModel.AssigneeEditor);
        await view.EndEditingAsync(TaskDetailViewModel.AssigneeEditor);

        Assert.DoesNotContain("updateTask", core.SentKinds());
        Assert.Null(view.ActiveEditor);
    }

    /// <summary>
    /// Picking a person finishes the picker (task a7748274).
    /// </summary>
    /// <remarks>
    /// The assignee picker is a plain <c>Flyout</c> of plain <c>Button</c>s, and a button inside a
    /// flyout does not dismiss it the way a <c>MenuFlyoutItem</c> does — so the popup used to sit
    /// over the field it had just changed, which reads as "it did not close and it did not update".
    /// Closing it is the shell's job; knowing that a pick ENDS the edit is a rule, and lives here,
    /// so the code-behind only hides the flyout and dispatches.
    /// </remarks>
    [Fact]
    public async Task Choosing_an_assignee_finishes_the_picker_task_a7748274()
    {
        var core = OpenedTask()
            .AnswerOk("beginEditing", Transition("assignee"))
            .AnswerOk("updateTask")
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("endEditing", Transition(null, commit: "assignee"));
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");
        await view.BeginEditingAsync(TaskDetailViewModel.AssigneeEditor);
        Assert.Equal(TaskDetailViewModel.AssigneeEditor, view.ActiveEditor);

        Assert.True(await view.ChooseAssigneeAsync("u1"));

        Assert.Contains("\"assigneeId\":\"u1\"",
            core.Sent.First(sent => sent.Contains("updateTask")));
        Assert.Null(view.ActiveEditor);
    }
}
