using System;
using System.Text;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorDespawn : EntityBehavior, ITimedDespawn
    {
        float minPlayerDistance = -1f;
        float belowLightLevel = -1f;
        float minSeconds = 30;
        float accumSeconds;
        float accumOffset = 2.5f;

        bool byCalendarDespawnMode = false;
        float deathTimeLocal;   // local copy of the attribute

        public float DeathTime
        {
            get { float? time = entity.Attributes.TryGetFloat("deathTime"); return deathTimeLocal = (time == null ? 0 : (float)time); }
            set { if (value != deathTimeLocal) { entity.Attributes.SetFloat("deathTime", value);  deathTimeLocal = value; } }      // Note: the SetFloat() is costly and creates a new FloatAttribute object every time, is there a better way?
        }

        public EntityBehaviorDespawn(Entity entity) : base(entity)
        {
        }

        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            JsonObject minDist = typeAttributes["minPlayerDistance"];
            minPlayerDistance = minDist.Exists ? minDist.AsFloat() : -1f;

            JsonObject belowLight = typeAttributes["belowLightLevel"];
            belowLightLevel = belowLight.Exists ? belowLight.AsFloat() : -1f;
            
            minSeconds = typeAttributes["minSeconds"].AsFloat(30);
            minSeconds += (entity.EntityId / 5f) % (minSeconds / 20);    // add 5% randomness, to mitigate many entities in a chunk all attempting to despawn in the same tick

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
            accumOffset += (entity.EntityId / 1000f) % 1;
            float dummy = DeathTime;   // set up the local value of deathTime
        }


        public override void OnGameTick(float deltaTime)
        {
            if (!entity.Alive || entity.World.Side == EnumAppSide.Client) return;

            if ((accumSeconds += deltaTime) > accumOffset)
            {
                accumSeconds = 0;

                bool playerInRange = PlayerInRange();
                if (playerInRange || LightLevelOk())
                {
                    DeathTime = 0;   // costly operation, cost of repeated operations mitigated through comparison with the local copy deathTimeLocal
                    return;
                }

                if (byCalendarDespawnMode)
                {
                    if (!playerInRange && entity.World.Calendar.TotalDays > entity.WatchedAttributes.GetDouble("despawnTotalDays"))
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
        }


        public bool PlayerInRange()
        {
            if (minPlayerDistance < 0f) return false;

            /*EntityPos pos = entity.ServerPos;
            EntityPlayer player = entity.World.NearestPlayer(pos.X, pos.Y, pos.Z)?.Entity;

            return player != null && player.ServerPos.SquareDistanceTo(pos.X, pos.Y, pos.Z) < minPlayerDistanceSquared;*/

            return entity.minRangeToClient < minPlayerDistance;
        }

        public bool LightLevelOk()
        {
            if (belowLightLevel < 0f) return false;

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

        public void SetTimer(int value)
        {
            minSeconds = value;
        }
    }
}
