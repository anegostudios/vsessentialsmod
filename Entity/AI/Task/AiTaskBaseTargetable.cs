﻿using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using VSEssentialsMod.Entity.AI.Task;

namespace Vintagestory.GameContent
{

    public abstract class AiTaskBaseTargetable : AiTaskBase, IWorldIntersectionSupplier
    {
        protected AssetLocation[] targetEntityCodes;
        protected AssetLocation[] skipEntityCodes;

        protected EnumCreatureHostility creatureHostility;
        protected bool friendlyTarget;

        public Entity targetEntity;

        public virtual bool AggressiveTargeting => true;

        public Entity TargetEntity => targetEntity;

        protected Entity attackedByEntity;
        protected long attackedByEntityMs;
        protected bool retaliateAttacks = true;

        public string triggerEmotionState;
        protected float tamingGenerations = 10f;

        protected bool noEntityCodes => targetEntityCodes.Length == 0;

        protected EntityPartitioning partitionUtil;
        protected EntityBehaviorControlledPhysics bhPhysics;

        protected bool RecentlyAttacked => entity.World.ElapsedMilliseconds - attackedByEntityMs < 30000;

        protected AiTaskBaseTargetable(EntityAgent entity) : base(entity)
        {
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            base.LoadConfig(taskConfig, aiConfig);

            partitionUtil = entity.Api.ModLoader.GetModSystem<EntityPartitioning>();

            creatureHostility = entity.World.Config.GetString("creatureHostility") switch
            {
                "aggressive" => EnumCreatureHostility.Aggressive,
                "passive" => EnumCreatureHostility.Passive,
                "off" => EnumCreatureHostility.NeverHostile,
                _ => EnumCreatureHostility.Aggressive
            };

            tamingGenerations = taskConfig["tamingGenerations"].AsFloat(10f);

            friendlyTarget = taskConfig["friendlyTarget"].AsBool(false);

            retaliateAttacks = taskConfig["retaliateAttacks"].AsBool(true);

            this.triggerEmotionState = taskConfig["triggerEmotionState"].AsString();

            // Defaulting to entity.Code.Domain instead of * would be more proper, but would also break every mod from 1.20.0-rc and earlier
            targetEntityCodes = taskConfig["entityCodes"].AsArray<string>(new string[] { "player" }).Select(str => AssetLocation.Create(str, "*")).ToArray();
            skipEntityCodes = taskConfig["skipEntityCodes"].AsArray<string>()?.Select(str => AssetLocation.Create(str, entity.Code.Domain)).ToArray();
        }

        /// <summary>
        /// Makes a similar sytem - "target codes from an array of entity codes with or without wildcards" - available to any other game element which requires it
        /// </summary>
        /// <param name="codes"></param>
        /// <param name="targetEntityCodesExact"></param>
        /// <param name="targetEntityCodesBeginsWith"></param>
        /// <param name="targetEntityFirstLetters"></param>
        [Obsolete("Use WildcardUtil instead for better consistency with the rest of the game")]
        public static void InitializeTargetCodes(string[] codes, ref string[] targetEntityCodesExact, ref string[] targetEntityCodesBeginsWith, ref string targetEntityFirstLetters)
        {
            List<string> targetEntityCodesList = new List<string>();
            List<string> beginswith = new List<string>();

            for (int i = 0; i < codes.Length; i++)
            {
                string code = codes[i];
                if (code.EndsWith('*'))
                {
                    beginswith.Add(code.Substring(0, code.Length - 1));
                }
                else targetEntityCodesList.Add(code);
            }

            targetEntityCodesBeginsWith = beginswith.ToArray();

            targetEntityCodesExact = new string[targetEntityCodesList.Count];
            int j = 0;
            foreach (string code in targetEntityCodesList)
            {
                if (code.Length == 0) continue;
                targetEntityCodesExact[j++] = code;
                char c = code[0];
                if (targetEntityFirstLetters.IndexOf(c) < 0) targetEntityFirstLetters += c;
            }

            foreach (string code in targetEntityCodesBeginsWith)
            {
                if (code.Length == 0)
                {
                    targetEntityFirstLetters = "";   // code.Length zero indicates universal wildcard "*", therefore IsTargetableEntity should match everything - used by BeeMob for example
                    break;
                }
                char c = code[0];
                if (targetEntityFirstLetters.IndexOf(c) < 0) targetEntityFirstLetters += c;
            }
        }

        public override void AfterInitialize()
        {
            bhPhysics = entity.GetBehavior<EntityBehaviorControlledPhysics>();
        }

        public override void StartExecute()
        {
            stepHeight = bhPhysics?.StepHeight ?? 0.6f;
            base.StartExecute();

            if (triggerEmotionState != null)
            {
                entity.GetBehavior<EntityBehaviorEmotionStates>()?.TryTriggerState(triggerEmotionState, 1, targetEntity?.EntityId ?? 0);
            }
            var physics = entity.GetBehavior<EntityBehaviorControlledPhysics>();
            if (physics != null)
            {
                stepHeight = physics.StepHeight;
            }
        }


        public virtual bool IsTargetableEntity(Entity e, float range, bool ignoreEntityCode = false)
        {
            if (!e.Alive) return false;
            if (ignoreEntityCode) return CanSense(e, range);

            if (IsTargetEntity(e.Code)) return CanSense(e, range);

            return false;
        }

        private bool IsTargetEntity(AssetLocation code)
        {
            for (int i = 0; i < targetEntityCodes.Length; ++i) {
                if (WildcardUtil.Match(targetEntityCodes[i], code)) {
                    return true;
                }
            }

            return false;
        }

