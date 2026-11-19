using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

class DebugShapeRenderer(ICoreClientAPI capi) : IRenderer
{
    private abstract class Request(DebugDraw shape, long elapsedTimeMs)
    {
        public readonly BlockPos Origin = shape.BlockPosition.Copy();
        public readonly long DrawUntil = (long)(elapsedTimeMs + shape.Duration.TotalMilliseconds);
        private MeshData? mesh;
        public MeshData Mesh => mesh ??= buildMesh();
        protected abstract MeshData buildMesh();
    }

    private class LineRequest(DebugDrawLine line, long elapsedTimeMs) : Request(line, elapsedTimeMs)
    {
        protected override MeshData buildMesh()
        {
            var mesh = createMesh(2, 2);
            addLine(mesh,
                line.RelativeOrigin.X, line.RelativeOrigin.Y, line.RelativeOrigin.Z,
                line.RelativeEnd.X, line.RelativeEnd.Y, line.RelativeEnd.Z,
                line.Color);
            return mesh;
        }
    }

    private class ConeRequest(DebugDrawCone cone, long elapsedTimeMs) : Request(cone, elapsedTimeMs)
    {
        protected override MeshData buildMesh()
        {
            int count = 2 * (cone.NumSegments + 1);
            var mesh = createMesh(count, count);

            if (cone.Length <= 0) return mesh;

            var coneOrigin = cone.RelativeOrigin;
            var axis = cone.Axis.Normalize().ToVec3f();

            var tangent = axis.GetOrthogonal();
            var bitangent = axis.Cross(tangent).Normalize();
            tangent = bitangent.Cross(axis).Normalize();

            var angleStep = GameMath.TWOPI / cone.NumSegments;
            var halfAngleCos = (float)Math.Cos(cone.HalfAngleRad);
            var halfAngleSin = (float)Math.Sin(cone.HalfAngleRad);

            var truncate = cone.HalfAngleRad >= MathF.PI / 2 ? 0f : MathF.Max(0f, cone.TruncateDistance);
            var virtualApex = coneOrigin - axis * truncate;

            var nearDistance = truncate > 0f ? truncate / halfAngleCos : 0f;
            var farDiscriminant = cone.Length * cone.Length - truncate * truncate * halfAngleSin * halfAngleSin;
            var farDistance = truncate * halfAngleCos + MathF.Sqrt(farDiscriminant);

            var nearRing = new Vec3f[cone.NumSegments];
            var farRing = new Vec3f[cone.NumSegments];

            // Slant lines
            for (int i = 0; i < cone.NumSegments; i++)
            {
                var rotationAngle = i * angleStep;
                var boundaryDir = (tangent * (float)Math.Cos(rotationAngle) + bitangent * (float)Math.Sin(rotationAngle)).NormalizedCopy();
                var dir = (axis * halfAngleCos + boundaryDir * halfAngleSin).NormalizedCopy();

                var nearPoint = virtualApex + dir * nearDistance;
                var farPoint = virtualApex + dir * farDistance;

                nearRing[i] = nearPoint;
                farRing[i] = farPoint;

                addLine(mesh,
                    nearPoint.X, nearPoint.Y, nearPoint.Z,
                    farPoint.X, farPoint.Y, farPoint.Z,
                    cone.Color);
            }

            // Caps
            for (int i = 0; i < cone.NumSegments; i++)
            {
                var next = (i + 1) % cone.NumSegments;

                if (truncate > 0f)
                {
                    addLine(mesh,
                        nearRing[i].X, nearRing[i].Y, nearRing[i].Z,
                        nearRing[next].X, nearRing[next].Y, nearRing[next].Z,
                        cone.Color);
                }

                addLine(mesh,
                    farRing[i].X, farRing[i].Y, farRing[i].Z,
                    farRing[next].X, farRing[next].Y, farRing[next].Z,
                    cone.Color);
            }

            // Center axis
            var axisEnd = coneOrigin + axis * cone.Length;
            addLine(mesh,
                coneOrigin.X, coneOrigin.Y, coneOrigin.Z,
                axisEnd.X, axisEnd.Y, axisEnd.Z,
                ColorUtil.WhiteArgb);

            return mesh;
        }
    }

    private class SphereRequest(DebugDrawSphere sphere, long elapsedTimeMs) : Request(sphere, elapsedTimeMs)
    {
        protected override MeshData buildMesh() => LineMeshUtil.GetWireframeSphere(sphere.RelativeOrigin, sphere.Radius, sphere.Color);
    }

    private class OBBRequest(DebugDrawOBB obb, long elapsedTimeMs) : Request(obb, elapsedTimeMs)
    {
        // Cube edges as pairs of corner indices (corner = x | y<<1 | z<<2).
        private static readonly int[] edges =
        [
            0, 1,  2, 3,  4, 5,  6, 7,
            0, 2,  1, 3,  4, 6,  5, 7,
            0, 4,  1, 5,  2, 6,  3, 7,
        ];

