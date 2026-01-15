using Cairo;
using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

#nullable enable

namespace Vintagestory.GameContent;

public class PlayerMapLayer : MarkerMapLayer
{
    private readonly ICoreClientAPI? capi;

    private LoadedTexture? ownPlayerTexture;
    private LoadedTexture? otherPlayerTexture;
    private MeshRef? quadModel;

    public override string Title => "Players";
    public override EnumMapAppSide DataSide => EnumMapAppSide.Client;
    public override string LayerGroupCode => "terrain";

    private readonly SystemRemotePlayerTracking playerTracking;

    public PlayerMapLayer(ICoreAPI api, IWorldMapManager mapsink) : base(api, mapsink)
    {
        capi = api as ICoreClientAPI;
        playerTracking = api.ModLoader.GetModSystem<SystemRemotePlayerTracking>();
    }

    public override void OnMapOpenedClient()
    {
        if (capi == null) return;

        int size = (int)GuiElement.scaled(32);

        if (ownPlayerTexture == null)
        {
            ImageSurface surface = new(Format.Argb32, size, size);
            Context ctx = new(surface);
            ctx.SetSourceRGBA(0, 0, 0, 0);
            ctx.Paint();
            capi.Gui.Icons.DrawMapPlayer(ctx, 0, 0, size, size, new double[] { 0, 0, 0, 1 }, new double[] { 1, 1, 1, 1 });
            ownPlayerTexture = new LoadedTexture(capi, capi.Gui.LoadCairoTexture(surface, false), size / 2, size / 2);
            ctx.Dispose();
            surface.Dispose();
        }

        if (otherPlayerTexture == null)
        {
            ImageSurface surface = new(Format.Argb32, size, size);
            Context ctx = new(surface);
            ctx.SetSourceRGBA(0, 0, 0, 0);
            ctx.Paint();
            capi.Gui.Icons.DrawMapPlayer(ctx, 0, 0, size, size, new double[] { 0.3, 0.3, 0.3, 1 }, new double[] { 0.7, 0.7, 0.7, 1 });
            otherPlayerTexture = new LoadedTexture(capi, capi.Gui.LoadCairoTexture(surface, false), size / 2, size / 2);
            ctx.Dispose();
            surface.Dispose();
        }

        quadModel ??= capi.Render.UploadMesh(QuadMeshUtil.GetQuad());
    }

    public override void Render(GuiElementMap map, float dt)
    {
        if (!Active || capi == null || ownPlayerTexture == null || otherPlayerTexture == null) return;

        bool hideOtherPlayers = capi.World.Config.GetBool("mapHideOtherPlayers", false);
        Vec2f viewPos = new();
        Vec3d worldPos = new();
        Matrixf mvMat = new();
        float yaw;

        IShaderProgram prog = capi.Render.GetEngineShader(EnumShaderProgram.Gui);
        prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
        prog.Uniform("applyColor", 0);
        prog.Uniform("extraGlow", 0);
        prog.Uniform("noTexture", 0f);

        // Color, always white for players.
        prog.Uniform("rgbaIn", ColorUtil.WhiteArgbVec);
        capi.Render.GlToggleBlend(true);

        foreach (IPlayer player in capi.World.AllOnlinePlayers)
        {
            if (hideOtherPlayers && player.PlayerUID != capi.World.Player.PlayerUID) continue;

            // This entity isn't being tracked, get it from the tracking system.
            if (player.Entity == null)
            {
                PacketPlayerPosition? pos = playerTracking.GetPlayerPositionInformation(player.PlayerUID);
                if (pos == null) continue;

                worldPos.Set(pos.PosX, 0, pos.PosZ);
                yaw = (float)pos.Yaw;
            }
            else
            {
                worldPos.Set(player.Entity.Pos.X, player.Entity.Pos.Y, player.Entity.Pos.Z);
                yaw = player.Entity.Pos.Yaw;
            }

            map.TranslateWorldPosToViewPos(worldPos, ref viewPos);

            float x = (float)(map.Bounds.renderX + viewPos.X);
            float y = (float)(map.Bounds.renderY + viewPos.Y);

            LoadedTexture tex = player == capi.World.Player ? ownPlayerTexture : otherPlayerTexture;

            prog.BindTexture2D("tex2d", tex.TextureId, 0);

            mvMat
            .Set(capi.Render.CurrentModelviewMatrix)
            .Translate(x, y, 60f)
            .Scale(tex.Width, tex.Height, 0f)
            .Scale(0.5f, 0.5f, 0f)
            .RotateZ(-yaw + (180f * GameMath.DEG2RAD))
                ;

            prog.UniformMatrix("modelViewMatrix", mvMat.Values);

            capi.Render.RenderMesh(quadModel);
        }
    }

    public override void OnMouseMoveClient(MouseEvent args, GuiElementMap mapElem, StringBuilder hoverText)
    {
        if (!Active || capi == null) return;

        Vec2f viewPos = new();
        Vec3d worldPos = new();

        foreach (IPlayer player in capi.World.AllOnlinePlayers)
        {
            // This entity isn't being tracked, get it from the tracking system.
            if (player.Entity == null)
            {
                PacketPlayerPosition? pos = playerTracking.GetPlayerPositionInformation(player.PlayerUID);
                if (pos == null) continue;

                worldPos.Set(pos.PosX, 0, pos.PosZ);
            }
            else
            {
                worldPos.Set(player.Entity.Pos.X, player.Entity.Pos.Y, player.Entity.Pos.Z);
            }

            mapElem.TranslateWorldPosToViewPos(worldPos, ref viewPos);

            double mouseX = args.X - mapElem.Bounds.renderX;
            double mouseY = args.Y - mapElem.Bounds.renderY;
            double sc = GuiElement.scaled(5);

            if (Math.Abs(viewPos.X - mouseX) < sc && Math.Abs(viewPos.Y - mouseY) < sc)
            {
                hoverText.Append("Player ");
                hoverText.AppendLine(player.PlayerName);
            }
        }
    }

    public override void Dispose()
    {
        ownPlayerTexture?.Dispose();
        ownPlayerTexture = null;

        otherPlayerTexture?.Dispose();
        otherPlayerTexture = null;

        quadModel?.Dispose();
        quadModel = null;
    }
}
