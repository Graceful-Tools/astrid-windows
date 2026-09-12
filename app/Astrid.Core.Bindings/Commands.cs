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
    /// What a chosen calendar day means for this task, as an instant.
    /// </summary>
    /// <remarks>
    /// Asked rather than computed. A calendar hands back a day; whether that is a date or a
    /// date-and-time depends on the task, and an all-day date written as a local midnight reads
    /// back as the day before for anyone west of UTC. Same reason the quick picks carry instants.
    /// </remarks>
    public static object DueDateOnDay(string taskId, string day) =>
        new DayRequest("dueDateOnDay", taskId, day);

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

    // ── API access ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The client-credentials pairs this account has registered.
    /// </summary>
    /// <remarks>
    /// Read-only, so the panel opens without minting anything. No secret comes back: the server
    /// stores a hash and shows plaintext once, at creation.
    /// </remarks>
    public static object ApiAccess() => new KindOnly("apiAccess");

    /// <summary>
    /// Mint an MCP token for this device, or hand back the one it already has.
    /// </summary>
    /// <remarks>
    /// The server decides which. Asking twice gives the same token rather than a second one, so a
    /// screen that lost its copy can get it back without revoking anything.
    /// </remarks>
    public static object CreateMcpToken() => new KindOnly("createMcpToken");

    /// <summary>Revoke every MCP token minted from a device. All of them: there is no id.</summary>
    public static object RevokeMcpTokens() => new KindOnly("revokeMcpTokens");

    /// <summary>Register a pair. Answers with the secret shown only this once.</summary>
    public static object CreateOAuthClient(string name) =>
        new NamedRequest("createOAuthClient", name);

    /// <summary>Revoke one pair, addressed by its public half.</summary>
    public static object DeleteOAuthClient(string clientId) =>
        new ClientIdRequest("deleteOAuthClient", clientId);

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

    /// <summary>Which look the app wears, and the ones it could.</summary>
    public static object Theme() => new KindOnly("theme");

    /// <summary>Choose a look. Per installation, not per account.</summary>
    public static object SetTheme(string theme) => new ThemeRequest("setTheme", theme);

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

    /// <summary>
    /// Change the signed-in user's name, photo, or both (task 19fd9289). The photo is a file on
    /// this machine; the core uploads it and puts its address on the profile.
    /// </summary>
    public static object UpdateProfile(string? name, string? photoPath) =>
        new ProfileRequest("updateProfile", name, photoPath);

    /// <summary>Send the verification email again.</summary>
    public static object ResendVerification() => new KindOnly("resendVerification");

    /// <summary>The contacts this account imported for collaborator suggestions (task 438494c7).</summary>
    public static object Contacts() => new KindOnly("contacts");

    /// <summary>Remove every imported contact.</summary>
    public static object ClearContacts() => new KindOnly("clearContacts");

    /// <summary>The passkeys the account signs in with (task 19fd9289).</summary>
    public static object Passkeys() => new KindOnly("passkeys");

    /// <summary>Rename a passkey.</summary>
    public static object RenamePasskey(string id, string name) =>
        new PasskeyNameRequest("renamePasskey", id, name);

    /// <summary>Revoke a passkey.</summary>
    public static object RevokePasskey(string id) => new WithId("revokePasskey", id);

    /// <summary>
    /// Delete the account for good. The core refuses anything but the exact phrase, as the server
    /// does, and signs out afterwards.
    /// </summary>
    public static object DeleteAccount(string confirmation) =>
        new DeleteAccountRequest("deleteAccount", confirmation);

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

    /// <summary>
    /// Change the account's task defaults or its task-detail layout (task c0f3db19). One field at
    /// a time, merged in the core, refused there when the server would refuse it.
    /// </summary>
    public static object UpdateSmartTaskSettings(IReadOnlyDictionary<string, object?> changes) =>
        new SettingsRequest("updateSmartTaskSettings", changes);

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
        bool? isAllDay = null, string? assigneeId = null, string? parentTaskId = null,
        bool quickAdd = false, string? locale = null) =>
        new CreateTaskRequest("createTask", title, description, listIds ?? [], priority,
            dueDateTime, isAllDay, assigneeId, parentTaskId, quickAdd, locale);

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

    /// <summary>
    /// Close a task as something other than done — <c>canceled</c>, <c>duplicate</c> or
    /// <c>not_planned</c> — or reopen it with <c>null</c> (task 016ce981). Not a way to complete
    /// one: a canceled close does not roll a repeating task forward, on purpose.
    /// </summary>
    public static object SetClosedReason(string taskId, string? closedReason) =>
        new ClosedReasonRequest("setClosedReason", taskId, closedReason);

    /// <summary>The board columns a task's menu can put it in, and which one it is in now.</summary>
    public static object TaskStatusOptions(string taskId) => new WithTaskId("taskStatusOptions", taskId);

    /// <summary>Put a task in a column from its menu: the same move a dragged card makes.</summary>
    public static object SetTaskStatus(string taskId, string columnId) =>
        new StatusRequest("setTaskStatus", taskId, columnId);

    /// <summary>Mint a link to the task other people can open. Online-only, like the web's.</summary>
    public static object ShareTask(string taskId) => new WithTaskId("shareTask", taskId);

    public static object SetTaskLists(string taskId, IReadOnlyList<string> listIds) =>
        new SetListsRequest("setTaskLists", taskId, listIds);

    /// <summary>The detail's list editor: what a task is in, what it could be added to (task d3f3b111).</summary>
    public static object ListPicks(string taskId, string query) =>
        new ListPicksRequest("listPicks", taskId, query);

    public static object AddTaskToList(string taskId, string listId) =>
        new TaskListRequest("addTaskToList", taskId, listId);

    public static object RemoveTaskFromList(string taskId, string listId) =>
        new TaskListRequest("removeTaskFromList", taskId, listId);

    /// <summary>Make a list and put the task in it. Colour and privacy are the core's to choose.</summary>
    public static object CreateListForTask(string taskId, string name) =>
        new CreateListForTaskRequest("createListForTask", taskId, name);

    /// <summary>A board's columns (task e5214fba), by the list the board was opened from.</summary>
    public static object AddBoardStatus(string listId, string name) =>
        new BoardStatusRequest("addBoardStatus", listId, null, name, null);

    public static object RenameBoardStatus(string listId, string role, string name) =>
        new BoardStatusRequest("renameBoardStatus", listId, role, name, null);

    /// <param name="direction"><c>up</c> or <c>down</c>.</param>
    public static object ReorderBoardStatus(string listId, string role, string direction) =>
        new BoardStatusRequest("reorderBoardStatus", listId, role, null, direction);

    public static object RemoveBoardStatus(string listId, string role) =>
        new BoardStatusRequest("removeBoardStatus", listId, role, null, null);

    /// <summary>The agents and repositories a list's coding-agent settings can be set to (task f44b4a0c).</summary>
    public static object ListAgentOptions(string listId) => new WithListId("listAgentOptions", listId);

    public static object SetTaskStatusRole(string taskId, string? statusRole) =>
        new StatusRoleRequest("setTaskStatusRole", taskId, statusRole);

    public static object CreateList(string name, string? color = null) =>
        new CreateListRequest("createList", name, color);

    public static object UpdateList(string listId, IReadOnlyDictionary<string, object?> changes) =>
        new UpdateRequest("updateList", null, listId, changes);

    public static object DeleteList(string listId) => new WithListId("deleteList", listId);

    /// <summary>Put a picture from this machine on a list (task 3a913e52). Online-only, like the profile photo.</summary>
    public static object SetListImage(string listId, string path) =>
        new ListImageRequest("setListImage", listId, path);

    /// <summary>Where a list's picture can be drawn from: a local path or an address, or null for none.</summary>
    public static object ListImage(string listId) => new WithListId("listImage", listId);

    /// <summary>The public lists anybody may browse and copy, most copied first (task f6bc59e8). Online-only.</summary>
    public static object PublicLists() => new KindOnly("publicLists");

    /// <summary>Copy a public list, with its tasks, into this account. The server makes the copy.</summary>
    public static object CopyList(string listId) => new WithListId("copyList", listId);

    public static object SetListFavorite(string listId, bool favorite) =>
        new FavoriteRequest("setListFavorite", listId, favorite);

    /// <param name="parentCommentId">The comment this one answers (task 97c817dd); null for a top-level one.</param>
    public static object PostComment(string taskId, string content, string? parentCommentId = null) =>
        new PostCommentRequest("postComment", taskId, content, parentCommentId);

    /// <summary>Change what a comment says. Offline through the Outbox, like posting one.</summary>
    public static object EditComment(string commentId, string content) =>
        new EditCommentRequest("editComment", commentId, content);

    /// <summary>What the comment box's popup should show for this text and caret (task 3271a0c5). Positions are UTF-16 units.</summary>
    public static object CommentSuggestions(string taskId, string text, int caret) =>
        new CommentSuggestionsRequest("commentSuggestions", taskId, text, caret);

    /// <summary>Put a chosen row into the text, the way the server will read it back.</summary>
    public static object ApplyCommentSuggestion(string text, int caret, string triggerKind, string id, string label) =>
        new ApplyCommentSuggestionRequest("applyCommentSuggestion", text, caret, triggerKind, id, label);

    public static object DeleteComment(string commentId) =>
        new CommentIdRequest("deleteComment", commentId);

    // ── The network ──────────────────────────────────────────────────────────────────────────

    public static object Sync() => new KindOnly("sync");

    public static object Drain() => new KindOnly("drain");

    public static object RefreshComments(string taskId) => new WithTaskId("refreshComments", taskId);

    public static object SearchUsers(string query) => new SearchRequest("searchUsers", query);

    public static object RefreshCapabilities() => new KindOnly("refreshCapabilities");

    /// <summary>This user's feature flags, from the cache. Null for a flag the server has not been asked about.</summary>
    public static object Features() => new KindOnly("features");

    /// <summary>The global quick-add chord and its parts.</summary>
    public static object Hotkey() => new KindOnly("hotkey");

    /// <summary>Choose another chord. Refused with a reason when the shell could not register it.</summary>
    public static object SetHotkey(string chord) => new ChordRequest("setHotkey", chord);

    /// <summary>
    /// Open an editor in the one editing session, committing and closing whatever was open
    /// (PRODUCT_CONTRACT.md §6). The answer names what to commit or revert.
    /// </summary>
    public static object BeginEditing(string editor) => new EditorRequest("beginEditing", editor);

    /// <summary>Close an editor, committing it. A stale end — one already handed off — is ignored.</summary>
    public static object EndEditing(string editor) => new EditorRequest("endEditing", editor);

    /// <summary>Close an editor, reverting it: the only transition that discards.</summary>
    public static object CancelEditing(string editor) => new EditorRequest("cancelEditing", editor);

    /// <summary>Close the session, committing whatever was open: navigating away and backgrounding save.</summary>
    public static object CommitAllEditing() => new KindOnly("commitAllEditing");

    /// <summary>Copy a task into a list, with or without its comments. The server makes the copy.</summary>
    public static object CopyTask(string taskId, string? targetListId, bool includeComments) =>
        new CopyTaskRequest("copyTask", taskId, targetListId, includeComments);

    /// <summary>The inbox, from the cache.</summary>
    public static object Notifications() => new KindOnly("notifications");

    /// <summary>The inbox, after asking the server.</summary>
    public static object RefreshNotifications() => new KindOnly("refreshNotifications");

    public static object MarkNotificationsRead(IReadOnlyList<string> ids) =>
        new IdsRequest("markNotificationsRead", ids);

    public static object MarkAllNotificationsRead() => new KindOnly("markAllNotificationsRead");

    /// <summary>
    /// The list's settings again, after the server has been asked for the roster. Follows
    /// <see cref="ListMembers"/>, which answers from the cache so the flyout opens at once.
    /// </summary>
    public static object RefreshListMembers(string listId) => new WithListId("refreshListMembers", listId);

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

    private sealed record CopyTaskRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string TaskId,
        [property: JsonPropertyName("targetListId")] string? TargetListId,
        [property: JsonPropertyName("includeComments")] bool IncludeComments);

    private sealed record IdsRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("ids")] IReadOnlyList<string> Ids);

    private sealed record ChordRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("chord")] string Chord);

    private sealed record EditorRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("editor")] string Editor);

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
        [property: JsonPropertyName("parentTaskId")] string? ParentTaskId,
        // True for the quick-add box, whose `#list` tags the core reads when the account has
        // smart parsing on (task 6ac2639a). The shell says where the title came from, not what
        // to do with it.
        [property: JsonPropertyName("quickAdd")] bool QuickAdd,
        // The reader's language tag, for the words the quick-add box reads out of a sentence —
        // "morgen" is tomorrow in German and a plain word in English.
        [property: JsonPropertyName("locale")] string? Locale);

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

    private sealed record ListPicksRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string TaskId,
        [property: JsonPropertyName("query")] string Query);

    private sealed record TaskListRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string TaskId,
        [property: JsonPropertyName("listId")] string ListId);

    private sealed record CreateListForTaskRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string TaskId,
        [property: JsonPropertyName("name")] string Name);

    private sealed record BoardStatusRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("listId")] string ListId,
        [property: JsonPropertyName("role"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Role,
        [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name,
        [property: JsonPropertyName("direction"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Direction);

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
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("parentCommentId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ParentCommentId);

    private sealed record EditCommentRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("commentId")] string CommentId,
        [property: JsonPropertyName("content")] string Content);

    private sealed record CommentSuggestionsRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string TaskId,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("caret")] int Caret);

    private sealed record ApplyCommentSuggestionRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("caret")] int Caret,
        [property: JsonPropertyName("triggerKind")] string TriggerKind,
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("label")] string Label);

    private sealed record ThemeRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("theme")] string Theme);

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

    /// <summary>A calendar day, as <c>YYYY-MM-DD</c>.</summary>
    private sealed record DayRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string TaskId,
        [property: JsonPropertyName("day")] string Day);

    private sealed record NamedRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("name")] string Name);

    /// <summary>A pair is addressed by its public half — the route matches nothing else.</summary>
    private sealed record ClientIdRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("clientId")] string ClientId);

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

    private sealed record ListImageRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("listId")] string ListId,
        [property: JsonPropertyName("path")] string Path);

    private sealed record ProfileRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("photoPath")] string? PhotoPath);

    private sealed record DeleteAccountRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("confirmation")] string Confirmation);

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

    private sealed record WithId(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("id")] string Id);

    private sealed record PasskeyNameRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name);

    private sealed record ClosedReasonRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string TaskId,
        [property: JsonPropertyName("closedReason")] string? ClosedReason);

    private sealed record StatusRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("taskId")] string TaskId,
        [property: JsonPropertyName("columnId")] string ColumnId);

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
