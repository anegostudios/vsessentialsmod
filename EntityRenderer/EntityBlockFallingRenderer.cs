using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    /// <summary>
    /// Handles rendering of falling blocks
    /// </summary>
    public class EntityBlockFallingRenderer : EntityRenderer, ITerrainMeshPool
    {
        protected EntityBlockFalling blockFallingEntity;
        protected MeshRef meshRef;
        protected Block block;
        protected int atlasTextureId;
        protected Matrixf ModelMat = new Matrixf();

        double accum;
        Vec3d prevPos = new Vec3d();
        Vec3d curPos = new Vec3d();
        long ellapsedMsPhysics;
        internal bool DoRender;

        MeshData mesh = new MeshData(4, 3, false, true, true, false, true);

        public EntityBlockFallingRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
        {
            this.blockFallingEntity = (EntityBlockFalling)entity;
            this.block = blockFallingEntity.Block;

            entity.PhysicsUpdateWatcher = OnPhysicsTick;

            if (!blockFallingEntity.InitialBlockRemoved)
            {
                BlockEntity be = api.World.BlockAccessor.GetBlockEntity(blockFallingEntity.initialPos);
                be?.OnTesselation(this, capi.Tesselator);

                if (mesh.VerticesCount > 0)
                {
                    mesh.CustomBytes = null;
                    mesh.CustomFloats = null;
                    mesh.CustomInts = null;
                    this.meshRef = capi.Render.UploadMesh(mesh);
                }
            }

            if (this.meshRef == null)
            {
                MeshData mesh = api.TesselatorManager.GetDefaultBlockMesh(block);
                byte[] rgba2 = mesh.Rgba2;
                mesh.Rgba2 = null; // Don't need rba2 but also need to restore that stuff afterwards
                this.meshRef = api.Render.UploadMesh(mesh);
                mesh.Rgba2 = rgba2;
            }
            
            int textureSubId = block.FirstTextureInventory.Baked.TextureSubId;
            this.atlasTextureId = api.BlockTextureAtlas.Positions[textureSubId].atlasTextureId;

            prevPos.Set(entity.Pos.X + entity.CollisionBox.X1, entity.Pos.Y + entity.CollisionBox.Y1, entity.Pos.Z + entity.CollisionBox.Z1);
        }


        public void OnPhysicsTick(float nextAccum)
        {
            this.accum = nextAccum;
            prevPos.Set(entity.Pos.X + entity.CollisionBox.X1, entity.Pos.Y + entity.CollisionBox.Y1, entity.Pos.Z + entity.CollisionBox.Z1);

            ellapsedMsPhysics = capi.ElapsedMilliseconds;
        }

        public void AddMeshData(MeshData data)
        {
            mesh.AddMeshData(data);
        }

        public void AddMeshData(MeshData data, int tintColor)
        {
            mesh.AddMeshData(data);
        }


        public override void DoRender3DOpaque(float dt, bool isShadowPass)
        {
            // TODO: ADD
            if (isShadowPass) return;

            if (!DoRender || !blockFallingEntity.InitialBlockRemoved) return;

            curPos.Set(entity.Pos.X + entity.CollisionBox.X1, entity.Pos.Y + entity.CollisionBox.Y1, entity.Pos.Z + entity.CollisionBox.Z1);

            RenderFallingBlockEntity();
        }

        private void RenderFallingBlockEntity()
        {
            IRenderAPI rapi = capi.Render;

            rapi.GlDisableCullFace();
            
            rapi.GlToggleBlend(true, EnumBlendMode.Standard);

            
            accum += (capi.ElapsedMilliseconds - ellapsedMsPhysics) / 1000f;
            ellapsedMsPhysics = capi.ElapsedMilliseconds;

            double alpha = accum / GlobalConstants.PhysicsFrameTime;
            
            IStandardShaderProgram prog = rapi.PreparedStandardShader((int)entity.Pos.X, (int)entity.Pos.Y, (int)entity.Pos.Z);
            Vec3d camPos = capi.World.Player.Entity.CameraPos;
            prog.Tex2D = atlasTextureId;
            prog.ModelMatrix = ModelMat
                .Identity()
                .Translate(
                    prevPos.X + (curPos.X - prevPos.X) * alpha - camPos.X, 
                    prevPos.Y + (curPos.Y - prevPos.Y) * alpha - camPos.Y, 
                    prevPos.Z + (curPos.Z - prevPos.Z) * alpha - camPos.Z
                )
                .Values
            ;
            prog.ViewMatrix = rapi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
            rapi.RenderMesh(meshRef);
            prog.Stop();
        }

        public override void Dispose()
        {
            capi.Render.DeleteMesh(meshRef);
        }

    }
}
