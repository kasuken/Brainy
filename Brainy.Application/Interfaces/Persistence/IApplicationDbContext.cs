using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Interfaces.Persistence;

/// <summary>
/// Abstraction over <c>BrainyDbContext</c> exposed to the Application layer.
/// Keeps the Application project free of EF Core infrastructure concerns while
/// still allowing LINQ queries via <see cref="DbSet{TEntity}"/>.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Area> Areas { get; }
    DbSet<Project> Projects { get; }
    DbSet<Resource> Resources { get; }
    DbSet<Source> Sources { get; }
    DbSet<Note> Notes { get; }
    DbSet<Tag> Tags { get; }
    DbSet<Highlight> Highlights { get; }
    DbSet<Summary> Summaries { get; }
    DbSet<ActionItem> ActionItems { get; }
    DbSet<NoteRelationship> NoteRelationships { get; }
    DbSet<TaskItem> Tasks { get; }
    DbSet<Output> Outputs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
