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

    public static object Comments(string taskId) => new WithTaskId("comments", taskId);

    public static object CurrentUser() => new KindOnly("currentUser");

    public static object OutboxStats() => new KindOnly("outboxStats");

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

    private sealed record SearchRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("query")] string Query);
}
