using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

#nullable disable

namespace Vintagestory.GameContent
{
    public class ModSystemWearableGearCache : ModSystem
    {
        private class CachedShape
        {
            public Shape Shape;
            public float TTL;
        }

        private readonly Dictionary<AssetLocation, CachedShape> wearablesCache = new();
        private ICoreAPI api;

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            api.Event.RegisterGameTickListener(freeUnusedWearables, 2001, 1891);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            api.Event.ReloadShapes += Clear;
        }

        public Shape GetOrLoadClonedShape(AssetLocation shapePath)
        {
            Shape shape;
            lock (wearablesCache)
            {
                if (!wearablesCache.TryGetValue(shapePath, out var cached))
                {
                    shape = Shape.TryGetFast(api, shapePath);
                    if (shape == null) return null;
                    wearablesCache[shapePath.Clone()] = new CachedShape { Shape = shape, TTL = 30 };
                }
                else
                {
                    cached.TTL = Math.Min(cached.TTL + 60, 240);
                    shape = cached.Shape;
                }
            }

            return shape.DeepClone();
        }


        private void freeUnusedWearables(float dt)
        {
            lock (wearablesCache)
            {
                List<AssetLocation> freeableWearables = null;
                foreach ((AssetLocation loc, CachedShape cached) in wearablesCache)
                {
                    cached.TTL -= dt;
                    if (cached.TTL <= 0)
                    {
                        freeableWearables ??= new List<AssetLocation>();
                        freeableWearables.Add(loc);
                    }
                }

                if (freeableWearables == null) return;
                foreach (var loc in freeableWearables) wearablesCache.Remove(loc);
            }
        }

        public void Clear()
        {
            lock (wearablesCache)
            {
                wearablesCache.Clear();
            }
        }

    }
}
