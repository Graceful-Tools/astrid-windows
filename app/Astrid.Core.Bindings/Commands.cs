using System.Text.Json.Serialization;

namespace Astrid.Core.Bindings;

/// <summary>
/// The commands the core understands, as C# records.
/// </summary>
/// <remarks>
/// <para>
/// A thin, typed front for the JSON in <c>astrid_core::app::Command</c>. Typed rather than raw
/// strings so a misspelled field is a compile error here instead of a "bad request" the user sees
/// as a button that does nothing.
/// </para>
/// <para>
/// Not exhaustive, and not meant to be: a command with no caller yet has no record. The core is
/// the list of what exists; this is the list of what the shell uses.
/// </para>
/// </remarks>
public static class Commands
{
    // ── Reads ────────────────────────────────────────────────────────────────────────────────

    public static object Lists() => new KindOnly("lists");

    public static object List(string listId) => new WithListId("list", listId);

    /// <summary>
    /// The rows to draw for a list — filtered, sorted, subtask-spliced and projected by the core.
    /// </summary>
    /// <param name="offset">Where the window starts. Zero for the top.</param>
    /// <param name="limit">
    /// How many rows. Pass the number visible plus a screenful, not the whole list: a list of ten
    /// thousand should cross the boundary as the fifty on screen.
    /// </param>
    public static object RowsForList(string listId, int offset = 0, int? limit = null,
        string? displayMode = null, string? surface = null) =>
        new RowsRequest("rowsForList", listId, displayMode, surface, offset, limit);

    public static object Task(string taskId) => new WithTaskId("task", taskId);

    /// <summary>
    /// Everything one task's detail screen needs, including the order to lay its fields out in.
    /// </summary>
    /// <remarks>
    /// One command rather than five. The field order — Who, Date, Priority, Lists — is a product
    /// decision shared with web and both Apple clients, and a shell that assembled the screen
    /// itself would be the fifth place to get it wrong.
    /// </remarks>
    public static object TaskDetail(string taskId, string? displayMode = null) =>
        new DetailRequest("taskDetail", taskId, displayMode);

    public static object Comments(string taskId) => new WithTaskId("comments", taskId);

    /// <summary>
    /// The quick date and time choices for a task, with the instant each one means.
    /// </summary>
    /// <remarks>
    /// The shell shows a label and sends back a value it did not have to compute. The arithmetic
    /// is where this goes wrong — a day is 23 or 25 hours across a daylight-saving boundary, an
    /// all-day date is stored differently from a timed one, and "morning" means 09:00 where the
    /// reader is — so it is done once, in the core, for all three clients.
    /// </remarks>
    public static object DueDateOptions(string taskId) =>
        new WithTaskId("dueDateOptions", taskId);

    /// <summary>
    /// Who a task can be assigned to, in the order the picker shows them.
    /// </summary>
    /// <remarks>
    /// One rule for every surface. On iOS the picker built its option list inside the view, so
    /// "who can this be assigned to" depended on what the surrounding screen had loaded, and the
    /// board could not offer an AI agent at all. Assigning is an ordinary update carrying an
    /// <c>assigneeId</c>; this is only the question of who may be offered.
    /// </remarks>
    /// <summary>The Agent Hub: the agents, their modes, the credentials, and Copilot.</summary>
    /// <remarks>
    /// One command, like the external-sync panel: a screen that drew the agents before it knew
    /// which had a credential would show "needs setup" on all of them and then correct itself.
    /// </remarks>
    public static object Agents() => new KindOnly("agents");

    /// <summary>What is on the clipboard, and which of it was meant to be attached.</summary>
    /// <remarks>
    /// Reading the clipboard is the shell's job; deciding what it means is not — files beat a
    /// rendition of them, a screenshot needs a name, and a text-only board is left to type.
    /// </remarks>
    public static object ClipboardPaste(IReadOnlyList<string> files, string? imageExtension,
        bool hasText) =>
        new PasteRequest("clipboardPaste", files, imageExtension, hasText);

