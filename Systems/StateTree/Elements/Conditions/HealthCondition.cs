using System;
using Vintagestory.API.Datastructures;

namespace Vintagestory.StateTree;

/// <summary>
/// Condition that checks the entity's current health.
/// </summary>
public class HealthCondition(
    bool invert,
    ITreeAttribute healthTree,
    ComparisonType comparisonType,
    float value
) : BaseCondition(invert)
{
    private const float Tolerance = 0.001f;

    public override float Evaluate()
    {
        var current = healthTree.GetFloat("currenthealth");
        var max = healthTree.GetFloat("maxhealth");
        var percentage = current / max;

        bool result = comparisonType switch
        {
            ComparisonType.Equal => Math.Abs(percentage - value) < Tolerance,
            ComparisonType.NotEqual => Math.Abs(percentage - value) > Tolerance,
            ComparisonType.GreaterThan => percentage > value,
            ComparisonType.LessThan => percentage < value,
            ComparisonType.GreaterThanOrEqual => percentage >= value,
            ComparisonType.LessThanOrEqual => percentage <= value,
            _ => throw new ArgumentOutOfRangeException(nameof(comparisonType), comparisonType, null)
        };

        return base.Evaluate(result ? 1.0f : 0.0f);
    }
}
