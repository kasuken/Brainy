namespace Brainy.Domain.Enums;

/// <summary>
/// T-shirt size estimate of the effort or complexity of a task.
/// Used to surface quick wins, plan capacity, and help users
/// break down large pieces of work. Optional on all tasks.
/// </summary>
public enum TaskComplexity
{
    /// <summary>Extra-small — a few minutes of focused work.</summary>
    XS = 0,

    /// <summary>Small — up to about half a day of effort.</summary>
    S = 1,

    /// <summary>Medium — roughly one day of effort.</summary>
    M = 2,

    /// <summary>Large — several days; may warrant subtasks.</summary>
    L = 3,

    /// <summary>Extra-large — a week or more; should probably be broken down.</summary>
    XL = 4,
}
