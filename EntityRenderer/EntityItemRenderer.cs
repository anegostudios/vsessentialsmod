using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    public class ModSystemDormancyStateChecker : ModSystem
    {
        ICoreAPI api;

        public override bool ShouldLoad(EnumAppSide forSide) => true;
        public override void Start(ICoreAPI api)
        {
            //api.Event.RegisterGameTickListener(on1stick, 1000);

            this.api = api;
        }

        private void on1stick(float dt)
        {
            // if (api.Side == EnumAppSide.Client)
            // {
            //     EntityBehaviorPassivePhysics.UsePhysicsDormancyStateClient = (api as ICoreClientAPI).World.LoadedEntities.Count > 1500;
            // }
            // else
            // {
            //     EntityBehaviorPassivePhysics.UsePhysicsDormancyStateServer = (api as ICoreServerAPI).World.LoadedEntities.Count > 1500;
            // }
        }
    }

    public class ModSystemItemRendererOptimizer : ModSystem, IRenderer
    {
        int itemCount;
        ICoreClientAPI capi;

        public double RenderOrder => 1;

        public int RenderRange => 1;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            EntityItemRenderer.RenderCount = 0;
        }

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return forSide == EnumAppSide.Client;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            api.Event.RegisterRenderer(this, EnumRenderStage.Before);
            api.Event.RegisterGameTickListener(onTick, 1001);
        }

        private void onTick(float dt)
        {
            itemCount = 0;
            foreach (var val in capi.World.LoadedEntities)
            {
                if (val.Value is EntityItem) itemCount++;
            }

            EntityItemRenderer.RunWittySkipRenderAlgorithm = itemCount > 400;
            EntityItemRenderer.RenderModulo = itemCount / 200;
            EntityItemRenderer.LastPos.Set(-99, -99, -99);
        }
    }

    public class EntityItemRenderer : EntityRenderer
    {
        public static bool RunWittySkipRenderAlgorithm;
        public static BlockPos LastPos = new BlockPos();
        public static int LastCollectibleId;
        public static int RenderCount;
        public static int RenderModulo;

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

        bool rotateWhenFalling;

        public EntityItemRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
        {
            entityitem = (EntityItem)entity;
            inslot = entityitem.Slot;
            rotateWhenFalling = inslot.Itemstack?.Collectible?.Attributes?["rotateWhenFalling"].AsBool(true) ?? true;   // Slot.Itemstack might be null if the Itemstack did not resolve, see EntityItem.Itemstack_set

            scaleRand = (float)api.World.Rand.NextDouble() / 20f - 1/40f;

            touchGroundMS = entityitem.itemSpawnedMilliseconds - api.World.Rand.Next(5000);

            yRotRand = (float)api.World.Rand.NextDouble() * GameMath.TWOPI;

            lerpedPos = entity.Pos.XYZ;
        }

        public override void DoRender3DOpaque(float dt, bool isShadowPass)
        {
            // Optimization: If this item is out of view, lets not render its shadow, since its very likely not gonna contribute to the scene anyway
            if (isShadowPass && !entity.IsRendered) return;

            if (RunWittySkipRenderAlgorithm)
            {
                int x = (int)entity.Pos.X;
                int y = (int)entity.Pos.Y;
                int z = (int)entity.Pos.Z;

                int collId = (entityitem.Itemstack.Class == EnumItemClass.Block ? -1 : 1) * entityitem.Itemstack.Id;

                if (LastPos.X == x && LastPos.Y == y && LastPos.Z == z && LastCollectibleId == collId)
                {
                    if ((entity.EntityId % RenderModulo) != 0) return;
                } else
                {
                    LastPos.Set(x, y, z);
                }

                LastCollectibleId = collId;
            }

            IRenderAPI rapi = capi.Render;

            // the value 22 is just trial&error, should probably be something proportial to the
            // 13ms game ticks (which is the physics frame rate)
            lerpedPos.X += (entity.Pos.X - lerpedPos.X) * 22 * dt;
            lerpedPos.Y += (entity.Pos.InternalY - lerpedPos.Y) * 22 * dt;
            lerpedPos.Z += (entity.Pos.Z - lerpedPos.Z) * 22 * dt;

            ItemRenderInfo renderInfo = rapi.GetItemStackRenderInfo(inslot, EnumItemRenderTarget.Ground, dt);
            if (renderInfo.ModelRef == null || renderInfo.Transform == null) return;

            IStandardShaderProgram prog = null;
            LoadModelMatrix(renderInfo, isShadowPass, dt);

            string textureSampleName = "tex";

            if (isShadowPass)
            {
                textureSampleName = "tex2d";
                float[] mvpMat = Mat4f.Mul(ModelMat, capi.Render.CurrentModelviewMatrix, ModelMat);
                Mat4f.Mul(mvpMat, capi.Render.CurrentProjectionMatrix, mvpMat);
                capi.Render.CurrentActiveShader.UniformMatrix("mvpMatrix", mvpMat);
                capi.Render.CurrentActiveShader.Uniform("origin", new Vec3f());
            }
            else
            {
                prog = rapi.StandardShader;
                prog.Use();
                prog.RgbaTint = entity.Swimming ? new Vec4f(0.5f, 0.5f, 0.5f, 1f) : ColorUtil.WhiteArgbVec;
                prog.DontWarpVertices = 0;
                prog.NormalShaded = 1;
                prog.AlphaTest = renderInfo.AlphaTest;
                prog.DamageEffect = renderInfo.DamageEffect;

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
                Vec4f lightrgbs = capi.World.BlockAccessor.GetLightRGBs(pos.X, pos.InternalY, pos.Z);
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
                            bps.basePos.Y = particleOutTransform.Y + entity.Pos.InternalY;
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

            rapi.RenderMultiTextureMesh(renderInfo.ModelRef, textureSampleName);

            if (!renderInfo.CullFaces)
            {
                rapi.GlEnableCullFace();
            }


            if (!isShadowPass)
            {
                prog.AddRenderFlags = 0; // Lets be nice and reset this to default value
                prog.DamageEffect = 0;
                prog.Stop();
            }

        }


        float xangle, yangle, zangle;


        private void LoadModelMatrix(ItemRenderInfo renderInfo, bool isShadowPass, float dt)
        {
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
                else if (rotateWhenFalling)
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
