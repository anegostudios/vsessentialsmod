using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    public class MultiChunkMapComponent : MapComponent
    {
        public const int ChunkLen = 3;
        public static LoadedTexture tmpTexture;

        public float renderZ = 50;
        public FastVec2i chunkCoord;
        public LoadedTexture Texture;

        static int[] emptyPixels;


        Vec3d worldPos;
        Vec2f viewPos = new Vec2f();

        bool[,] chunkSet = new bool[ChunkLen, ChunkLen];
        const int chunksize = GlobalConstants.ChunkSize;

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
        public static float MaxTTL = 30f;   // Allow map chunk textures to survive off-screen for 30 seconds before being unloaded; gives more lee-way for players who are panning zoomed-out maps 

        public bool IsChunkSet(int dx, int dz)
        {
            if (dx < 0 || dz < 0) return false;
            return chunkSet[dx, dz];
        }


        public MultiChunkMapComponent(ICoreClientAPI capi, FastVec2i baseChunkCord) : base(capi)
        {
            this.chunkCoord = baseChunkCord;

            worldPos = new Vec3d(baseChunkCord.X * chunksize, 0, baseChunkCord.Y * chunksize);

            if (emptyPixels == null)
            {
                int size = ChunkLen * chunksize;
                emptyPixels = new int[size * size];
            }
        }

        
        int[][] pixelsToSet;
        public void setChunk(int dx, int dz, int[] pixels)
        {
            if (dx < 0 || dx >= ChunkLen || dz < 0 || dz >= ChunkLen) throw new ArgumentOutOfRangeException("dx/dz must be within [0," + (ChunkLen - 1) + "]");

            if (pixelsToSet == null) pixelsToSet = new int[ChunkLen * ChunkLen][];

            pixelsToSet[dz * ChunkLen + dx] = pixels;
            chunkSet[dx, dz] = true;
        }

        public void FinishSetChunks()
        {
            if (pixelsToSet == null) return;

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

            FrameBufferRef fb = capi.Render.CreateFrameBuffer(Texture);
            for (int i = 0; i < pixelsToSet.Length; i++)
            {
                if (pixelsToSet[i] == null) continue;

                capi.Render.LoadOrUpdateTextureFromRgba(pixelsToSet[i], false, 0, ref tmpTexture);

                capi.Render.GlToggleBlend(false);
                capi.Render.GLDisableDepthTest();
                capi.Render.RenderTextureIntoFrameBuffer(0, tmpTexture, 0, 0, chunksize, chunksize, fb, chunksize * (i % ChunkLen), chunksize * (i / ChunkLen));
            }
            capi.Render.DestroyFrameBuffer(fb);

            capi.Render.BindTexture2d(Texture.TextureId);
            capi.Render.GlGenerateTex2DMipmaps();

            pixelsToSet = null;
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

        public bool IsVisible(HashSet<FastVec2i> curVisibleChunks)
        {
            for (int dx = 0; dx < ChunkLen; dx++)
            {
                for (int dz = 0; dz < ChunkLen; dz++)
                {
                    FastVec2i tmpVec = new FastVec2i(chunkCoord.X + dx, chunkCoord.Y + dz);
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
