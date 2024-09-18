using System.Text;
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

        EnumDespawnMode despawnMode = EnumDespawnMode.AfterSeconds;
        float deathTimeLocal;   // local copy of the attribute

        public float DeathTime
        {
            get
            {
                float? time = entity.Attributes.TryGetFloat("deathTime");
                return deathTimeLocal = (time == null ? 0 : (float)time);
            }
            set
            {
                if (value != deathTimeLocal)
                {
                    entity.Attributes.SetFloat("deathTime", value);
                    deathTimeLocal = value;
                }
            } 
        }

        public float DespawnSeconds
        {
            get { return minSeconds; }
            set { minSeconds = value; }
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

            int minSecondsOverride = entity.Attributes.GetInt("minsecondsToDespawn");
            if (minSecondsOverride > 0) minSeconds = minSecondsOverride;
            else {
                minSeconds = typeAttributes["minSeconds"].AsFloat(30);
                minSeconds += (float)((entity.EntityId / 5.0) % (minSeconds / 20));    // add 5% randomness, to mitigate many entities in a chunk all attempting to despawn in the same tick
            }

            var obj = typeAttributes["afterDays"];
            if (entity.WatchedAttributes.HasAttribute("despawnTotalDays"))
            {
                despawnMode = obj.Exists ? EnumDespawnMode.AfterSecondsOrAfterDays : EnumDespawnMode.AfterSecondsOrAfterDaysIgnorePlayer;
            }
            else if (obj.Exists)
            {
                despawnMode = EnumDespawnMode.AfterSecondsOrAfterDays;
                entity.WatchedAttributes.SetDouble("despawnTotalDays", entity.World.Calendar.TotalDays + obj.AsFloat(14));
            }

            // Reduce chance of many entities running the light check at the same time
            accumOffset += (float)((entity.EntityId / 200.0) % 1);

            deathTimeLocal = DeathTime;
        }


        public override void OnGameTick(float deltaTime)
        {
            if (!entity.Alive || entity.World.Side == EnumAppSide.Client) return;

            if ((accumSeconds += deltaTime) > accumOffset)
            {
                if (despawnMode == EnumDespawnMode.AfterSecondsOrAfterDaysIgnorePlayer)
                {
                    if (entity.World.Calendar.TotalDays > entity.WatchedAttributes.GetDouble("despawnTotalDays"))
                    {
                        entity.Die(EnumDespawnReason.Expire, null);
                        accumSeconds = 0;
                        return;
                    }
                }

                bool playerInRange = PlayerInRange();
                if (playerInRange || LightLevelOk())
                {
                    accumSeconds = 0;
                    DeathTime = 0;
                    return;
                }

                if (despawnMode == EnumDespawnMode.AfterSecondsOrAfterDays && !playerInRange)
                {
                    if (entity.World.Calendar.TotalDays > entity.WatchedAttributes.GetDouble("despawnTotalDays"))
                    {
                        entity.Die(EnumDespawnReason.Expire, null);
                        accumSeconds = 0;
                        return;
                    }
                }

                if ((DeathTime += accumSeconds) > minSeconds)
                {
                    entity.Die(EnumDespawnReason.Expire, null);
                    accumSeconds = 0;
                    return;
                }

                accumSeconds = 0;
            }
        }


        public bool PlayerInRange()
        {
            if (minPlayerDistance < 0f) return false;

            return entity.minRangeToClient < minPlayerDistance;
        }

        public bool LightLevelOk()
        {
            if (belowLightLevel < 0f) return false;

            EntityPos pos = entity.ServerPos;
            int level = entity.World.BlockAccessor.GetLightLevel((int) pos.X, (int)pos.Y, (int)pos.Z, EnumLightLevelType.MaxLight);

            return level >= belowLightLevel;
        }

        public override string PropertyName() => "timeddespawn";

        public override void GetInfoText(StringBuilder infotext)
        {
            if (belowLightLevel >= 0 && !LightLevelOk() && entity.Alive)
            {
                infotext.AppendLine(Lang.Get("Deprived of light, might die soon"));
            }

            base.GetInfoText(infotext);
        }


        public void SetDespawnByCalendarDate(double totaldays)
        {
            entity.WatchedAttributes.SetDouble("despawnTotalDays", totaldays);
            despawnMode = EnumDespawnMode.AfterSecondsOrAfterDaysIgnorePlayer;
        }
    }



    enum EnumDespawnMode
    {
        AfterSeconds = 0,
        AfterSecondsOrAfterDays = 1,
        AfterSecondsOrAfterDaysIgnorePlayer = 2
    }
}
