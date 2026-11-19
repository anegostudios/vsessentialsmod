using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class AiTaskSeekBlockAndLay : AiTaskSeekPoiBase<IAnimalNest>
    {
        protected bool laid = false;

        /// <summary>
        /// Time in (game) days for the creature to sit on the nest (i.e. incubating eggs) before switching tasks (e.g. to feed)
        /// </summary>
        [JsonProperty]
        protected float sitDays = 1f;
        /// <summary>
        /// Time in (real) seconds for the creature to remain on the nest when laying a single egg
        /// </summary>
        [JsonProperty]
        protected float layTime = 1.5f;
        /// <summary>
        /// Cumulative time in (game) days for which a creature must sit before any fertile eggs will hatch
        /// </summary>
        [JsonProperty]
        protected double incubationDays = 5;
        /// <summary>
        /// Codes for the chicks to hatch from eggs laid by this creature
        /// </summary>
        [JsonProperty]
        protected string?[] chickCodes;
        /// <summary>
        /// Types of nest this creature lays eggs in
        /// </summary>
        [JsonProperty]
        protected string?[]? nestTypes = null;
        /// <summary>
        /// Chance that an egg will be laid on the ground, if a search for the nest fails
        /// </summary>
        [JsonProperty]
        protected double onGroundChance = 0.3;
        [JsonProperty]
        protected AssetLocation? failBlockCode;

        protected double sitEndDay = 0;
        [JsonProperty]
        protected float PortionsEatenForLay = 3;
        [JsonProperty]
        protected string? requiresNearbyEntityCode;
        [JsonProperty]
        protected float requiresNearbyEntityRange = 5;

        protected AnimationMetaData? sitAnimMeta;

        protected double attemptLayEggTotalHours;

        public AiTaskSeekBlockAndLay(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
        {
            entity.WatchedAttributes.SetBool("doesSit", true);

            if (taskConfig["sitAnimation"].Exists)
            {
                sitAnimMeta = new AnimationMetaData()
                {
                    Code = taskConfig["sitAnimation"].AsString()?.ToLowerInvariant(),
                    Animation = taskConfig["sitAnimation"].AsString()?.ToLowerInvariant(),
                    AnimationSpeed = taskConfig["sitAnimationSpeed"].AsFloat(1f)
                }.Init();
            }

            // Also check for an individual chickCode (old format). If neither is found, eggs will be infertile.
            chickCodes ??= new string?[] { taskConfig["chickCode"].AsString(null) };

            if (animMeta != null)
            {
                animMeta.EaseInSpeed = 1f;
                animMeta.EaseOutSpeed = 1f;
            }
        }

        protected override void SetDefaultValues()
        {
            base.SetDefaultValues();
            // Spread timing randomly with on average about one call per three seconds. This prevents animals loaded at the same time from moving at the exact same instant.
            ExecutionChance = 0.01;
        }

        public override bool ShouldExecute()
        {
            if (entity.World.Rand.NextDouble() > ExecutionChance) return false;
            if (!IsSearchTime()) return false;

            // Hen needs to be not hungry, in order to EITHER lay an egg OR sit and incubate for a long time
            if (!DidConsumeFood(PortionsEatenForLay)) return false;

            if (attemptLayEggTotalHours <= 0) attemptLayEggTotalHours = entity.World.Calendar.TotalHours;

            lastPOISearchTotalMs = entity.World.ElapsedMilliseconds;

            targetPoi = FindPOI(42) as IAnimalNest;

            if (targetPoi == null)
            {
                LayEggOnGround();
            }

            return targetPoi != null;
        }


        private IPointOfInterest FindPOI(int radius)
        {
            // We want the hen to search for the most full HenBox nearby - so 'proximity' is weighted also according to how full the box is (see IAnimalNest.DistanceWeighting)
            return pointsOfInterest.GetWeightedNearestPoi(entity.Pos.XYZ, radius, (poi) =>
            {
                if (poi.Type != "nest") return false;
                IAnimalNest? nestPoi = poi as IAnimalNest;

                return nestPoi?.Occupied(entity) == false && nestPoi.IsSuitableFor(entity, nestTypes) && !RecentlyFailedSeek(nestPoi);
            });
        }

        public override float MinDistanceToTarget()
        {
            return 0.1f;
        }

        public override void StartExecute()
        {
            // Avoid playing the sound at the start
            AssetLocation savedSound = sound;
            sound = null;
            base.StartExecute();
            sound = savedSound;

            laid = false;
        }

        protected virtual ItemStack? MakeEggItem(string? chickCode)
        {
            ICoreAPI api = entity.Api;
            ItemStack? eggStack;
            JsonItemStack?[]? eggTypes = entity.Properties.Attributes?["eggTypes"].AsArray<JsonItemStack>();
            if (eggTypes == null)
            {
                string fallbackCode = "egg-chicken-raw";
                if (entity != null) api.Logger.Warning("No egg type specified for entity " + entity.Code + ", falling back to " + fallbackCode);
                eggStack = new ItemStack(api.World.GetItem(fallbackCode));
            }
            else
            {
                JsonItemStack? jsonEgg = eggTypes[api.World.Rand.Next(eggTypes.Length)];
                if (jsonEgg == null || !jsonEgg.Resolve(api.World, entity.Code, false))
                {
                    api.Logger.Warning("Failed to resolve egg " + jsonEgg?.Type + " with code " + jsonEgg?.Code + " for entity " + entity.Code);
                    return null;
                }
                eggStack = new ItemStack(jsonEgg.ResolvedItemstack!.Collectible);
            }

            if (chickCode != null)
            {
                TreeAttribute chickTree = new TreeAttribute();
                chickCode = AssetLocation.Create(chickCode, entity?.Code.Domain ?? GlobalConstants.DefaultDomain);
                chickTree.SetString("code", chickCode);
                chickTree.SetInt("generation", entity?.WatchedAttributes.GetInt("generation", 0) + 1 ?? 0);
                chickTree.SetDouble("incubationDays", incubationDays);
                EntityAgent? eagent = entity as EntityAgent;
                if (eagent != null) chickTree.SetLong("herdID", eagent.HerdId);
                eggStack.Attributes["chick"] = chickTree;
            }

            return eggStack;
        }

        protected override void OnArrival()
        {
            sitEndDay = entity.World.Calendar.TotalDays + sitDays;

            if (sitAnimMeta != null)
            {
                entity.AnimManager.StartAnimation(sitAnimMeta);
            }
        }

        protected override void TickTargetReached()
        {
            if (targetPoi == null || !targetPoi.IsSuitableFor(entity, nestTypes))
            {
                done = true;
                OnBadTarget();
            }
            if (targetPoi == null) return;

            targetPoi.SetOccupier(entity);

            if (timeSinceTargetReached >= layTime && !laid)
            {
                laid = true;

                // Potential gameplay/realism issue: the rooster has to be nearby at the exact moment the egg is laid, instead of looking to see whether there was a rooster / hen interaction ;) recently ...
                // To mitigate this issue, we increase the rooster search range to 9 blocks in the JSON
                string? chickCode = null;
                if (GetRequiredEntityNearby() != null && chickCodes.Length > 0)
                {
                    chickCode = chickCodes[entity.World.Rand.Next(chickCodes.Length)];
                }
                if (targetPoi.TryAddEgg(MakeEggItem(chickCode)))
                {
                    ConsumeFood(PortionsEatenForLay);
                    attemptLayEggTotalHours = -1;
                    MakeLaySound();
                    done = true;
                }
            }

            // Stop sitting - this allows a broody hen to go and eat for example
            if (entity.World.Calendar.TotalDays >= sitEndDay)
            {
                done = true;
            }
        }


        public override void FinishExecute(bool cancelled)
        {
            base.FinishExecute(cancelled);
            attemptLayEggTotalHours = -1;

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

        protected override bool ShouldAbort()
        {
            return base.ShouldAbort() || targetPoi == null || targetPoi.Occupied(entity);
        }


        protected override void OnStuck()
        {
            base.OnStuck();
            OnBadTarget();
        }

        protected virtual void OnBadTarget()
        {
            IAnimalNest? newTarget = null;
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

            if (newTarget != null)
            {
                targetPoi = newTarget;
                timeSinceTargetReached = 0;
                done = false;
                laid = false;
                pathTraverser.NavigateTo_Async(targetPoi.Position, moveSpeed, MinDistanceToTarget(), OnGoalReached, OnStuck, null, 1000, 1, creatureType, PreferredGait);
            }
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

        private Entity? GetRequiredEntityNearby()
        {
            if (requiresNearbyEntityCode == null) return null;

            return entity.World.GetNearestEntity(entity.Pos.XYZ, requiresNearbyEntityRange, requiresNearbyEntityRange, (e) =>
            {
                if (e.Alive && e.WildCardMatch(new AssetLocation(requiresNearbyEntityCode)))
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

            Block? block = entity.World.GetBlock(failBlockCode);
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
            BlockPos pos = entity.Pos.XYZ.AsBlockPos.Add(dx, dy, dz);
            if (blockAccess.GetBlock(pos, BlockLayersAccess.Fluid).IsLiquid()) return false;
            if (!blockAccess.GetBlock(pos).IsReplacableBy(block)) return false;

            pos.Y--;
            if (blockAccess.GetMostSolidBlock(pos).CanAttachBlockAt(blockAccess, block, pos, BlockFacing.UP))
            {
                pos.Y++;
                blockAccess.SetBlock(block.BlockId, pos);

                // Instantly despawn the block again if it expired already
                BlockEntityTransient? betran = blockAccess.GetBlockEntity(pos) as BlockEntityTransient;
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
