using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class MultiChunkMapComponent : MapComponent
    {
        public static int ChunkLen=3;
        public static LoadedTexture tmpTexture;

        public float renderZ = 50;
        public Vec2i chunkCoord;
        public LoadedTexture Texture;

        static int[] emptyPixels;


        Vec3d worldPos;
        Vec2f viewPos = new Vec2f();

        bool[,] chunkSet = new bool[ChunkLen, ChunkLen];
        int chunksize;

        public bool AnyChunkSet
        {
            get
            {
                for (int dx = 0; dx < ChunkLen; dx++)
                {
                    for (int dz = 0; dz < ChunkLen; dz++)
                    {
                        if (chunkSet[dx, dz]) return true;
                    }
                }

                return false;
            }
        }

        public float TTL = MaxTTL;
        public static float MaxTTL = 15f;

        public bool IsChunkSet(int dx, int dz)
        {
            if (dx < 0 || dz < 0) return false;
            return chunkSet[dx, dz];
        }


        public MultiChunkMapComponent(ICoreClientAPI capi, Vec2i baseChunkCord) : base(capi)
        {
            this.chunkCoord = baseChunkCord;
            chunksize = capi.World.BlockAccessor.ChunkSize;

            worldPos = new Vec3d(baseChunkCord.X * chunksize, 0, baseChunkCord.Y * chunksize);

            if (emptyPixels == null)
            {
                int size = ChunkLen * chunksize;
                emptyPixels = new int[size * size];
            }
        }

        

        public void setChunk(int dx, int dz, int[] pixels)
        {
            if (dx < 0 || dx >= ChunkLen || dz < 0 || dz >= ChunkLen) throw new ArgumentOutOfRangeException("dx/dz must be within [0," + (ChunkLen - 1) + "]");

            if (tmpTexture == null || tmpTexture.Disposed)
            {
                tmpTexture = new LoadedTexture(capi, 0, chunksize, chunksize);
            }

            if (Texture == null || Texture.Disposed)
            {
                int size = ChunkLen * chunksize;
                Texture = new LoadedTexture(capi, 0, size, size);
                capi.Render.LoadOrUpdateTextureFromRgba(emptyPixels, false, 0, ref Texture);
            }
            
            capi.Render.LoadOrUpdateTextureFromRgba(pixels, false, 0, ref tmpTexture);

            capi.Render.GlToggleBlend(false);
            capi.Render.GLDisableDepthTest();
            capi.Render.RenderTextureIntoTexture(tmpTexture, 0, 0, chunksize, chunksize, Texture, chunksize * dx, chunksize * dz);

            capi.Render.BindTexture2d(Texture.TextureId);
            capi.Render.GlGenerateTex2DMipmaps();

            chunkSet[dx, dz] = true;
        }

        public void unsetChunk(int dx, int dz)
        {
            if (dx < 0 || dx >= ChunkLen || dz < 0 || dz >= ChunkLen) throw new ArgumentOutOfRangeException("dx/dz must be within [0,"+(ChunkLen - 1)+ "]");

            chunkSet[dx, dz] = false;
        }


        public override void Render(GuiElementMap map, float dt)
        {
            map.TranslateWorldPosToViewPos(worldPos, ref viewPos);

#if DEBUG
            if (Texture.Disposed) throw new Exception("Fatal. Trying to render a disposed texture");
#endif

            capi.Render.Render2DTexture(
                Texture.TextureId,
                (int)(map.Bounds.renderX + viewPos.X),
                (int)(map.Bounds.renderY + viewPos.Y),
                (int)(Texture.Width * map.ZoomLevel),
                (int)(Texture.Height * map.ZoomLevel),
                renderZ
            );
        }

        public override void Dispose()
        {
            base.Dispose();
            
        }

        public void ActuallyDispose()
        {
            Texture.Dispose();
        }

        Vec2i tmpVec = new Vec2i();
        public bool IsVisible(HashSet<Vec2i> curVisibleChunks)
        {
            for (int dx = 0; dx < ChunkLen; dx++)
            {
                for (int dz = 0; dz < ChunkLen; dz++)
                {
                    tmpVec.Set(chunkCoord.X + dx, chunkCoord.Y + dz);
                    if (curVisibleChunks.Contains(tmpVec)) return true;
                }
            }

            return false;
        }

        public static void DisposeStatic()
        {
            tmpTexture?.Dispose();
            emptyPixels = null;
            tmpTexture = null;
        }
    }


}
