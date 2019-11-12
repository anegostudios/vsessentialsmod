using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent
{
    public class DayTimeFrame
    {
        public double FromHour;
        public double ToHour;

        public bool Matches(double hourOfDay)
        {
            return FromHour <= hourOfDay && ToHour >= hourOfDay;
        }
    }

    public class AiTaskIdle : AiTaskBase
    {
        public AiTaskIdle(EntityAgent entity) : base(entity)
        {
        }

        public int minduration;
        public int maxduration;
        public float chance;
        public AssetLocation onBlockBelowCode;
        public long idleUntilMs;

        public DayTimeFrame[] duringDayTimeFrames;

        string[] stopOnNearbyEntityCodesExact = new string[] { "player" };
        string[] stopOnNearbyEntityCodesBeginsWith = new string[0];
        float stopRange=0;
        EntityPartitioning partitionUtil;

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            partitionUtil = entity.Api.ModLoader.GetModSystem<EntityPartitioning>();

            this.minduration = taskConfig["minduration"].AsInt(2000);
            this.maxduration = taskConfig["maxduration"].AsInt(4000);
            this.chance = taskConfig["chance"].AsFloat(1.1f);
            string code = taskConfig["onBlockBelowCode"].AsString(null);

            if (code != null && code.Length > 0)
            {
                this.onBlockBelowCode = new AssetLocation(code);
            }

            if (taskConfig["stopRange"] != null)
            {
                stopRange = taskConfig["stopRange"].AsFloat(0f);
            }

            if (taskConfig["duringDayTimeFrames"] != null)
            {
                duringDayTimeFrames = taskConfig["duringDayTimeFrames"].AsObject<DayTimeFrame[]>(null);
            }

            if (taskConfig["stopOnNearbyEntityCodes"] != null)
            {
                string[] codes = taskConfig["stopOnNearbyEntityCodes"].AsArray<string>(new string[] { "player" });

                List<string> exact = new List<string>();
                List<string> beginswith = new List<string>();

                for (int i = 0; i < codes.Length; i++)
                {
                    string ecode = codes[i];
                    if (ecode.EndsWith("*")) beginswith.Add(ecode.Substring(0, ecode.Length - 1));
                    else exact.Add(ecode);
                }

                stopOnNearbyEntityCodesExact = exact.ToArray();
                stopOnNearbyEntityCodesBeginsWith = beginswith.ToArray();
            }



            idleUntilMs = entity.World.ElapsedMilliseconds + minduration + entity.World.Rand.Next(maxduration - minduration);

            int generation = entity.WatchedAttributes.GetInt("generation", 0);
            float fearReductionFactor = Math.Max(0f, (10f - generation) / 10f);
            if (whenInEmotionState != null) fearReductionFactor = 1;

            stopRange *= fearReductionFactor;

            base.LoadConfig(taskConfig, aiConfig);
        }

        public override bool ShouldExecute()
        {
            
            if (entity.World.Rand.NextDouble() < chance && cooldownUntilMs < entity.World.ElapsedMilliseconds)
            {
                if (entity.Properties.Habitat == EnumHabitat.Land && entity.FeetInLiquid) return false;

                if (whenInEmotionState != null && !entity.HasEmotionState(whenInEmotionState)) return false;
                if (whenNotInEmotionState != null && entity.HasEmotionState(whenNotInEmotionState)) return false;

                if (entityInRange()) return false;

                if (duringDayTimeFrames != null)
                {
                    bool match = false;
                    double hourOfDay = entity.World.Calendar.HourOfDay / entity.World.Calendar.HoursPerDay * 24f;
                    for (int i = 0; !match && i < duringDayTimeFrames.Length; i++)
                    {
                        match |= duringDayTimeFrames[i].Matches(hourOfDay);
                    }
                    if (!match) return false;
                }

                if (onBlockBelowCode == null) return true;
                Block block = entity.World.BlockAccessor.GetBlock((int)entity.ServerPos.X, (int)entity.ServerPos.Y, (int)entity.ServerPos.Z);
                Block belowBlock = entity.World.BlockAccessor.GetBlock((int)entity.ServerPos.X, (int)entity.ServerPos.Y - 1, (int)entity.ServerPos.Z);
                return block.WildCardMatch(onBlockBelowCode) || (belowBlock.WildCardMatch(onBlockBelowCode) && block.Replaceable >= 6000);
            }

            return false;
        }

        public override void StartExecute()
        {
            base.StartExecute();
            idleUntilMs = entity.World.ElapsedMilliseconds + minduration + entity.World.Rand.Next(maxduration - minduration);
            entity.IdleSoundChanceModifier = 0f;
        }

        public override bool ContinueExecute(float dt)
        {
            if (rand.NextDouble() < 0.1f)
            {
                if (entityInRange()) return false;

                if (duringDayTimeFrames != null)
                {
                    bool match = false;
                    double hourOfDay = entity.World.Calendar.HourOfDay / entity.World.Calendar.HoursPerDay * 24f;
                    for (int i = 0; !match && i < duringDayTimeFrames.Length; i++)
                    {
                        match |= duringDayTimeFrames[i].Matches(hourOfDay);
                    }
                    if (!match) return false;
                }
            }

            return entity.World.ElapsedMilliseconds < idleUntilMs;
        }

        public override void FinishExecute(bool cancelled)
        {
            base.FinishExecute(cancelled);

            entity.IdleSoundChanceModifier = 1f;
        }


        bool entityInRange()
        {
            if (stopRange <= 0) return false;

            bool found = false;

            partitionUtil.WalkEntities(entity.ServerPos.XYZ, stopRange, (e) => {
                if (!e.Alive || !e.IsInteractable || e.EntityId == this.entity.EntityId) return true;

                for (int i = 0; i < stopOnNearbyEntityCodesExact.Length; i++)
                {
                    if (e.Code.Path == stopOnNearbyEntityCodesExact[i])
                    {
                        if (e.Code.Path == "player")
                        {
                            IPlayer player = entity.World.PlayerByUid(((EntityPlayer)e).PlayerUID);
                            if (player == null || (player.WorldData.CurrentGameMode != EnumGameMode.Creative && player.WorldData.CurrentGameMode != EnumGameMode.Spectator))
                            {
                                found = true;
                                return false;
                            }

                            return false;
                        }

                        found = true;
                        return false;
                    }
                }

                for (int i = 0; i < stopOnNearbyEntityCodesBeginsWith.Length; i++)
                {
                    if (e.Code.Path.StartsWith(stopOnNearbyEntityCodesBeginsWith[i]))
                    {
                        found = true;
                        return false;
                    }
                }

                return true;
            });

            return found;
        }


    }
}