        public virtual bool CanSense(Entity e, double range)
        {
            if (e.EntityId == entity.EntityId || !e.IsInteractable) return false;
            if (e is EntityPlayer eplr) return CanSensePlayer(eplr, range);

            if (skipEntityCodes != null)
            {
                for (int i = 0; i < skipEntityCodes.Length; i++)
                {
                    if (WildcardUtil.Match(skipEntityCodes[i], e.Code)) return false;
                }
            }

            return true;
        }

        public virtual bool CanSensePlayer(EntityPlayer eplr, double range)
        {
            if (!friendlyTarget && AggressiveTargeting)
            {
                if (creatureHostility == EnumCreatureHostility.NeverHostile) return false;
                if (creatureHostility == EnumCreatureHostility.Passive && (bhEmo == null || (!IsInEmotionState("aggressiveondamage") && !IsInEmotionState("aggressivearoundentities")))) return false;
            }

            float rangeMul = eplr.Stats.GetBlended("animalSeekingRange");
            IPlayer player = eplr.Player;

            // Sneaking reduces the detection range
            if (eplr.Controls.Sneak && eplr.OnGround)
            {
                rangeMul *= 0.6f;
            }

            if ((rangeMul == 1 || entity.ServerPos.DistanceTo(eplr.Pos) < range * rangeMul)
                && targetablePlayerMode(player)
                && entity.ServerPos.Dimension == eplr.Pos.Dimension) return true;
            
            return false;
        }

        protected virtual bool targetablePlayerMode(IPlayer player)
        {
            return (player == null || (player.WorldData.CurrentGameMode != EnumGameMode.Creative && player.WorldData.CurrentGameMode != EnumGameMode.Spectator && (player as IServerPlayer).ConnectionState == EnumClientState.Playing));
        }

        protected BlockSelection blockSel = new BlockSelection();
        protected EntitySelection entitySel = new EntitySelection();

        protected readonly Vec3d rayTraceFrom = new Vec3d();
        protected readonly Vec3d rayTraceTo = new Vec3d();
        protected readonly Vec3d tmpPos = new Vec3d();
        protected virtual bool hasDirectContact(Entity targetEntity, float minDist, float minVerDist)
        {
            if (targetEntity.Pos.Dimension != entity.Pos.Dimension) return false;

            Cuboidd targetBox = targetEntity.SelectionBox.ToDouble().Translate(targetEntity.ServerPos.X, targetEntity.ServerPos.Y, targetEntity.ServerPos.Z);
            tmpPos.Set(entity.ServerPos).Add(0, entity.SelectionBox.Y2 / 2, 0).Ahead(entity.SelectionBox.XSize / 2, 0, entity.ServerPos.Yaw);
            double dist = targetBox.ShortestDistanceFrom(tmpPos);
            double vertDist = Math.Abs(targetBox.ShortestVerticalDistanceFrom(tmpPos.Y));
            if (dist >= minDist || vertDist >= minVerDist) return false;

            rayTraceFrom.Set(entity.ServerPos);
            rayTraceFrom.Y += 1 / 32f;
            rayTraceTo.Set(targetEntity.ServerPos);
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


        Vec3d tmpVec = new Vec3d();
        protected void updateTargetPosFleeMode(Vec3d targetPos, float yaw)
        {
            // Simple steering behavior
            tmpVec = tmpVec.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            tmpVec.Ahead(0.9, 0, yaw);

            // Try straight
            if (traversable(tmpVec))
            {
                targetPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z).Ahead(10, 0, yaw);
                return;
            }

            // Try 90 degrees left
            tmpVec = tmpVec.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            tmpVec.Ahead(0.9, 0, yaw - GameMath.PIHALF);
            if (traversable(tmpVec))
            {
                targetPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z).Ahead(10, 0, yaw - GameMath.PIHALF);
                return;
            }

            // Try 90 degrees right
            tmpVec = tmpVec.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            tmpVec.Ahead(0.9, 0, yaw + GameMath.PIHALF);
            if (traversable(tmpVec))
            {
                targetPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z).Ahead(10, 0, yaw + GameMath.PIHALF);
                return;
            }

            // Try backwards
            tmpVec = tmpVec.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            tmpVec.Ahead(0.9, 0, yaw + GameMath.PI);
            targetPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z).Ahead(10, 0, yaw + GameMath.PI);
        }


        protected Vec3d collTmpVec = new Vec3d();
        protected float stepHeight;
        protected bool traversable(Vec3d pos)
        {
            return
                !world.CollisionTester.IsColliding(world.BlockAccessor, entity.SelectionBox, pos, false) ||
                !world.CollisionTester.IsColliding(world.BlockAccessor, entity.SelectionBox, collTmpVec.Set(pos).Add(0, Math.Min(1, stepHeight), 0), false)
            ;
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

        public IBlockAccessor blockAccessor { get => entity.World.BlockAccessor; }

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
                return entity.World.PlayerByUid(uid)?.Entity;
            }
            else
            {
                var id = entity.WatchedAttributes.GetLong("guardedEntityId");
                return entity.World.GetEntityById(id);
            }
        }

        public int GetOwnGeneration()
        {
            int generation = entity.WatchedAttributes.GetInt("generation", 0);
            if (entity.Properties.Attributes?.IsTrue("tamed") == true) generation += 10;
            return generation;
        }
        protected bool isNonAttackingPlayer(Entity e)
        {
            return attackedByEntity != null && attackedByEntity.EntityId != e.EntityId && e is EntityPlayer;
        }



        public override void OnEntityHurt(DamageSource source, float damage)
        {
            attackedByEntity = source.GetCauseEntity();
            attackedByEntityMs = entity.World.ElapsedMilliseconds;
            base.OnEntityHurt(source, damage);
        }

        public void ClearAttacker()
        {
            attackedByEntity = null;
            attackedByEntityMs = -9999;
        }

    }
}
