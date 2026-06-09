namespace Brainy.Domain.Enums;

/// <summary>
/// The nature of a relationship between two notes.
/// </summary>
public enum RelationshipType
{
    Related = 0,
    References = 1,
    FollowUp = 2,
    Duplicate = 3,
    Supports = 4,
    Contradicts = 5
}
