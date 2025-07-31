using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.GameContent
{
    public delegate bool PoiMatcher(IPointOfInterest poi);

    public interface IPointOfInterest
    {
        Vec3d Position { get; }

        /// <summary>
        /// i.e. "food"
        /// </summary>
        string Type { get; }
    }

    public class WeightedFoodTag
    {
        public string Code;
        public float Weight;
    }

    public class CreatureDiet
    {
        /// <summary>
        /// Categories of food this creature, primarly for dropped items and troughs.
        /// </summary>
        public EnumFoodCategory[] FoodCategories;
        /// <summary>
        /// Tags as defined in the individual items/blocks attributes
        /// </summary>
        public string[] FoodTags;
        /// <summary>
        /// Weighted tags as defined in the individual items/blocks attributes
        /// </summary>
        public WeightedFoodTag[] WeightedFoodTags;

        /// <summary>
        /// Tested first: Blacklist of certain foods
        /// </summary>
        public string[] SkipFoodTags;


        [OnDeserialized]
        private void OnDeserialized(StreamingContext ctx)
        {
            if (FoodTags != null)
            {
                List<WeightedFoodTag> wfoodTags = new(WeightedFoodTags ?? []);
                foreach (var tag in FoodTags) wfoodTags.Add(new WeightedFoodTag() { Code = tag, Weight = 1 });
                WeightedFoodTags = wfoodTags.ToArray();
            }
        }


        public bool Matches(EnumFoodCategory foodSourceCategory, string[] foodSourceTags, float foodTagMinWeight = 0)
        {
            if (SkipFoodTags != null && foodSourceTags != null)
            {
                for (int i = 0; i < foodSourceTags.Length; i++)
                {
                    if (SkipFoodTags.Contains(foodSourceTags[i])) return false;
                }
            }

            if (FoodCategories != null && FoodCategories.Contains(foodSourceCategory)) return true;

            if (this.WeightedFoodTags != null && foodSourceTags != null)
            {
                for (int i = 0; i < foodSourceTags.Length; i++)
                {
                    for (int j = 0; j < WeightedFoodTags.Length; j++)
                    {
                        var wFoodTag = WeightedFoodTags[j];
                        if (wFoodTag.Weight >= foodTagMinWeight && wFoodTag.Code == foodSourceTags[i])
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public bool Matches(ItemStack itemstack, bool checkCategory = true, float foodTagMinWeight = 0)
        {
            var cobj = itemstack.Collectible;
            var foodCat = checkCategory ? (cobj?.NutritionProps?.FoodCategory ?? EnumFoodCategory.NoNutrition) : EnumFoodCategory.NoNutrition;
            var foodTags = cobj?.Attributes?["foodTags"].AsArray<string>();

            return Matches(foodCat, foodTags, foodTagMinWeight);
        }
    }

    public interface IAnimalFoodSource : IPointOfInterest
    {
        bool IsSuitableFor(Entity entity, CreatureDiet diet);

        /// <summary>
        /// Return amount of saturation given from eating this portion
        /// </summary>
        /// <returns></returns>
        float ConsumeOnePortion(Entity entity);
    }


    public interface IAnimalNest : IPointOfInterest
    {
        bool IsSuitableFor(Entity entity, string[] nestTypes);

        /// <summary>
        /// Return true if occupied by an entity, which is not the same as the entity specified in the parameter
        /// </summary>
        bool Occupied(Entity entity);

        void SetOccupier(Entity entity);

        /// <summary>
        /// Returns true if an egg was successfully added
        /// </summary>
        bool TryAddEgg(ItemStack egg);

        float DistanceWeighting { get; }
    }



    /// <summary>
    /// Point-of-Interest registry, not synchronized to client
    /// </summary>
    public class POIRegistry : ModSystem
    {
        Dictionary<Vec2i, List<IPointOfInterest>> PoisByChunkColumn = new Dictionary<Vec2i, List<IPointOfInterest>>();

        Vec2i tmp = new Vec2i();
        const int chunksize = GlobalConstants.ChunkSize;

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return true;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
        }

        public void WalkPois(Vec3d centerPos, float radius, PoiMatcher callback = null)
        {
            int mincx = (int)(centerPos.X - radius) / chunksize;
            int mincz = (int)(centerPos.Z - radius) / chunksize;
            int maxcx = (int)(centerPos.X + radius) / chunksize;
            int maxcz = (int)(centerPos.Z + radius) / chunksize;

            float radiusSq = radius * radius;
            
            for (int cx = mincx; cx < maxcx; cx++)
            {
                for (int cz = mincz; cz < maxcz; cz++)
                {
                    tmp.Set(cx, cz);
                    PoisByChunkColumn.TryGetValue(tmp, out List<IPointOfInterest> pois);
                    if (pois == null) continue;

                    for (int i = 0; i < pois.Count; i++)
                    {
                        Vec3d poipos = pois[i].Position;
                        if (poipos.SquareDistanceTo(centerPos) > radiusSq) continue;

                        callback(pois[i]);
                    }
                }
            }
        }


        public IPointOfInterest GetNearestPoi(Vec3d centerPos, float radius, PoiMatcher matcher = null)
        {
            int mincx = (int)(centerPos.X - radius) / chunksize;
            int mincz = (int)(centerPos.Z - radius) / chunksize;
            int maxcx = (int)(centerPos.X + radius) / chunksize;
            int maxcz = (int)(centerPos.Z + radius) / chunksize;

            float radiusSq = radius * radius;

            float nearestDistSq = 9999999;
            IPointOfInterest nearestPoi = null;

            for (int cx = mincx; cx <= maxcx; cx++)
            {
                for (int cz = mincz; cz <= maxcz; cz++)
                {
                    tmp.Set(cx, cz);
                    PoisByChunkColumn.TryGetValue(tmp, out List<IPointOfInterest> pois);
                    if (pois == null) continue;

                    for (int i = 0; i < pois.Count; i++)
                    {
                        Vec3d poipos = pois[i].Position;
                        float distSq = poipos.SquareDistanceTo(centerPos);
                        if (distSq > radiusSq) continue;

                        if (distSq < nearestDistSq && matcher(pois[i])) 
                        {
                            nearestPoi = pois[i];
                            nearestDistSq = distSq;
                        }
                    }
                }
            }

            return nearestPoi;
        }

        public IPointOfInterest GetWeightedNearestPoi(Vec3d centerPos, float radius, PoiMatcher matcher = null)
        {
            int mincx = (int)(centerPos.X - radius) / chunksize;
            int mincz = (int)(centerPos.Z - radius) / chunksize;
            int maxcx = (int)(centerPos.X + radius) / chunksize;
            int maxcz = (int)(centerPos.Z + radius) / chunksize;

            float radiusSq = radius * radius;

            float nearestDistSq = 9999999;
            IPointOfInterest nearestPoi = null;

            for (int cx = mincx; cx <= maxcx; cx++)
            {
                double chunkDistX = 0;  // x-distance from centerPos to nearest position in this chunk
                if (cx * chunksize > centerPos.X) chunkDistX = cx * chunksize - centerPos.X;
                else if ((cx + 1) * chunksize < centerPos.X) chunkDistX = centerPos.X - (cx + 1) * chunksize;

                for (int cz = mincz; cz <= maxcz; cz++)
                {
                    double cdistZ = 0;  // z-distance from centerPos to nearest position in this chunk
                    if (cz * chunksize > centerPos.Z) cdistZ = cz * chunksize - centerPos.Z;
                    else if ((cz + 1) * chunksize < centerPos.Z) cdistZ = centerPos.Z - (cz + 1) * chunksize;
                    if (chunkDistX * chunkDistX + cdistZ * cdistZ > nearestDistSq) continue;  // skip the search if this whole chunk is further than the nearest found so far

                    tmp.Set(cx, cz);
                    PoisByChunkColumn.TryGetValue(tmp, out List<IPointOfInterest> pois);
                    if (pois == null) continue;

                    for (int i = 0; i < pois.Count; i++)
                    {
                        Vec3d poipos = pois[i].Position;
                        float weight = pois[i] is IAnimalNest nest ? nest.DistanceWeighting : 1f; 
                        float distSq = poipos.SquareDistanceTo(centerPos) * weight;
                        if (distSq > radiusSq) continue;

                        if (distSq < nearestDistSq && matcher(pois[i]))
                        {
                            nearestPoi = pois[i];
                            nearestDistSq = distSq;
                        }
                    }
                }
            }

            return nearestPoi;
        }




        public void AddPOI(IPointOfInterest poi)
        {
            tmp.Set((int)poi.Position.X / chunksize, (int)poi.Position.Z / chunksize);

            PoisByChunkColumn.TryGetValue(tmp, out List<IPointOfInterest> pois);
            if (pois == null) PoisByChunkColumn[tmp.Copy()] = pois = new List<IPointOfInterest>();

            if (!pois.Contains(poi))
            {
                pois.Add(poi);
            }
        }


        public void RemovePOI(IPointOfInterest poi)
        {
            tmp.Set((int)poi.Position.X / chunksize, (int)poi.Position.Z / chunksize);

            PoisByChunkColumn.TryGetValue(tmp, out List<IPointOfInterest> pois);
            if (pois != null)
            {
                pois.Remove(poi);
                if (pois.Count == 0) PoisByChunkColumn.Remove(tmp);
            }
        }

    }
}
