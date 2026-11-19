using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent;

public class MeleeHitDebugSystem : ModSystem
{
    private DebugDrawSystem debugDraw = null!;
    private float durationSeconds;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        debugDraw = api.ModLoader.GetModSystem<DebugDrawSystem>() ?? throw new InvalidOperationException("Melee hit diagnostics require the debug drawing system.");
        api.ChatCommands.GetOrCreate("debug")
            .BeginSubCommand("melee")
                .WithDescription("Record melee strike boxes, overlap markers, and the target's full hitbox pose. Duration in seconds, 0 disables recording.")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(api.ChatCommands.Parsers.OptionalFloat("seconds", 30))
                .HandleWith(args =>
                {
                    if (api.ModLoader.GetModSystem<OBBMeleeSweepSystem>() == null)
                    {
                        return TextCommandResult.Error(Lang.Get("OBB combat is disabled in this world. Run /worldconfig obbCombat true, then leave and reopen the world."));
                    }
                    float seconds = (float)args[0];
                    if (!float.IsFinite(seconds) || seconds < 0 || seconds > 300)
                    {
                        return TextCommandResult.Error(Lang.Get("Duration must be between 0 and 300 seconds."));
                    }
                    durationSeconds = seconds;
                    return TextCommandResult.Success(seconds == 0
                        ? Lang.Get("Melee impact recording off.")
                        : Lang.Get("Melee impacts retained for {0} seconds. Target: gray, struck bone: white. Slash: red, pierce: cyan, tear: purple, blunt: orange.", seconds));
                })
            .EndSubCommand();
    }

    public void RecordHit(EntityBehaviorHitboxGroups hitboxes, BoneHitbox hitBone, TransformedCuboidd weaponBox, EnumDamageType damageType, IReadOnlyList<TransformedCuboidd>? weaponBoxes = null)
    {
        if (durationSeconds <= 0) return;

        var origin = hitboxes.entity.Pos.AsBlockPos;
        var duration = TimeSpan.FromSeconds(durationSeconds);
        int color = damageType switch
        {
            EnumDamageType.SlashingAttack => ColorUtil.ColorFromRgba(255, 64, 64, 255),
            EnumDamageType.PiercingAttack => ColorUtil.ColorFromRgba(64, 224, 255, 255),
            EnumDamageType.Tearing => ColorUtil.ColorFromRgba(208, 96, 255, 255),
            EnumDamageType.BluntAttack => ColorUtil.ColorFromRgba(255, 176, 48, 255),
            _ => ColorUtil.WhiteArgb
        };
        var matrix = OBBMath.BuildBoneWorldMatrix(hitboxes.entity, hitBone);
        var worldMatrix = new double[16];
        OBBMath.Widen(matrix.Values, worldMatrix);
        var bounds = new Cuboidd(0, 0, 0, 1, 1, 1);
        var box = new TransformedCuboidd(bounds, worldMatrix);
        bool hasSegment = weaponBox.TryGetIntersectionSegment(box, out var start, out var end);
        if (weaponBoxes == null) drawBox(weaponBox, origin, color, duration);
        else
        {
            for (int i = 0; i < weaponBoxes.Count; i++) drawBox(weaponBoxes[i], origin, color, duration);
        }
        drawBox(box, origin, ColorUtil.WhiteArgb, duration);

        var poses = new HashSet<ElementPose> { hitBone.Pose };
        foreach (var group in hitboxes.Groups)
        {
            foreach (var bone in group.Bones)
            {
                if (bone.Pose?.AnimModelMatrix == null || !poses.Add(bone.Pose)) continue;
                OBBMath.BuildBoneWorldMatrix(hitboxes.entity, bone, matrix);
                OBBMath.Widen(matrix.Values, worldMatrix);
                box.Set(bounds, worldMatrix);
                drawBox(box, origin, ColorUtil.ColorFromRgba(160, 160, 160, 255), duration);
            }
        }
        if (!hasSegment) return;

        var localStart = new Vec3f((float)(start.X - origin.X), (float)(start.Y - origin.InternalY), (float)(start.Z - origin.Z));
        var localEnd = new Vec3f((float)(end.X - origin.X), (float)(end.Y - origin.InternalY), (float)(end.Z - origin.Z));
        debugDraw.RequestDebugDrawLine(new DebugDrawLine {
            Handle = debugDraw.RequestHandle(),
            BlockPosition = origin,
            RelativeOrigin = localStart,
            RelativeEnd = localEnd,
            Color = color,
            Duration = duration
        });
        debugDraw.RequestDebugDrawSphere(new DebugDrawSphere {
            Handle = debugDraw.RequestHandle(),
            BlockPosition = origin,
            RelativeOrigin = localStart,
            Radius = 0.045f,
            Color = color,
            Duration = duration
        });
        debugDraw.RequestDebugDrawSphere(new DebugDrawSphere {
            Handle = debugDraw.RequestHandle(),
            BlockPosition = origin,
            RelativeOrigin = localEnd,
            Radius = 0.025f,
            Color = color,
            Duration = duration
        });
    }

    private void drawBox(TransformedCuboidd box, BlockPos origin, int color, TimeSpan duration)
    {
        var center = box.GetCenter(new Vec3d());
        var x = box.GetHalfAxisX(new Vec3d());
        var y = box.GetHalfAxisY(new Vec3d());
        var z = box.GetHalfAxisZ(new Vec3d());
        debugDraw.RequestDebugDrawOBB(new DebugDrawOBB {
            Handle = debugDraw.RequestHandle(),
            BlockPosition = origin,
            LocalMatrix = [
                (float)(2 * x.X), (float)(2 * x.Y), (float)(2 * x.Z), 0,
                (float)(2 * y.X), (float)(2 * y.Y), (float)(2 * y.Z), 0,
                (float)(2 * z.X), (float)(2 * z.Y), (float)(2 * z.Z), 0,
                (float)(center.X - x.X - y.X - z.X - origin.X),
                (float)(center.Y - x.Y - y.Y - z.Y - origin.InternalY),
                (float)(center.Z - x.Z - y.Z - z.Z - origin.Z), 1
            ],
            LocalMin = new Vec3f(),
            LocalMax = new Vec3f(1, 1, 1),
            Color = color,
            Duration = duration
        });
    }
}
