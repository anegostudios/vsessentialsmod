using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    /// <summary>
    /// Handles rendering of falling blocks
    /// </summary>
    public class EntityBlockFallingRenderer : EntityRenderer, ITerrainMeshPool
    {
        protected EntityBlockFalling blockFallingEntity;
        protected MultiTextureMeshRef meshRef;
        protected Block block;
        protected Matrixf ModelMat = new Matrixf();

        Vec3d prevPos = new Vec3d();
        Vec3d curPos = new Vec3d();
        internal bool DoRender;

        MeshData mesh = new MeshData(4, 3, false, true, true, true);

        public EntityBlockFallingRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
        {
            this.blockFallingEntity = (EntityBlockFalling)entity;
            this.block = blockFallingEntity.Block;

            entity.PhysicsUpdateWatcher = OnPhysicsTick;

            if (!blockFallingEntity.InitialBlockRemoved)
            {
                int posx = blockFallingEntity.blockEntityAttributes?.GetInt("posx", blockFallingEntity.initialPos.X) ?? blockFallingEntity.initialPos.X;
                int posy = blockFallingEntity.blockEntityAttributes?.GetInt("posy", blockFallingEntity.initialPos.Y) ?? blockFallingEntity.initialPos.Y;
                int posz = blockFallingEntity.blockEntityAttributes?.GetInt("posz", blockFallingEntity.initialPos.Z) ?? blockFallingEntity.initialPos.Z;

                BlockEntity be = api.World.BlockAccessor.GetBlockEntity(new BlockPos(posx, posy, posz));
                be?.OnTesselation(this, capi.Tesselator);

                if (mesh.VerticesCount > 0)
                {
                    mesh.CustomBytes = null;
                    mesh.CustomFloats = null;
                    mesh.CustomInts = null;
                    this.meshRef = capi.Render.UploadMultiTextureMesh(mesh);
                }
            }

            if (this.meshRef == null)
            {
                MeshData mesh = api.TesselatorManager.GetDefaultBlockMesh(block);
                this.meshRef = api.Render.UploadMultiTextureMesh(mesh);
            }

            int textureSubId = block.FirstTextureInventory.Baked.TextureSubId;

            prevPos.Set(entity.Pos.X + entity.SelectionBox.X1, entity.Pos.Y + entity.SelectionBox.Y1, entity.Pos.Z + entity.SelectionBox.Z1);
        }

        public void OnPhysicsTick(float nextAccum, Vec3d prevPos)
        {
            this.prevPos.Set(prevPos.X + entity.SelectionBox.X1, prevPos.Y + entity.SelectionBox.Y1, prevPos.Z + entity.SelectionBox.Z1);
        }

        public void AddMeshData(MeshData data, int lodlevel = 1)
        {
            if (data == null) return;
            mesh.AddMeshData(data);
        }

        public void AddMeshData(MeshData data, ColorMapData colormapdata, int lodlevel = 1)
        {
            if (data == null) return;
            mesh.AddMeshData(data);
        }
        public void AddMeshData(MeshData data, float[] tfMatrix, int lodLevel = 1)
        {
            if (data == null) return;
            mesh.AddMeshData(data);
        }

        public override void DoRender3DOpaque(float dt, bool isShadowPass)
        {
            // TODO: ADD
            if (isShadowPass) return;

            if (!DoRender || (!blockFallingEntity.InitialBlockRemoved && entity.World.BlockAccessor.GetBlock(blockFallingEntity.initialPos).Id != 0)) return;

            rotaccum += dt;

            curPos.Set(entity.Pos.X + entity.SelectionBox.X1, entity.Pos.Y + entity.SelectionBox.Y1, entity.Pos.Z + entity.SelectionBox.Z1);

            RenderFallingBlockEntity();
        }

        double rotaccum=0;

        private void RenderFallingBlockEntity()
        {
            IRenderAPI rapi = capi.Render;

            rapi.GlDisableCullFace();

            rapi.GlToggleBlend(true, EnumBlendMode.Standard);

            float div = entity.Collided ? 4f : 1.5f;

            IStandardShaderProgram prog = rapi.PreparedStandardShader((int)entity.Pos.X, (int)(entity.Pos.Y + 0.2), (int)entity.Pos.Z);
            Vec3d camPos = capi.World.Player.Entity.CameraPos;
            prog.ModelMatrix = ModelMat
                .Identity()
                .Translate(
                    curPos.X - camPos.X + GameMath.Sin(capi.InWorldEllapsedMilliseconds / 120f + 30) / 20f / div,
                    curPos.Y - camPos.Y,
                    curPos.Z - camPos.Z + GameMath.Cos(capi.InWorldEllapsedMilliseconds / 110f + 20) / 20f / div
                )
                .RotateX((float)(Math.Sin(rotaccum * 10) / 10.0 / div))
                .RotateZ((float)(Math.Cos(10 + rotaccum * 9.0) / 10.0 / div))
                .Values
            ;
            prog.ViewMatrix = rapi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
            rapi.RenderMultiTextureMesh(meshRef, "tex");
            prog.Stop();
        }

        public override void Dispose()
        {
            meshRef?.Dispose();
        }
    }
}
