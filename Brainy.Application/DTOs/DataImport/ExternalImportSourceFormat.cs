namespace Brainy.Application.DTOs.DataImport;

/// <summary>
/// The external product/format an <see cref="Brainy.Application.Interfaces.Services.IExternalImportService"/>
/// upload is parsed as. Unlike Brainy's own JSON export, none of these formats are
/// versioned Brainy schemas, so each is parsed on its own terms and never asked to prove
/// a schema version.
/// </summary>
public enum ExternalImportSourceFormat
{
    /// <summary>
    /// An Obsidian vault: a folder of Markdown files (optionally zipped), each with
    /// optional YAML front matter, <c>[[wiki links]]</c> between notes, inline
    /// <c>#hashtags</c>, and an optional shared attachments folder.
    /// </summary>
    ObsidianVault = 0,

    /// <summary>
    /// A Notion "Markdown &amp; CSV" export zip: one Markdown file per page (Notion
    /// appends a hex id to each file/folder name) and one CSV file per database.
    /// </summary>
    NotionExport = 1,

    /// <summary>
    /// A generic folder of Markdown files with no assumed product conventions beyond
    /// optional YAML front matter and inline <c>#hashtags</c>.
    /// </summary>
    MarkdownFolder = 2,

    /// <summary>An Evernote <c>.enex</c> export file (a single XML document).</summary>
    Evernote = 3
}