        protected override MeshData buildMesh()
        {
            var mesh = createMesh(edges.Length, edges.Length);
            var m = obb.LocalMatrix;
            var lmin = obb.LocalMin;
            var lmax = obb.LocalMax;

            Span<float> cx = stackalloc float[8];
            Span<float> cy = stackalloc float[8];
            Span<float> cz = stackalloc float[8];
            for (int i = 0; i < 8; i++)
            {
                float x = (i & 1) != 0 ? lmax.X : lmin.X;
                float y = (i & 2) != 0 ? lmax.Y : lmin.Y;
                float z = (i & 4) != 0 ? lmax.Z : lmin.Z;
                cx[i] = m[0] * x + m[4] * y + m[8]  * z + m[12];
                cy[i] = m[1] * x + m[5] * y + m[9]  * z + m[13];
                cz[i] = m[2] * x + m[6] * y + m[10] * z + m[14];
            }

            for (int e = 0; e < edges.Length; e += 2)
            {
                int a = edges[e], b = edges[e + 1];
                addLine(mesh, cx[a], cy[a], cz[a], cx[b], cy[b], cz[b], obb.Color);
            }
            return mesh;
        }
    }

    private readonly Dictionary<long, Request> requests = new();
    private readonly List<long> finishedRequests = new();
    private readonly BlockPos meshOrigin = new(0);
    private readonly Matrixf modelViewMatrix = new();
    private MeshRef? meshRef;
    private bool meshDirty;

    public double RenderOrder => 0.5;
    public int RenderRange => 100;

    public void RequestDraw(DebugDrawLine line)
    {
        requests[line.Handle] = new LineRequest(line, capi.World.ElapsedMilliseconds);
        meshDirty = true;
    }

    public void RequestDraw(DebugDrawCone cone)
    {
        requests[cone.Handle] = new ConeRequest(cone, capi.World.ElapsedMilliseconds);
        meshDirty = true;
    }

    public void RequestDraw(DebugDrawSphere sphere)
    {
        requests[sphere.Handle] = new SphereRequest(sphere, capi.World.ElapsedMilliseconds);
        meshDirty = true;
    }

    public void RequestDraw(DebugDrawOBB obb)
    {
        requests[obb.Handle] = new OBBRequest(obb, capi.World.ElapsedMilliseconds);
        meshDirty = true;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        finishedRequests.Clear();
        long now = capi.World.ElapsedMilliseconds;
        foreach (var (handle, request) in requests)
        {
            if (now > request.DrawUntil)
            {
                finishedRequests.Add(handle);
            }
        }

        foreach (var handle in finishedRequests)
        {
            requests.Remove(handle);
            meshDirty = true;
        }

        if (meshDirty) rebuildMesh();
        if (meshRef == null) return;

        var cameraPos = capi.World.Player.Entity.CameraPos;
        modelViewMatrix.Set(capi.Render.CameraMatrixOrigin)
            .Translate(meshOrigin.X - cameraPos.X, meshOrigin.InternalY - cameraPos.Y, meshOrigin.Z - cameraPos.Z);
        var prog = capi.Render.GetEngineShader(EnumShaderProgram.Autocamera);
        prog.Use();
        capi.Render.LineWidth = 2;
        capi.Render.BindTexture2d(0);
        prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
        prog.UniformMatrix("modelViewMatrix", modelViewMatrix.Values);
        capi.Render.RenderMesh(meshRef);
        prog.Stop();
    }

    private void rebuildMesh()
    {
        meshDirty = false;
        meshRef?.Dispose();
        meshRef = null;
        if (requests.Count == 0) return;

        int vertices = 0, indices = 0;
        foreach (var request in requests.Values)
        {
            vertices += request.Mesh.VerticesCount;
            indices += request.Mesh.IndicesCount;
        }
        var mesh = createMesh(vertices, indices);
        meshOrigin.Set(capi.World.Player.Entity.Pos);
        foreach (var request in requests.Values)
        {
            mesh.AddMeshData(request.Mesh, request.Origin.X - meshOrigin.X, request.Origin.InternalY - meshOrigin.InternalY, request.Origin.Z - meshOrigin.Z);
        }
        meshRef = capi.Render.UploadMesh(mesh);
    }

    private static MeshData createMesh(int vertices, int indices)
    {
        var mesh = new MeshData(vertices, indices, false, false, true, true);
        mesh.SetMode(EnumDrawMode.Lines);
        return mesh;
    }

    private static void addLine(MeshData mesh, float x1, float y1, float z1, float x2, float y2, float z2, int color)
    {
        int vertex = mesh.VerticesCount;
        mesh.AddVertexSkipTex(x1, y1, z1, color);
        mesh.AddVertexSkipTex(x2, y2, z2, color);
        mesh.AddIndex(vertex);
        mesh.AddIndex(vertex + 1);
    }

    public void Dispose()
    {
        meshRef?.Dispose();
        meshRef = null;
        requests.Clear();
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    }
}
