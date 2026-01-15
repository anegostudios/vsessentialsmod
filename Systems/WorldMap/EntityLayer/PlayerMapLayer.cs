using Cairo;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

#nullable enable

namespace Vintagestory.GameContent;

public class PlayerMapLayer : MarkerMapLayer
{
    // Both PlayerMapComponents and PlayerPositionMapComponents will be used.
    private readonly Dictionary<string, MapComponent> mapComps = new();
    private readonly ICoreClientAPI? capi;

    private LoadedTexture? ownTexture;
    private LoadedTexture? otherTexture;

    public override string Title => "Players";
    public override EnumMapAppSide DataSide => EnumMapAppSide.Client;
    public override string LayerGroupCode => "terrain";

    private readonly SystemRemotePlayerTracking playerTracking;

    public PlayerMapLayer(ICoreAPI api, IWorldMapManager mapsink) : base(api, mapsink)
    {
        capi = api as ICoreClientAPI;
        playerTracking = api.ModLoader.GetModSystem<SystemRemotePlayerTracking>();
    }

    // These two should now only be called when the player entity comes into/out of range.
    private void Event_PlayerDespawn(IClientPlayer byPlayer)
    {
        if (mapComps.TryGetValue(byPlayer.PlayerUID, out MapComponent? mp))
        {
            mp.Dispose();
            mapComps.Remove(byPlayer.PlayerUID);
        }
    }

    private void Event_PlayerSpawn(IClientPlayer byPlayer)
    {
        if (capi == null || otherTexture == null) return;

        if (capi.World.Config.GetBool("mapHideOtherPlayers", false) && byPlayer.PlayerUID != capi.World.Player.PlayerUID)
        {
            return;
        }

        if (mapComps.TryGetValue(byPlayer.PlayerUID, out MapComponent? comp))
        {
            comp.Dispose();
            mapComps.Remove(byPlayer.PlayerUID);
        }

        if (mapSink.IsOpened)
        {
            EntityMapComponent cmp = new(capi, otherTexture, byPlayer.Entity);
            mapComps[byPlayer.PlayerUID] = cmp;
        }
    }

    public override void OnLoaded()
    {
        if (capi != null)
        {
            // Only client side.

            // When an entity is spawned/despawned on the client and is an EntityPlayer this event is triggered.
            // Spawn is actually called TWICE, in ServerMain::DisconnectPlayer. A player packet is sent.
            // This packet should not be called.
            capi.Event.PlayerEntitySpawn += Event_PlayerSpawn;
            capi.Event.PlayerEntityDespawn += Event_PlayerDespawn;
        }
    }

    public override void OnMapOpenedClient()
    {
        if (capi == null) return;

        int size = (int)GuiElement.scaled(32);

        if (ownTexture == null)
        {
            ImageSurface surface = new(Format.Argb32, size, size);
            Context ctx = new(surface);
            ctx.SetSourceRGBA(0, 0, 0, 0);
            ctx.Paint();
            capi.Gui.Icons.DrawMapPlayer(ctx, 0, 0, size, size, new double[] { 0, 0, 0, 1 }, new double[] { 1, 1, 1, 1 });

            ownTexture = new LoadedTexture(capi, capi.Gui.LoadCairoTexture(surface, false), size / 2, size / 2);
            ctx.Dispose();
            surface.Dispose();
        }

        if (otherTexture == null)
        {
            ImageSurface surface = new(Format.Argb32, size, size);
            Context ctx = new(surface);
            ctx.SetSourceRGBA(0, 0, 0, 0);
            ctx.Paint();
            capi.Gui.Icons.DrawMapPlayer(ctx, 0, 0, size, size, new double[] { 0.3, 0.3, 0.3, 1 }, new double[] { 0.7, 0.7, 0.7, 1 });
            otherTexture = new LoadedTexture(capi, capi.Gui.LoadCairoTexture(surface, false), size / 2, size / 2);
            ctx.Dispose();
            surface.Dispose();
        }

        foreach (IPlayer player in capi.World.AllOnlinePlayers)
        {
            // Dispose all previous map components.
            if (mapComps.TryGetValue(player.PlayerUID, out MapComponent? cmp))
            {
                cmp?.Dispose();
                mapComps.Remove(player.PlayerUID);
            }

            if (capi.World.Config.GetBool("mapHideOtherPlayers", false) && player.PlayerUID != capi.World.Player.PlayerUID) continue;

            // This entity isn't being tracked, get it from the tracking system.
            if (player.Entity == null)
            {
                PacketPlayerPosition ppos = playerTracking.GetPlayerPositionInformation(player.PlayerUID);
                cmp = new PlayerPositionMapComponent(capi, player == capi.World.Player ? ownTexture : otherTexture, ppos);
                //capi.World.Logger.Warning("Can't add player {0} to world map, missing entity :<", player.PlayerUID);
            }
            else
            {
                cmp = new PlayerMapComponent(capi, player == capi.World.Player ? ownTexture : otherTexture, (IClientPlayer)player);
            }

            mapComps[player.PlayerUID] = cmp;
        }
    }

    public override void Render(GuiElementMap mapElem, float dt)
    {
        if (!Active) return;

        foreach (MapComponent mapComp in mapComps.Values)
        {
            mapComp.Render(mapElem, dt);
        }
    }

    public override void OnMouseMoveClient(MouseEvent args, GuiElementMap mapElem, StringBuilder hoverText)
    {
        if (!Active) return;

        foreach (MapComponent mapComp in mapComps.Values)
        {
            mapComp.OnMouseMove(args, mapElem, hoverText);
        }
    }

    public override void OnMouseUpClient(MouseEvent args, GuiElementMap mapElem)
    {
        if (!Active) return;

        foreach (MapComponent mapComp in mapComps.Values)
        {
            mapComp.OnMouseUpOnElement(args, mapElem);
        }
    }

    public override void Dispose()
    {
        foreach (MapComponent mapComp in mapComps.Values)
        {
            mapComp.Dispose();
        }

        ownTexture?.Dispose();
        ownTexture = null;

        otherTexture?.Dispose();
        otherTexture = null;
    }
}
