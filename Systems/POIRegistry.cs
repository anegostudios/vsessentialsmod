using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

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


    public interface IAnimalFoodSource : IPointOfInterest
    {
        bool IsSuitableFor(Entity entity);

        /// <summary>
        /// Return amount of saturation given from eating this portion
        /// </summary>
        /// <returns></returns>
        float ConsumeOnePortion();
    }



    /// <summary>
    /// Point-of-Interest registry
    /// </summary>
    public class POIRegistry : ModSystem
    {
        Dictionary<Vec2i, List<IPointOfInterest>> PoisByChunkColumn = new Dictionary<Vec2i, List<IPointOfInterest>>();

        Vec2i tmp = new Vec2i();
        int chunksize;

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return forSide == EnumAppSide.Server;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            chunksize = api.World.BlockAccessor.ChunkSize;
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
                for (int cz = mincx; cz < maxcz; cz++)
                {
                    List<IPointOfInterest> pois = null;
                    tmp.Set(cx, cz);
                    PoisByChunkColumn.TryGetValue(tmp, out pois);
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
                    List<IPointOfInterest> pois = null;
                    tmp.Set(cx, cz);
                    PoisByChunkColumn.TryGetValue(tmp, out pois);
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




        public void AddPOI(IPointOfInterest poi)
        {
            tmp.Set((int)poi.Position.X / chunksize, (int)poi.Position.Z / chunksize);

            List<IPointOfInterest> pois = null;
            PoisByChunkColumn.TryGetValue(tmp, out pois);
            if (pois == null) PoisByChunkColumn[tmp] = pois = new List<IPointOfInterest>();

            if (!pois.Contains(poi))
            {
                pois.Add(poi);
            }
        }


        public void RemovePOI(IPointOfInterest poi)
        {
            tmp.Set((int)poi.Position.X / chunksize, (int)poi.Position.Z / chunksize);

            List<IPointOfInterest> pois = null;
            PoisByChunkColumn.TryGetValue(tmp, out pois);
            if (pois != null) pois.Remove(poi);
        }

    }
}