    /// <summary>Where an account's own agent is told about work.</summary>
    public static object WebhookSettings() => new KindOnly("webhookSettings");

    /// <summary>Save where deliveries go, and what is delivered.</summary>
    /// <remarks>
    /// <paramref name="regenerateSecret"/> asks for a new signing secret. The server answers with
    /// it once and never again, so whatever shows it has one chance to.
    /// </remarks>
    public static object SaveWebhook(string url, bool enabled, IReadOnlyList<string> events,
        IReadOnlyList<string> agents, bool regenerateSecret = false) =>
        new WebhookRequest("saveWebhook", url, enabled, events, agents, regenerateSecret);

    public static object DeleteWebhook() => new KindOnly("deleteWebhook");

    /// <summary>Fire a test delivery, which is the only way to know the wiring works.</summary>
    public static object TestWebhook() => new KindOnly("testWebhook");

    /// <summary>The agents this account has registered of its own.</summary>
    public static object CustomAgents() => new KindOnly("customAgents");

    /// <summary>Register one. Answers with credentials shown only this once.</summary>
    public static object RegisterCustomAgent(string name, IReadOnlyList<string>? listIds = null) =>
        new RegisterAgentRequest("registerCustomAgent", name, listIds);

    public static object DeleteCustomAgent(string agentId) =>
        new AgentIdRequest("deleteCustomAgent", agentId);

    /// <summary>Start connecting Copilot. Answers with the URL a browser should open.</summary>
    public static object ConnectCopilot() => new KindOnly("connectCopilot");

    public static object DisconnectCopilot() => new KindOnly("disconnectCopilot");

    /// <summary>Change how one agent runs.</summary>
    public static object SetAgentMode(string agent, string mode) =>
        new AgentModeRequest("setAgentMode", agent, mode);

    /// <summary>Store a key for one service. Straight to the server; never cached here.</summary>
    public static object SaveAgentCredential(string serviceId, string key) =>
        new CredentialRequest("saveAgentCredential", serviceId, key);

    /// <summary>Ask the server whether a stored key works.</summary>
    public static object TestAgentCredential(string serviceId) =>
        new ServiceRequest("testAgentCredential", serviceId);

    public static object DeleteAgentCredential(string serviceId) =>
        new ServiceRequest("deleteAgentCredential", serviceId);

    /// <summary>
    /// What a list's external sync looks like: what is connected, what it could be linked to, and
    /// what it is linked to.
    /// </summary>
    /// <remarks>
    /// One command rather than three: the panel needs all of it before it can draw a single row,
    /// and three round trips to fill one panel is three chances to show it half done.
    /// </remarks>
    public static object ExternalSync(string listId) =>
        new WithListId("externalSync", listId);

    /// <summary>The URL to open in a browser to connect a provider.</summary>
    public static object ConnectProvider(string provider) =>
        new ProviderRequest("connectProvider", provider);

    public static object DisconnectProvider(string provider) =>
        new ProviderRequest("disconnectProvider", provider);

    /// <summary>Mirror a list to a container on the other side.</summary>
    public static object LinkList(string provider, string listId, string containerId) =>
        new LinkRequest("linkList", provider, listId, containerId);

    public static object UnlinkList(string provider, string linkId) =>
        new UnlinkRequest("unlinkList", provider, linkId);

    /// <summary>The My Tasks entry for the sidebar: the view the app opens on.</summary>
    public static object MyTasksList() => new KindOnly("myTasksList");

    /// <summary>Fetch My Tasks' filters from the account.</summary>
    public static object RefreshMyTasks() => new KindOnly("refreshMyTasks");

    /// <summary>Set one filter on one list.</summary>
    /// <remarks>
    /// Not an <c>updateList</c> from here: where a filter is written depends on what is being
    /// filtered — a list's go on the list, My Tasks' go on the account — and that is a decision.
    /// </remarks>
    public static object SetFilter(string listId, string field, string value) =>
        new FilterRequest("setFilter", listId, field, value);

