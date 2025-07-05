using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    public class AiTaskGetOutOfWater : AiTaskBase
    {
        Vec3d target = new Vec3d();
        BlockPos pos = new BlockPos();

        bool done;
        float moveSpeed = 0.03f;
        int searchattempts = 0;

        public AiTaskGetOutOfWater(EntityAgent entity) : base(entity)
        {

        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            base.LoadConfig(taskConfig, aiConfig);

            moveSpeed = taskConfig["movespeed"].AsFloat(0.06f);
        }

        public override bool ShouldExecute()
        {
            if (!entity.Swimming) return false;
            if (rand.NextDouble() > 0.04f) return false;

            int range = GameMath.Min(50, 30 + searchattempts*2);

            target.Y = entity.ServerPos.Y;
            int tries = 10;
            int px = (int) entity.ServerPos.X;
            int pz = (int)entity.ServerPos.Z;
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
            pathTraverser.WalkTowards(target, moveSpeed, 0.5f, OnGoalReached, OnStuck);

            //entity.world.SpawnParticles(10, ColorUtil.WhiteArgb, target.AddCopy(new Vec3f(-0.1f, -0.1f, -0.1f)), target.AddCopy(new Vec3f(0.1f, 0.1f, 0.1f)), new Vec3f(), new Vec3f(), 1f, 1f);

        }

        public override bool ContinueExecute(float dt)
        {
            /*if (entity.Swimming)
            {
                Block aboveblock = entity.World.BlockAccessor.GetBlock((int)entity.ServerPos.X, (int)(entity.ServerPos.Y + entity.CollisionBox.Y2 * 0.25f), (int)entity.ServerPos.Z);
                if (aboveblock.IsLiquid()) entity.ServerPos.Motion.Y = Math.Min(entity.ServerPos.Motion.Y + 0.005f, 0.03f);
            }*/
            
            //Check if time is still valid for task.
            if (!IsInValidDayTimeHours(false)) return false;

            if (rand.NextDouble() < 0.1f)
            {
                if (!entity.FeetInLiquid) return false;

                //Block block = entity.World.BlockAccessor.GetBlock((int)entity.ServerPos.X, (int)entity.ServerPos.Y, (int)entity.ServerPos.Z);
                //if (block.CollisionBoxes != null && block.CollisionBoxes.Length > 0 && !entity.FeetInLiquid) return false;
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
