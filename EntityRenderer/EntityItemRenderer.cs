using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityItemRenderer : EntityRenderer
    {
        EntityItem entityitem;
        
        long touchGroundMS;

        public float[] ModelMat = Mat4f.Create();

        // Tiny item size randomization to prevent z-fighting
        float scaleRand;

        float yRotRand;


        public EntityItemRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
        {
            entityitem = (EntityItem)entity;
            scaleRand = (float)api.World.Rand.NextDouble() / 20f - 1/40f;

            touchGroundMS = entityitem.itemSpawnedMilliseconds - api.World.Rand.Next(5000);

            yRotRand = (float)api.World.Rand.NextDouble() * GameMath.TWOPI;
        }

        public override void DoRender3DOpaque(float dt, bool isShadowPass)
        {
            IRenderAPI rapi = capi.Render;
            IEntityPlayer entityPlayer = capi.World.Player.Entity;


            ItemRenderInfo renderInfo = rapi.GetItemStackRenderInfo(entityitem.Itemstack, EnumItemRenderTarget.Ground);
            if (renderInfo.ModelRef == null) return;

            IStandardShaderProgram prog = null;
            LoadModelMatrix(renderInfo, isShadowPass);
            
            if (isShadowPass)
            {
                rapi.CurrentActiveShader.BindTexture2D("tex2d", renderInfo.TextureId, 0);
                float[] mvpMat = Mat4f.Mul(ModelMat, capi.Render.CurrentModelviewMatrix, ModelMat);
                Mat4f.Mul(mvpMat, capi.Render.CurrentProjectionMatrix, mvpMat);
                capi.Render.CurrentActiveShader.UniformMatrix("mvpMatrix", mvpMat);
                capi.Render.CurrentActiveShader.Uniform("origin", new Vec3f());
            }
            else
            {
                prog = rapi.StandardShader;
                prog.Use();
                prog.Tex2D = renderInfo.TextureId;
                prog.RgbaTint = ColorUtil.WhiteArgbVec;

                if (entity.Swimming)
                {
                    prog.WaterWave = entityitem.Itemstack.Collectible.MaterialDensity > 1000 ? 0 : 1;
                    prog.WaterWaveCounter = capi.Render.WaterWaveCounter;
                    prog.Playerpos = new Vec3f((float)entityPlayer.CameraPos.X, (float)entityPlayer.CameraPos.Y, (float)entityPlayer.CameraPos.Z);
                }
                else
                {
                    prog.WaterWave = 0;
                }

                BlockPos pos = entityitem.Pos.AsBlockPos;
                Vec4f lightrgbs = capi.World.BlockAccessor.GetLightRGBs(pos.X, pos.Y, pos.Z);
                int temp = (int)entityitem.Itemstack.Collectible.GetTemperature(capi.World, entityitem.Itemstack);
                float[] glowColor = ColorUtil.GetIncandescenceColorAsColor4f(temp);
                lightrgbs[0] += 2 * glowColor[0];
                lightrgbs[1] += 2 * glowColor[1];
                lightrgbs[2] += 2 * glowColor[2];

                prog.ExtraGlow = GameMath.Clamp((temp - 500) / 3, 0, 255);
                prog.RgbaAmbientIn = rapi.AmbientColor;
                prog.RgbaLightIn = lightrgbs;
                prog.RgbaBlockIn = ColorUtil.WhiteArgbVec;
                prog.RgbaFogIn = rapi.FogColor;
                prog.FogMinIn = rapi.FogMin;
                prog.FogDensityIn = rapi.FogDensity;

                prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
                prog.ViewMatrix = rapi.CameraMatrixOriginf;
                prog.ModelMatrix = ModelMat;
            }


            if (!renderInfo.CullFaces)
            {
                rapi.GlDisableCullFace();
            }

            rapi.RenderMesh(renderInfo.ModelRef);

            if (!renderInfo.CullFaces)
            {
                rapi.GlEnableCullFace();
            }

            
            if (!isShadowPass)
            {
                prog.Stop();
            }
            
        }


        private void LoadModelMatrix(ItemRenderInfo renderInfo, bool isShadowPass)
        {
            IRenderAPI rapi = capi.Render;
            IEntityPlayer entityPlayer = capi.World.Player.Entity;

            Mat4f.Identity(ModelMat);
            Mat4f.Translate(ModelMat, ModelMat, 
                (float)(entityitem.Pos.X - entityPlayer.CameraPos.X), 
                (float)(entityitem.Pos.Y - entityPlayer.CameraPos.Y), 
                (float)(entityitem.Pos.Z - entityPlayer.CameraPos.Z)
            );            

            float sizeX = 0.2f * renderInfo.Transform.ScaleXYZ.X;
            float sizeY = 0.2f * renderInfo.Transform.ScaleXYZ.Y;
            float sizeZ = 0.2f * renderInfo.Transform.ScaleXYZ.Z;



            long ellapseMs = capi.World.ElapsedMilliseconds;
            if (entity.Collided || entity.Swimming || capi.IsGamePaused) touchGroundMS = ellapseMs;

            float easeIn = Math.Min(1, (ellapseMs - touchGroundMS) / 200);
            float angleMs = Math.Max(ellapseMs - touchGroundMS, 0) * easeIn;


            float yangle = angleMs / 7f;
            float xangle = angleMs / 7f;
            float zangle = angleMs / 7f;

            float dx = 0, dz = 0;

            if (entity.Swimming)
            {
                float diff = 1;
                if (entityitem.Itemstack.Collectible.MaterialDensity > 1000)
                {
                    dx = GameMath.Sin((float)(ellapseMs / 1000.0)) / 50;
                    dz = -GameMath.Sin((float)(ellapseMs / 3000.0)) / 50;
                    diff = 0.1f;
                }

                xangle = GameMath.Sin((float)(ellapseMs / 1000.0)) * 8 * diff;
                yangle = GameMath.Cos((float)(ellapseMs / 2000.0)) * 3 * diff;
                zangle = -GameMath.Sin((float)(ellapseMs / 3000.0)) * 8 * diff;
            }



            Mat4f.Translate(ModelMat, ModelMat, dx + renderInfo.Transform.Translation.X, renderInfo.Transform.Translation.Y, dz +  renderInfo.Transform.Translation.Z);
            Mat4f.Scale(ModelMat, ModelMat, new float[] { sizeX + scaleRand, sizeY + scaleRand, sizeZ + scaleRand });
            Mat4f.RotateY(ModelMat, ModelMat, GameMath.DEG2RAD * (renderInfo.Transform.Rotation.Y + yangle) + (renderInfo.Transform.Rotate ? yRotRand : 0));
            Mat4f.RotateZ(ModelMat, ModelMat, GameMath.DEG2RAD * (renderInfo.Transform.Rotation.Z + zangle));
            Mat4f.RotateX(ModelMat, ModelMat, GameMath.DEG2RAD * (renderInfo.Transform.Rotation.X + xangle));
            Mat4f.Translate(ModelMat, ModelMat, -renderInfo.Transform.Origin.X , -renderInfo.Transform.Origin.Y, -renderInfo.Transform.Origin.Z);
        }



        public override void Dispose()
        {

        }

    }
}
