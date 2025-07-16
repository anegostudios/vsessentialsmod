using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

#nullable disable

namespace Vintagestory.GameContent
{

    public class AiTaskUseInventory : AiTaskBase
    {
        protected AssetLocation useSound;

        protected float useTime = 1f;

        protected float useTimeNow = 0;
        protected bool soundPlayed = false;
        protected bool doConsumePortion = true;

        protected HashSet<EnumFoodCategory> eatItemCategories = new HashSet<EnumFoodCategory>();
        protected HashSet<AssetLocation> eatItemCodes = new HashSet<AssetLocation>();

        protected bool isEdible;


        public AiTaskUseInventory(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
        {
            JsonObject soundCfg = taskConfig["useSound"];
            if (soundCfg.Exists)
            {
                string eatsoundstring = soundCfg.AsString(null);
                if (eatsoundstring != null) useSound = new AssetLocation(eatsoundstring).WithPathPrefix("sounds/");
            }

            useTime = taskConfig["useTime"].AsFloat(1.5f);

            foreach (var val in taskConfig["eatItemCategories"].AsArray<EnumFoodCategory>(System.Array.Empty<EnumFoodCategory>()))
            {
                eatItemCategories.Add(val);
            }

            foreach (var val in taskConfig["eatItemCodes"].AsArray(System.Array.Empty<AssetLocation>()))
            {
                eatItemCodes.Add(val);
            }
        }

        public override bool ShouldExecute()
        {
            if (entity.World.Rand.NextDouble() < 0.005) return false;
            if (cooldownUntilMs > entity.World.ElapsedMilliseconds) return false;
            if (cooldownUntilTotalHours > entity.World.Calendar.TotalHours) return false;
            if (!PreconditionsSatisifed()) return false;

            EntityBehaviorMultiplyBase bh = entity.GetBehavior<EntityBehaviorMultiplyBase>();
            if (bh != null && !bh.ShouldEat && entity.World.Rand.NextDouble() < 0.996) return false; // 0.4% chance go to the food source anyway just because (without eating anything).

            ItemSlot leftSlot = entity.LeftHandItemSlot;

            if (leftSlot.Empty) return false;

            isEdible = false;

            EnumFoodCategory? cat = leftSlot.Itemstack.Collectible?.NutritionProps?.FoodCategory;
            if (cat != null && eatItemCategories.Contains((EnumFoodCategory)cat))
            {
                isEdible = true;
                return true;
            }

            AssetLocation code = leftSlot.Itemstack?.Collectible?.Code;
            if (code != null && eatItemCodes.Contains(code))
            {
                isEdible = true;
                return true;
            }

            if (!leftSlot.Empty)
            {
                entity.World.SpawnItemEntity(leftSlot.TakeOutWhole(), entity.ServerPos.XYZ);
            }

            return false;
        }


        public override void StartExecute()
        {
            base.StartExecute();
            
            soundPlayed = false;
            useTimeNow = 0;
        }

        public override bool ContinueExecute(float dt)
        {
            //Check if time is still valid for task.
            if (!IsInValidDayTimeHours(false)) return false;

            useTimeNow += dt;

            if (useTimeNow > useTime * 0.75f && !soundPlayed)
            {
                soundPlayed = true;
                if (useSound != null) entity.World.PlaySoundAt(useSound, entity, null, true, 16, 1);
            }

            if (entity.LeftHandItemSlot == null || entity.LeftHandItemSlot.Empty) return false;

            entity.World.SpawnCubeParticles(entity.ServerPos.XYZ, entity.LeftHandItemSlot.Itemstack, 0.25f, 1, 0.25f + 0.5f * (float)entity.World.Rand.NextDouble());

            if (useTimeNow >= useTime)
            {
                if (isEdible)
                {
                    ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
                    if (tree == null) entity.WatchedAttributes["hunger"] = tree = new TreeAttribute();

                    if (doConsumePortion)
                    {
                        float sat = 1;
                        tree.SetFloat("saturation", sat + tree.GetFloat("saturation", 0));
                    }
                }

                entity.LeftHandItemSlot.TakeOut(1);

                return false;
            }
        
            return true;
        }



        public override void FinishExecute(bool cancelled)
        {
            base.FinishExecute(cancelled);

            if (cancelled)
            {
                cooldownUntilTotalHours = 0;
            }
        }
    }
}
