using Newtonsoft.Json;
using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class AiTaskStayCloseToEntity : AiTaskBaseTargetable
    {
        // Kept for backwards compatibility. The same json value will set this and optionalMoveSpeed. If code mods use this, try to keep this and optionalMoveSpeed the same.
        protected float moveSpeed = 0.03f;
        /// <summary>
        /// Speed to move during the task. If gaits are used for navigation, setting this overrides that and replaces the dynamic speed changes.
        /// </summary>
        [JsonProperty("moveSpeed")]
        protected float? optionalMoveSpeed;

        [JsonProperty("fastestGait")]
        private string? gaitCode = null;
        public GaitMeta? FastestGait { get; set; } = null;
        /// <summary>
        /// Affects pathfinding, <see cref="EnumAICreatureType"/>. If not set, value from entity attributes will be used.
        /// </summary>
        [JsonProperty]
        public EnumAICreatureType? creatureType = null;

        /// <summary>
        /// Tries to stay within this distance of the target. When farther away than this, will move towards the target.
        /// </summary>
        [JsonProperty]
        protected float maxDistance = 3f;

        /// <summary>
        /// Once moving towards the target, get this close before stopping.
        /// </summary>
        [JsonProperty]
        protected float arrivalDistance = 3f;

        protected bool stuck = false;

        /// <summary>
        /// If true, the target entity must have a lower entity ID (existed in the world for longer).
        /// Not recommended for animals because growth to adulthood and trap/release both raise entity ID.
        /// </summary>
        [JsonProperty]
        protected bool onlyIfLowerId = false;

        /// <summary>
        /// Allow for entity to teleport when stuck or too far away.
        /// </summary>
        [JsonProperty]
        protected bool allowTeleport = false;

        /// <summary>
        /// Range after which entity will try to teleport to target.
        /// </summary>
        [JsonProperty]
        protected float teleportAfterRange = 30f;

        /// <summary>
        /// Chance to teleport at each tick, if should teleport.
        /// </summary>
        [JsonProperty]
        public float TeleportChance { get; set; } = 0.05f;

        /// <summary>
        /// Max distance between target and teleport destination.
        /// </summary>
        [JsonProperty]
        protected float teleportToRange = 4.5f;

        /// <summary>
        /// Delay between task start and entity being able to teleport.
        /// </summary>
        [JsonProperty]
        public float TeleportDelaySec = 4f;

        /// <summary>
        /// If the target is more than this far away, it cannot be reached even by teleporting.
        /// </summary>
        [JsonProperty]
        public int TeleportMaxRange = int.MaxValue;

        /// <summary>
        /// Time until which entity will try to find path to target or teleport to it.
        /// </summary>
        [JsonProperty]
        public float minSeekSeconds = 3f;

        protected FastVec3d targetOffset;
        // For retargeting navigation
        protected FastVec3d initialTargetPos;
        // For tracking and matching target entity's speed
        protected FastVec3d prevTargetPos;
        protected FastVec3d prevPrevTargetPos;
        protected long lastTargetPosMs;
        protected long prevTargetPosMs;
        protected long prevPrevTargetPosMs;

        public int allowTeleportCount = 0;
        protected float retargetDistance = 2f;

        float executingSeconds = 0;

        protected int stuckCounter;
        protected EntityBehaviorGait? gaitBehavior;
        protected bool fallingBack = false;

        public AiTaskStayCloseToEntity(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
        {
            moveSpeed = taskConfig["moveSpeed"].AsFloat(moveSpeed);

            string? entityCode = taskConfig["entityCode"].AsString();
            if (entityCode != null)
            {
                // Obsolete json setup. May log a warning later, but leave it for now so as not to flood the logs.
                InitializeTargetCodes([ entityCode ], ref targetEntityCodesExact, ref targetEntityCodesBeginsWith, ref targetEntityFirstLetters);
            }
        }

        protected override void SetDefaultValues()
        {
            base.SetDefaultValues();
            ExecutionChance = 0.01;
            searchRange = 8f;
        }

        public override void AfterInitialize()
        {
            gaitBehavior = entity.GetBehavior<EntityBehaviorGait>();
            if (gaitBehavior?.NavigateUsingGaits == true)
            {
                FastestGait = gaitCode != null ? gaitBehavior.Gaits[gaitCode] : gaitBehavior.Gaits.Values.MaxBy(g => g.MoveSpeed);
            }
            else
            {
                optionalMoveSpeed ??= moveSpeed;
            }
        }

        public override bool ShouldExecute()
        {
            if (stuckCounter > 3)
            {
                stuckCounter = 0;
                cooldownUntilMs = entity.World.ElapsedMilliseconds + 60 * 1000;
            }
            if (IsOnCooldown()) return false;

            if (rand.NextDouble() > ExecutionChance) return false;
            if (!PreconditionsSatisfied()) return false;

            if (targetEntity == null || !targetEntity.Alive)
            {
                targetEntity = partitionUtil.GetNearestEntity(entity.Pos.XYZ, searchRange, (e) => IsTargetableEntity(e, searchRange), SearchType);
            }

            if (targetEntity == null) return false;

            double dist = entity.Pos.SquareDistanceTo(targetEntity.Pos);

            return dist > maxDistance * maxDistance;
        }

        public override bool IsTargetableEntity(Entity e, float range)
        {
            if (onlyIfLowerId && e.EntityId > entity.EntityId) return false;
            return base.IsTargetableEntity(e, range);
        }


        public override void StartExecute()
        {
            if (targetEntity == null) return;

            base.StartExecute();

            executingSeconds = 0;
            fallingBack = false;
            initialTargetPos.Set(targetEntity.Pos.X, targetEntity.Pos.InternalY, targetEntity.Pos.Z);
            prevTargetPos.Set(initialTargetPos);
            prevPrevTargetPos.Set(initialTargetPos);
            lastTargetPosMs = entity.World.ElapsedMilliseconds;
            prevTargetPosMs = lastTargetPosMs - 1;
            prevPrevTargetPosMs = prevTargetPosMs - 1;

            if (targetEntity.Pos.DistanceTo(entity.Pos) > TeleportMaxRange)
            {
                stuck = true;
                return;
            }

            float extraDistance = (targetEntity.SelectionBox.XSize + entity.SelectionBox.XSize) / 2 + 0.2f;
            if (optionalMoveSpeed != null)
            {
                pathTraverser.NavigateTo_Async(targetEntity.Pos.XYZ, optionalMoveSpeed.Value, extraDistance, OnGoalReached, OnStuck, OnNoPath, 1000, 1, creatureType, FastestGait);
            }
            else
            {
                GaitMeta? nextGait = ChooseNextGait(initialTargetPos);
                pathTraverser.NavigateTo_Async(targetEntity.Pos.XYZ, nextGait?.MoveSpeed ?? moveSpeed, extraDistance, OnGoalReached, OnStuck, OnNoPath, 1000, 1, creatureType, nextGait);
            }

            targetOffset.Set(entity.World.Rand.NextDouble() * 2 - 1, 0, entity.World.Rand.NextDouble() * 2 - 1);
            stuck = false;
        }

        public override bool CanContinueExecute()
        {
            return pathTraverser.Ready;
        }

        public override bool ContinueExecute(float dt)
        {
            if (targetEntity == null) return false;

            if (!base.ContinueExecute(dt)) return false;

            FastVec3d targetPos = targetEntity.Pos.XYZFast + targetOffset;
            if (optionalMoveSpeed == null)
            {
                GaitMeta? nextGait = ChooseNextGait(targetPos);
                if (nextGait != null && nextGait != pathTraverser.preferredGait)
                {
                    pathTraverser.preferredGait = nextGait;
                    pathTraverser.movingSpeed = nextGait.MoveSpeed;
                }
            }
            if (targetEntity.Pos.SquareDistanceTo(initialTargetPos) > retargetDistance * retargetDistance)
            {
                prevPrevTargetPos.Set(prevTargetPos);
                prevTargetPos.Set(initialTargetPos);
                initialTargetPos.Set(targetEntity.Pos.X, targetEntity.Pos.InternalY, targetEntity.Pos.Z);
                prevPrevTargetPosMs = prevTargetPosMs;
                prevTargetPosMs = lastTargetPosMs;
                lastTargetPosMs = entity.World.ElapsedMilliseconds;
                pathTraverser.Retarget();
            }

            pathTraverser.CurrentTarget!.Set(targetPos);

            double dist = entity.Pos.SquareDistanceTo(targetPos);

            if (dist <= arrivalDistance * arrivalDistance)
            {
                return false;
            }

            if ((allowTeleport || allowTeleportCount > 0) && executingSeconds > TeleportDelaySec && (dist > teleportAfterRange * teleportAfterRange || stuck) && entity.World.Rand.NextDouble() < TeleportChance)
            {
                tryTeleport();
            }

            executingSeconds += dt;

            return (!stuck && pathTraverser.Active) || executingSeconds < minSeekSeconds;
        }

        protected virtual GaitMeta? ChooseNextGait(FastVec3d targetPos)
        {
            double distance = entity.Pos.DistanceTo(targetPos);
            if ((fallingBack && distance > 0.75 * maxDistance + 0.25 * arrivalDistance)
                ||(!fallingBack && distance < 0.75 * arrivalDistance + 0.25 * maxDistance))
            {
                fallingBack = !fallingBack;
            }
            if (gaitBehavior?.NavigateUsingGaits != true)
            {
                entity.Api.Logger.Warning("Task for entity code " + entity.Code
                    + " was expected to have either a move speed or a navigating gait behavior. Setting move speed to the default.");
                optionalMoveSpeed = moveSpeed;
                return null;
            }
            // Check how fast the target is moving
            double targetMoveSpeed = (targetEntity.Pos.XYZFast - prevPrevTargetPos).Length() / (entity.World.ElapsedMilliseconds - prevPrevTargetPosMs);
            // 
            float maxMs = 5000;
            targetMoveSpeed = Math.Max(targetMoveSpeed, distance / maxMs);
            // Now we have targetMoveSpeed in blocks per millisecond, but we need it in movement per physics tick
            targetMoveSpeed *= 1000f / 60;

            EnumHabitat targetEnvironment = gaitBehavior.GetTargetEnvironment();
            GaitMeta nextGait = fallingBack ? gaitBehavior.IdleGaits[targetEnvironment] : FastestGait;
            foreach (GaitMeta gait in gaitBehavior.Gaits.Values)
            {
                if (!gait.Natural || gait.Environment != targetEnvironment || gait.Direction != EnumDirection.Forwards || gait.MoveSpeed > FastestGait.MoveSpeed) continue;
                if (nextGait.Direction != EnumDirection.Forwards
                    || (fallingBack && gait.EquilibriumMoveSpeed <= targetMoveSpeed && gait.MoveSpeed > nextGait.MoveSpeed)
                    || (!fallingBack && gait.EquilibriumMoveSpeed >= targetMoveSpeed && gait.MoveSpeed < nextGait.MoveSpeed))
                {
                    nextGait = gait;
                }
            }
            return nextGait;
        }

        protected virtual bool FindDecentTeleportPos(Entity targetEntity, out FastVec3d pos)
        {
            var blockAccessor = entity.World.BlockAccessor;
            var rnd = entity.World.Rand;

            pos = new();
            BlockPos blockPos = new(targetEntity.Pos.Dimension);
            for (int i = 0; i < 30; i++)
            {
                // On first teleport attempt, search within 2 blocks of the target, widening the search on each attempt
                float range = 2 + i / 30f * (teleportToRange - 2);

                double rndx = rnd.NextDouble() * 2 * range - range;
                double rndz = rnd.NextDouble() * 2 * range - range;

                for (int j = 0; j < 8; j++)
                {
                    // Produces: 0, -1, 1, -2, 2, -3, 3
                    int dy = (1 - (j % 2) * 2) * (int)Math.Ceiling(j / 2f);

                    pos.Set(targetEntity.Pos.X + rndx, targetEntity.Pos.InternalY + dy, targetEntity.Pos.Z + rndz);

                    blockPos.SetAndCorrectDimension((int)pos.X, (int)pos.Y, (int)pos.Z);
                    Block aboveBlock = blockAccessor.GetBlock(blockPos);
                    var boxes = aboveBlock.GetCollisionBoxes(blockAccessor, blockPos);
                    if (boxes != null && boxes.Length > 0) continue;
                    if (aboveBlock.Attributes?["insideDamage"].AsInt(0) > 0) continue;

                    blockPos.Down();
                    Block belowBlock = blockAccessor.GetBlock(blockPos);
                    boxes = belowBlock.GetCollisionBoxes(blockAccessor, blockPos);
                    if (boxes == null || boxes.Length == 0) continue;
                    if (belowBlock.Attributes?["insideDamage"].AsInt(0) > 0) continue;

                    pos.Y = (int)pos.Y - 1 + boxes.Max(c => c.Y2);

                    return true;
                }

            }

            return false;
        }


        protected void tryTeleport()
        {
            if ((!allowTeleport && allowTeleportCount <= 0) || targetEntity == null) return;
            bool found = FindDecentTeleportPos(targetEntity, out FastVec3d pos);
            if (found)
            {
                entity.TeleportToDouble(pos.X, pos.Y, pos.Z, () =>
                {
                    prevPrevTargetPos.Set(prevTargetPos);
                    prevTargetPos.Set(initialTargetPos);
                    initialTargetPos.Set(targetEntity.Pos.X, targetEntity.Pos.InternalY, targetEntity.Pos.Z);
                    prevPrevTargetPosMs = prevTargetPosMs;
                    prevTargetPosMs = lastTargetPosMs;
                    lastTargetPosMs = entity.World.ElapsedMilliseconds;
                    pathTraverser.Retarget();
                    allowTeleportCount = Math.Max(0, allowTeleportCount - 1);
                });
            }
        }


        public override void FinishExecute(bool cancelled)
        {
            if (stuck) stuckCounter++;
            else stuckCounter = 0;
            pathTraverser.Stop();
            base.FinishExecute(cancelled);
        }

        protected virtual void OnStuck()
        {
            stuck = true;
            //tryTeleport();
        }

        public virtual void OnNoPath()
        {
            // tryTeleport();
        }

        protected virtual void OnGoalReached()
        {
        }
    }
}