    /// <summary>How Google lists get linked, and what a list made here is called.</summary>
    public static object GoogleSyncMode() => new KindOnly("googleSyncMode");

    /// <summary>Choose how Google lists get linked.</summary>
    /// <remarks>
    /// The choice is the account's rather than this machine's, so somebody who turns on "every
    /// list" at a desk finds it on their laptop too.
    /// </remarks>
    public static object SetGoogleSyncMode(string mode) =>
        new SyncModeRequest("setGoogleSyncMode", mode);

    /// <summary>
    /// Run one Google pass over every linked list.
    /// </summary>
    /// <remarks>
    /// GitHub needs no equivalent: a cron on the server does that one, so a list linked here syncs
    /// whether or not this app is running.
    /// </remarks>
    public static object SyncExternal() => new KindOnly("syncExternal");

    /// <summary>Whether the first-run tour has been seen on this machine.</summary>
    /// <remarks>
    /// Per installation rather than per account: it is about where this app's hotkey is and what
    /// its palette does, and somebody who has used Astrid for a year on a laptop still wants to be
    /// told that the first time they open it on a desktop.
    /// </remarks>
    public static object HasSeenTour() => new KindOnly("hasSeenTour");

    /// <summary>Remember that the tour has been seen.</summary>
    public static object TourSeen() => new KindOnly("tourSeen");

    /// <summary>Everything one box can find: commands, lists, tasks, ranked.</summary>
    /// <remarks>
    /// The matcher is the Mac's, character for character. The ranking is what a palette is, and
    /// two clients that rank differently are two products — typing the same three letters has to
    /// put the same row first.
    /// </remarks>
    public static object Palette(string query) => new PaletteRequest("palette", query);

    /// <summary>The three numbers on this account's profile.</summary>
    public static object ProfileStats() => new KindOnly("profileStats");

    /// <summary>Write everything this account has to a file on this machine.</summary>
    /// <remarks>
    /// Straight to a path rather than back across the boundary: an export is somebody's entire
    /// history, and carrying megabytes through JSON to hand them to a save dialog would be work
    /// for its own sake.
    /// </remarks>
    public static object ExportAccount(string format, string path) =>
        new ExportRequest("exportAccount", format, path);

    /// <summary>The account and its reminder settings, from the cache.</summary>
    public static object Settings() => new KindOnly("settings");

    /// <summary>Fetch the account and its settings from the server.</summary>
    public static object RefreshSettings() => new KindOnly("refreshSettings");

    /// <summary>
    /// Change the reminder settings.
    /// </summary>
    /// <remarks>
    /// The fields are the server's — <c>enablePushReminders</c>, <c>dailyDigestTime</c> — and they
    /// are merged into what is stored, so one toggle does not clear everything else this account
    /// has chosen, possibly on another client.
    /// </remarks>
    public static object UpdateReminderSettings(IReadOnlyDictionary<string, object?> changes) =>
        new SettingsRequest("updateReminderSettings", changes);

    /// <summary>Start timing a task.</summary>
    /// <remarks>
    /// The start time is kept in the cache rather than in memory, so a timer survives a restart —
    /// on Apple it does not, and a timer left running an hour ago is simply lost.
    /// </remarks>
    public static object StartTimer(string taskId) => new WithTaskId("startTimer", taskId);

    /// <summary>Stop timing, and record what the session was worth.</summary>
    public static object StopTimer(string taskId) => new WithTaskId("stopTimer", taskId);

    /// <summary>
    /// The files on a task: its own, and its comments'.
    /// </summary>
    /// <remarks>
    /// There is no attach-to-task endpoint anywhere. A file reaches a task by being uploaded and
    /// then named by a comment, which is why the Mac's attachments section was empty on nearly
    /// every task until it started gathering both.
    /// </remarks>
    public static object Attachments(string taskId) =>
        new WithTaskId("attachments", taskId);

