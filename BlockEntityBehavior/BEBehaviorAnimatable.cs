using System;
using System.Text;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    /// <summary>
    /// Makes block animatable. This behavior has no properties.
    /// Defined with the "Animatable" code.
    /// </summary>
    /// <example><code lang="json">
    ///"entityBehaviors": [
    /// { "name": "Animatable" }
    ///],
    /// </code></example>
    [DocumentAsJson]
    public class BEBehaviorAnimatable : BlockEntityBehavior
    {
        public BEBehaviorAnimatable(BlockEntity blockentity) : base(blockentity) { }

        public BlockEntityAnimationUtil animUtil;

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);
            animUtil = CreateAnimationUtil(api);
        }

        protected virtual BlockEntityAnimationUtil CreateAnimationUtil(ICoreAPI api)
        {
            return new BlockEntityAnimationUtil(api, Blockentity);
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            animUtil?.Dispose();
        }

        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            base.OnBlockBroken(byPlayer);
            animUtil?.Dispose();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            animUtil?.Dispose();
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            if (animUtil.OnTesselation(mesher)) return true;
            return animUtil.activeAnimationsByAnimCode.Count > 0 || (animUtil.animator != null && animUtil.animator.ActiveAnimationCount > 0);
        }

        /// <summary>
        /// Bakes a shape mesh's per-vertex joint transforms and model transform into a static terrain mesh.
        /// The source mesh is not modified; animation-only vertex attributes are removed from the result.
        /// </summary>
        public static MeshData BakePoseMesh(MeshData source, float[] jointMatrices, float[] modelTransform)
        {
            MeshData mesh = source.Clone();
            mesh.NormalsCount = source.NormalsCount;
            int[] jointIds = source.CustomInts?.Values;
            float[] normal = new float[4];
            Span<float> matrix = stackalloc float[16];
            int previousJoint = -1;

            for (int vertex = 0; vertex < mesh.VerticesCount; vertex++)
            {
                int jointId = jointIds == null ? 0 : jointIds[vertex];
                if (jointId < 0 || (long)jointId * 16 + 16 > jointMatrices.Length)
                {
                    throw new ArgumentException("Mesh joint is outside the animation matrices.", nameof(jointMatrices));
                }

                if (jointId != previousJoint)
                {
                    modelTransform.AsSpan().CopyTo(matrix);
                    Mat4f.Mul(matrix, jointMatrices.AsSpan(jointId * 16, 16));
                    previousJoint = jointId;
                }
                Mat4f.MulWithVec3_Position(matrix, mesh.xyz, mesh.xyz, vertex * 3);

                if (mesh.Flags != null)
                {
                    VertexFlags.UnpackNormal(mesh.Flags[vertex], normal);
                    TransformPoseNormal(matrix, normal);
                    mesh.Flags[vertex] = (mesh.Flags[vertex] & ~VertexFlags.NormalBitMask)
                        | VertexFlags.PackNormal(normal[0], normal[1], normal[2]);
                }
                if (mesh.Normals != null)
                {
                    // Decode signed 10-bit components as the vertex attribute reader does.
                    int packed = mesh.Normals[vertex];
                    normal[0] = Math.Max(-1, (packed << 22 >> 22) / 511f);
                    normal[1] = Math.Max(-1, (packed << 12 >> 22) / 511f);
                    normal[2] = Math.Max(-1, (packed << 2 >> 22) / 511f);
                    TransformPoseNormal(matrix, normal);
                    mesh.Normals[vertex] = (mesh.Normals[vertex] & unchecked((int)0xc0000000))
                        | NormalUtil.PackNormal(normal[0], normal[1], normal[2]);
                }

                if (vertex % mesh.VerticesPerFace == 0 && mesh.XyzFaces != null)
                {
                    int face = vertex / mesh.VerticesPerFace;
                    if (face < mesh.XyzFacesCount && mesh.XyzFaces[face] != 0)
                    {
                        Vec3f facing = BlockFacing.ALLFACES[mesh.XyzFaces[face] - 1].Normalf;
                        mesh.XyzFaces[face] = Mat4f.MulWithVec3_BlockFacing(matrix, facing).MeshDataIndex;
                    }
                }
            }

            // These attributes contain joint IDs and animated damage effects, not terrain shader data.
            mesh.CustomInts = null;
            mesh.CustomFloats = null;
            return mesh;
        }

        private static void TransformPoseNormal(Span<float> matrix, float[] normal)
        {
            Mat4f.MulWithVec3(matrix, normal, normal);
            float length = GameMath.RootSumOfSquares(normal[0], normal[1], normal[2]);
            if (length == 0) return;
            normal[0] /= length;
            normal[1] /= length;
            normal[2] /= length;
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            if (Api is ICoreClientAPI capi && capi.Settings.Bool["extendedDebugInfo"] == true)
            {
                dsc.Append("<font color=\"#bbb\">Active animations:");
                foreach (var val in animUtil.activeAnimationsByAnimCode)
                {
                    dsc.Append(val.Key + " (frame " + animUtil.animator.GetAnimationState(val.Key)?.CurrentFrame +")");
                }

                dsc.AppendLine("</font>");

            }
        }
    }
}
