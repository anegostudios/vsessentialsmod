using Newtonsoft.Json;
using System;
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
        protected TagCondition<EntityTagSet>[] EntityTags = [];

        protected TagCondition<EntityTagSet>[] SkipEntityTags = [];

        protected bool noTags = true;

        [JsonProperty]
        protected bool reverseTagsCheck = false;


        [JsonProperty]
        protected float MinTargetWeight = 0;
        [JsonProperty]
        protected float MaxTargetWeight = float.MaxValue;

        protected string[] targetEntityCodesBeginsWith = Array.Empty<string>();
        protected string[] targetEntityCodesExact = null!;

        protected AssetLocation[]? skipEntityCodes;

        protected string targetEntityFirstLetters = "";

        protected EnumCreatureHostility creatureHostility;
        [JsonProperty]
        protected bool friendlyTarget = false;

        public Entity? targetEntity;

        public virtual bool AggressiveTargeting => true;

        public Entity? TargetEntity => targetEntity;

        protected Entity? attackedByEntity;
        protected long attackedByEntityMs;
        [JsonProperty]
        protected bool retaliateAttacks = true;

        [JsonProperty]
        public string? triggerEmotionState;
        [JsonProperty]
        protected float tamingGenerations = 10f;

        protected bool noEntityCodes => targetEntityCodesExact.Length == 0 && targetEntityCodesBeginsWith.Length == 0;

        protected EntityPartitioning partitionUtil;
        protected EntityBehaviorControlledPhysics? bhPhysics;

        protected bool RecentlyAttacked => entity.World.ElapsedMilliseconds - attackedByEntityMs < 30000;

        /// <summary> If set to true all entities with 'IsInteractable' flag set to false will be ignored. </summary>
        [JsonProperty]
        public bool TargetOnlyInteractableEntities = true;

        protected AiTaskBaseTargetable(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
        {
            partitionUtil = entity.Api.ModLoader.GetModSystem<EntityPartitioning>();

            creatureHostility = entity.World.Config.GetString("creatureHostility") switch
            {
                "aggressive" => EnumCreatureHostility.Aggressive,
                "passive" => EnumCreatureHostility.Passive,
                "off" => EnumCreatureHostility.NeverHostile,
                _ => EnumCreatureHostility.Aggressive
            };

            skipEntityCodes = taskConfig["skipEntityCodes"].AsArray<string>()?.Select(str => AssetLocation.Create(str, entity.Code.Domain)).ToArray();

            string?[] codes = taskConfig["entityCodes"].AsArray<string>(new string[] { "player" });
            for (int i = 0; i < codes.Length; i++)
            {
                if (codes[i] == null)
                {
                    throw new ArgumentNullException(GetType().ToString() + " on entity " + entity.Code + " was initialized with a null target code at index " + i);
                }
            }
            InitializeTargetCodes(codes!, ref targetEntityCodesExact!, ref targetEntityCodesBeginsWith, ref targetEntityFirstLetters);

            List<List<string>> entityTags = taskConfig["entityTags"].AsObject<List<List<string>>>([]);

            List<List<string>> skipEntityTags = taskConfig["skipEntityTags"].AsObject<List<List<string>>>([]);

            if (entityTags != null)
            {
                EntityTags = [.. entityTags.Select(tagList => TagCondition<EntityTagSet>.Get(entity.Api, tagList.ToArray()))];
            }
            if (skipEntityTags != null)
            {
                SkipEntityTags = [.. skipEntityTags.Select(tagList => TagCondition<EntityTagSet>.Get(entity.Api, tagList.ToArray()))];
            }

            noTags = EntityTags.Length == 0 && SkipEntityTags.Length == 0;
        }

        /// <summary>
        /// Makes a similar sytem - "target codes from an array of entity codes with or without wildcards" - available to any other game element which requires it
        /// </summary>
        /// <param name="codes"></param>
        /// <param name="targetEntityCodesExact"></param>
        /// <param name="targetEntityCodesBeginsWith"></param>
        /// <param name="targetEntityFirstLetters"></param>
        public static void InitializeTargetCodes(string[] codes, ref string[]? targetEntityCodesExact, ref string[] targetEntityCodesBeginsWith, ref string targetEntityFirstLetters)
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

        protected virtual bool CheckTargetWeight(float weight)
        {
            float weightFraction = entity.Properties.Weight > 0 ? weight / entity.Properties.Weight : float.MaxValue;
            if (MinTargetWeight > 0 && weightFraction < MinTargetWeight) return false;
            if (MaxTargetWeight > 0 && weightFraction > MaxTargetWeight) return false;
            return true;
        }

        protected virtual bool CheckTargetTags(EntityTagSet tags)
        {
            if (!reverseTagsCheck)
            {
                if (TagCondition<EntityTagSet>.OverlapsWithEach(tags, EntityTags))
                {
                    if (SkipEntityTags.Length == 0) return true;

                    if (!reverseTagsCheck)
                    {
                        if (!TagCondition<EntityTagSet>.OverlapsWithEach(tags, SkipEntityTags)) return true;
                    }
                    else
                    {
                        if (!TagCondition<EntityTagSet>.SupersetOfAtLeastOne(tags, SkipEntityTags)) return true;
                    }
                }
            }
            else
            {
                if (TagCondition<EntityTagSet>.SupersetOfAtLeastOne(tags, EntityTags))
                {
                    if (SkipEntityTags.Length == 0) return true;

                    if (!reverseTagsCheck)
                    {
                        if (!TagCondition<EntityTagSet>.OverlapsWithEach(tags, SkipEntityTags)) return true;
                    }
                    else
                    {
                        if (!TagCondition<EntityTagSet>.SupersetOfAtLeastOne(tags, SkipEntityTags)) return true;
                    }
                }
            }

            return false;
        }


        public virtual bool IsTargetableEntityWithTags(Entity e, float range)
        {
            if (CheckTargetTags(e.Tags) && CheckTargetWeight(e.Properties.Weight)) return e.Alive && CanSense(e, range);
            if (targetEntityFirstLetters.Length == 0 || IsTargetEntity(e.Code.Path)) return e.Alive && CanSense(e, range);

            return false;
        }

        /// <summary>
        /// Optimised code path for most common case in inner search loop
        /// </summary>
        public virtual bool IsTargetableEntityNoTagsNoAll(Entity e, float range)
        {
            if (IsTargetEntity(e.Code.Path)) return e.Alive && CanSense(e, range);

            return false;
        }

        /// <summary>
        /// Optimised code path for simplified case in inner search loop
        /// </summary>
        public virtual bool IsTargetableEntityNoTagsAll(Entity e, float range)
        {
            return e.Alive && CanSense(e, range);
        }

        public virtual bool IsTargetableEntity(Entity e, float range)
        {
            if (!e.Alive) return false;
            if (!noTags && CheckTargetTags(e.Tags) && CheckTargetWeight(e.Properties.Weight)) return CanSense(e, range);
            // targetEntityFirstLetters.Length == 0 means target everything (there was a universal wildcard "*", for example BeeMob)
            if (targetEntityFirstLetters.Length == 0 || IsTargetEntity(e.Code.Path)) return CanSense(e, range);

            return false;
        }

        protected bool IsTargetEntity(string testPath)
        {
            if (targetEntityFirstLetters.IndexOf(testPath[0]) < 0) return false;   // early exit if we don't have the first letter

            var targetEntityCodes = this.targetEntityCodesExact;
            for (int i = 0; i < targetEntityCodes.Length; i++)
            {
                if (testPath == targetEntityCodes[i]) return true;
            }

            targetEntityCodes = this.targetEntityCodesBeginsWith;
            for (int i = 0; i < targetEntityCodes.Length; i++)
            {
                if (testPath.StartsWithFast(targetEntityCodes[i])) return true;
            }

            return false;
        }

        public virtual bool CanSense(Entity e, double range)
        {
            if (e.EntityId == entity.EntityId || (TargetOnlyInteractableEntities && !e.IsInteractable)) return false;
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

            if ((rangeMul == 1 || entity.Pos.DistanceTo(eplr.Pos) < range * rangeMul)
                && targetablePlayerMode(player)
                && entity.Pos.Dimension == eplr.Pos.Dimension) return true;

            return false;
        }

        protected virtual bool targetablePlayerMode(IPlayer? player)
        {
            return (player == null || (player.WorldData.CurrentGameMode != EnumGameMode.Creative && player.WorldData.CurrentGameMode != EnumGameMode.Spectator && (player as IServerPlayer)?.ConnectionState == EnumClientState.Playing));
        }

        protected BlockSelection? blockSel = new BlockSelection();
        protected EntitySelection? entitySel = new EntitySelection();

        protected readonly Vec3d rayTraceFrom = new Vec3d();
        protected readonly Vec3d rayTraceTo = new Vec3d();
        protected readonly Vec3d tmpPos = new Vec3d();
        protected virtual bool hasDirectContact(Entity targetEntity, float minDist, float minVerDist)
        {
            if (targetEntity.Pos.Dimension != entity.Pos.Dimension) return false;

            Cuboidd targetBox = targetEntity.SelectionBox.ToDouble().Translate(targetEntity.Pos.X, targetEntity.Pos.Y, targetEntity.Pos.Z);
            tmpPos.Set(entity.Pos).Add(0, entity.SelectionBox.Y2 / 2, 0).Ahead(entity.SelectionBox.XSize / 2, 0, entity.Pos.Yaw);
            double dist = targetBox.ShortestDistanceFrom(tmpPos);
            double vertDist = Math.Abs(targetBox.ShortestVerticalDistanceFrom(tmpPos.Y));
            if (dist >= minDist || vertDist >= minVerDist) return false;

            rayTraceFrom.Set(entity.Pos);
            rayTraceFrom.Y += 1 / 32f;
            rayTraceTo.Set(targetEntity.Pos);
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
            tmpVec = tmpVec.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
            tmpVec.Ahead(0.9, 0, yaw);

            // Try straight
            if (traversable(tmpVec))
            {
                targetPos.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z).Ahead(10, 0, yaw);
                return;
            }

            // Try 90 degrees left
            tmpVec = tmpVec.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
            tmpVec.Ahead(0.9, 0, yaw - GameMath.PIHALF);
            if (traversable(tmpVec))
            {
                targetPos.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z).Ahead(10, 0, yaw - GameMath.PIHALF);
                return;
            }

            // Try 90 degrees right
            tmpVec = tmpVec.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
            tmpVec.Ahead(0.9, 0, yaw + GameMath.PIHALF);
            if (traversable(tmpVec))
            {
                targetPos.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z).Ahead(10, 0, yaw + GameMath.PIHALF);
                return;
            }

            // Try backwards
            tmpVec = tmpVec.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
            tmpVec.Ahead(0.9, 0, yaw + GameMath.PI);
            targetPos.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z).Ahead(10, 0, yaw + GameMath.PI);
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


        public Entity[] GetEntitiesAround(Vec3d position, float horRange, float vertRange, ActionConsumable<Entity>? matches = null)
        {
            return Array.Empty<Entity>();
        }

        public Entity? GetGuardedEntity()
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
            return (attackedByEntity == null || (attackedByEntity != null && attackedByEntity.EntityId != e.EntityId)) && e is EntityPlayer;
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

        protected virtual bool ShouldRetaliateForRange(float range)
        {
            return retaliateAttacks &&
                attackedByEntity != null &&
                attackedByEntity.Alive &&
                attackedByEntity.IsInteractable &&
                CanSense(attackedByEntity, range) &&
                !IgnoreDamageFrom(attackedByEntity);
        }

        public virtual bool IgnoreDamageFrom(Entity attacker)
        {
            return entity.ToleratesDamageFrom(attacker);
        }

    }
}
