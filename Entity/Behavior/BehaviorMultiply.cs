using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorMultiply : EntityBehavior
    {
        ITreeAttribute multiplyTree;
        JsonObject attributes;
        long callbackId;

        internal float PregnancyDays
        {
            get { return attributes["pregnancyDays"].AsFloat(3f); }
        }

        internal AssetLocation SpawnEntityCode
        {
            get { return new AssetLocation(attributes["spawnEntityCode"].AsString("")); }
        }

        internal string RequiresNearbyEntityCode
        {
            get { return attributes["requiresNearbyEntityCode"].AsString(""); }
        }

        internal float RequiresNearbyEntityRange
        {
            get { return attributes["requiresNearbyEntityRange"].AsFloat(5); }
        }

        /*internal int GrowthCapQuantity
        {
            get { return attributes["growthCapQuantity"].AsInt(10); }
        }

        internal float GrowthCapRange
        {
            get { return attributes["growthCapRange"].AsFloat(10); }
        }

        internal AssetLocation[] GrowthCapEntityCodes
        {
            get { return AssetLocation.toLocations(attributes["growthCapEntityCodes"].AsStringArray(new string[0])); }
        }*/

        public double MultiplyCooldownDaysMin
        {
            get { return attributes["multiplyCooldownDaysMin"].AsFloat(6); }
        }

        public double MultiplyCooldownDaysMax
        {
            get { return attributes["multiplyCooldownDaysMax"].AsFloat(12); }
        }

        public float PortionsEatenForMultiply
        {
            get { return attributes["portionsEatenForMultiply"].AsFloat(3); }
        }

        public float SpawnQuantityMin
        {
            get { return attributes["spawnQuantityMin"].AsFloat(1); }
        }
        public float SpawnQuantityMax
        {
            get { return attributes["spawnQuantityMax"].AsFloat(2); }
        }


        public double TotalDaysLastBirth
        {
            get { return multiplyTree.GetDouble("totalDaysLastBirth"); }
            set { multiplyTree.SetDouble("totalDaysLastBirth", value); }
        }

        public double TotalDaysPregnancyStart
        {
            get { return multiplyTree.GetDouble("totalDaysPregnancyStart"); }
            set { multiplyTree.SetDouble("totalDaysPregnancyStart", value); }
        }

        public double TotalDaysCooldownUntil
        {
            get { return multiplyTree.GetDouble("totalDaysCooldownUntil"); }
            set { multiplyTree.SetDouble("totalDaysCooldownUntil", value); }
        }

        public bool IsPregnant
        {
            get { return multiplyTree.GetBool("isPregnant"); }
            set { multiplyTree.SetBool("isPregnant", value); }
        }

        bool eatAnyway = false;

        public bool ShouldEat
        {
            get
            {
                return 
                    eatAnyway || 
                    (
                        !IsPregnant 
                        && GetSaturation() < PortionsEatenForMultiply 
                        && TotalDaysCooldownUntil <= entity.World.Calendar.TotalDays
                    )
                ;
            }
        }

        public EntityBehaviorMultiply(Entity entity) : base(entity)
        {

        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            this.attributes = attributes;


            eatAnyway = attributes["eatAnyway"].AsBool(false);


            multiplyTree = entity.WatchedAttributes.GetTreeAttribute("multiply");

            if (entity.World.Side == EnumAppSide.Server)
            {
                if (multiplyTree == null)
                {
                    entity.WatchedAttributes.SetAttribute("multiply", multiplyTree = new TreeAttribute());
                    TotalDaysLastBirth = entity.World.Calendar.TotalHours;

                    double daysNow = entity.World.Calendar.TotalHours / 24f;
                    TotalDaysCooldownUntil = daysNow + (MultiplyCooldownDaysMin + entity.World.Rand.NextDouble() * (MultiplyCooldownDaysMax - MultiplyCooldownDaysMin));
                }

                callbackId = entity.World.RegisterCallback(CheckMultiply, 3000);
            }
        }


        private void CheckMultiply(float dt)
        {
            if (!entity.Alive) return;

            callbackId = entity.World.RegisterCallback(CheckMultiply, 3000);

            if (entity.World.Calendar == null) return;
            
            double daysNow = entity.World.Calendar.TotalHours / 24f;

            if (!IsPregnant)
            {
                if (TryGetPregnant())
                {
                    IsPregnant = true;
                    TotalDaysPregnancyStart = daysNow;
                }

                return;
            }


            /*if (GrowthCapQuantity > 0 && IsGrowthCapped())
            {
                TimeLastMultiply = entity.World.Calendar.TotalHours;
                return;
            }*/

            
            if (daysNow - TotalDaysPregnancyStart > PregnancyDays)
            {
                Random rand = entity.World.Rand;

                float q = SpawnQuantityMin + (float)rand.NextDouble() * (SpawnQuantityMax - SpawnQuantityMin);
                TotalDaysLastBirth = daysNow;
                TotalDaysCooldownUntil = daysNow + (MultiplyCooldownDaysMin + rand.NextDouble() * (MultiplyCooldownDaysMax - MultiplyCooldownDaysMin));
                IsPregnant = false;
                entity.WatchedAttributes.MarkPathDirty("multiply");
                EntityProperties childType = entity.World.GetEntityType(SpawnEntityCode);

                int generation = entity.WatchedAttributes.GetInt("generation", 0);
                    
                while (q > 1 || rand.NextDouble() < q)
                {
                    q--;
                    Entity childEntity = entity.World.ClassRegistry.CreateEntity(childType);

                    childEntity.ServerPos.SetFrom(entity.ServerPos);
                    childEntity.ServerPos.Motion.X += (rand.NextDouble() - 0.5f) / 20f;
                    childEntity.ServerPos.Motion.Z += (rand.NextDouble() - 0.5f) / 20f;

                    childEntity.Pos.SetFrom(childEntity.ServerPos);
                    entity.World.SpawnEntity(childEntity);
                    entity.Attributes.SetString("origin", "reproduction");
                    childEntity.WatchedAttributes.SetInt("generation", generation + 1);
                }
                
            }

            entity.World.FrameProfiler.Mark("entity-multiply");
        }

        private bool TryGetPregnant()
        {
            if (entity.World.Rand.NextDouble() > 0.01) return false;
            if (!HasRequiredEntityNearby()) return false;
            if (TotalDaysCooldownUntil > entity.World.Calendar.TotalDays) return false;

            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (tree == null) return false;

            float saturation = tree.GetFloat("saturation", 0);
            
            if (saturation >= PortionsEatenForMultiply)
            {
                if (entity.World.Rand.NextDouble() < 0.2)
                {
                    tree.SetFloat("saturation", saturation - 1);
                    return false;
                }

                tree.SetFloat("saturation", saturation - PortionsEatenForMultiply);
                IsPregnant = true;
                TotalDaysPregnancyStart = entity.World.Calendar.TotalDays;
                entity.WatchedAttributes.MarkPathDirty("multiply");

                return true;
            }

            //double daysPassed = (entity.World.Calendar.TotalHours - DaysLastBirth) / 24f;
            //DaysLastBirth = entity.World.Calendar.TotalHours;

            return false;
        }


        float GetSaturation()
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (tree == null) return 0;

            return tree.GetFloat("saturation", 0);
        }

        private bool HasRequiredEntityNearby()
        {
            if (RequiresNearbyEntityCode == null) return true;

            return entity.World.GetEntitiesAround(entity.ServerPos.XYZ, RequiresNearbyEntityRange, RequiresNearbyEntityRange, (e) =>
            {
                return e.WildCardMatch(new AssetLocation(RequiresNearbyEntityCode));
            }).Length > 0;
        }

        /*public bool IsGrowthCapped()
        {
            bool haveUnloadedchunk = false;

            AssetLocation[] entityCodes = GrowthCapEntityCodes;
            int count = CountEntitiesAround(entity.ServerPos.XYZ, GrowthCapRange, GrowthCapRange, (e) =>
            {
                return entityCodes.Contains(e.Code);
            }, ref haveUnloadedchunk);

            return haveUnloadedchunk || count >= GrowthCapQuantity;
        }*/


       /* public int CountEntitiesAround(Vec3d position, float horRange, float vertRange, ActionConsumable<Entity> matches, ref bool unloadedchunk)
        {
            int chunksize = entity.World.BlockAccessor.ChunkSize;
            int mincx = (int)((position.X - horRange) / chunksize);
            int maxcx = (int)((position.X + horRange) / chunksize);
            int mincy = (int)((position.Y - vertRange) / chunksize);
            int maxcy = (int)((position.Y + vertRange) / chunksize);
            int mincz = (int)((position.Z - horRange) / chunksize);
            int maxcz = (int)((position.Z + horRange) / chunksize);

            int count = 0;

            float horRangeSq = horRange * horRange;

            for (int cx = mincx; cx <= maxcx; cx++)
            {
                for (int cy = mincy; cy <= maxcy; cy++)
                {
                    for (int cz = mincz; cz <= maxcz; cz++)
                    {
                        IWorldChunk chunk = this.entity.World.BlockAccessor.GetChunk(cx, cy, cz);
                        if (chunk == null)
                        {
                            unloadedchunk = true;
                            return 0;
                        }

                        if (chunk.Entities == null) continue;
                        Entity ent;

                        for (int i = 0; i < chunk.EntitiesCount; i++)
                        {
                            ent = chunk.Entities[i];

                            if (ent == null || !ent.ServerPos.InRangeOf(position, horRangeSq, vertRange) || !matches(ent) || entity.State == EnumEntityState.Despawned) continue;

                            count++;
                        }
                    }
                }
            }

            return count;
        }
        */

        public override void OnEntityDespawn(EntityDespawnReason despawn)
        {
            entity.World.UnregisterCallback(callbackId);
        }



        public override string PropertyName()
        {
            return "multiply";
        }

        public override void GetInfoText(StringBuilder infotext)
        {
            multiplyTree = entity.WatchedAttributes.GetTreeAttribute("multiply");

            if (IsPregnant) infotext.AppendLine(Lang.Get("Is pregnant"));
            else
            {
                if (entity.Alive)
                {
                    ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
                    if (tree != null)
                    {
                        float saturation = tree.GetFloat("saturation", 0);
                        infotext.AppendLine(Lang.Get("Saturation: {0}", saturation));
                    }

                    double daysLeft = TotalDaysCooldownUntil - entity.World.Calendar.TotalDays;

                    if (daysLeft > 0)
                    {
                        if (daysLeft > 3)
                        {
                            infotext.AppendLine(Lang.Get("Several days left before ready to mate"));
                        }
                        else
                        {
                            infotext.AppendLine(Lang.Get("Less than 3 days before ready to mate"));
                        }

                    }
                    else
                    {
                        infotext.AppendLine(Lang.Get("Ready to mate"));
                    }
                }
            }

            base.GetInfoText(infotext);
        }
    }
}
