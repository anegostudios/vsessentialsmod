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

        Vec3d lerpedPos = new Vec3d();
        ItemSlot inslot;
        float accum = 0;
        Vec4f particleOutTransform = new Vec4f();
        Vec4f glowRgb = new Vec4f();

        public EntityItemRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
        {
            entityitem = (EntityItem)entity;
            inslot = entityitem.Slot;

            scaleRand = (float)api.World.Rand.NextDouble() / 20f - 1/40f;

            touchGroundMS = entityitem.itemSpawnedMilliseconds - api.World.Rand.Next(5000);

            yRotRand = (float)api.World.Rand.NextDouble() * GameMath.TWOPI;

            lerpedPos = entity.Pos.XYZ;
        }

        public override void DoRender3DOpaque(float dt, bool isShadowPass)
        {
            IRenderAPI rapi = capi.Render;

            // the value 22 is just trial&error, should probably be something proportial to the
            // 13ms game ticks (which is the physics frame rate)
            lerpedPos.X += (entity.Pos.X - lerpedPos.X) * 22 * dt;
            lerpedPos.Y += (entity.Pos.Y - lerpedPos.Y) * 22 * dt;
            lerpedPos.Z += (entity.Pos.Z - lerpedPos.Z) * 22 * dt;

            ItemRenderInfo renderInfo = rapi.GetItemStackRenderInfo(inslot, EnumItemRenderTarget.Ground);
            if (renderInfo.ModelRef == null) return;
            inslot.Itemstack.Collectible.OnBeforeRender(capi, inslot.Itemstack, EnumItemRenderTarget.Ground, ref renderInfo);

            IStandardShaderProgram prog = null;
            LoadModelMatrix(renderInfo, isShadowPass, dt);
                
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
                prog.DontWarpVertices = 0;
                prog.NormalShaded = 1;
                prog.AlphaTest = renderInfo.AlphaTest;

                if (entity.Swimming)
                {
                    prog.AddRenderFlags = (entityitem.Itemstack.Collectible.MaterialDensity > 1000 ? 0 : 1) << 12;
                    prog.WaterWaveCounter = capi.Render.ShaderUniforms.WaterWaveCounter;
                }
                else
                {
                    prog.AddRenderFlags = 0;
                }

                prog.OverlayOpacity = renderInfo.OverlayOpacity;
                if (renderInfo.OverlayTexture != null && renderInfo.OverlayOpacity > 0)
                {
                    prog.Tex2dOverlay2D = renderInfo.OverlayTexture.TextureId;
                    prog.OverlayTextureSize = new Vec2f(renderInfo.OverlayTexture.Width, renderInfo.OverlayTexture.Height);
                    prog.BaseTextureSize = new Vec2f(renderInfo.TextureSize.Width, renderInfo.TextureSize.Height);
                    TextureAtlasPosition texPos = rapi.GetTextureAtlasPosition(entityitem.Itemstack);
                    prog.BaseUvOrigin = new Vec2f(texPos.x1, texPos.y1);
                }


                BlockPos pos = entityitem.Pos.AsBlockPos;
                Vec4f lightrgbs = capi.World.BlockAccessor.GetLightRGBs(pos.X, pos.Y, pos.Z);
                int temp = (int)entityitem.Itemstack.Collectible.GetTemperature(capi.World, entityitem.Itemstack);
                float[] glowColor = ColorUtil.GetIncandescenceColorAsColor4f(temp);
                int extraGlow = GameMath.Clamp((temp - 550) / 2, 0, 255);
                glowRgb.R = glowColor[0];
                glowRgb.G = glowColor[1];
                glowRgb.B = glowColor[2];
                glowRgb.A = extraGlow / 255f;

                prog.ExtraGlow = extraGlow;
                prog.RgbaAmbientIn = rapi.AmbientColor;
                prog.RgbaLightIn = lightrgbs;
                prog.RgbaGlowIn = glowRgb;
                prog.RgbaFogIn = rapi.FogColor;
                prog.FogMinIn = rapi.FogMin;
                prog.FogDensityIn = rapi.FogDensity;
                prog.ExtraGodray = 0;
                prog.NormalShaded = renderInfo.NormalShaded ? 1 : 0;

                prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
                prog.ViewMatrix = rapi.CameraMatrixOriginf;
                prog.ModelMatrix = ModelMat;


                ItemStack stack = entityitem.Itemstack;
                AdvancedParticleProperties[] ParticleProperties = stack.Block?.ParticleProperties;

                if (stack.Block != null && !capi.IsGamePaused)
                {
                    Mat4f.MulWithVec4(ModelMat, new Vec4f(stack.Block.TopMiddlePos.X, stack.Block.TopMiddlePos.Y - 0.4f, stack.Block.TopMiddlePos.Z - 0.5f, 0), particleOutTransform); // No idea why the -0.5f and -0.4f

                    accum += dt;
                    if (ParticleProperties != null && ParticleProperties.Length > 0 && accum > 0.025f)
                    {
                        accum = accum % 0.025f;

                        for (int i = 0; i < ParticleProperties.Length; i++)
                        {
                            AdvancedParticleProperties bps = ParticleProperties[i];
                            bps.basePos.X = particleOutTransform.X + entity.Pos.X;
                            bps.basePos.Y = particleOutTransform.Y + entity.Pos.Y;
                            bps.basePos.Z = particleOutTransform.Z + entity.Pos.Z;

                            entityitem.World.SpawnParticles(bps);
                        }
                    }
                }

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


        float xangle, yangle, zangle;


        private void LoadModelMatrix(ItemRenderInfo renderInfo, bool isShadowPass, float dt)
        {
            IRenderAPI rapi = capi.Render;
            EntityPlayer entityPlayer = capi.World.Player.Entity;

            Mat4f.Identity(ModelMat);
            Mat4f.Translate(ModelMat, ModelMat, 
                (float)(lerpedPos.X - entityPlayer.CameraPos.X), 
                (float)(lerpedPos.Y - entityPlayer.CameraPos.Y), 
                (float)(lerpedPos.Z - entityPlayer.CameraPos.Z)
            );            

            float sizeX = 0.2f * renderInfo.Transform.ScaleXYZ.X;
            float sizeY = 0.2f * renderInfo.Transform.ScaleXYZ.Y;
            float sizeZ = 0.2f * renderInfo.Transform.ScaleXYZ.Z;

            float dx = 0, dz = 0;


            if (!isShadowPass)
            {
                long ellapseMs = capi.World.ElapsedMilliseconds;
                bool freefall = !(entity.Collided || entity.Swimming || capi.IsGamePaused);
                if (!freefall) touchGroundMS = ellapseMs;

                if (entity.Collided)
                {
                    xangle *= 0.55f;
                    yangle *= 0.55f;
                    zangle *= 0.55f;
                }
                else
                {
                    float easeIn = Math.Min(1, (ellapseMs - touchGroundMS) / 200);
                    float angleGain = freefall ? 1000 * dt / 7 * easeIn : 0;

                    yangle += angleGain;
                    xangle += angleGain;
                    zangle += angleGain;
                }

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
