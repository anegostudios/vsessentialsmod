using System.Collections.Generic;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

/// <summary>
/// Action that applies directly to its target.
/// </summary>
public class DirectAction(Entity abilityUser, IList<IEffect> onHitEffects)
    : AbilityAction(abilityUser, onHitEffects)
{
    public override void Run(ITargetSelector targetSelector)
    {
        base.Run(targetSelector);
        OnHit([targetSelector.GetTarget()]);
    }
}
