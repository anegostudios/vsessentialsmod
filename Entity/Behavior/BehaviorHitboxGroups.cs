using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    public class BoneHitbox
    {
        public string ElementName;
        public ElementPose Pose;
        public float SizeX, SizeY, SizeZ;
    }

    public class HitboxGroup
    {
        public string Name;
        public BoneHitbox[] Bones;
        public float DamageMultiplier = 1f;
        public int DebugColorArgb;
    }

    /// <summary>
    /// Per-bone hitboxes grouped into hit regions for damage multipliers and debug visualization
    /// </summary>
    public class EntityBehaviorHitboxGroups : EntityBehavior
    {
        public HitboxGroup[] Groups { get; internal set; } = Array.Empty<HitboxGroup>();

        struct GroupSpec { public string Name; public string[] ElementNames; public float DmgMul; public int Color; }
        GroupSpec[] specs = Array.Empty<GroupSpec>();

        readonly Matrixf hitTestBoneMatrix = new();
        readonly double[] hitTestBoneWorld = new double[16];
        readonly Cuboidd hitTestBoneBounds = new(0, 0, 0, 1, 1, 1);
        readonly TransformedCuboidd hitTestBoneBox = new();
        readonly double[] hitTestAttackWorld = new double[16];
        readonly Cuboidd hitTestAttackBounds = new();
        readonly TransformedCuboidd hitTestAttackBox = new();

        public EntityBehaviorHitboxGroups(Entity entity) : base(entity) { }

        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            base.Initialize(properties, typeAttributes);

            var groupsAttr = typeAttributes?["groups"];
            if (groupsAttr == null || !groupsAttr.Exists)
            {
                entity.Api?.Logger?.Warning("EntityBehaviorHitboxGroups on {0}: missing 'groups' attribute", entity.Code);
                return;
            }

            var built = new List<GroupSpec>();
            foreach (var token in groupsAttr.Token.Children<JProperty>())
            {
                var groupObj = new JsonObject(token.Value);
                built.Add(new GroupSpec
                {
                    Name = token.Name,
                    ElementNames = groupObj["elements"].AsArray<string>(Array.Empty<string>()),
                    DmgMul = groupObj["damageMultiplier"].AsFloat(1f),
                    Color = ParseColor(groupObj["color"].AsString(null), token.Name)
                });
            }

            specs = built.ToArray();
        }

        /// <summary>
        /// Poses this entity's bones now: the server does not pose creatures per tick, and an unposed
        /// ElementPose holds an identity matrix, collapsing every bone onto the model origin.
        /// </summary>
        internal void RefreshPoses()
        {
            (entity.AnimManager?.Animator as AnimatorBase)?.RecalculateMatrices();
        }

        public override void OnTesselated()
        {
            var animator = (entity as EntityPlayer)?.TpAnimManager?.Animator ?? entity.AnimManager?.Animator;
            if (animator == null) return;

            var built = new HitboxGroup[specs.Length];
            for (int g = 0; g < specs.Length; g++)
            {
                var s = specs[g];
                var bones = new List<BoneHitbox>(s.ElementNames.Length);
                foreach (var name in s.ElementNames)
                {
                    var bone = ResolveBone(animator, name);
                    if (bone != null) bones.Add(bone);
                }

                built[g] = new HitboxGroup
                {
                    Name = s.Name,
                    Bones = bones.ToArray(),
                    DamageMultiplier = s.DmgMul,
                    DebugColorArgb = s.Color
                };
            }

            Groups = built;
        }

        public HitboxGroup GetHitGroup(double[] attackerWorldInverse, Vec3f localMin, Vec3f localMax)
        {
            if (Mat4d.Invert(hitTestAttackWorld, attackerWorldInverse) == null) return null;
            hitTestAttackBounds.Set(localMin.X, localMin.Y, localMin.Z, localMax.X, localMax.Y, localMax.Z);
            hitTestAttackBox.Set(hitTestAttackBounds, hitTestAttackWorld);
            return GetHitGroup(hitTestAttackBox);
        }

        /// <summary>
        /// Tests the current pose in absolute world coordinates, including the dimension offset in Y.
        /// Returns the touching or intersecting group with the highest damage multiplier.
        /// The query box is not modified.
        /// </summary>
        public HitboxGroup GetHitGroup(TransformedCuboidd attackBox) => GetHitGroup(attackBox, out _);

        public HitboxGroup GetHitGroup(TransformedCuboidd attackBox, out BoneHitbox hitBone, string boneName = null)
        {
            return GetHitGroup([attackBox], out hitBone, out _, boneName);
        }

        public HitboxGroup GetHitGroup(ReadOnlySpan<TransformedCuboidd> attackBoxes, out BoneHitbox hitBone, out TransformedCuboidd hitBox, string boneName = null)
        {
            if (Groups.Length == 0 && specs.Length > 0) OnTesselated();

            RefreshPoses();

            HitboxGroup best = null;
            hitBone = null;
            hitBox = null;
            foreach (var group in Groups)
            {
                if (group.DamageMultiplier <= best?.DamageMultiplier) continue;

                foreach (var bone in group.Bones)
                {
                    if (boneName != null && bone.ElementName != boneName) continue;
                    if (bone.Pose?.AnimModelMatrix == null) continue;

                    OBBMath.BuildBoneWorldMatrix(entity, bone, hitTestBoneMatrix);
                    OBBMath.Widen(hitTestBoneMatrix.Values, hitTestBoneWorld);
                    hitTestBoneBox.Set(hitTestBoneBounds, hitTestBoneWorld);

                    foreach (var attackBox in attackBoxes)
                    {
                        if (hitTestBoneBox.IntersectsOrTouches(attackBox))
                        {
                            best = group;
                            hitBone = bone;
                            hitBox = attackBox;
                            break;
                        }
                    }
                    if (best == group) break;
                }
            }
            return best;
        }

        public bool TryGetHitPosition(TransformedCuboidd attackBox, BoneHitbox hitBone, out FastVec3d hitPosition)
        {
            OBBMath.BuildBoneWorldMatrix(entity, hitBone, hitTestBoneMatrix);
            OBBMath.Widen(hitTestBoneMatrix.Values, hitTestBoneWorld);
            hitTestBoneBox.Set(hitTestBoneBounds, hitTestBoneWorld);
            return attackBox.TryGetIntersectionSegment(hitTestBoneBox, out hitPosition, out _);
        }

        BoneHitbox ResolveBone(IAnimator animator, string name)
        {
            var pose = animator.GetPosebyName(name);
            if (pose?.ForElement == null)
            {
                entity.Api?.Logger?.Warning("EntityBehaviorHitboxGroups on {0}: element '{1}' not found", entity.Code, name);
                return null;
            }
            var elem = pose.ForElement;
            if (elem.From == null || elem.To == null) return null;

            return new BoneHitbox
            {
                ElementName = name,
                Pose = pose,
                SizeX = (float)(elem.To[0] - elem.From[0]) / 16f,
                SizeY = (float)(elem.To[1] - elem.From[1]) / 16f,
                SizeZ = (float)(elem.To[2] - elem.From[2]) / 16f
            };
        }

        public override string PropertyName() => "hitboxgroups";

        static int ParseColor(string str, string groupName)
        {
            if (!string.IsNullOrEmpty(str))
            {
                str = str.TrimStart('#');
                if (int.TryParse(str, NumberStyles.HexNumber, null, out int rgb))
                    return unchecked((int)0xFF000000) | (rgb & 0x00FFFFFF);
            }

            return groupName?.ToLowerInvariant() switch
            {
                "head" => unchecked((int)0xFFFF4040),
                "body" or "torso" => unchecked((int)0xFF40FF40),
                "limbs" => unchecked((int)0xFF4080FF),
                "arms" => unchecked((int)0xFF40C0FF),
                "legs" => unchecked((int)0xFFC080FF),
                _ => unchecked((int)0xFFFFFFFF),
            };
        }
    }
}
