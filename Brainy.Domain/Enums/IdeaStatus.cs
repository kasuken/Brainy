namespace Brainy.Domain.Enums;

/// <summary>
/// Lifecycle state of an <see cref="Entities.Idea"/>, tracking its journey through the
/// recommended idea workflow: capture, evaluation, incubation, and a conscious decision
/// to commit, reject, or (once shipped) close out.
/// </summary>
public enum IdeaStatus
{
    /// <summary>Newly captured; not yet evaluated.</summary>
    Captured = 0,

    /// <summary>Actively being assessed — research, competitors, and framing are being explored.</summary>
    Evaluating = 1,

    /// <summary>Deliberately left to develop over time without active pressure to decide.</summary>
    Incubated = 2,

    /// <summary>
    /// Accepted after satisfying all commitment criteria (specific user and problem, suitability
    /// reason, evidence, a validation experiment, and a conscious replacement decision).
    /// A project has been created; the idea retains only a link and its decision record.
    /// </summary>
    Committed = 3,

    /// <summary>Evaluated and deliberately not pursued.</summary>
    Rejected = 4,

    /// <summary>The committed project reached completion and the idea has shipped.</summary>
    Shipped = 5
}
