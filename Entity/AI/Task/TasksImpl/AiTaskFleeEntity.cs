using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class AiTaskFleeEntity : AiTaskBase
    {
        EntityAgent targetEntity;
        Vec3d targetPos;
        float moveSpeed = 0.02f;
        float seekingRange = 25f;
        float executionChance = 0.04f;
        float fleeingDistance = 31f;
        float minDayLight = -1f;
        float fleeDurationMs = 5000;
        bool cancelOnHurt = false;

        long fleeStartMs;
        bool stuck;

        string[] fleeEntityCodesExact = new string[] { "player" };
        string[] fleeEntityCodesBeginsWith = new string[0];

        EntityPartitioning partitionUtil;


        public AiTaskFleeEntity(EntityAgent entity) : base(entity)
        {
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            partitionUtil = entity.Api.ModLoader.GetModSystem<EntityPartitioning>();

            base.LoadConfig(taskConfig, aiConfig);

            if (taskConfig["movespeed"] != null)
            {
                moveSpeed = taskConfig["movespeed"].AsFloat(0.02f);
            }

            if (taskConfig["seekingRange"] != null)
            {
                seekingRange = taskConfig["seekingRange"].AsFloat(25);
            }

            if (taskConfig["executionChance"] != null)
            {
                executionChance = taskConfig["executionChance"].AsFloat(0.04f);
            }

            if (taskConfig["minDayLight"] != null)
            {
                minDayLight = taskConfig["minDayLight"].AsFloat(-1f);
            }

            if (taskConfig["cancelOnHurt"] != null)
            {
                cancelOnHurt = taskConfig["cancelOnHurt"].AsBool(false);
            }

            if (taskConfig["fleeingDistance"] != null)
            {
                fleeingDistance = taskConfig["fleeingDistance"].AsFloat(25f);
            } else fleeingDistance = seekingRange + 6;

            if (taskConfig["fleeDurationMs"] != null)
            {
                fleeDurationMs = taskConfig["fleeDurationMs"].AsInt(5000);
            }

            if (taskConfig["entityCodes"] != null)
            {
                string[] codes = taskConfig["entityCodes"].AsStringArray(new string[] { "player" });

                List<string> exact = new List<string>();
                List<string> beginswith = new List<string>();

                for (int i = 0; i < codes.Length; i++)
                {
                    string code = codes[i];
                    if (code.EndsWith("*")) beginswith.Add(code.Substring(0, code.Length - 1));
                    else exact.Add(code);
                }

                fleeEntityCodesExact = exact.ToArray();
                fleeEntityCodesBeginsWith = beginswith.ToArray();
            }

        }


        

        public override bool ShouldExecute()
        {
            soundChance = Math.Min(1.01f, soundChance + 1 / 500f);

            if (rand.NextDouble() > executionChance || entity.World.Calendar.DayLightStrength < minDayLight) return false;
            if (whenInEmotionState != null && !entity.HasEmotionState(whenInEmotionState)) return false;
            if (whenNotInEmotionState != null && entity.HasEmotionState(whenNotInEmotionState)) return false;

            int generation = entity.WatchedAttributes.GetInt("generation", 0);
            float fearReductionFactor = Math.Max(0.01f, (50f - generation) / 50f);
            if (whenInEmotionState != null) fearReductionFactor = 1;

            targetEntity = (EntityAgent)partitionUtil.GetNearestEntity(entity.ServerPos.XYZ, fearReductionFactor * seekingRange, (e) => {
                if (!e.Alive || !e.IsInteractable || e.EntityId == this.entity.EntityId) return false;

                for (int i = 0; i < fleeEntityCodesExact.Length; i++)
                {
                    if (e.Code.Path == fleeEntityCodesExact[i])
                    {
                        if (e.Code.Path == "player")
                        {
                            IPlayer player = entity.World.PlayerByUid(((EntityPlayer)e).PlayerUID);
                            return player == null || (player.WorldData.CurrentGameMode != EnumGameMode.Creative && player.WorldData.CurrentGameMode != EnumGameMode.Spectator);
                        }
                        return true;
                    }
                }

                for (int i = 0; i < fleeEntityCodesBeginsWith.Length; i++)
                {
                    if (e.Code.Path.StartsWith(fleeEntityCodesBeginsWith[i])) return true;
                }

                return false;
            });

            
            if (targetEntity != null)
            {
                updateTargetPos();
                
                return true;
            }

            return false;
        }


        public override void StartExecute()
        {
            base.StartExecute();

            soundChance = Math.Max(0.025f, soundChance - 0.2f);

            float size = targetEntity.CollisionBox.X2 - targetEntity.CollisionBox.X1;

            pathTraverser.GoTo(targetPos, moveSpeed, size + 0.2f, OnGoalReached, OnStuck);

            fleeStartMs = entity.World.ElapsedMilliseconds;
            stuck = false;

        }

        public override bool ContinueExecute(float dt)
        {
            updateTargetPos();

            pathTraverser.CurrentTarget.X = targetPos.X;
            pathTraverser.CurrentTarget.Y = targetPos.Y;
            pathTraverser.CurrentTarget.Z = targetPos.Z;

            if (entity.ServerPos.SquareDistanceTo(targetEntity.ServerPos.XYZ) > fleeingDistance * fleeingDistance)
            {
                return false;
            }

            if (entity.IsActivityRunning("invulnerable")) return false;

            return !stuck && targetEntity.Alive && (entity.World.ElapsedMilliseconds - fleeStartMs < fleeDurationMs);
        }


        private void updateTargetPos()
        {
            Vec3d diff = targetEntity.Pos.XYZ.Sub(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            float yaw = (float)Math.Atan2(diff.X, diff.Z);

            targetPos = entity.Pos.XYZ.Ahead(10, 0, yaw - GameMath.PI/2);
        }

        public override void FinishExecute(bool cancelled)
        {
            pathTraverser.Stop();

            base.FinishExecute(cancelled);
        }


        private void OnStuck()
        {
            stuck = true;
        }

        private void OnGoalReached()
        {
            pathTraverser.Active = true;
        }
    }
}
