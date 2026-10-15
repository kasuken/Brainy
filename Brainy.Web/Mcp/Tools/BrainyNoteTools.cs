using System.ComponentModel;
using Brainy.Application.DTOs.Notes;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Enums;
using ModelContextProtocol.Server;

namespace Brainy.Web.Mcp.Tools;

/// <summary>MCP tools for reading and maintaining the signed-in user's notes.</summary>
[McpServerToolType]
internal sealed class BrainyNoteTools
{
    [McpServerTool(Name = "get_note")]
    [Description("Get a note by id, including its full content. Use search to find the note first. " +
                 "Returns null if the note does not exist or is not owned by the signed-in user.")]
    public static async Task<McpNote?> GetNoteAsync(
        INoteService noteService,
        [Description("The note's id, such as one returned by search.")] Guid noteId,
        CancellationToken cancellationToken = default)
    {
        var note = await noteService.GetByIdAsync(noteId, cancellationToken).ConfigureAwait(false);
        return note is null ? null : McpNote.From(note);
    }

    [McpServerTool(Name = "create_note")]
    [Description("Capture a new note in the signed-in user's Brainy account. Notes are added to " +
                 "the Inbox by default so they can be organized later.")]
    public static async Task<McpNote> CreateNoteAsync(
        INoteService noteService,
        [Description("The note title.")] string title,
        [Description("The complete note content. Defaults to empty.")] string content = "",
        [Description("Optional PARA category: Project, Area, Resource, or Archive. Defaults to Project.")] string? paraCategory = null,
        [Description("Optional id of a project owned by the signed-in user.")] Guid? projectId = null,
        [Description("Optional id of an area owned by the signed-in user.")] Guid? areaId = null,
        [Description("Optional id of a resource owned by the signed-in user.")] Guid? resourceId = null,
        [Description("Optional source URL for this note.")] string? sourceUrl = null,
        [Description("Optional title for the source URL.")] string? sourceTitle = null,
        [Description("Optional tag names to apply to the note.")] string[]? tags = null,
        CancellationToken cancellationToken = default)
    {
        var dto = new CreateNoteDto(
            Title: title,
            Content: content,
            ParaCategory: McpToolHelpers.ParseEnum<ParaCategory>(paraCategory, nameof(paraCategory)) ?? ParaCategory.Project,
            ProjectId: projectId,
            AreaId: areaId,
            ResourceId: resourceId,
            Status: NoteStatus.Inbox,
            SourceUrl: sourceUrl,
            SourceTitle: sourceTitle,
            Tags: tags);

        var created = await noteService.CreateAsync(dto, cancellationToken).ConfigureAwait(false);
        return McpNote.From(created);
    }

    [McpServerTool(Name = "update_note")]
    [Description("Update the supplied fields of an existing note. Omitted fields remain unchanged. " +
                 "Set tags to an empty array to remove all tags. Set sourceUrl to an empty string to clear it.")]
    public static async Task<McpNote> UpdateNoteAsync(
        INoteService noteService,
        [Description("The id of the note to update.")] Guid noteId,
        [Description("New title, if changing it.")] string? title = null,
        [Description("New full content, if changing it. An empty string clears the content.")] string? content = null,
        [Description("New status: Inbox, Active, or Archived.")] string? status = null,
        [Description("New PARA category: Project, Area, Resource, or Archive.")] string? paraCategory = null,
        [Description("Replacement tags. Omit to keep current tags; pass an empty array to remove all.")] string[]? tags = null,
        [Description("New source URL. Omit to keep it; pass an empty string to clear it.")] string? sourceUrl = null,
        [Description("New title for the source URL; only used when sourceUrl is supplied.")] string? sourceTitle = null,
        CancellationToken cancellationToken = default)
    {
        if (title is null && content is null && status is null && paraCategory is null && tags is null && sourceUrl is null)
            throw new ArgumentException("Provide at least one note field to update.", nameof(noteId));

        var existing = await noteService.GetByIdAsync(noteId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No note with id {noteId} was found for the current user.");

        var dto = new UpdateNoteDto(
            Id: existing.Id,
            Title: title ?? existing.Title,
            Content: content ?? existing.Content,
            AiSummary: existing.AiSummary,
            Status: McpToolHelpers.ParseEnum<NoteStatus>(status, nameof(status)) ?? existing.Status,
            ParaCategory: McpToolHelpers.ParseEnum<ParaCategory>(paraCategory, nameof(paraCategory)) ?? existing.ParaCategory,
            ProjectId: existing.ProjectId,
            AreaId: existing.AreaId,
            ResourceId: existing.ResourceId,
            SourceUrl: sourceUrl,
            SourceTitle: sourceTitle,
            RowVersion: existing.RowVersion,
            Tags: tags);

        var updated = await noteService.UpdateAsync(dto, cancellationToken).ConfigureAwait(false);
        return McpNote.From(updated);
    }
}

/// <summary>A full note projection returned by MCP read and write tools.</summary>
public record McpNote(
    Guid Id,
    string Title,
    string Content,
    [property: Description("AI-generated summary, when one exists.")] string? AiSummary,
    string Status,
    bool IsArchived,
    string ParaCategory,
    Guid? SourceId,
    string? SourceUrl,
    string? SourceTitle,
    Guid? ProjectId,
    Guid? AreaId,
    Guid? ResourceId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    bool IsFavorite,
    bool HasImages,
    IReadOnlyList<string> Tags)
{
    public static McpNote From(NoteDto note) => new(
        note.Id,
        note.Title,
        note.Content,
        note.AiSummary,
        note.Status.ToString(),
        note.IsArchived,
        note.ParaCategory.ToString(),
        note.SourceId,
        note.SourceUrl,
        note.SourceTitle,
        note.ProjectId,
        note.AreaId,
        note.ResourceId,
        note.CreatedAtUtc,
        note.UpdatedAtUtc,
        note.IsFavorite,
        note.HasImages,
        note.Tags ?? Array.Empty<string>());
}