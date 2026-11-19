using System;
using System.Collections.Generic;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

#nullable disable

namespace Vintagestory.GameContent
{
    /// <summary>
    /// Allows entity to pick dropped items up. This class has no further properties.
    /// <br/>Uses the "collectitems" code
    /// </summary>
    [DocumentAsJson]
    public class EntityBehaviorCollectEntities : EntityBehavior
    {
        int waitTicks = 0;
        int lastCollectedEntityIndex = 0;
        Vec3d tmp = new Vec3d();

        float itemsPerSecond = 23f;
        float unconsumedDeltaTime;

        protected EntityPartitioning partitionUtil;

        public EntityBehaviorCollectEntities(Entity entity) : base(entity)
        {
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            partitionUtil = entity.Api.ModLoader.GetModSystem<EntityPartitioning>();
        }


        public override void OnGameTick(float deltaTime)
        {
            // Only running for active entities
            if (entity.State != EnumEntityState.Active || !entity.Alive) return;

            IPlayer player = (entity as EntityPlayer)?.Player;

            if ((player as IServerPlayer)?.ItemCollectMode == 1 && (entity as EntityAgent)?.Controls.Sneak == false) return;

            if (entity.IsActivityRunning("invulnerable"))
            {
                waitTicks = 3;
                return;
            }
            if (waitTicks-- > 0) return;

            if (player?.WorldData.CurrentGameMode == EnumGameMode.Spectator) return;

            tmp.Set(entity.Pos.X, entity.Pos.InternalY + entity.SelectionBox.Y1 + entity.SelectionBox.Y2 / 2, entity.Pos.Z);

            List<Entity> entities = new List<Entity>();
            partitionUtil.WalkEntities(entity.Pos.X, entity.Pos.Y, entity.Pos.Z, 1.5, (e) => { entities.Add(e); return true; }, onRangeTest, EnumEntitySearchType.Inanimate);
            
            if (entities.Count == 0)
            {
                unconsumedDeltaTime = 0;
                return;
            }


            deltaTime = System.Math.Min(1f, deltaTime + unconsumedDeltaTime);

            while ((deltaTime - 1/itemsPerSecond) > 0)
            {
                Entity targetItem = null;
                int targetIndex = 0;

                for (; targetIndex < entities.Count; targetIndex++)
                {
                    if (entities[targetIndex] == null) continue;

                    if (targetIndex >= lastCollectedEntityIndex)
                    {
                        targetItem = entities[targetIndex];
                        break;
                    }
                }

                if (targetItem == null)
                {
                    targetItem = entities[0];
                    targetIndex = 0;
                }
                if (targetItem == null) return;

                if (!OnFoundCollectible(targetItem))
                {
                    lastCollectedEntityIndex = (lastCollectedEntityIndex + 1) % entities.Count;
                } else
                {
                    entities[targetIndex] = null;
                }

                deltaTime -= 1 / itemsPerSecond;
            }

            unconsumedDeltaTime = deltaTime;
        }

        private bool onRangeTest(Entity e, double posX, double posY, double posZ, double radiusSq)
        {
            if (!e.CanCollect(entity)) return false;
            var ePos = e.Pos;
            double dx = ePos.X - posX;
            double dy = ePos.InternalY - posY;
            double dz = ePos.Z - posZ;

            return (dx * dx + dy * dy + dz * dz) < radiusSq;
        }

        public virtual bool OnFoundCollectible(Entity foundEntity)
        {
            ItemStack itemstack = foundEntity.OnCollected(this.entity);
            bool collected = false;

            var origStack = itemstack.Clone();

            if (itemstack != null && itemstack.StackSize > 0)
            {
                collected = entity.TryGiveItemStack(itemstack);
            }

            if (itemstack != null && itemstack.StackSize <= 0)
            {
                foundEntity.Die(EnumDespawnReason.PickedUp);
            }

            if (collected)
            {
                itemstack.Collectible.OnCollected(itemstack, entity);

                TreeAttribute tree = new TreeAttribute();
                tree["itemstack"] = new ItemstackAttribute(origStack.Clone());
                tree["byentityid"] = new LongAttribute(this.entity.EntityId);
                entity.World.Api.Event.PushEvent("onitemcollected", tree);

                entity.World.PlaySoundAt(new AssetLocation("sounds/player/collect"), entity);
                return true;
            }

            return false;
        }

        private bool entityMatcher(Entity foundEntity)
        {
            return foundEntity.CanCollect(entity);
        }


        public override string PropertyName()
        {
            return "collectitems";
        }
    }
}