    /// <summary>Fetch a file's bytes; answers with the path they were written to.</summary>
    public static object DownloadAttachment(string taskId, string fileId) =>
        new DownloadRequest("downloadAttachment", taskId, fileId);

    /// <summary>Attach a file from this machine, and post the comment that carries it.</summary>
    /// <remarks>
    /// Works offline, like every other write here: the bytes are copied into a pending directory
    /// and the journal row names the copy, so the file survives the original being moved away.
    /// </remarks>
    public static object AttachFile(string taskId, string path, string? content = null) =>
        new AttachRequest("attachFile", taskId, path, content);

    /// <summary>
    /// What a list is filtered and sorted by, and what else it could be.
    /// </summary>
    /// <remarks>
    /// Setting one is an ordinary <see cref="UpdateList"/> carrying the field the group names.
    /// The values are the ones the core's rules match on — they are saved on the list and read by
    /// every client, so one spelled differently would be a filter the others keep and this one
    /// silently ignores.
    /// </remarks>
    public static object FilterOptions(string listId) =>
        new WithListId("filterOptions", listId);

    /// <summary>A list's conversation, from the cache.</summary>
    public static object Chat(string listId) => new WithListId("chat", listId);

    /// <summary>Catch the conversation up with the server.</summary>
    /// <remarks>
    /// Separate from <see cref="Chat"/> so a panel draws instantly from what is already on this
    /// machine and catches up afterwards — the same order the task list uses.
    /// </remarks>
    public static object RefreshChat(string listId) => new WithListId("refreshChat", listId);

    /// <summary>Say something. In the transcript before this returns.</summary>
    public static object SendChatMessage(string channelId, string content, string? replyToId = null) =>
        new SendMessageRequest("sendChatMessage", channelId, content, replyToId);

    /// <summary>
    /// Who a list is shared with, and what this account may do about it.
    /// </summary>
    /// <remarks>
    /// Reaches the network. Membership is not a local fact: a cached member list a day old is how
    /// somebody removed last week is still offered a role.
    /// </remarks>
    public static object ListMembers(string listId) =>
        new WithListId("listMembers", listId);

    /// <summary>Invite somebody by email.</summary>
    public static object InviteToList(string listId, string email, string role) =>
        new InviteRequest("inviteToList", listId, email, role);

    public static object SetMemberRole(string listId, string userId, string role) =>
        new MemberRoleRequest("setMemberRole", listId, userId, role);

    public static object RemoveMember(string listId, string userId) =>
        new MemberRequest("removeMember", listId, userId);

    /// <summary>Leave a list somebody else owns.</summary>
    public static object LeaveList(string listId) => new WithListId("leaveList", listId);

    /// <summary>
    /// The board a list belongs to: its columns, and the cards in each.
    /// </summary>
    /// <remarks>
    /// Cards come back as rows, so a card draws with the same converters a list row does. Which
    /// columns a board has and which one a card is in is decided in the core and locked against
    /// astrid-web's own implementation — a card in the wrong column looks like somebody moved it,
    /// and a card in no column looks like it was deleted.
    /// </remarks>
    public static object Board(string listId, int? limit = null) =>
        new BoardRequest("board", listId, limit);

    /// <summary>Move a card to a column.</summary>
    public static object MoveTaskToColumn(string taskId, string columnId, string listId) =>
        new MoveRequest("moveTaskToColumn", taskId, columnId, listId);

    /// <summary>
    /// When to be reminded about one task, and the instant each choice means.
    /// </summary>
    /// <remarks>
    /// Offsets from the due time rather than a clock: "an hour before" is what somebody means, and
    /// an hour before a time is not the same wall-clock answer across a daylight-saving boundary.
    /// </remarks>
    public static object ReminderOptions(string taskId) =>
        new WithTaskId("reminderOptions", taskId);

