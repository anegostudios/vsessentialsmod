using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
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
        Vec3d tmpPos = new Vec3d();


        public EntityBehaviorFloatUpWhenStuck(Entity entity) : base(entity)
        {

        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            onlyWhenDead = attributes["onlyWhenDead"].AsBool(false);
        }


        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.ElapsedMilliseconds < 2000) return;

            if (counter++ > 10 || (stuckInBlock && counter > 1))
            {
                if ((onlyWhenDead && entity.Alive) || entity.Properties.CanClimbAnywhere) return;

                stuckInBlock = false;
                counter = 0;
                entity.Properties.Habitat = EnumHabitat.Land;
                if (!entity.Swimming)
                {
                    tmpPos.Set(entity.SidedPos.X, entity.SidedPos.Y, entity.SidedPos.Z);
                    Cuboidd collbox = entity.World.CollisionTester.GetCollidingCollisionBox(entity.World.BlockAccessor, entity.CollisionBox, tmpPos, false);

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
            /// North: Negative Z
            /// East: Positive X
            /// South: Positive Z
            /// West: Negative X

            double[] distByFacing = new double[]
            {
                posZ - collBox.Z1, // N
                collBox.X2 - posX, // E
                collBox.Z2 - posZ, // S
                posX - collBox.X1, // W
                collBox.Y2 - posY, // U
                99 // D
            };

            BlockFacing pushDir = BlockFacing.UP;
            double shortestDist = 99;
            for (int i = 0; i < distByFacing.Length; i++)
            {
                BlockFacing face = BlockFacing.ALLFACES[i];
                if (distByFacing[i] < shortestDist && (
                    !entity.World.CollisionTester.IsColliding(entity.World.BlockAccessor, entity.CollisionBox, tmpPos.Set(posX + face.Normali.X, posY, posZ + face.Normali.Z), false)
                    || !entity.World.CollisionTester.IsColliding(entity.World.BlockAccessor, entity.CollisionBox, tmpPos.Set(posX + face.Normali.X/2f, posY, posZ + face.Normali.Z/2f), false)
                    || !entity.World.CollisionTester.IsColliding(entity.World.BlockAccessor, entity.CollisionBox, tmpPos.Set(posX + face.Normali.X/4f, posY, posZ + face.Normali.Z/4f), false)
                    || !entity.World.CollisionTester.IsColliding(entity.World.BlockAccessor, entity.CollisionBox, tmpPos.Set(posX + face.Normali.X / 8f, posY, posZ + face.Normali.Z / 8f), false)
                    || !entity.World.CollisionTester.IsColliding(entity.World.BlockAccessor, entity.CollisionBox, tmpPos.Set(posX + face.Normali.X / 16f, posY, posZ + face.Normali.Z / 16f), false)
                ))
                {
                    shortestDist = distByFacing[i];
                    pushDir = face;
                }
            }


            dt = Math.Min(dt, 0.1f);

            // Add some tiny amounts of random horizontal motion to give it a chance to wiggle out of an edge case
            // This might break badly because its not client<->server sync
            float rndx = ((float)entity.World.Rand.NextDouble() - 0.5f) / 600f;
            float rndz = ((float)entity.World.Rand.NextDouble() - 0.5f) / 600f;

            entity.SidedPos.X += pushDir.Normali.X * dt * 0.4f;
            entity.SidedPos.Y += pushDir.Normali.Y * dt * 0.4f;
            entity.SidedPos.Z += pushDir.Normali.Z * dt * 0.4f;

            entity.SidedPos.Motion.X = pushDir.Normali.X * dt + rndx;
            entity.SidedPos.Motion.Y = pushDir.Normali.Y * dt * 2;
            entity.SidedPos.Motion.Z = pushDir.Normali.Z * dt + rndz;

            entity.Properties.Habitat = EnumHabitat.Air;
        }


        public override string PropertyName()
        {
            return "floatupwhenstuck";
        }
    }
}
