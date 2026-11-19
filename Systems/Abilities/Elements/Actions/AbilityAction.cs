using System;
using System.Collections.Generic;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

public abstract class AbilityAction(Entity abilityUser, IList<IEffect> onHitEffects)
{
    protected static readonly Vec3d WorldUp = new(0, 1, 0);
    protected static readonly int DebugDrawColor = ColorUtil.ColorFromRgba(177, 0, 255, 255);
    protected static readonly TimeSpan DebugDrawDuration = TimeSpan.FromSeconds(5d);

    public System.Action OnFinish { get; set; }

    public string AbilityId { get; set; }

    public virtual void OnAbilityExecute() {}

    protected void OnHit(Entity[] targets)
    {
        foreach (var target in targets)
        {
            foreach (var effect in onHitEffects)
            {
                effect.Apply(abilityUser, target);
            }
        }
    }

    public virtual void Run(ITargetSelector targetSelector)
    {
        // By default, actions finish in a single tick. Actions that need to invoke this later on should avoid calling
        // base.Run when overriding this method.
        OnFinish?.Invoke();
    }
}
