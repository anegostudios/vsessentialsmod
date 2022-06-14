using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

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


        bool entityWasInRange;
        long lastEntityInRangeTestTotalMs;

        public DayTimeFrame[] duringDayTimeFrames;

        string[] stopOnNearbyEntityCodesExact = null;
        string[] stopOnNearbyEntityCodesBeginsWith = new string[0];
        float stopRange=0;
        bool stopOnHurt = false;
        EntityPartitioning partitionUtil;

        bool stopNow;

        float tamingGenerations = 10f;

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            partitionUtil = entity.Api.ModLoader.GetModSystem<EntityPartitioning>();

            this.minduration = taskConfig["minduration"].AsInt(2000);
            this.maxduration = taskConfig["maxduration"].AsInt(4000);
            this.chance = taskConfig["chance"].AsFloat(1.1f);
            string code = taskConfig["onBlockBelowCode"].AsString(null);

            tamingGenerations = taskConfig["tamingGenerations"].AsFloat(10f);

            if (code != null && code.Length > 0)
            {
                this.onBlockBelowCode = new AssetLocation(code);
            }

            stopRange = taskConfig["stopRange"].AsFloat(0f);
            stopOnHurt = taskConfig["stopOnHurt"].AsBool(false);
            duringDayTimeFrames = taskConfig["duringDayTimeFrames"].AsObject<DayTimeFrame[]>(null);

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
            float fearReductionFactor = Math.Max(0f, (tamingGenerations - generation) / tamingGenerations);
            if (whenInEmotionState != null) fearReductionFactor = 1;

            stopRange *= fearReductionFactor;

            base.LoadConfig(taskConfig, aiConfig);
        }

        public override bool ShouldExecute()
        {
            long ellapsedMs = entity.World.ElapsedMilliseconds;
            if (entity.World.Rand.NextDouble() < chance && cooldownUntilMs < ellapsedMs)
            {
                if (entity.Properties.Habitat == EnumHabitat.Land && entity.FeetInLiquid) return false;

                if (whenInEmotionState != null && bhEmo?.IsInEmotionState(whenInEmotionState) != true) return false;
                if (whenNotInEmotionState != null && bhEmo?.IsInEmotionState(whenNotInEmotionState) == true) return false;

                // The entityInRange test is expensive. So we only test for it every 4 seconds
                // which should have zero impact on the behavior. It'll merely execute this task 4 seconds later
                if (ellapsedMs - lastEntityInRangeTestTotalMs > 2000)
                {
                    entityWasInRange = entityInRange();
                    lastEntityInRangeTestTotalMs = ellapsedMs;
                }

                if (entityWasInRange) return false;

                if (duringDayTimeFrames != null)
                {
                    bool match = false;
                    // introduce a bit of randomness so that (e.g.) hens do not all wake up simultaneously at 06:00, which looks artificial
                    double hourOfDay = entity.World.Calendar.HourOfDay / entity.World.Calendar.HoursPerDay * 24f + (entity.World.Rand.NextDouble() * 0.3f - 0.15f);
                    for (int i = 0; !match && i < duringDayTimeFrames.Length; i++)
                    {
                        match |= duringDayTimeFrames[i].Matches(hourOfDay);
                    }
                    if (!match) return false;
                }

                Block belowBlock = entity.World.BlockAccessor.GetBlock((int)entity.ServerPos.X, (int)entity.ServerPos.Y - 1, (int)entity.ServerPos.Z);
                // Only with a solid block below (and here not lake ice: entities should not idle on lake ice!)
                if (!belowBlock.SideSolid[API.MathTools.BlockFacing.UP.Index]) return false;

                if (onBlockBelowCode == null) return true;
                Block block = entity.World.BlockAccessor.GetBlock((int)entity.ServerPos.X, (int)entity.ServerPos.Y, (int)entity.ServerPos.Z);

                return block.WildCardMatch(onBlockBelowCode) || (belowBlock.WildCardMatch(onBlockBelowCode) && block.Replaceable >= 6000);
            }

            return false;
        }

        public override void StartExecute()
        {
            base.StartExecute();
            idleUntilMs = entity.World.ElapsedMilliseconds + minduration + entity.World.Rand.Next(maxduration - minduration);
            entity.IdleSoundChanceModifier = 0f;
            stopNow = false;
        }

        public override bool ContinueExecute(float dt)
        {
            if (rand.NextDouble() < 0.3f)
            {
                long ellapsedMs = entity.World.ElapsedMilliseconds;

                // The entityInRange test is expensive. So we only test for it every 1 second
                // which should have zero impact on the behavior. It'll merely execute this task 1 second later
                if (ellapsedMs - lastEntityInRangeTestTotalMs > 1500 && stopOnNearbyEntityCodesExact != null)
                {
                    entityWasInRange = entityInRange();
                    lastEntityInRangeTestTotalMs = ellapsedMs;
                }
                if (entityWasInRange) return false;


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

            return !stopNow && entity.World.ElapsedMilliseconds < idleUntilMs;
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
                    if (e.Code.Path.StartsWithFast(stopOnNearbyEntityCodesBeginsWith[i]))
                    {
                        found = true;
                        return false;
                    }
                }

                return true;
            });

            return found;
        }


        public override void OnEntityHurt(DamageSource source, float damage)
        {
            if (stopOnHurt)
            {
                stopNow = true;
            }
        }


    }
}