    /// <summary>
    /// Reminders that have come due and have not been shown yet.
    /// </summary>
    /// <remarks>
    /// The server owns push and email — it knows about quiet hours, digests, and every device
    /// somebody owns. This is only what a running client can notice: that a reminder's time has
    /// arrived for the app open in front of them.
    /// </remarks>
    public static object RemindersDue() => new KindOnly("remindersDue");

    /// <summary>Remember that a reminder was shown, so it is not shown twice.</summary>
    public static object ReminderShown(string taskId) =>
        new WithTaskId("reminderShown", taskId);

    /// <summary>Move a reminder forward.</summary>
    /// <remarks>
    /// A write, not a timer: an in-memory snooze is lost on a restart, and it would leave the
    /// server's copy where it was, so the push still arrives at the original time.
    /// </remarks>
    public static object SnoozeReminder(string taskId, int minutes) =>
        new SnoozeRequest("snoozeReminder", taskId, minutes);

    /// <summary>
    /// The repeat presets, and how this task's repeat describes itself.
    /// </summary>
    /// <remarks>
    /// The summary comes back as parts carrying resource keys rather than a finished sentence: a
    /// sentence built by joining fragments is exactly what does not survive translation, where the
    /// order of "every 2 weeks" and "on Mondays" is not the English order.
    /// </remarks>
    public static object RepeatOptions(string taskId) =>
        new WithTaskId("repeatOptions", taskId);

    public static object AssigneeOptions(string taskId) =>
        new WithTaskId("assigneeOptions", taskId);

    public static object CurrentUser() => new KindOnly("currentUser");

    /// <summary>
    /// Find a task by what it says.
    /// </summary>
    /// <remarks>
    /// Over the cache: there is no server search endpoint, and every client matches over what it
    /// already has. Instant, and it works on a train.
    /// </remarks>
    public static object SearchTasks(string query, string? listId = null,
        bool? includeCompleted = null, int? limit = null) =>
        new SearchTasksRequest("searchTasks", query, listId, includeCompleted, limit);

    /// <summary>Whether there is a stored session. Not whether the server still accepts it.</summary>
    public static object IsSignedIn() => new KindOnly("isSignedIn");

    public static object OutboxStats() => new KindOnly("outboxStats");

    /// <summary>
    /// What a pressed key means, given what is on screen.
    /// </summary>
    /// <remarks>
    /// The shell asks rather than knowing: the bare-key scheme is a cross-platform contract, and
    /// so is the guard about when a key may fire at all. Pure — no cache, no network — so it is
    /// safe to ask while a key is being handled.
    /// </remarks>
    public static object ResolveShortcut(string key, bool hasSelection = false,
        bool isTextFieldFocused = false, bool isModalPresented = false) =>
        new ShortcutRequest("resolveShortcut", key, hasSelection, isTextFieldFocused,
            isModalPresented);

    /// <summary>The whole scheme, for a shortcuts sheet.</summary>
    public static object Shortcuts() => new KindOnly("shortcuts");

    // ── Writes ───────────────────────────────────────────────────────────────────────────────

    public static object CreateTask(string title, IReadOnlyList<string>? listIds = null,
        string? description = null, int? priority = null, string? dueDateTime = null,
        bool? isAllDay = null, string? assigneeId = null, string? parentTaskId = null) =>
        new CreateTaskRequest("createTask", title, description, listIds ?? [], priority,
            dueDateTime, isAllDay, assigneeId, parentTaskId);

    /// <summary>
    /// Edit a task. <paramref name="changes"/> carries only what changed; a property present and
    /// null clears the field, and one that is absent leaves it alone.
    /// </summary>
    /// <remarks>
    /// <b>Cannot complete a task.</b> The core refuses a <c>completed</c> field here, because that
    /// path skips the repeat rollover. Use <see cref="CompleteTask"/>.
    /// </remarks>
    public static object UpdateTask(string taskId, IReadOnlyDictionary<string, object?> changes) =>
        new UpdateRequest("updateTask", taskId, null, changes);

