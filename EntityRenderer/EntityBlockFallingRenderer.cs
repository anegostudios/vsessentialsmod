using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
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

        public EntityBlockFallingRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
        {
            this.blockFallingEntity = (EntityBlockFalling)entity;
            this.block = blockFallingEntity.Block;
            

            if (blockFallingEntity.removedBlockentity is IBlockShapeSupplier)
            {
                (blockFallingEntity.removedBlockentity as IBlockShapeSupplier).OnTesselation(this, capi.Tesselator);
            }

            if (this.meshRef == null)
            {
                this.meshRef = api.Render.UploadMesh(api.TesselatorManager.GetDefaultBlockMesh(block));
            }
            

            int textureSubId = block.FirstTextureInventory.Baked.TextureSubId;
            this.atlasTextureId = api.BlockTextureAtlas.Positions[textureSubId].atlasTextureId;
        }


        public void AddMeshData(MeshData data)
        {
            this.meshRef = capi.Render.UploadMesh(data);
        }

        public void AddMeshData(MeshData data, int tintColor)
        {
            this.meshRef = capi.Render.UploadMesh(data);
        }


        public override void DoRender3DOpaque(float dt, bool isShadowPass)
        {
            // TODO: ADD
            if (isShadowPass) return;

            if (!blockFallingEntity.InitialBlockRemoved) return;

            float x = (float)entity.Pos.X + entity.CollisionBox.X1;
            float y = (float)entity.Pos.Y + entity.CollisionBox.Y1;
            float z = (float)entity.Pos.Z + entity.CollisionBox.Z1;

            RenderFallingBlockEntity(x, y, z);
        }

        private void RenderFallingBlockEntity(float x, float y, float z)
        {
            IRenderAPI rapi = capi.Render;

            rapi.GlDisableCullFace();
            
            rapi.GlToggleBlend(true, EnumBlendMode.Standard);

            IStandardShaderProgram prog = rapi.PreparedStandardShader((int)entity.Pos.X, (int)entity.Pos.Y, (int)entity.Pos.Z);
            Vec3d camPos = capi.World.Player.Entity.CameraPos;
            prog.Tex2D = atlasTextureId;
            prog.ModelMatrix = ModelMat
                .Identity()
                .Translate(x - camPos.X, y - camPos.Y, z - camPos.Z)
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
