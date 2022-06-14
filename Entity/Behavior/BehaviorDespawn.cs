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
        float? minPlayerDistanceSquared = null;
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
            minPlayerDistanceSquared = null;
            if (minDist.Exists)
            {
                float minPlayerDist = minDist.AsFloat();
                minPlayerDistanceSquared = minPlayerDist * minPlayerDist;
            }

            JsonObject belowLight = typeAttributes["belowLightLevel"];
            belowLightLevel = belowLight.Exists ? (float?)belowLight.AsFloat() : null;
            
            minSeconds = typeAttributes["minSeconds"].AsFloat(30);
        }


        bool shouldStayAlive;
        public override void OnGameTick(float deltaTime)
        {
            if (!entity.Alive || entity.World.Side == EnumAppSide.Client) return;

            deltaTime = Math.Min(deltaTime, 2);

            accumSeconds += deltaTime;
            if (accumSeconds > 1)
            {
                shouldStayAlive = PlayerInRange() || LightLevelOk();
                accumSeconds = 0;
            }

            if (shouldStayAlive)
            {
                DeathTime = 0;
                return;
            }

            if ((DeathTime += deltaTime) > minSeconds)
            {
                entity.Die(EnumDespawnReason.Expire, null);
                return;
            }
        }


        public bool PlayerInRange()
        {
            if (minPlayerDistanceSquared == null) return false;

            EntityPos pos = entity.ServerPos;
            EntityPlayer player = entity.World.NearestPlayer(pos.X, pos.Y, pos.Z)?.Entity;

            return player != null && player.ServerPos.SquareDistanceTo(pos.X, pos.Y, pos.Z) < minPlayerDistanceSquared;
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
