using Newtonsoft.Json;
﻿using System.Text;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

#nullable disable

namespace Vintagestory.GameContent
{
    /// <summary>
    /// Common code for egg-laying and live births: this is all connected with food saturation and cooldowns.
    /// <br/>This behavior will not make entities have offspring. You will want to use a behavior that extends from this (See <see cref="EntityBehaviorMultiply"/>).
    /// <br/>Uses the "multiplybase" code
    /// </summary>
    [DocumentAsJson]
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class EntityBehaviorMultiplyBase : EntityBehavior
    {
        protected ITreeAttribute multiplyTree;

        /// <summary>
        /// The minimum number of in-game days that should pass before the creature can reproduce again
        /// </summary>
        [DocumentAsJson("Optional", "6")]
        [JsonProperty] public double MultiplyCooldownDaysMin = 6;

        /// <summary>
        /// The maximum number of in-game days that should pass before the creature can reproduce again
        /// </summary>
        [DocumentAsJson("Optional", "12")]
        [JsonProperty] public double MultiplyCooldownDaysMax = 12;

        /// <summary>
        /// How many portions the creature should eat to be able to multiply
        /// </summary>
        [DocumentAsJson("Optional", "3")]
        [JsonProperty] public float PortionsEatenForMultiply = 3;

        public double TotalDaysCooldownUntil
        {
            get { return multiplyTree.GetDouble("totalDaysCooldownUntil"); }
            set { multiplyTree.SetDouble("totalDaysCooldownUntil", value); entity.WatchedAttributes.MarkPathDirty("multiply"); }
        }

        /// <summary>
        /// Whether the creature should eat even if not hungry
        /// </summary>
        [DocumentAsJson("Optional", "false")]
        [JsonProperty] protected bool eatAnyway = false;

        public virtual bool ShouldEat => eatAnyway ||
            (
                GetSaturation() < PortionsEatenForMultiply
                && TotalDaysCooldownUntil <= entity.World.Calendar.TotalDays
            );

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
                JsonUtil.PopulateObject(this, attributes.Token, JsonUtil.CreateSerializerForDomain(entity.Code.Domain));
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
