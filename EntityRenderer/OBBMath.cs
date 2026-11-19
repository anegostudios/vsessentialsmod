using System.Collections.Concurrent;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.GameContent
{
    /// <summary>
    /// Helpers for building world-space matrices for entities and their bones / held items.
    /// Mirrors the matrix-building paths used by EntityShapeRenderer and HitboxGroupsDebugRenderer
    /// but without the camera transform, so the result is in absolute world space.
    /// </summary>
    public static class OBBMath
    {
        public static Matrixf BuildEntityWorldMatrix(Entity e, Matrixf into = null)
        {
            into ??= new Matrixf();
            var pos = e.Pos;
            into.Identity().Translate(pos.X, pos.InternalY, pos.Z);
            ApplyEntityOrientation(into, e);
            return into;
        }

        public static Matrixf BuildFirstPersonWorldMatrix(EntityPlayer player, Matrixf into = null)
        {
            into ??= new Matrixf();
            var pos = player.Pos;
            into.Identity().Translate(pos.X, pos.InternalY, pos.Z);
            ApplyPlayerOrientation(into, player, true);
            return into;
        }

        static Matrixf BuildSelfRenderWorldMatrix(EntityPlayer player, Matrixf into)
        {
            var capi = player.World.Api as ICoreClientAPI;
            if (capi?.World.Player?.Entity.EntityId != player.EntityId) return null;
            if (player.Properties.Client.Renderer is not EntityPlayerShapeRenderer renderer) return null;

            var camPos = player.CameraPos;
            into ??= new Matrixf();
            return into.Identity().Translate(camPos.X, camPos.Y, camPos.Z).Mul(renderer.ModelMat);
        }

        static Matrixf BuildAttackerWorldMatrix(Entity attacker, Matrixf into)
        {
            if (attacker is EntityPlayer player)
            {
                var selfMatrix = BuildSelfRenderWorldMatrix(player, into);
                if (selfMatrix != null) return selfMatrix;
            }

            return BuildEntityWorldMatrix(attacker, into);
        }

        public static void ApplyEntityOrientation(Matrixf m, Entity e)
        {
            if (e is EntityPlayer player) ApplyPlayerOrientation(m, player);
            else ApplyGenericOrientation(m, e);
        }

        private static void ApplyGenericOrientation(Matrixf m, Entity e)
        {
            float halfY2 = e.SelectionBox.Y2 / 2f;
            float scale = e.Properties.Client.Size;
            var renderer = e.Properties.Client.Renderer as EntityShapeRenderer;

            m.RotateY(GameMath.PIHALF + (float)e.Pos.Yaw);

            if (renderer != null)
            {
                m.Translate(0, halfY2, 0)
                    .RotateX(renderer.xangle)
                    .RotateY(renderer.yangle)
                    .RotateZ(renderer.zangle)
                    .Translate(0, -halfY2, 0);
            }

            m.Translate(0, 0.7f, 0)
                .RotateX(renderer?.nowSwivelRad ?? 0)
                .Translate(0, -0.7f, 0)
                .Scale(scale, scale, scale)
                .Translate(-0.5f, 0, -0.5f);
        }

        private static void ApplyPlayerOrientation(Matrixf m, EntityPlayer player, bool firstPerson = false)
        {
            var pos = player.Pos;
            float scale = player.Properties.Client.Size;
            var shape = player.Properties.Client.Shape;
            float rotX = shape?.rotateX ?? 0;
            float rotY = shape?.rotateY ?? 0;
            float rotZ = shape?.rotateZ ?? 0;
            var renderer = player.Properties.Client.Renderer as EntityPlayerShapeRenderer;
            float swivel = renderer?.nowSwivelRad ?? player.sidewaysSwivelAngle;
            float bodyYaw = player.World?.Side == EnumAppSide.Server
                ? player.BodyYawServer
                : renderer?.smoothedBodyYaw ?? player.BodyYaw;

            m.RotateX(pos.Roll + rotX * GameMath.DEG2RAD)
                .RotateY(bodyYaw + GameMath.PIHALF + rotY * GameMath.DEG2RAD);

            if (!firstPerson || (!player.Swimming && !player.Controls.Gliding && player.MountedOn == null))
            {
                m.RotateZ(player.WalkPitch + rotZ * GameMath.DEG2RAD);
            }

            m.RotateX(swivel);

            if (firstPerson) ApplyFirstPersonPitchFollow(m, player);

            m.Scale(scale, scale, scale)
                .Translate(-0.5f, 0, -0.5f);
        }

        private static void ApplyFirstPersonPitchFollow(Matrixf m, EntityPlayer player)
        {
            var stack = player.RightHandItemSlot?.Itemstack;
            float itemFollow = stack?.ItemAttributes?["heldItemPitchFollow"].AsFloat(0.75f) ?? 0.75f;
            float mountFollow = player.MountedOn?.FpHandPitchFollow ?? 1;
            float follow = player.Controls.IsFlying ? 1f : itemFollow * mountFollow;
            float eyeY = (float)player.LocalEyePos.Y;

            m.Translate(0, eyeY, 0)
                .RotateZ((player.Pos.Pitch - GameMath.PI) * follow)
                .Translate(0, -eyeY, 0);
        }

        public static Matrixf BuildBoneWorldMatrix(Entity e, BoneHitbox bone, Matrixf into = null)
        {
            var m = BuildEntityWorldMatrix(e, into);
            m.Mul(bone.Pose.AnimModelMatrix);
            m.Scale(bone.SizeX, bone.SizeY, bone.SizeZ);
            return m;
        }

        public static bool TryBuildWeaponWorldMatrix(
            Entity attacker, string boneName,
            out Matrixf worldMatrix, Matrixf reuse = null)
        {
            return TryBuildWeaponWorldMatrix(attacker, boneName, BuildAttackerWorldMatrix(attacker, reuse), out worldMatrix);
        }

        public static bool TryBuildWeaponWorldMatrix(
            Entity attacker, string boneName,
            Matrixf entityWorld, out Matrixf worldMatrix)
        {
            var anim = attacker.AnimManager?.Animator;
            if (anim == null) { worldMatrix = null; return false; }

            float[] boneMatrix = null;
            var pose = anim.GetPosebyName(boneName);
            if (pose?.AnimModelMatrix != null)
            {
                boneMatrix = pose.AnimModelMatrix;
            }
            else
            {
                var apap = anim.GetAttachmentPointPose(boneName);
                if (apap?.AnimModelMatrix != null) boneMatrix = apap.AnimModelMatrix;
            }
            if (boneMatrix == null) { worldMatrix = null; return false; }

            worldMatrix = entityWorld.Mul(boneMatrix);
            return true;
        }

        public static bool TryBuildItemShapeWorldMatrix(
            Entity attacker, string attachmentPointName, out Matrixf worldMatrix, Matrixf reuse = null)
        {
            return TryBuildItemShapeWorldMatrix(attacker, attachmentPointName, BuildAttackerWorldMatrix(attacker, reuse), out worldMatrix);
        }

        public static bool TryBuildItemShapeWorldMatrix(
            Entity attacker, string attachmentPointName, Matrixf entityWorld, out Matrixf worldMatrix)
        {
            worldMatrix = null;

            var anim = attacker.AnimManager?.Animator;
            if (anim == null) return false;

            var apap = anim.GetAttachmentPointPose(attachmentPointName);
            if (apap?.AnimModelMatrix == null || apap.AttachPoint == null) return false;

            var itemStack = (attacker as EntityAgent)?.RightHandItemSlot?.Itemstack;
            if (itemStack == null) return false;

            var handTf = itemStack.Collectible?.TpHandTransform?.EnsureDefaultValues();
            worldMatrix = ComposeItemShapeWorldMatrix(entityWorld, apap, handTf);
            return true;
        }

        public static Matrixf ComposeItemShapeWorldMatrix(
            Matrixf entityWorld, AttachmentPointAndPose apap, ModelTransform handTf)
        {
            var ap = apap.AttachPoint;
            var origin = handTf?.Origin ?? new FastVec3f(0, 0, 0);
            var translation = handTf?.Translation ?? new FastVec3f(0, 0, 0);
            var rotation = handTf?.Rotation ?? new FastVec3f(0, 0, 0);
            var scale = handTf?.ScaleXYZ ?? new FastVec3f(1, 1, 1);

            var m = entityWorld;
            m.Mul(apap.AnimModelMatrix);
            m.Translate(origin.X, origin.Y, origin.Z);
            m.Scale(scale.X, scale.Y, scale.Z);
            m.Translate(
                (float)ap.PosX / 16f + translation.X,
                (float)ap.PosY / 16f + translation.Y,
                (float)ap.PosZ / 16f + translation.Z);
            m.Rotate(
                (float)(ap.RotationX + rotation.X) * GameMath.DEG2RAD,
                (float)(ap.RotationY + rotation.Y) * GameMath.DEG2RAD,
                (float)(ap.RotationZ + rotation.Z) * GameMath.DEG2RAD);
            m.Translate(-origin.X, -origin.Y, -origin.Z);
            return m;
        }

        /// <summary>
        /// Mirrors ShapeElement.GetInverseModelMatrix without the final invert.
        /// </summary>
        public static float[] BuildShapeElementForwardMatrix(ShapeElement elem)
        {
            var ancestors = elem.GetParentPath();

            float[] result = Mat4f.Create();
            Mat4f.Identity(result);
            float[] tmp = new float[16];

            for (int i = 0; i < ancestors.Count; i++)
            {
                Mat4f.Identity(tmp);
                ancestors[i].GetLocalTransformMatrix(0, tmp);
                Mat4f.Mul(result, result, tmp);
            }

            Mat4f.Identity(tmp);
            elem.GetLocalTransformMatrix(0, tmp);
            Mat4f.Mul(result, result, tmp);

            return result;
        }

        public static bool TryComputeShapeBounds(Shape shape, out Vec3f localMin, out Vec3f localMax, string elementName = null)
        {
            localMin = localMax = null;
            if (shape?.Elements == null || shape.Elements.Length == 0) return false;

            ShapeElement[] roots;
            if (string.IsNullOrEmpty(elementName)) roots = shape.Elements;
            else
            {
                var named = shape.GetElementByName(elementName);
                if (named == null) return false;
                roots = new[] { named };
            }

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            bool any = false;

            var pending = new Stack<ShapeElement>(roots);
            while (pending.Count > 0)
            {
                var elem = pending.Pop();
                if (elem.Children != null) foreach (var child in elem.Children) pending.Push(child);
                if (elem.From == null || elem.To == null) continue;

                var fwd = BuildShapeElementForwardMatrix(elem);
                float sx = (float)(elem.To[0] - elem.From[0]) / 16f;
                float sy = (float)(elem.To[1] - elem.From[1]) / 16f;
                float sz = (float)(elem.To[2] - elem.From[2]) / 16f;

                for (int corner = 0; corner < 8; corner++)
                {
                    float lx = (corner & 1) == 0 ? 0 : sx;
                    float ly = (corner & 2) == 0 ? 0 : sy;
                    float lz = (corner & 4) == 0 ? 0 : sz;
                    float wx = fwd[0] * lx + fwd[4] * ly + fwd[8] * lz + fwd[12];
                    float wy = fwd[1] * lx + fwd[5] * ly + fwd[9] * lz + fwd[13];
                    float wz = fwd[2] * lx + fwd[6] * ly + fwd[10] * lz + fwd[14];
                    if (wx < minX) minX = wx; if (wy < minY) minY = wy; if (wz < minZ) minZ = wz;
                    if (wx > maxX) maxX = wx; if (wy > maxY) maxY = wy; if (wz > maxZ) maxZ = wz;
                    any = true;
                }
            }

            if (!any) return false;
            localMin = new Vec3f(minX, minY, minZ);
            localMax = new Vec3f(maxX, maxY, maxZ);
            return true;
        }

        public static bool TryGetShapeBounds(ICoreAPI api, AssetLocation shapePath, out Vec3f localMin, out Vec3f localMax, string elementName = null)
        {
            localMin = localMax = null;
            if (shapePath == null) return false;

            var shapeBoundsCache = ObjectCacheUtil.GetOrCreate(api, "obbShapeBounds", () => new ConcurrentDictionary<string, Vec3f[]>());
            var key = string.IsNullOrEmpty(elementName) ? shapePath.ToString() : shapePath + "|" + elementName;
            if (shapeBoundsCache.TryGetValue(key, out var cached))
            {
                localMin = cached[0];
                localMax = cached[1];
                return true;
            }

            var shape = Shape.TryGet(api, shapePath);
            if (shape?.Elements == null) return false;

            foreach (var elem in shape.Elements) elem.ResolveReferences();

            if (!TryComputeShapeBounds(shape, out localMin, out localMax, elementName)) return false;

            shapeBoundsCache[key] = new[] { localMin, localMax };
            return true;
        }

        public static void Widen(float[] src, double[] dst)
        {
            for (int i = 0; i < 16; i++) dst[i] = src[i];
        }

        public static Cuboidd[] BuildShapeColliders(Shape shape, CompositeShape compositeShape, string[] selectiveElements, string[] ignoreElements)
        {
            var collisionShape = new Shape { Elements = shape.CloneElements() };
            collisionShape.RemoveElements(compositeShape.IgnoreElements);
            if (compositeShape.SelectiveElements != null)
            {
                var visibleElements = new HashSet<ShapeElement>();
                foreach (string selector in compositeShape.SelectiveElements)
                {
                    collisionShape.WalkElements(selector, element => visibleElements.Add(element));
                }
                collisionShape.WalkElements("*", element =>
                {
                    if (!visibleElements.Contains(element)) element.To = element.From;
                });
            }

            var transform = new Matrixf().Identity()
                .Translate(compositeShape.offsetX + 0.5f, compositeShape.offsetY + 0.5f, compositeShape.offsetZ + 0.5f)
                .Rotate(compositeShape.rotateX * GameMath.DEG2RAD, compositeShape.rotateY * GameMath.DEG2RAD, compositeShape.rotateZ * GameMath.DEG2RAD)
                .Translate(0, -0.5f, 0)
                .Scale(compositeShape.Scale, compositeShape.Scale, compositeShape.Scale)
                .Translate(-0.5f, 0, -0.5f);
            var modelTransform = new double[16];
            Widen(transform.Values, modelTransform);
            return ShapeColliderBuilder.Build(collisionShape, selectiveElements, ignoreElements, modelTransform);
        }
    }
}
