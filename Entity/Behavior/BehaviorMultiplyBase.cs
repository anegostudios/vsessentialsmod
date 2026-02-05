using Newtonsoft.Json;
﻿using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

#nullable disable

namespace Vintagestory.GameContent
{
    /// <summary>
    /// Common code for egg-laying and live births: this is all connected with food saturation and cooldowns
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class EntityBehaviorMultiplyBase : EntityBehavior
    {
        protected ITreeAttribute multiplyTree;

        [JsonProperty] public double MultiplyCooldownDaysMin = 6;
        [JsonProperty] public double MultiplyCooldownDaysMax = 12;
        [JsonProperty] public float PortionsEatenForMultiply = 3;

        public double TotalDaysCooldownUntil
        {
            get { return multiplyTree.GetDouble("totalDaysCooldownUntil"); }
            set { multiplyTree.SetDouble("totalDaysCooldownUntil", value); entity.WatchedAttributes.MarkPathDirty("multiply"); }
        }

        [JsonProperty] protected bool eatAnyway = false;

        public virtual bool ShouldEat
        {
            get
            {
                return 
                    eatAnyway || 
                    (
                        GetSaturation() < PortionsEatenForMultiply 
                        && TotalDaysCooldownUntil <= entity.World.Calendar.TotalDays
                    )
                ;
            }
        }

        public virtual float PortionsLeftToEat
        {
            get
            {
                return PortionsEatenForMultiply - GetSaturation();
            }
        }

        public EntityBehaviorMultiplyBase(Entity entity) : base(entity)
        {

        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            if (attributes.Exists)
            {
                // Note that the generic type here is only used for proving the target is a class rather than a struct,
                // and does not limit which fields will be populated
                JsonUtil.Populate<EntityBehaviorMultiplyBase>(attributes.Token, this);
            }

            multiplyTree = entity.WatchedAttributes.GetTreeAttribute("multiply");

            if (entity.World.Side == EnumAppSide.Server)
            {
                if (multiplyTree == null)
                {
                    entity.WatchedAttributes.SetAttribute("multiply", multiplyTree = new TreeAttribute());

                    double daysNow = entity.World.Calendar.TotalHours / 24f;
                    TotalDaysCooldownUntil = daysNow + (MultiplyCooldownDaysMin + entity.World.Rand.NextDouble() * (MultiplyCooldownDaysMax - MultiplyCooldownDaysMin));
                }
            }
        }

        protected float GetSaturation()
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (tree == null) return 0;

            return tree.GetFloat("saturation", 0);
        }

        public override string PropertyName()
        {
            return "multiplybase";
        }

        public override void GetInfoText(StringBuilder infotext)
        {
            if (entity.Alive)
            {
                ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
                if (tree != null)
                {
                    float saturation = tree.GetFloat("saturation", 0);
                    infotext.AppendLine(Lang.Get("Portions eaten: {0}", saturation));
                    if (saturation >= PortionsEatenForMultiply) infotext.AppendLine(Lang.Get("Ready to lay"));
                }
            }
        }
    }
}
