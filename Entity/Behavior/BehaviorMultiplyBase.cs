using System.Text;
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
    public class EntityBehaviorMultiplyBase : EntityBehavior
    {
        protected ITreeAttribute multiplyTree;

        public double MultiplyCooldownDaysMin { get; set; }
        public double MultiplyCooldownDaysMax { get; set; }
        public float PortionsEatenForMultiply { get; set; }

        public double TotalDaysCooldownUntil
        {
            get { return multiplyTree.GetDouble("totalDaysCooldownUntil"); }
            set { multiplyTree.SetDouble("totalDaysCooldownUntil", value); entity.WatchedAttributes.MarkPathDirty("multiply"); }
        }

        protected bool eatAnyway = false;

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

            eatAnyway = attributes["eatAnyway"].AsBool(false);
            MultiplyCooldownDaysMin = attributes["multiplyCooldownDaysMin"].AsFloat(6);
            MultiplyCooldownDaysMax = attributes["multiplyCooldownDaysMax"].AsFloat(12);
            PortionsEatenForMultiply = attributes["portionsEatenForMultiply"].AsFloat(3);


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
