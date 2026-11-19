using System;
using Vintagestory.API.Common;

namespace Vintagestory.StateTree;

public class WorldTimeCondition(
    bool invert,
    IGameCalendar calendar,
    float after,
    float before
) : BaseCondition(invert)
{
    private readonly float after = NormalizeHour(after, calendar.HoursPerDay);
    private readonly float before = NormalizeHour(before, calendar.HoursPerDay);

    private const float Tolerance = 0.001f;

    public override float Evaluate()
    {
        float current = NormalizeHour(calendar.HourOfDay, calendar.HoursPerDay);

        if (Math.Abs(after - before) < Tolerance)
            return base.Evaluate(0f);

        bool isInside = after < before
            ? current >= after && current < before
            : current >= after || current < before;

        return base.Evaluate(isInside ? 1f : 0f);
    }

    private static float NormalizeHour(float hour, float hoursPerDay)
    {
        if (hoursPerDay <= 0)
            return hour;

        float normalized = hour % hoursPerDay;
        if (normalized < 0)
            normalized += hoursPerDay;

        return normalized;
    }
}
