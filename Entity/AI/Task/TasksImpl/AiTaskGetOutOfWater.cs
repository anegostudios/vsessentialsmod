using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class AiTaskGetOutOfWater : AiTaskBase
    {
        Vec3d target = new Vec3d();
        BlockPos pos = new BlockPos();

        bool done;
        float moveSpeed = 0.03f;

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


            target.Y = entity.ServerPos.Y;
            int tries = 6;
            int px = (int) entity.ServerPos.X;
            int pz = (int)entity.ServerPos.Z;
            IBlockAccessor blockAccessor = entity.World.BlockAccessor;
            while (tries-- > 0)
            {
                pos.X = px + rand.Next(21) - 10;
                pos.Z = pz + rand.Next(21) - 10;
                pos.Y = blockAccessor.GetTerrainMapheightAt(pos);

                Cuboidf[] blockBoxes = blockAccessor.GetBlock(pos).GetCollisionBoxes(blockAccessor, pos);
                pos.Y--;
                Cuboidf[] belowBoxes = blockAccessor.GetBlock(pos).GetCollisionBoxes(blockAccessor, pos);

                bool canStep = blockBoxes == null || blockBoxes.Max((cuboid) => cuboid.Y2) <= 1f;
                bool canStand = belowBoxes != null && belowBoxes.Length > 0;

                if (canStand && canStep)
                {
                    target.Set(pos.X + 0.5, pos.Y + 1, pos.Z + 0.5);
                    return true;
                }
            }

            return false;
        }

        public override void StartExecute()
        {
            base.StartExecute();

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