    /// <summary>Complete or un-complete a task. The only way to do either.</summary>
    public static object CompleteTask(string taskId, bool completed) =>
        new CompleteRequest("completeTask", taskId, completed);

    public static object DeleteTask(string taskId) => new WithTaskId("deleteTask", taskId);

    public static object SetTaskLists(string taskId, IReadOnlyList<string> listIds) =>
        new SetListsRequest("setTaskLists", taskId, listIds);

    public static object SetTaskStatusRole(string taskId, string? statusRole) =>
        new StatusRoleRequest("setTaskStatusRole", taskId, statusRole);

    public static object CreateList(string name, string? color = null) =>
        new CreateListRequest("createList", name, color);

    public static object UpdateList(string listId, IReadOnlyDictionary<string, object?> changes) =>
        new UpdateRequest("updateList", null, listId, changes);

    public static object DeleteList(string listId) => new WithListId("deleteList", listId);

    public static object SetListFavorite(string listId, bool favorite) =>
        new FavoriteRequest("setListFavorite", listId, favorite);

    public static object PostComment(string taskId, string content) =>
        new PostCommentRequest("postComment", taskId, content);

    public static object DeleteComment(string commentId) =>
        new CommentIdRequest("deleteComment", commentId);

    // ── The network ──────────────────────────────────────────────────────────────────────────

    public static object Sync() => new KindOnly("sync");

    public static object Drain() => new KindOnly("drain");

    public static object RefreshComments(string taskId) => new WithTaskId("refreshComments", taskId);

    public static object SearchUsers(string query) => new SearchRequest("searchUsers", query);

    public static object RefreshCapabilities() => new KindOnly("refreshCapabilities");

    /// <summary>Start signing in. Answers with the URL to open in the browser.</summary>
    public static object BeginSignIn() => new KindOnly("beginSignIn");

    /// <summary>Finish signing in, from the URL Windows activated the app with.</summary>
    public static object CompleteSignIn(string callbackUrl) =>
        new CallbackRequest("completeSignIn", callbackUrl);

    public static object CancelSignIn() => new KindOnly("cancelSignIn");

    public static object SignOut() => new KindOnly("signOut");

    // The shapes. One per distinct field set rather than one per command, so a new command that
    // takes a task id needs no new type.
    private sealed record KindOnly([property: JsonPropertyName("kind")] string Kind);

    private sealed record WithTaskId(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string TaskId);

