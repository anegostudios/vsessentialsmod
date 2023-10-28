using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorFloatUpWhenStuck : EntityBehavior
    {
        bool onlyWhenDead;

        int counter = 0;
        bool stuckInBlock;
        float pushVelocityMul = 1f;
        Vec3d tmpPos = new Vec3d();


        public EntityBehaviorFloatUpWhenStuck(Entity entity) : base(entity)
        {

        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            onlyWhenDead = attributes["onlyWhenDead"].AsBool(false);
            pushVelocityMul = attributes["pushVelocityMul"].AsFloat(1f);
            counter = ((int)entity.EntityId / 10) % 10;   // slightly randomise the counter to prevent one big tick after a chunk with many entities is loaded
        }


        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.ElapsedMilliseconds < 2000) return;

            if (counter++ > 10 || (stuckInBlock && counter > 1))
            {
                counter = 0;
                if ((onlyWhenDead && entity.Alive) || entity.Properties.CanClimbAnywhere) return;

                stuckInBlock = false;
                entity.Properties.Habitat = EnumHabitat.Land;   // One effect of this line (1.18.2) is to convert a dead Salmon fom EnumHabitat.Underwater to EnumHabitat.Land
                if (!entity.Swimming)
                {
                    tmpPos.Set(entity.SidedPos.X, entity.SidedPos.Y, entity.SidedPos.Z);
                    Cuboidd collbox = entity.World.CollisionTester.GetCollidingCollisionBox(entity.World.BlockAccessor, entity.CollisionBox.Clone().ShrinkBy(0.01f), tmpPos, false);

                    if (collbox != null)
                    {
                        PushoutOfCollisionbox(deltaTime, collbox);
                        stuckInBlock = true;
                    }
                }
            }
        }




        private void PushoutOfCollisionbox(float dt, Cuboidd collBox)
        {
            double posX = entity.SidedPos.X;
            double posY = entity.SidedPos.Y;
            double posZ = entity.SidedPos.Z;

            var ba = entity.World.BlockAccessor;

            Vec3i pushDir = null;
            double shortestDist = 99;
            for (int i = 0; i < Cardinal.ALL.Length; i++)
            {
                // Already found a good solution, no need to search further
                if (shortestDist <= 0.25f) break;

                var cardinal = Cardinal.ALL[i];
                for (int dist = 1; dist <= 4; dist++)
                {
                    var r = dist / 4f;
                    if (!entity.World.CollisionTester.IsColliding(ba, entity.CollisionBox, tmpPos.Set(posX + cardinal.Normali.X * r, posY, posZ + cardinal.Normali.Z * r), false))
                    {
                        if (r < shortestDist)
                        {
                                                // Make going diagonal a bit more costly
                            shortestDist = r + (cardinal.IsDiagnoal ? 0.1f : 0);
                            pushDir = cardinal.Normali;
                            break;
                        }
                    }
                }
            }

            if (pushDir == null) pushDir = BlockFacing.UP.Normali;

            dt = Math.Min(dt, 0.1f);

            // Add some tiny amounts of random horizontal motion to give it a chance to wiggle out of an edge case
            // This might break badly because its not client<->server sync
            float rndx = ((float)entity.World.Rand.NextDouble() - 0.5f) / 600f;
            float rndz = ((float)entity.World.Rand.NextDouble() - 0.5f) / 600f;

            entity.SidedPos.X += pushDir.X * dt * 0.4f;
            entity.SidedPos.Y += pushDir.Y * dt * 0.4f;
            entity.SidedPos.Z += pushDir.Z * dt * 0.4f;

            entity.SidedPos.Motion.X = pushVelocityMul * pushDir.X * dt + rndx;
            entity.SidedPos.Motion.Y = pushVelocityMul * pushDir.Y * dt * 2;
            entity.SidedPos.Motion.Z = pushVelocityMul * pushDir.Z * dt + rndz;

            entity.Properties.Habitat = EnumHabitat.Air;
        }


        public override string PropertyName()
        {
            return "floatupwhenstuck";
        }
    }
}
