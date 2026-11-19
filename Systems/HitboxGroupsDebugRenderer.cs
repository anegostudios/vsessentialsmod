using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

#nullable disable

namespace Vintagestory.ServerMods
{
    /// <summary>
    /// Renders per-bone hitbox wireframes for debugging. Toggle with .debug hitboxes [on|off]
    /// </summary>
    public class HitboxGroupsDebugRenderer : ModSystem, IRenderer
    {
        const int MaxRange = 32;
        const float LineWidth = 1.4f;

        ICoreClientAPI capi;
        WireframeCube cube;
        Matrixf mat = new Matrixf();
        readonly Matrixf weaponMatrix = new();
        readonly Matrixf childRotation = new();
        readonly Vec4f weaponColor = new Vec4f(1, 1, 0, 1);
        bool active;

        public double RenderOrder => 1;
        public int RenderRange => MaxRange;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            cube = WireframeCube.CreateUnitCube(api, ColorUtil.WhiteArgb);
            api.Event.RegisterRenderer(this, EnumRenderStage.AfterOIT, "hitboxgroupsdebug");

            api.ChatCommands.GetOrCreate("debug")
                .RequiresPrivilege(Privilege.controlserver)
                .BeginSubCommand("hitboxes")
                    .WithDescription("Toggle per-bone hitbox wireframe overlay")
                    .WithArgs(api.ChatCommands.Parsers.OptionalBool("on"))
                    .HandleWith(args =>
                    {
                        active = args[0] is bool b ? b : !active;
                        return TextCommandResult.Success("hitboxes " + (active ? "on" : "off"));
                    })
                .EndSubCommand();
        }

        public override void Dispose()
        {
            base.Dispose();
            capi?.Event.UnregisterRenderer(this, EnumRenderStage.AfterOIT);
            cube?.Dispose();
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (!active || capi.HideGuis) return;

            var eplr = capi.World.Player.Entity;
            capi.World.GetEntitiesAround(eplr.Pos.XYZ, MaxRange, MaxRange, e =>
            {
                var beh = e.GetBehavior<EntityBehaviorHitboxGroups>();
                if (beh == null || beh.Groups.Length == 0) return false;

                beh.RefreshPoses();

                foreach (var grp in beh.Groups)
                {
                    var color = ColorUtil.ToRGBAVec4f(grp.DebugColorArgb);
                    foreach (var bone in grp.Bones)
                    {
                        if (bone.Pose?.AnimModelMatrix == null) continue;
                        BuildBoneMatrix(e, bone);
                        cube.Render(capi, mat, LineWidth, color);
                    }
                }
                return false;
            });

            var sweep = capi.ModLoader.GetModSystem<OBBMeleeSweepSystem>()?.GetActiveSweep(eplr.EntityId);
            var renderer = eplr.Properties.Client.Renderer as EntityPlayerShapeRenderer;
            if (sweep != null && renderer != null && sweep.TryGetRenderOBB(weaponMatrix.Set(renderer.ModelMat), out var obbMin, out var obbMax))
            {
                bool handProjection = renderer.CurrentRenderMode == RenderMode.FirstPerson;
                if (handProjection) capi.Render.Set3DProjection(capi.Render.ShaderUniforms.ZFar, renderer.HandRenderFov);

                if (sweep.CollisionBoxes != null)
                {
                    foreach (var box in sweep.CollisionBoxes)
                    {
                        mat.Set(capi.Render.CameraMatrixOrigin).Mul(weaponMatrix.Values);
                        if (box is RotatedCuboidd rotated) mat.Mul(childRotation.Set(rotated.GetRotationMatrix()));
                        mat.Translate(box.X1, box.Y1, box.Z1).Scale((float)(box.X2 - box.X1), (float)(box.Y2 - box.Y1), (float)(box.Z2 - box.Z1));
                        cube.Render(capi, mat, LineWidth, weaponColor);
                    }
                }
                else
                {
                    mat.Set(capi.Render.CameraMatrixOrigin)
                       .Mul(weaponMatrix.Values)
                       .Translate(obbMin.X, obbMin.Y, obbMin.Z)
                       .Scale(obbMax.X - obbMin.X, obbMax.Y - obbMin.Y, obbMax.Z - obbMin.Z);
                    cube.Render(capi, mat, LineWidth, weaponColor);
                }

                if (handProjection) capi.Render.Reset3DProjection();
            }
        }

        void BuildBoneMatrix(Entity e, BoneHitbox bone)
        {
            var eplr = capi.World.Player.Entity;
            var pos = e.Pos;

            mat.Identity()
               .Set(capi.Render.CameraMatrixOrigin)
               .Translate(pos.X - eplr.CameraPos.X, pos.InternalY - eplr.CameraPos.Y, pos.Z - eplr.CameraPos.Z);

            OBBMath.ApplyEntityOrientation(mat, e);

            mat.Mul(bone.Pose.AnimModelMatrix)
               .Scale(bone.SizeX, bone.SizeY, bone.SizeZ);
        }
    }
}
