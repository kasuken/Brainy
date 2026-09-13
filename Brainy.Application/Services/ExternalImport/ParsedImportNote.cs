namespace Brainy.Application.Services.ExternalImport;

/// <summary>
/// One attachment a <see cref="ParsedImportNote"/> references. <see cref="RawToken"/> is
/// the exact substring found in the note's raw content (e.g. <c>![[image.png]]</c> or
/// <c>![alt](attachments/image.png)</c>); when the attachment is imported, that substring
/// is replaced with Brainy's own <c>![name](/api/note-images/{id})</c> reference so the
/// image renders in the note editor. <see cref="Data"/> is null when the referenced file
/// could not be located or read from the source, in which case the raw token is left
/// untouched and the omission is reported rather than silently dropped.
/// </summary>
internal sealed record ParsedAttachment(
    string RawToken,
    string FileName,
    string? ContentType,
    byte[]? Data);

/// <summary>
/// One note recovered from an external source, in a form the shared import planner can
/// reconcile against Brainy's data model without knowing anything about the source
/// format. <see cref="Key"/> identifies the note within this one import batch only (a
/// file path, a Notion page id, an Evernote note's position, ...) so that
/// <see cref="LinkTargetKeys"/> — links to other notes found in this same batch — can be
/// resolved into <see cref="Brainy.Domain.Entities.NoteRelationship"/> rows.
/// </summary>
internal sealed record ParsedImportNote(
    string Key,
    string Title,
    string Content,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> LinkTargetKeys,
    IReadOnlyList<ParsedAttachment> Attachments);

/// <summary>
/// Everything a source-format parser recovers from one uploaded archive/file:
/// the notes themselves, plus messages about anything the parser understood but could not
/// safely or faithfully bring in (rejected archive entries, unreadable attachments,
/// unmapped rows/sections) — never silently dropped.
/// </summary>
internal sealed record ExternalImportParseResult(
    IReadOnlyList<ParsedImportNote> Notes,
    IReadOnlyList<string> Warnings);
