namespace Brainy.Domain.Enums;

/// <summary>
/// The kind of deliverable produced by reusing stored knowledge (Express).
/// </summary>
public enum OutputType
{
    BlogPost = 0,
    LinkedInPost = 1,
    Report = 2,
    ProductSpec = 3,
    MeetingBrief = 4,
    Roadmap = 5,
    DecisionRecord = 6,
    LearningPlan = 7,
    ResearchSummary = 8,
    PresentationOutline = 9,
    Proposal = 10,
    EmailDraft = 11,
    Custom = 99
}
