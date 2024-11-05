using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class AiTaskSeekBlockAndLay : AiTaskBase
    {
        POIRegistry porregistry;
        IAnimalNest targetPoi;

        float moveSpeed = 0.02f;
        bool nowStuck = false;
        bool laid = false;

        /// <summary>
        /// Time in (game) days for the creature to sit on the nest (i.e. incubating eggs) before switching tasks (e.g. to feed)
        /// </summary>
        float sitDays = 1f;
        /// <summary>
        /// Time in (real) seconds for the creature to remain on the nest when laying a single egg
        /// </summary>
        float layTime = 1f;
        /// <summary>
        /// Cumulative time in (game) days for which a creature must sit before any fertile eggs will hatch
        /// </summary>
        double incubationDays = 5;
        /// <summary>
        /// Code for the chick to hatch from an egg laid by this creature
        /// </summary>
        string chickCode = null;
        /// <summary>
        /// Chance that an egg will be laid on the ground, if a search for the nest fails
        /// </summary>
        double onGroundChance = 0.3;
        AssetLocation failBlockCode;

        float sitTimeNow = 0;
        double sitEndDay = 0;
        bool sitAnimStarted = false;
        float PortionsEatenForLay;
        string requiresNearbyEntityCode;
        float requiresNearbyEntityRange = 5;

        AnimationMetaData sitAnimMeta;

        Dictionary<IAnimalNest, FailedAttempt> failedSeekTargets = new Dictionary<IAnimalNest, FailedAttempt>();

        long lastPOISearchTotalMs;

        double attemptLayEggTotalHours;

        public AiTaskSeekBlockAndLay(EntityAgent entity) : base(entity)
        {
            porregistry = entity.Api.ModLoader.GetModSystem<POIRegistry>();

            entity.WatchedAttributes.SetBool("doesSit", true);
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            base.LoadConfig(taskConfig, aiConfig);

            moveSpeed = taskConfig["movespeed"].AsFloat(0.02f);

            sitDays = taskConfig["sitDays"].AsFloat(1f);

            layTime = taskConfig["layTime"].AsFloat(1.5f);

            incubationDays = taskConfig["incubationDays"].AsDouble(5);

            if (taskConfig["sitAnimation"].Exists)
            {
                sitAnimMeta = new AnimationMetaData()
                {
                    Code = taskConfig["sitAnimation"].AsString()?.ToLowerInvariant(),
                    Animation = taskConfig["sitAnimation"].AsString()?.ToLowerInvariant(),
                    AnimationSpeed = taskConfig["sitAnimationSpeed"].AsFloat(1f)
                }.Init();
            }

            chickCode = taskConfig["chickCode"].AsString(null);
            PortionsEatenForLay = taskConfig["portionsEatenForLay"].AsFloat(3);
            requiresNearbyEntityCode = taskConfig["requiresNearbyEntityCode"].AsString(null);
            requiresNearbyEntityRange = taskConfig["requiresNearbyEntityRange"].AsFloat(5);
            string code = taskConfig["failBlockCode"].AsString(null);
            if (code != null) failBlockCode = new AssetLocation(code);
        }

        public override bool ShouldExecute()
        {
            // Don't check often: skip this 97% of the time
            if (entity.World.Rand.NextDouble() > 0.03) return false;
            // Don't search more often than every 15 seconds
            if (lastPOISearchTotalMs + 15000 > entity.World.ElapsedMilliseconds) return false;
            if (cooldownUntilMs > entity.World.ElapsedMilliseconds) return false;
            if (cooldownUntilTotalHours > entity.World.Calendar.TotalHours) return false;
            if (!PreconditionsSatisifed()) return false;


            PortionsEatenForLay = 3;

            // Now the behavior will certainly happen, we can consume food
            // Hen needs to be not hungry, in order to EITHER lay an egg OR sit and incubate for a long time
            if (!DidConsumeFood(PortionsEatenForLay)) return false;

            if (attemptLayEggTotalHours <= 0) attemptLayEggTotalHours = entity.World.Calendar.TotalHours;

            lastPOISearchTotalMs = entity.World.ElapsedMilliseconds;

            targetPoi = FindPOI(42) as IAnimalNest;

            if (targetPoi == null)
            {
                // Failed search: may lay an infertile egg on the ground
                LayEggOnGround();
            }

            return targetPoi != null;
        }


        private IPointOfInterest FindPOI(int radius)
        {
            // We want the hen to search for the most full HenBox nearby - so 'proximity' is weighted also according to how full the box is (see IAnimalNest.DistanceWeighting)
            return porregistry.GetWeightedNearestPoi(entity.ServerPos.XYZ, radius, (poi) =>
            {
                if (poi.Type != "nest") return false;
                IAnimalNest nestPoi;

                if ((nestPoi = poi as IAnimalNest)?.IsSuitableFor(entity) == true && !nestPoi.Occupied(entity))
                {
                    FailedAttempt attempt;
                    failedSeekTargets.TryGetValue(nestPoi, out attempt);
                    if (attempt == null || (attempt.Count < 4 || attempt.LastTryMs < world.ElapsedMilliseconds - 60000))
                    {
                        return true;
                    }
                }

                return false;
            });
        }

        public float MinDistanceToTarget()
        {
            return 0.01f;
        }

        public override void StartExecute()
        {
            // Do not call base method; we will only make the sound if actually lays
            if (animMeta != null)
            {
                animMeta.EaseInSpeed = 1f;
                animMeta.EaseOutSpeed = 1f;
                entity.AnimManager.StartAnimation(animMeta);
            }

            nowStuck = false;
            sitTimeNow = 0;
            laid = false;
            pathTraverser.NavigateTo_Async(targetPoi.Position, moveSpeed, MinDistanceToTarget() - 0.1f, OnGoalReached, OnStuck, null, 1000, 1);
            sitAnimStarted = false;
        }

        public override bool CanContinueExecute()
        {
            return pathTraverser.Ready;
        }

        public override bool ContinueExecute(float dt)
        {
            if (targetPoi.Occupied(entity))
            {
                onBadTarget();
                return false;
            }

            Vec3d pos = targetPoi.Position;
            double distance = pos.HorizontalSquareDistanceTo(entity.ServerPos.X, entity.ServerPos.Z);

            pathTraverser.CurrentTarget.X = pos.X;
            pathTraverser.CurrentTarget.Y = pos.Y;
            pathTraverser.CurrentTarget.Z = pos.Z;

            //Cuboidd targetBox = entity.CollisionBox.ToDouble().Translate(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
            //double distance = targetBox.ShortestDistanceFrom(pos);          

            float minDist = MinDistanceToTarget();

            if (distance <= minDist)
            {
                pathTraverser.Stop();
                if (animMeta != null)
                {
                    entity.AnimManager.StopAnimation(animMeta.Code);
                }

                EntityBehaviorMultiply bh = entity.GetBehavior<EntityBehaviorMultiply>();

                if (targetPoi.IsSuitableFor(entity) != true)
                {
                    onBadTarget();
                    return false;
                }

                targetPoi.SetOccupier(entity);
                
                if (sitAnimMeta != null && !sitAnimStarted)
                {
                    entity.AnimManager.StartAnimation(sitAnimMeta);                        

                    sitAnimStarted = true;
                    sitEndDay = entity.World.Calendar.TotalDays + sitDays;
                }

                sitTimeNow += dt;

                if (sitTimeNow >= layTime && !laid)
                {
                    laid = true;

                    // Potential gameplay/realism issue: the rooster has to be nearby at the exact moment the egg is laid, instead of looking to see whether there was a rooster / hen interaction ;) recently ...
                    // To mitigate this issue, we increase the rooster search range to 9 blocks in the JSON
                    if (targetPoi.TryAddEgg(entity, GetRequiredEntityNearby() == null ? null : chickCode, incubationDays))
                    {
                        ConsumeFood(PortionsEatenForLay);
                        attemptLayEggTotalHours = -1;
                        MakeLaySound();
                        failedSeekTargets.Remove(targetPoi);
                        return false;
                    }
                }

                // Stop sitting - this allows a broody hen to go and eat for example
                if (entity.World.Calendar.TotalDays >= sitEndDay)
                {
                    failedSeekTargets.Remove(targetPoi);
                    return false;
                }
            } else
            {
                if (!pathTraverser.Active)
                {
                    float rndx = (float)entity.World.Rand.NextDouble() * 0.3f - 0.15f;
                    float rndz = (float)entity.World.Rand.NextDouble() * 0.3f - 0.15f;
                    pathTraverser.NavigateTo(targetPoi.Position.AddCopy(rndx, 0, rndz), moveSpeed, MinDistanceToTarget() - 0.15f, OnGoalReached, OnStuck, null, false, 500);
                }
            }


            if (nowStuck)
            {
                return false;
            }

            if (attemptLayEggTotalHours > 0 && entity.World.Calendar.TotalHours - attemptLayEggTotalHours > 12)
            {
                LayEggOnGround();
                return false;
            }


            return true;
        }


        public override void FinishExecute(bool cancelled)
        {
            base.FinishExecute(cancelled);
            attemptLayEggTotalHours = -1;
            pathTraverser.Stop();

            if (sitAnimMeta != null)
            {
                entity.AnimManager.StopAnimation(sitAnimMeta.Code);
            }

            targetPoi?.SetOccupier(null);

            if (cancelled)
            {
                cooldownUntilTotalHours = 0;
            }
        }



        private void OnStuck()
        {
            nowStuck = true;

            onBadTarget();
        }

        void onBadTarget()
        {
            IAnimalNest newTarget = null;
            if (attemptLayEggTotalHours >= 0 && entity.World.Calendar.TotalHours - attemptLayEggTotalHours > 12)
            {
                LayEggOnGround();
            }
            else
            {
                if (rand.NextDouble() > 0.4)
                {
                    // Look for another nearby henbox
                    newTarget = FindPOI(18) as IAnimalNest;
                }
            }

            FailedAttempt attempt = null;
            failedSeekTargets.TryGetValue(targetPoi, out attempt);
            if (attempt == null)
            {
                failedSeekTargets[targetPoi] = attempt = new FailedAttempt();
            }

            attempt.Count++;
            attempt.LastTryMs = world.ElapsedMilliseconds;

            if (newTarget != null)
            {
                targetPoi = newTarget;
                nowStuck = false;
                sitTimeNow = 0;
                laid = false;
                pathTraverser.NavigateTo_Async(targetPoi.Position, moveSpeed, MinDistanceToTarget() - 0.1f, OnGoalReached, OnStuck, null, 1000, 1);
                sitAnimStarted = false;
            }
        }

        private void OnGoalReached()
        {
            pathTraverser.Active = true;
            failedSeekTargets.Remove(targetPoi);
        }

        private bool DidConsumeFood(float portion)
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (tree == null) return false;

            float saturation = tree.GetFloat("saturation", 0);

            return saturation >= portion;
        }


        private bool ConsumeFood(float portion)
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (tree == null) return false;

            float saturation = tree.GetFloat("saturation", 0);

            if (saturation >= portion)
            {
                float portionEaten = entity.World.Rand.NextDouble() < 0.25 ? portion : 1;
                tree.SetFloat("saturation", saturation - portionEaten);   // Generally hens will only lose 1 saturation when laying, therefore can lay an egg a day over several days
                return true;
            }

            return false;
        }

        private Entity GetRequiredEntityNearby()
        {
            if (requiresNearbyEntityCode == null) return null;

            return entity.World.GetNearestEntity(entity.ServerPos.XYZ, requiresNearbyEntityRange, requiresNearbyEntityRange, (e) =>
            {
                if (e.WildCardMatch(new AssetLocation(requiresNearbyEntityCode)))
                {
                    ITreeAttribute tree = e.WatchedAttributes.GetTreeAttribute("hunger");
                    if (!e.WatchedAttributes.GetBool("doesEat") || tree == null) return true;
                    tree.SetFloat("saturation", Math.Max(0, tree.GetFloat("saturation", 0) - 1));
                    return true;
                }

                return false;
            });
        }

        /// <summary>
        /// Called in the various paths to failure, after food was consumed
        /// </summary>
        private void LayEggOnGround()
        {
            //Only a chance of laying an egg on the ground - maybe the egg was lost or eaten or otherwise failed
            if (entity.World.Rand.NextDouble() > onGroundChance) return;

            Block block = entity.World.GetBlock(failBlockCode);
            if (block == null) return;

            bool placed =
                TryPlace(block, 0, 0, 0) ||
                TryPlace(block, 1, 0, 0) ||
                TryPlace(block, 0, 0, -1) ||
                TryPlace(block, -1, 0, 0) ||
                TryPlace(block, 0, 0, 1)
            ;

            if (placed)
            {
                ConsumeFood(PortionsEatenForLay);
                attemptLayEggTotalHours = -1;
            }
        }

        private bool TryPlace(Block block, int dx, int dy, int dz)
        {
            IBlockAccessor blockAccess = entity.World.BlockAccessor;
            BlockPos pos = entity.ServerPos.XYZ.AsBlockPos.Add(dx, dy, dz);
            if (blockAccess.GetBlock(pos, BlockLayersAccess.Fluid).IsLiquid()) return false;
            if (!blockAccess.GetBlock(pos).IsReplacableBy(block)) return false;

            pos.Y--;
            if (blockAccess.GetMostSolidBlock(pos).CanAttachBlockAt(blockAccess, block, pos, BlockFacing.UP))
            {
                pos.Y++;
                blockAccess.SetBlock(block.BlockId, pos);

                // Instantly despawn the block again if it expired already
                BlockEntityTransient betran = blockAccess.GetBlockEntity(pos) as BlockEntityTransient;
                betran?.SetPlaceTime(entity.World.Calendar.TotalHours);

                if (betran?.IsDueTransition() == true)
                {
                    blockAccess.SetBlock(0, pos);
                }

                return true;
            }

            return false;
        }

        private void MakeLaySound()
        {
            if (sound == null) return;

            if (soundStartMs > 0)
            {
                entity.World.RegisterCallback((dt) =>
                {
                    entity.World.PlaySoundAt(sound, entity, null, true, soundRange);
                    lastSoundTotalMs = entity.World.ElapsedMilliseconds;
                }, soundStartMs);
            }
            else
            {
                entity.World.PlaySoundAt(sound, entity, null, true, soundRange);
                lastSoundTotalMs = entity.World.ElapsedMilliseconds;
            }
        }
    }
}
