using OpenTK.Mathematics;
using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

#nullable enable

public abstract class TrackableMapComponent : MapComponent
{
    private readonly MeshRef quadModel;
    private readonly LoadedTexture texture;
    private Vec2f viewPos = new();
    private readonly Vec3d worldPos = new();
    private readonly Matrixf mvMat = new();
    private readonly int color;

    protected TrackableMapComponent(ICoreClientAPI capi, LoadedTexture texture, string? color = null) : base(capi)
    {
        quadModel = capi.Render.UploadMesh(QuadMeshUtil.GetQuad());
        this.texture = texture;
        this.color = color == null ? 0 : (ColorUtil.Hex2Int(color) | (255 << 24));
    }

    public abstract Vector4d GetTrackablePositionAndYaw();
    public abstract void AppendHoverInfo(StringBuilder hoverText);
    public abstract bool ShouldRender();

    public override void OnMouseMove(MouseEvent args, GuiElementMap mapElem, StringBuilder hoverText)
    {
        Vec2f viewPos = new();

        Vector4d pos = GetTrackablePositionAndYaw();
        worldPos.Set(pos.X, pos.Y, pos.Z);

        mapElem.TranslateWorldPosToViewPos(worldPos, ref viewPos);

        double mouseX = args.X - mapElem.Bounds.renderX;
        double mouseY = args.Y - mapElem.Bounds.renderY;
        double sc = GuiElement.scaled(5);

        if (Math.Abs(viewPos.X - mouseX) < sc && Math.Abs(viewPos.Y - mouseY) < sc)
        {
            AppendHoverInfo(hoverText);
        }
    }

    public override void Render(GuiElementMap map, float dt)
    {
        if (!ShouldRender() || texture.Disposed || quadModel.Disposed) return;

        Vector4d pos = GetTrackablePositionAndYaw();
        worldPos.Set(pos.X, pos.Y, pos.Z);

        map.TranslateWorldPosToViewPos(worldPos, ref viewPos);

        float x = (float)(map.Bounds.renderX + viewPos.X);
        float y = (float)(map.Bounds.renderY + viewPos.Y);

        ICoreClientAPI api = map.Api;

        capi.Render.GlToggleBlend(true);

        IShaderProgram prog = api.Render.GetEngineShader(EnumShaderProgram.Gui);
        if (color == 0)
        {
            prog.Uniform("rgbaIn", ColorUtil.WhiteArgbVec);
        }
        else
        {
            Vec4f vec = new();
            ColorUtil.ToRGBAVec4f(color, ref vec);
            prog.Uniform("rgbaIn", vec);
        }

        prog.Uniform("applyColor", 0);
        prog.Uniform("extraGlow", 0);
        prog.Uniform("noTexture", 0f);
        prog.BindTexture2D("tex2d", texture.TextureId, 0);

        mvMat
            .Set(api.Render.CurrentModelviewMatrix)
            .Translate(x, y, 60f)
            .Scale(texture.Width, texture.Height, 0f)
            .Scale(0.5f, 0.5f, 0f)
            .RotateZ((float)-pos.W + (180f * GameMath.DEG2RAD))
        ;

        prog.UniformMatrix("projectionMatrix", api.Render.CurrentProjectionMatrix);
        prog.UniformMatrix("modelViewMatrix", mvMat.Values);

        api.Render.RenderMesh(quadModel);
    }

    public override void Dispose()
    {
        base.Dispose();
        quadModel.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class PlayerMapComponent : TrackableMapComponent
{
    private readonly IClientPlayer player;

    public PlayerMapComponent(ICoreClientAPI capi, LoadedTexture texture, IClientPlayer player, string? color = null) : base(capi, texture, color)
    {
        this.player = player;
    }

    public override void AppendHoverInfo(StringBuilder hoverText)
    {
        hoverText.AppendLine("Player " + player.PlayerName);
    }

    public override Vector4d GetTrackablePositionAndYaw()
    {
        return player.Entity == null
            ? Vector4d.Zero
            : new Vector4d(player.Entity.Pos.X, player.Entity.Pos.Y, player.Entity.Pos.Z, player.Entity.Pos.Yaw);
    }

    public override bool ShouldRender()
    {
        return (player.WorldData?.CurrentGameMode == EnumGameMode.Spectator != true || capi.World.Player == player) && (player.Entity.Controls.Sneak != true || player == capi.World.Player);
    }
}

public class PlayerPositionMapComponent : TrackableMapComponent
{
    private readonly PacketPlayerPosition position;

    public PlayerPositionMapComponent(ICoreClientAPI capi, LoadedTexture texture, PacketPlayerPosition position, string? color = null) : base(capi, texture, color)
    {
        this.position = position;
    }

    public override void AppendHoverInfo(StringBuilder hoverText)
    {
        hoverText.AppendLine("Player " + (position.Player?.PlayerName ?? "Unknown"));
    }

    public override Vector4d GetTrackablePositionAndYaw()
    {
        return new Vector4d(position.PosX, 0, position.PosZ, position.Yaw);
    }

    public override bool ShouldRender()
    {
        if (position.Player == null) return true;
        return position.Player.WorldData?.CurrentGameMode == EnumGameMode.Spectator != true || capi.World.Player == position.Player;
    }
}

public class EntityMapComponent : TrackableMapComponent
{
    private readonly Entity entity;

    public EntityMapComponent(ICoreClientAPI capi, LoadedTexture texture, Entity entity, string? color = null) : base(capi, texture, color)
    {
        this.entity = entity;
    }

    public override void AppendHoverInfo(StringBuilder hoverText)
    {
        hoverText.AppendLine(entity.GetName());
    }

    public override Vector4d GetTrackablePositionAndYaw()
    {
        return new Vector4d(entity.Pos.X, entity.Pos.Y, entity.Pos.Z, entity.Pos.Yaw);
    }

    public override bool ShouldRender()
    {
        return true;
    }
}
