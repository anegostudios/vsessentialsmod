using System;
using System.Collections.Generic;
using System.Threading;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public abstract class AiTaskBaseTargetable : AiTaskBase, IWorldIntersectionSupplier
    {
        protected HashSet<string> targetEntityCodesExact = new HashSet<string>();
        protected string[] targetEntityCodesBeginsWith = new string[0];

        protected string creatureHostility;
        protected bool friendlyTarget;

        protected Entity targetEntity;

        public virtual bool AggressiveTargeting => true;

        public Entity TargetEntity => targetEntity;

        protected Entity attackedByEntity;
        protected long attackedByEntityMs;
        protected bool retaliateAttacks = true;

        protected AiTaskBaseTargetable(EntityAgent entity) : base(entity)
        {
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            base.LoadConfig(taskConfig, aiConfig);

            if (taskConfig["entityCodes"] != null)
            {
                string[] codes = taskConfig["entityCodes"].AsArray<string>(new string[] { "player" });

                List<string> beginswith = new List<string>();

                for (int i = 0; i < codes.Length; i++)
                {
                    string code = codes[i];
                    if (code.EndsWith("*")) beginswith.Add(code.Substring(0, code.Length - 1));
                    else targetEntityCodesExact.Add(code);
                }
                
                targetEntityCodesBeginsWith = beginswith.ToArray();
            }

            creatureHostility = entity.World.Config.GetString("creatureHostility");

            friendlyTarget = taskConfig["friendlyTarget"].AsBool(false);
        }


        public virtual bool IsTargetableEntity(Entity e, float range, bool ignoreEntityCode = false)
        {
            if (!e.Alive || !e.IsInteractable || e.EntityId == entity.EntityId || !CanSense(e, range)) return false;
            if (ignoreEntityCode) return true;

            if (targetEntityCodesExact.Contains(e.Code.Path)) return true;

            for (int i = 0; i < targetEntityCodesBeginsWith.Length; i++)
            {
                if (e.Code.Path.StartsWithFast(targetEntityCodesBeginsWith[i])) return true;
            }

            return false;
        }


        public bool CanSense(Entity e, double range)
        {
            if (e is EntityPlayer eplr)
            {
                if (!friendlyTarget && AggressiveTargeting)
                {
                    if (creatureHostility == "off") return false;
                    if (creatureHostility == "passive" && (bhEmo == null || !bhEmo.IsInEmotionState("aggressiveondamage"))) return false;
                }

                float rangeMul = e.Stats.GetBlended("animalSeekingRange");
                IPlayer player = eplr.Player;

                // Sneaking reduces the detection range
                if (eplr.Controls.Sneak && eplr.OnGround)
                {
                    rangeMul *= 0.6f;
                }

                return
                    (rangeMul == 1 || entity.ServerPos.DistanceTo(e.Pos.XYZ) < range * rangeMul) &&
                    (player == null || (player.WorldData.CurrentGameMode != EnumGameMode.Creative && player.WorldData.CurrentGameMode != EnumGameMode.Spectator && (player as IServerPlayer).ConnectionState == EnumClientState.Playing))
                ;
            }

            return true;
        }


        protected BlockSelection blockSel = new BlockSelection();
        protected EntitySelection entitySel = new EntitySelection();

        protected bool hasDirectContact(Entity targetEntity, float minDist, float minVerDist)
        {
            Cuboidd targetBox = targetEntity.SelectionBox.ToDouble().Translate(targetEntity.ServerPos.X, targetEntity.ServerPos.Y, targetEntity.ServerPos.Z);
            Vec3d pos = entity.ServerPos.XYZ.Add(0, entity.SelectionBox.Y2 / 2, 0).Ahead(entity.SelectionBox.XSize / 2, 0, entity.ServerPos.Yaw);
            double dist = targetBox.ShortestDistanceFrom(pos);
            double vertDist = Math.Abs(targetBox.ShortestVerticalDistanceFrom(pos.Y));
            if (dist >= minDist || vertDist >= minVerDist) return false;

            Vec3d rayTraceFrom = entity.ServerPos.XYZ;
            rayTraceFrom.Y += 1 / 32f;
            Vec3d rayTraceTo = targetEntity.ServerPos.XYZ;
            rayTraceTo.Y += 1 / 32f;
            bool directContact = false;

            entity.World.RayTraceForSelection(this, rayTraceFrom, rayTraceTo, ref blockSel, ref entitySel);
            directContact = blockSel == null;

            if (!directContact)
            {
                rayTraceFrom.Y += entity.SelectionBox.Y2 * 7 / 16f;
                rayTraceTo.Y += targetEntity.SelectionBox.Y2 * 7 / 16f;
                entity.World.RayTraceForSelection(this, rayTraceFrom, rayTraceTo, ref blockSel, ref entitySel);
                directContact = blockSel == null;
            }

            if (!directContact)
            {
                rayTraceFrom.Y += entity.SelectionBox.Y2 * 7 / 16f;
                rayTraceTo.Y += targetEntity.SelectionBox.Y2 * 7 / 16f;
                entity.World.RayTraceForSelection(this, rayTraceFrom, rayTraceTo, ref blockSel, ref entitySel);
                directContact = blockSel == null;
            }

            if (!directContact) return false;

            return true;
        }



        public Vec3i MapSize { get { return entity.World.BlockAccessor.MapSize; } }

        public Block GetBlock(BlockPos pos)
        {
            return entity.World.BlockAccessor.GetBlock(pos);
        }

        public Cuboidf[] GetBlockIntersectionBoxes(BlockPos pos)
        {
            return entity.World.BlockAccessor.GetBlock(pos).GetCollisionBoxes(entity.World.BlockAccessor, pos);
        }

        public bool IsValidPos(BlockPos pos)
        {
            return entity.World.BlockAccessor.IsValidPos(pos);
        }


        public Entity[] GetEntitiesAround(Vec3d position, float horRange, float vertRange, ActionConsumable<Entity> matches = null)
        {
            return new Entity[0];
        }

        public Entity GetGuardedEntity()
        {
            var uid = entity.WatchedAttributes.GetString("guardedPlayerUid");
            if (uid != null)
            {
                return entity.World.PlayerByUid(uid).Entity;
            }
            else
            {
                var id = entity.WatchedAttributes.GetLong("guardedEntityId");
                return entity.World.GetEntityById(id);
            }
        }


        public override void OnEntityHurt(DamageSource source, float damage)
        {
            attackedByEntity = source.SourceEntity;
            attackedByEntityMs = entity.World.ElapsedMilliseconds;
            base.OnEntityHurt(source, damage);
        }

    }
}
