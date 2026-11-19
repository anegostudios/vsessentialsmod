using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    public class AiTaskGetOutOfWater : AiTaskBase
    {
        [JsonProperty]
        float moveSpeed;
        private string gaitCode;
        GaitMeta PreferredGait {get; set;} = null;

        Vec3d target = new Vec3d();
        BlockPos pos = new BlockPos(API.Config.Dimensions.WillSetLater);

        bool done;
        int searchattempts = 0;

        public AiTaskGetOutOfWater(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
        {
            gaitCode = taskConfig["preferredGait"].AsString();
        }

        protected override void SetDefaultValues()
        {
            base.SetDefaultValues();
            ExecutionChance = 0.04;
            WhenSwimming = true;
            moveSpeed = 0.06f;
        }

        public override void AfterInitialize()
        {
            if (gaitCode != null)
            {
                EntityBehaviorGait gaitBehavior = entity.GetBehavior<EntityBehaviorGait>();
                if (gaitBehavior != null)
                {
                    PreferredGait = gaitBehavior.Gaits[gaitCode];
                }
            }
        }

        public override bool ShouldExecute()
        {
            if (!PreconditionsSatisfied()) return false;
            if (rand.NextDouble() > ExecutionChance) return false;

            int range = GameMath.Min(50, 30 + searchattempts*2);

            target.Y = entity.Pos.Y;
            int tries = 10;
            int px = (int) entity.Pos.X;
            int pz = (int)entity.Pos.Z;
            pos.SetDimension(entity.Pos.Dimension);
            IBlockAccessor blockAccessor = entity.World.BlockAccessor;
            Vec3d tmpPos = new Vec3d();
            while (tries-- > 0)
            {
                pos.X = px + rand.Next(range+1) - range / 2;
                pos.Z = pz + rand.Next(range+1) - range / 2;
                pos.Y = blockAccessor.GetTerrainMapheightAt(pos)+1;

                var fblock = blockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);
                if (fblock.IsLiquid()) continue;

                var block = blockAccessor.GetBlock(pos);

                if (!entity.World.CollisionTester.IsColliding(blockAccessor, entity.CollisionBox, tmpPos.Set(pos.X + 0.5, pos.Y + 0.1f, pos.Z + 0.5)))
                {
                    if (entity.World.CollisionTester.IsColliding(blockAccessor, entity.CollisionBox, tmpPos.Set(pos.X + 0.5, pos.Y - 0.1f, pos.Z + 0.5)))
                    {
                        target.Set(pos.X + 0.5, pos.Y + 1, pos.Z + 0.5);
                        return true;
                    }
                }
            }

            searchattempts++;
            return false;
        }

        public override void StartExecute()
        {
            base.StartExecute();

            searchattempts = 0;
            done = false;
            pathTraverser.WalkTowards(target, moveSpeed, 0.5f, OnGoalReached, OnStuck, EnumAICreatureType.Default, PreferredGait);
        }

        public override bool CanContinueExecute()
        {
            return pathTraverser.Ready;
        }

        public override bool ContinueExecute(float dt)
        {
            // Staying near the surface of the water is handled by the gait behavior, if present, or otherwise the path navigator

            //Check if time is still valid for task.
            if (!IsInValidDayTimeHours(false)) return false;

            if (rand.NextDouble() < 0.1f)
            {
                if (!entity.FeetInLiquid) return false;
            }

            return !done;
        }

        public override void FinishExecute(bool cancelled)
        {
            base.FinishExecute(cancelled);

            pathTraverser.Stop();
        }

        private void OnStuck()
        {
            done = true;
        }

        private void OnGoalReached()
        {
            done = true;
        }
    }
}
