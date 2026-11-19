using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent {
    public class FailedNavigationAttempt
    {
        public long LastTryMs;
        public int Count;
    }

    public abstract class AiTaskSeekPoiBase<T> : AiTaskBase where T : IPointOfInterest {
        protected POIRegistry pointsOfInterest;

        [JsonProperty]
        protected float moveSpeed = 0.02f;
        [JsonProperty("preferredGait")]
        private string? gaitCode = null;
        public GaitMeta? PreferredGait { get; set; } = null;

        /// <summary>
        /// Determines pathfinding behavior, <see cref="EnumAICreatureType"/>.
        /// </summary>
        [JsonProperty]
        public EnumAICreatureType? creatureType = null;

        /// <summary>
        /// How far the entity can be from the target and still count as having reached it.
        /// This should generally be the distance from the center of the target to a collidable edge, plus some wriggle room
        /// Not used by SeekBlockAndLay, which expects to go inside the target rather than collide with it
        /// </summary>
        // Not made a json property, because in theory this ought to be a property of the target rather than the task
        protected float extraTargetDistance = 0.5f;

        protected long lastPOISearchTotalMs;
        protected long searchRate = 15000;
        protected Dictionary<T, FailedNavigationAttempt> failedSeekTargets = new Dictionary<T, FailedNavigationAttempt>();
        protected T? targetPoi;
        protected float timeSinceTargetReached;
        protected bool done = false;

        public AiTaskSeekPoiBase(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)  : base(entity, taskConfig, aiConfig)
        {
            pointsOfInterest = entity.Api.ModLoader.GetModSystem<POIRegistry>() ?? throw new Exception("A POIRegistry is required for seeking point of interest task");
        }

        public override void AfterInitialize()
        {
            base.AfterInitialize();
            if (gaitCode != null)
            {
                EntityBehaviorGait? gaitBehavior = entity.GetBehavior<EntityBehaviorGait>();
                if (gaitBehavior != null)
                {
                    PreferredGait = gaitBehavior.Gaits.TryGetValue(gaitCode);
                    if (PreferredGait == null)
                    {
                        entity.Api.Logger.Warning("Json Error: Entity " + entity.Code + " could not find gait " + gaitCode);
                    }
                }
                else
                {
                    entity.Api.Logger.Warning("Json Error: Entity " + entity.Code + " has a gait code " + gaitCode + " set, but no gait behavior. It will be ignored.");
                }
            }
        }

        protected bool IsSearchTime()
        {
            return lastPOISearchTotalMs + searchRate <= entity.World.ElapsedMilliseconds
                && !IsOnCooldown()
                && PreconditionsSatisfied();
        }

        protected bool RecentlyFailedSeek(T poi)
        {
            failedSeekTargets.TryGetValue(poi, out FailedNavigationAttempt? attempt);
            return attempt != null && (attempt.LastTryMs + 60000 > entity.World.ElapsedMilliseconds || (attempt.Count > 4 && attempt.LastTryMs + 240000 > entity.World.ElapsedMilliseconds));
        }

        public override void StartExecute()
        {
            base.StartExecute();
            done = false;
            timeSinceTargetReached = 0;
            ArgumentNullException.ThrowIfNull(targetPoi);
            pathTraverser.NavigateTo_Async(targetPoi.Position, moveSpeed, MinDistanceToTarget(), OnGoalReached, OnStuck, null, 1000, 1, creatureType, PreferredGait);
        }

        public override bool CanContinueExecute()
        {
            return pathTraverser.Ready;
        }

        public virtual float MinDistanceToTarget()
        {
            return entity.SelectionBox.XSize / 2 + extraTargetDistance;
        }

        public override bool ContinueExecute(float dt)
        {
            if (done || ShouldAbort() || targetPoi == null)
            {
                return false;
            }

            Vec3d pos = targetPoi.Position;
            double distance = pos.HorizontalSquareDistanceTo(entity.Pos.X, entity.Pos.Z);

            pathTraverser.CurrentTarget!.Set(pos);

            float minDist = MinDistanceToTarget();
            if (distance <= minDist * minDist)
            {
                pathTraverser.Stop();
                failedSeekTargets.Remove(targetPoi);
                if (animMeta != null)
                {
                    entity.AnimManager.StopAnimation(animMeta.Code);
                }

                if (timeSinceTargetReached == 0)
                {
                    OnArrival();
                }
                timeSinceTargetReached += dt;
                TickTargetReached();
            }
            else if (!pathTraverser.Active)
            {
                // Return to the target if another animal has pushed us out of the way
                float rndx = (float)entity.World.Rand.NextDouble() * 0.2f - 0.1f;
                float rndz = (float)entity.World.Rand.NextDouble() * 0.2f - 0.1f;
                pathTraverser.NavigateTo_Async(targetPoi.Position.AddCopy(rndx, 0, rndz), moveSpeed, MinDistanceToTarget(), OnGoalReached, OnStuck, null, 1000, 1, creatureType, PreferredGait);
            }

            return !done;
        }


        public override void FinishExecute(bool cancelled)
        {
            base.FinishExecute(cancelled);
            pathTraverser.Stop();

            if (animMeta != null)
            {
                entity.AnimManager.StopAnimation(animMeta.Code);
            }
        }

        protected virtual void OnStuck()
        {
            done = true;

            if (targetPoi == null) return;

            failedSeekTargets.TryGetValue(targetPoi, out FailedNavigationAttempt? attempt);
            if (attempt == null)
            {
                failedSeekTargets[targetPoi] = attempt = new FailedNavigationAttempt();
            }

            attempt.Count++;
            attempt.LastTryMs = entity.World.ElapsedMilliseconds;
        }

        protected virtual void OnGoalReached()
        {
            if (targetPoi != null)
            {
                failedSeekTargets.Remove(targetPoi);
            }
        }

        protected virtual bool ShouldAbort()
        {
            return !IsInValidDayTimeHours(false);
        }

        protected abstract void OnArrival();

        protected abstract void TickTargetReached();
    }
}
