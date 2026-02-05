using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
#nullable disable

namespace Vintagestory.Systems
{
    public class ModSystemTfEditFromAttributes : ModSystem
    {
        public override bool ShouldLoad(EnumAppSide forSide) => true;

        ICoreClientAPI capi;
        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            api.Event.RegisterEventBusListener(OnEventBusEvent);
        }

        // attributes: {
        //   transformsFromAttributes: {
        //      typeCode: "salmon",
        //      types: {
        //        salmon: { gui: { ... }, handTp: { ... }, ground: { ... } },
        //        bass: { .... }
        //      }
        //   }
        // }
        private void OnEventBusEvent(string eventName, ref EnumHandling handling, IAttribute data)
        {
            var tree = data as TreeAttribute;
            if (eventName == "onsettransform" || eventName == "ongettransform")
            {
                var itemstack = capi.World.Player.InventoryManager.ActiveHotbarSlot.Itemstack;
                if (itemstack == null) return;

                var metadata = itemstack.ItemAttributes?["transformsFromAttributes"];
                if (metadata == null || !metadata.Exists) return;

                string code = itemstack.Attributes.GetString(metadata["typeCode"].AsString());
                if (code == null) return;

                string target = tree.GetString("target");

                var types = metadata["types"];

                if (eventName == "ongettransform")
                {
                    ModelTransform tf = types[code][target].AsObject<ModelTransform>();
                    if (tf != null)
                    {
                        tf.ToTreeAttribute(tree);
                        tree.SetBool("preventDefault", true);
                    }
                }
                else
                {
                    if (!types[code].Exists) types.Token[code] = new JObject();
                    if (!types[code][target].Exists)
                    {
                        types[code].Token[target] = new JObject();
                    }

                    types[code].Token[target] = JToken.FromObject(ModelTransform.CreateFromTreeAttribute(tree));
                    tree.SetBool("preventDefault", true);
                }
            }
        }
    }
}
