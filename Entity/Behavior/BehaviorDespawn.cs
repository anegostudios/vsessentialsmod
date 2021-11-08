using System.Text;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorDespawn : EntityBehavior
    {
        float? minPlayerDistance = null;
        float? belowLightLevel = null;

        float minSeconds = 30;

        float accumSeconds;

        public float DeathTime
        {
            get { float? time = entity.Attributes.TryGetFloat("deathTime"); return time == null ? 0 : (float)time; }
            set { entity.Attributes.SetFloat("deathTime", value); }
        }

        public EntityBehaviorDespawn(Entity entity) : base(entity)
        {
        }

        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            JsonObject minDist = typeAttributes["minPlayerDistance"];
            minPlayerDistance = (minDist.Exists) ? (float?)minDist.AsFloat() : null;

            JsonObject belowLight = typeAttributes["belowLightLevel"];
            belowLightLevel = (belowLight.Exists) ? (float?)belowLight.AsFloat() : null;
            

            minSeconds = typeAttributes["minSeconds"].AsFloat(30);
        }

        public override void OnGameTick(float deltaTime)
        {
            if (!entity.Alive || entity.World.Side == EnumAppSide.Client) return;

            accumSeconds += deltaTime;

            if (accumSeconds > 1 && (LightLevelOk() || PlayerInRange()))
            {
                DeathTime = 0;
                accumSeconds = 0;
                return;
            }

            deltaTime = System.Math.Min(deltaTime, 2);
            
            if ((DeathTime += deltaTime) > minSeconds)
            {
                //entity.World.Logger.Notification("despawn " + entity.Code + " plr in range " + PlayerInRange() + ", min seconds: " + minSeconds);
                entity.Die(EnumDespawnReason.Expire, null);
                return;
            }
        }


        public bool PlayerInRange()
        {
            if (minPlayerDistance == null) return false;

            IPlayer player = entity.World.NearestPlayer(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);

            return player?.Entity != null && player.Entity.ServerPos.SquareDistanceTo(entity.ServerPos.XYZ) < minPlayerDistance * minPlayerDistance;
        }

        public bool LightLevelOk()
        {
            if (belowLightLevel == null) return false;

            int level = entity.World.BlockAccessor.GetLightLevel(entity.ServerPos.AsBlockPos, EnumLightLevelType.MaxLight);

            return level >= belowLightLevel;
        }

        public override string PropertyName()
        {
            return "timeddespawn";
        }

        public override void GetInfoText(StringBuilder infotext)
        {
            if (belowLightLevel != null && !LightLevelOk() && entity.Alive)
            {
                infotext.AppendLine(Lang.Get("Deprived of light, might die soon"));
            }


            base.GetInfoText(infotext);
        }
    }
}
