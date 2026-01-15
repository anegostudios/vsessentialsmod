using Cairo;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

#nullable enable

/// <summary>
/// This map layer is only added to the WorldMapManager if the server config value for it is enabled.
/// </summary>
public class EntityMapLayer : MarkerMapLayer
{
    private readonly Dictionary<long, MapComponent> mapComps = new();
    private readonly ICoreClientAPI? capi;

    private LoadedTexture? otherTexture;

    public override string Title => "Creatures";
    public override EnumMapAppSide DataSide => EnumMapAppSide.Client;
    public override string LayerGroupCode => "creatures";

    public EntityMapLayer(ICoreAPI api, IWorldMapManager mapsink) : base(api, mapsink)
    {
        capi = api as ICoreClientAPI;
    }

    public override void OnLoaded()
    {
        if (capi != null)
        {
            // Only client side
            capi.Event.OnEntitySpawn += Event_OnEntitySpawn;
            capi.Event.OnEntityLoaded += Event_OnEntitySpawn;
            capi.Event.OnEntityDespawn += Event_OnEntityDespawn;
        }
    }

    private void Event_OnEntityDespawn(Entity entity, EntityDespawnData reasonData)
    {
        if (mapComps.TryGetValue(entity.EntityId, out MapComponent? mp))
        {
            mp.Dispose();
            mapComps.Remove(entity.EntityId);
        }
    }

    private void Event_OnEntitySpawn(Entity entity)
    {
        if (capi == null || otherTexture == null) return;
        if (entity is EntityPlayer) return;
        if (entity.Code.Path.Contains("drifter")) return;

        if (mapSink.IsOpened && !mapComps.ContainsKey(entity.EntityId))
        {
            EntityMapComponent cmp = new(capi, otherTexture, entity, entity.Properties.Color);
            mapComps[entity.EntityId] = cmp;
        }
    }

    public override void OnMapOpenedClient()
    {
        if (capi == null) return;

        int size = (int)GuiElement.scaled(32);

        if (otherTexture == null)
        {
            ImageSurface surface = new(Format.Argb32, size, size);
            Context ctx = new(surface);
            ctx.SetSourceRGBA(0, 0, 0, 0);
            ctx.Paint();
            capi.Gui.Icons.DrawMapPlayer(ctx, 0, 0, size, size, new double[] { 0.3, 0.3, 0.3, 1 }, new double[] { 0.95, 0.95, 0.95, 1 });
            otherTexture = new LoadedTexture(capi, capi.Gui.LoadCairoTexture(surface, false), size / 2, size / 2);
            ctx.Dispose();
            surface.Dispose();
        }

        foreach (KeyValuePair<long, Entity> val in capi.World.LoadedEntities)
        {
            // Players are rendered in PlayerMapLayer
            if (val.Value is EntityPlayer) continue;

            if (mapComps.TryGetValue(val.Value.EntityId, out MapComponent? cmp))
            {
                cmp?.Dispose();
                mapComps.Remove(val.Value.EntityId);
            }

            cmp = new EntityMapComponent(capi, otherTexture, val.Value, val.Value.Properties.Color);
            mapComps[val.Value.EntityId] = cmp;
        }
    }

    public override void Render(GuiElementMap mapElem, float dt)
    {
        if (!Active) return;

        foreach (KeyValuePair<long, MapComponent> val in mapComps)
        {
            val.Value.Render(mapElem, dt);
        }
    }

    public override void OnMouseMoveClient(MouseEvent args, GuiElementMap mapElem, StringBuilder hoverText)
    {
        if (!Active) return;

        foreach (KeyValuePair<long, MapComponent> val in mapComps)
        {
            val.Value.OnMouseMove(args, mapElem, hoverText);
        }
    }

    public override void OnMouseUpClient(MouseEvent args, GuiElementMap mapElem)
    {
        if (!Active) return;

        foreach (KeyValuePair<long, MapComponent> val in mapComps)
        {
            val.Value.OnMouseUpOnElement(args, mapElem);
        }
    }

    public override void Dispose()
    {
        foreach (KeyValuePair<long, MapComponent> val in mapComps)
        {
            val.Value?.Dispose();
        }

        otherTexture?.Dispose();
        otherTexture = null;
    }
}