    private sealed record WithListId(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("listId")] string ListId);

    private sealed record CommentIdRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("commentId")] string CommentId);

    private sealed record RowsRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("listId")] string ListId,
        [property: JsonPropertyName("displayMode")] string? DisplayMode,
        [property: JsonPropertyName("surface")] string? Surface,
        [property: JsonPropertyName("offset")] int Offset,
        [property: JsonPropertyName("limit")] int? Limit);

    private sealed record CreateTaskRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("listIds")] IReadOnlyList<string> ListIds,
        [property: JsonPropertyName("priority")] int? Priority,
        [property: JsonPropertyName("dueDateTime")] string? DueDateTime,
        [property: JsonPropertyName("isAllDay")] bool? IsAllDay,
        [property: JsonPropertyName("assigneeId")] string? AssigneeId,
        [property: JsonPropertyName("parentTaskId")] string? ParentTaskId);

    private sealed record UpdateRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string? TaskId,
        [property: JsonPropertyName("listId")] string? ListId,
        [property: JsonPropertyName("changes")] IReadOnlyDictionary<string, object?> Changes);

    private sealed record CompleteRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string TaskId,
        [property: JsonPropertyName("completed")] bool Completed);

    private sealed record SetListsRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string TaskId,
        [property: JsonPropertyName("listIds")] IReadOnlyList<string> ListIds);

    private sealed record StatusRoleRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string TaskId,
        [property: JsonPropertyName("statusRole")] string? StatusRole);

    private sealed record CreateListRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("color")] string? Color);

    private sealed record FavoriteRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("listId")] string ListId,
        [property: JsonPropertyName("favorite")] bool Favorite);

    private sealed record PostCommentRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string TaskId,
        [property: JsonPropertyName("content")] string Content);

    private sealed record PasteRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("files")] IReadOnlyList<string> Files,
        [property: JsonPropertyName("imageExtension"),
                   JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? ImageExtension,
        [property: JsonPropertyName("hasText")] bool HasText);

    private sealed record WebhookRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("events")] IReadOnlyList<string> Events,
        [property: JsonPropertyName("agents")] IReadOnlyList<string> Agents,
        [property: JsonPropertyName("regenerateSecret")] bool RegenerateSecret);

    private sealed record RegisterAgentRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("name")] string Name,
        // Absent rather than empty when nothing is chosen: absent means the account's lists, and
        // an empty array would mean none of them.
        [property: JsonPropertyName("listIds"),
                   JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? ListIds);

    private sealed record AgentIdRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("agentId")] string AgentId);

    private sealed record AgentModeRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("agent")] string Agent,
        [property: JsonPropertyName("mode")] string Mode);

    private sealed record CredentialRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("serviceId")] string ServiceId,
        [property: JsonPropertyName("key")] string Key);

    private sealed record ServiceRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("serviceId")] string ServiceId);

    private sealed record ProviderRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("provider")] string Provider);

    private sealed record LinkRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("listId")] string ListId,
        [property: JsonPropertyName("containerId")] string ContainerId);

    private sealed record FilterRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("listId")] string ListId,
        [property: JsonPropertyName("field")] string Field,
        [property: JsonPropertyName("value")] string Value);

    private sealed record SyncModeRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("mode")] string Mode);

    private sealed record UnlinkRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("linkId")] string LinkId);

    private sealed record PaletteRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("query")] string Query);

    private sealed record ExportRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("path")] string Path);

    private sealed record SettingsRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("changes")] IReadOnlyDictionary<string, object?> Changes);

    private sealed record DownloadRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string TaskId,
        [property: JsonPropertyName("fileId")] string FileId);

    private sealed record AttachRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string TaskId,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("content")] string? Content);

    private sealed record SendMessageRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("channelId")] string ChannelId,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("replyToId")] string? ReplyToId);

    private sealed record InviteRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("listId")] string ListId,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("role")] string Role);

    private sealed record MemberRoleRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("listId")] string ListId,
        [property: JsonPropertyName("userId")] string UserId,
        [property: JsonPropertyName("role")] string Role);

    private sealed record MemberRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("listId")] string ListId,
        [property: JsonPropertyName("userId")] string UserId);

    private sealed record BoardRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("listId")] string ListId,
        [property: JsonPropertyName("limit")] int? Limit);

    private sealed record MoveRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string TaskId,
        [property: JsonPropertyName("columnId")] string ColumnId,
        [property: JsonPropertyName("listId")] string ListId);

    private sealed record SnoozeRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string TaskId,
        [property: JsonPropertyName("minutes")] int Minutes);

    private sealed record SearchTasksRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("listId")] string? ListId,
        [property: JsonPropertyName("includeCompleted")] bool? IncludeCompleted,
        [property: JsonPropertyName("limit")] int? Limit);

    private sealed record DetailRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string TaskId,
        [property: JsonPropertyName("displayMode")] string? DisplayMode);

    private sealed record ShortcutRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("hasSelection")] bool HasSelection,
        [property: JsonPropertyName("isTextFieldFocused")] bool IsTextFieldFocused,
        [property: JsonPropertyName("isModalPresented")] bool IsModalPresented);

    private sealed record CallbackRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("callbackUrl")] string CallbackUrl);

    private sealed record SearchRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("query")] string Query);
}
