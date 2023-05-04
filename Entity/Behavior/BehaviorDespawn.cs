using System;
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
        float accumOffset = 0;

        bool byCalendarDespawnMode=false;

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
            minPlayerDistance = null;
            if (minDist.Exists)
            {
                minPlayerDistance = minDist.AsFloat();
            }

            JsonObject belowLight = typeAttributes["belowLightLevel"];
            belowLightLevel = belowLight.Exists ? (float?)belowLight.AsFloat() : null;
            
            minSeconds = typeAttributes["minSeconds"].AsFloat(30);

            var obj = typeAttributes["afterDays"];
            if (obj.Exists)
            {
                byCalendarDespawnMode = true;
                if (!entity.WatchedAttributes.HasAttribute("despawnTotalDays"))
                {
                    entity.WatchedAttributes.SetDouble("despawnTotalDays", entity.World.Calendar.TotalDays + obj.AsFloat(14));
                }
            }

            // Reduce chance of many entities running the light check at the same time
            accumOffset = (float)((entity.EntityId / 1000.0) % 1);
        }


        bool shouldStayAlive;
        public override void OnGameTick(float deltaTime)
        {
            if (!entity.Alive || entity.World.Side == EnumAppSide.Client) return;

            if ((accumSeconds += deltaTime) > 2.5f + accumOffset)
            {
                shouldStayAlive = PlayerInRange() || LightLevelOk();
                accumSeconds = 0;
                if (shouldStayAlive)
                {
                    DeathTime = 0;
                    return;
                }
            }

            if (shouldStayAlive)
            {
                return;
            }

            if (byCalendarDespawnMode)
            {
                if (!PlayerInRange() && entity.World.Calendar.TotalDays > entity.WatchedAttributes.GetDouble("despawnTotalDays"))
                {
                    entity.Die(EnumDespawnReason.Expire, null);
                    return;
                }
            }

            if ((DeathTime += deltaTime) > minSeconds)
            {
                entity.Die(EnumDespawnReason.Expire, null);
                return;
            }
        }


        public bool PlayerInRange()
        {
            if (minPlayerDistance == null) return false;

            /*EntityPos pos = entity.ServerPos;
            EntityPlayer player = entity.World.NearestPlayer(pos.X, pos.Y, pos.Z)?.Entity;

            return player != null && player.ServerPos.SquareDistanceTo(pos.X, pos.Y, pos.Z) < minPlayerDistanceSquared;*/

            return entity.minRangeToClient < minPlayerDistance;
        }

        public bool LightLevelOk()
        {
            if (belowLightLevel == null) return false;

            EntityPos pos = entity.ServerPos;
            int level = entity.World.BlockAccessor.GetLightLevel((int) pos.X, (int)pos.Y, (int)pos.Z, EnumLightLevelType.MaxLight);

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
