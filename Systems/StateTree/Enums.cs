namespace Vintagestory.StateTree;

/// <summary>
/// Node selection mode.
/// </summary>
public enum SelectionMode
{
    /// <summary>
    /// Nodes are evaluated in definition order. The first node whose conditions are met is selected.
    /// </summary>
    Sequence,
}

/// <summary>
/// Action list execution mode.
/// </summary>
public enum ActionListMode
{
    /// <summary>
    /// Actions are executed in definition order.
    /// </summary>
    Sequence,

    /// <summary>
    /// One action is selected at random. The selection persists until the node resets.
    /// </summary>
    Random,
}

/// <summary>
/// Condition list evaluation mode.
/// </summary>
public enum ConditionListMode
{
    /// <summary>
    /// All conditions must be met.
    /// </summary>
    MatchAll,

    /// <summary>
    /// Any condition must be met.
    /// </summary>
    MatchAny,
}

public enum ComparisonType
{
    Equal,
    NotEqual,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual
}
