using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent
{
    public record GaitMeta
    {
        public required string Code { get; set; } // Unique identifier for the gait, ideally matched with rideable controls
        public virtual float YawMultiplier { get; set; } = 3.5f;
        public virtual float MoveSpeed { get; set; } = 0f;
        public virtual bool Backwards { get; set; } = false;
        public virtual float StaminaCost { get; set; } = 0f;
        public string? FallbackGaitCode { get; set; } // Gait to slow down to such as when fatiguing
        public bool IsSprint { get; set; } // Used to toggle entity.Controls.Sprint from rideable, consider alternatives?
        public required AssetLocation Sound { get; set; }
    }

    public class EntityBehaviorGait : EntityBehavior
    {
        public override string PropertyName()
        {
            return "gait";
        }

        public virtual readonly FastSmallDictionary<string, GaitMeta> Gaits = new(1);
        public virtual GaitMeta CurrentGait
        {
            get => Gaits[entity.WatchedAttributes.GetString("currentgait")];
            set => entity.WatchedAttributes.SetString("currentgait", value.Code);
        }

        public virtual GaitMeta IdleGait = null!;
        public virtual GaitMeta FallbackGait => CurrentGait.FallbackGaitCode is null ? IdleGait : Gaits[CurrentGait.FallbackGaitCode];

        public float GetYawMultiplier() => CurrentGait?.YawMultiplier ?? 3.5f; // Default yaw multiplier if not set

        public virtual void SetIdle() => CurrentGait = IdleGait;
        public virtual bool IsIdle => CurrentGait == IdleGait;
        public virtual bool IsBackward => CurrentGait.Backwards;
        public virtual bool IsForward => !CurrentGait.Backwards && CurrentGait != IdleGait;        
        public virtual GaitMeta CascadingFallbackGait(int n)
        {
            var result = CurrentGait;

            while (n > 0)
            {
                if (result.FallbackGaitCode is null) return IdleGait;
                result = Gaits[result.FallbackGaitCode];
                n--;
            }

            return result;
        }

        protected ICoreAPI? api;
        public EntityBehaviorGait(Entity entity) : base(entity)
        {
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            api = entity.Api;

            GaitMeta[] gaitarray = attributes["gaits"].AsArray<GaitMeta>();
            foreach (GaitMeta gait in gaitarray)
            {
                Gaits[gait.Code] = gait;
            }

            string idleGaitCode = attributes["idleGait"].AsString("idle");
            if (!Gaits.TryGetValue(idleGaitCode, out IdleGait)) throw new ArgumentException("JSON error. No idle gait for {0}", entity.Code);
            CurrentGait = IdleGait; // Set initial gait to Idle
        }
    }
}
