using System;
using System.Collections.Generic;
using System.Linq;
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
        protected string[] targetEntityCodesBeginsWith = new string[0];
        protected string[] targetEntityCodesExact;

        protected AssetLocation[] skipEntityCodes;

        protected string targetEntityFirstLetters = "";

        protected string creatureHostility;
        protected bool friendlyTarget;

        public Entity targetEntity;

        public virtual bool AggressiveTargeting => true;

        public Entity TargetEntity => targetEntity;

        protected Entity attackedByEntity;
        protected long attackedByEntityMs;
        protected bool retaliateAttacks = true;

        public string triggerEmotionState;

        protected bool noEntityCodes => targetEntityCodesExact.Length == 0 && targetEntityCodesBeginsWith.Length == 0;

        protected EntityPartitioning partitionUtil;
        protected EntityBehaviorControlledPhysics bhPhysics;

        protected AiTaskBaseTargetable(EntityAgent entity) : base(entity)
        {
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            base.LoadConfig(taskConfig, aiConfig);

            partitionUtil = entity.Api.ModLoader.GetModSystem<EntityPartitioning>();

            creatureHostility = entity.World.Config.GetString("creatureHostility");

            friendlyTarget = taskConfig["friendlyTarget"].AsBool(false);

            retaliateAttacks = taskConfig["retaliateAttacks"].AsBool(true);

            this.triggerEmotionState = taskConfig["triggerEmotionState"].AsString();

            skipEntityCodes = taskConfig["skipEntityCodes"].AsArray<string>()?.Select(str => AssetLocation.Create(str, entity.Code.Domain)).ToArray();

            string[] codes = taskConfig["entityCodes"].AsArray<string>(new string[] { "player" });
            InitializeTargetCodes(codes, ref targetEntityCodesExact, ref targetEntityCodesBeginsWith, ref targetEntityFirstLetters);
        }

        /// <summary>
        /// Makes a similar sytem - "target codes from an array of entity codes with or without wildcards" - available to any other game element which requires it
        /// </summary>
        /// <param name="codes"></param>
        /// <param name="targetEntityCodesExact"></param>
        /// <param name="targetEntityCodesBeginsWith"></param>
        /// <param name="targetEntityFirstLetters"></param>
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
            stepHeight = bhPhysics?.stepHeight ?? 0.6f;
            base.StartExecute();

            if (triggerEmotionState != null)
            {
                entity.GetBehavior<EntityBehaviorEmotionStates>()?.TryTriggerState(triggerEmotionState, 1, targetEntity?.EntityId ?? 0);
            }
        }


        public virtual bool IsTargetableEntity(Entity e, float range, bool ignoreEntityCode = false)
        {
            if (!e.Alive) return false;
            if (ignoreEntityCode) return CanSense(e, range);

            if (IsTargetEntity(e.Code.Path)) return CanSense(e, range);

            return false;
        }

        private bool IsTargetEntity(string testPath)
        {
            if (targetEntityFirstLetters.Length == 0) return true;     // target everything (there was a universal wildcard "*", for example BeeMob)
            if (targetEntityFirstLetters.IndexOf(testPath[0]) < 0) return false;   // early exit if we don't have the first letter

            for (int i = 0; i < targetEntityCodesExact.Length; i++)
            {
                if (testPath == targetEntityCodesExact[i]) return true;
            }

            for (int i = 0; i < targetEntityCodesBeginsWith.Length; i++)
            {
                if (testPath.StartsWithFast(targetEntityCodesBeginsWith[i])) return true;
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
                if (creatureHostility == "off") return false;
                if (creatureHostility == "passive" && (bhEmo == null || (!IsInEmotionState("aggressiveondamage") && !IsInEmotionState("aggressivearoundentities")))) return false;
            }

            float rangeMul = eplr.Stats.GetBlended("animalSeekingRange");
            IPlayer player = eplr.Player;

            // Sneaking reduces the detection range
            if (eplr.Controls.Sneak && eplr.OnGround)
            {
                rangeMul *= 0.6f;
            }

            return
                (rangeMul == 1 || entity.ServerPos.DistanceTo(eplr.Pos) < range * rangeMul) &&
                (player == null || (player.WorldData.CurrentGameMode != EnumGameMode.Creative && player.WorldData.CurrentGameMode != EnumGameMode.Spectator && (player as IServerPlayer).ConnectionState == EnumClientState.Playing))
            ;
        }

        protected BlockSelection blockSel = new BlockSelection();
        protected EntitySelection entitySel = new EntitySelection();

        protected readonly Vec3d rayTraceFrom = new Vec3d();
        protected readonly Vec3d rayTraceTo = new Vec3d();
        protected readonly Vec3d tmpPos = new Vec3d();
        protected virtual bool hasDirectContact(Entity targetEntity, float minDist, float minVerDist)
        {
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
        protected void updateTargetPosFleeMode(Vec3d targetPos)
        {
            float yaw = (float)Math.Atan2(targetEntity.ServerPos.X - entity.ServerPos.X, targetEntity.ServerPos.Z - entity.ServerPos.Z);

            // Simple steering behavior
            tmpVec = tmpVec.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            tmpVec.Ahead(0.9, 0, yaw - GameMath.PI / 2);

            // Running into wall?
            if (traversable(tmpVec))
            {
                targetPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z).Ahead(10, 0, yaw - GameMath.PI / 2);
                return;
            }

            // Try 90 degrees left
            tmpVec = tmpVec.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            tmpVec.Ahead(0.9, 0, yaw - GameMath.PI);
            if (traversable(tmpVec))
            {
                targetPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z).Ahead(10, 0, yaw - GameMath.PI);
                return;
            }

            // Try 90 degrees right
            tmpVec = tmpVec.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            tmpVec.Ahead(0.9, 0, yaw);
            if (traversable(tmpVec))
            {
                targetPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z).Ahead(10, 0, yaw);
                return;
            }

            // Run towards target o.O
            tmpVec = tmpVec.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            tmpVec.Ahead(0.9, 0, -yaw);
            targetPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z).Ahead(10, 0, -yaw);
        }


        Vec3d collTmpVec = new Vec3d();
        float stepHeight;
        bool traversable(Vec3d pos)
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


        public override void OnEntityHurt(DamageSource source, float damage)
        {
            attackedByEntity = source.GetCauseEntity();
            attackedByEntityMs = entity.World.ElapsedMilliseconds;
            base.OnEntityHurt(source, damage);
        }

    }
}
