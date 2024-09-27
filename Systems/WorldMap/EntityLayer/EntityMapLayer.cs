using Cairo;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent
{

    public class EntityMapLayer : MarkerMapLayer
    {
        Dictionary<long, EntityMapComponent> MapComps = new Dictionary<long, EntityMapComponent>();
        ICoreClientAPI capi;

        LoadedTexture otherTexture;

        public override string Title => "Creatures";
        public override EnumMapAppSide DataSide => EnumMapAppSide.Client;

        public override string LayerGroupCode => "creatures";

        public EntityMapLayer(ICoreAPI api, IWorldMapManager mapsink) : base(api, mapsink)
        {
            capi = (api as ICoreClientAPI);
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
            EntityMapComponent mp;
            if (MapComps.TryGetValue(entity.EntityId, out mp))
            {
                mp.Dispose();
                MapComps.Remove(entity.EntityId);
            }
        }

        private void Event_OnEntitySpawn(Entity entity)
        {
            if (entity is EntityPlayer) return;
            if (entity.Code.Path.Contains("drifter")) return;

            if (mapSink.IsOpened && !MapComps.ContainsKey(entity.EntityId))
            {
                EntityMapComponent cmp = new EntityMapComponent(capi, otherTexture, entity, entity.Properties.Color);
                MapComps[entity.EntityId] = cmp;
            }
        }

        

        public override void OnMapOpenedClient()
        {
            int size = (int)GuiElement.scaled(32);
                        
            if (otherTexture == null)
            {
                ImageSurface surface = new ImageSurface(Format.Argb32, size, size);
                Context ctx = new Context(surface);
                ctx.SetSourceRGBA(0, 0, 0, 0);
                ctx.Paint();
                capi.Gui.Icons.DrawMapPlayer(ctx, 0, 0, size, size, new double[] { 0.3, 0.3, 0.3, 1 }, new double[] { 0.95, 0.95, 0.95, 1 });
                otherTexture = new LoadedTexture(capi, capi.Gui.LoadCairoTexture(surface, false), size / 2, size / 2);
                ctx.Dispose();
                surface.Dispose();
            }



            foreach (var val in capi.World.LoadedEntities)
            {
                EntityMapComponent cmp;

                if (val.Value is EntityPlayer) continue;

                if (MapComps.TryGetValue(val.Value.EntityId, out cmp))
                {
                    cmp?.Dispose();
                    MapComps.Remove(val.Value.EntityId);
                }
                
                cmp = new EntityMapComponent(capi, otherTexture, val.Value, val.Value.Properties.Color);
                MapComps[val.Value.EntityId] = cmp;
            }
        }


        public override void Render(GuiElementMap mapElem, float dt)
        {
            if (!Active) return;

            foreach (var val in MapComps)
            {
                val.Value.Render(mapElem, dt);
            }
        }

        public override void OnMouseMoveClient(MouseEvent args, GuiElementMap mapElem, StringBuilder hoverText)
        {
            if (!Active) return;

            foreach (var val in MapComps)
            {
                val.Value.OnMouseMove(args, mapElem, hoverText);
            }
        }

        public override void OnMouseUpClient(MouseEvent args, GuiElementMap mapElem)
        {
            if (!Active) return;

            foreach (var val in MapComps)
            {
                val.Value.OnMouseUpOnElement(args, mapElem);
            }
        }

        public override void Dispose()
        {
            foreach (var val in MapComps)
            {
                val.Value?.Dispose();
            }

            otherTexture?.Dispose();
            otherTexture = null;
        }
    }
}
