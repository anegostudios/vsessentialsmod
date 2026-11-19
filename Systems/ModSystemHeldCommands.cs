using Newtonsoft.Json.Linq;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.GameContent;

public class ModSystemHeldCommands : ModSystem
{
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        var cmdapi = api.ChatCommands;
        var parsers = api.ChatCommands.Parsers;
        var sapi = api;

        cmdapi.GetOrCreate("debug")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSub("helddurability")
                .WithAlias("helddura")
                .WithDesc("Set held item durability")
                .WithArgs(parsers.Int("durability"))
                .HandleWith(args =>
                {
                    var slot = (args.Caller.Entity as EntityAgent).ActiveHandItemSlot;
                    ItemStack stack = slot.Itemstack;
                    if (stack == null)
                    {
                        return TextCommandResult.Error("Nothing in active hands");
                    }
                    slot.Itemstack.Collectible.SetDurability(slot.Itemstack, (int)args[0]);
                    slot.MarkDirty();
                    return TextCommandResult.Success((int)args[0] + " durability set.");
                })
            .EndSub()

            .BeginSub("heldtemperature")
                .WithAlias("heldtemp")
                .WithDesc("Set held item temperature")
                .WithArgs(parsers.Int("temperature in °C"))
                .HandleWith(args =>
                {
                    var slot = (args.Caller.Entity as EntityAgent).ActiveHandItemSlot;
                    ItemStack stack = slot.Itemstack;
                    if (stack == null)
                    {
                        return TextCommandResult.Error("Nothing in active hands");
                    }
                    else
                    {
                        int temp = (int)args[0];
                        stack.Collectible.SetTemperature(sapi.World, stack, temp);
                        slot.MarkDirty();
                        return TextCommandResult.Success(temp + " °C set.");
                    }
                })
            .EndSub()

            .BeginSub("heldcoattr")
                .WithDesc("Get/Set collectible attributes of the currently held item")
                .WithArgs(parsers.Word("key"), parsers.OptionalAll("value"))
                .HandleWith(getSetCollectibleAttr)
            .EndSub()

            .BeginSub("heldstattr")
                .WithDesc("Get/Set itemstack attributes of the currently held item")
                .WithArgs(parsers.Word("key"), parsers.OptionalWordRange("type", "int", "bool", "string", "tree", "double", "float"), parsers.OptionalAll("value"))
                .HandleWith(getSetItemstackAttr)
            .EndSub()
        ;


        cmdapi
            .GetOrCreate("heldname")
            .RequiresPrivilege("customizeStackText")
            .WithDesc("Set a custom name on the currently held item")
            .WithArgs(parsers.All("name"))
            .HandleWith(args =>
            {
                var slot = (args.Caller.Entity as EntityAgent).ActiveHandItemSlot;
                ItemStack stack = slot.Itemstack;
                if (stack == null)
                {
                    return TextCommandResult.Error("Nothing in active hands");
                }
                else
                {
                    string name = (string)args[0];
                    if (name.Length == 0)
                    {
                        stack.Attributes.RemoveAttribute("customName");
                        slot.MarkDirty();
                        return TextCommandResult.Success("Name removed");
                    }

                    stack.Attributes.SetString("customName", name);
                    stack.Attributes.SetString("customSetter", args.Caller.GetName());
                    slot.MarkDirty();
                    return TextCommandResult.Success("Name set to " + name);

                }
            })
        ;

        cmdapi
            .GetOrCreate("helddesc")
            .RequiresPrivilege("customizeStackText")
            .WithDesc("Set a custom description on the currently held item")
            .WithArgs(parsers.All("description"))
            .HandleWith(args =>
            {
                var slot = (args.Caller.Entity as EntityAgent).ActiveHandItemSlot;
                ItemStack stack = slot.Itemstack;
                if (stack == null)
                {
                    return TextCommandResult.Error("Nothing in active hands");
                }
                else
                {
                    string desc = (string)args[0];
                    if (desc.Length == 0)
                    {
                        stack.Attributes.RemoveAttribute("customDesc");
                        slot.MarkDirty();
                        return TextCommandResult.Success("Description removed");
                    }

                    stack.Attributes.SetString("customDesc", desc);
                    stack.Attributes.SetString("customSetter", args.Caller.GetName());
                    slot.MarkDirty();
                    return TextCommandResult.Success("Description set to " + desc);
                }
            })
        ;

    }


    private TextCommandResult getSetCollectibleAttr(TextCommandCallingArgs args)
    {
        var slot = (args.Caller.Entity as EntityAgent).ActiveHandItemSlot;
        ItemStack stack = slot.Itemstack;
        if (stack == null)
        {
            return TextCommandResult.Success("Nothing in right hands");
        }
        else
        {
            string key = (string)args[0];
            string value = (string)args[1];
            var jtoken = stack.Collectible.Attributes.Token;

            if (key == null)
            {
                return TextCommandResult.Error("Syntax: /debug heldcoattr key value");
            }
            if (value == null)
            {
                return TextCommandResult.Success(Lang.Get("Collectible Attribute {0} has value {1}.", key, jtoken[key]));
            }

            jtoken[key] = JToken.Parse(value);

            slot.MarkDirty();
            return TextCommandResult.Success(Lang.Get("Collectible Attribute {0} set to {1}.", key, value));
        }
    }

    private TextCommandResult getSetItemstackAttr(TextCommandCallingArgs args)
    {
        var slot = (args.Caller.Entity as EntityAgent).ActiveHandItemSlot;
        ItemStack stack = slot.Itemstack;
        if (stack == null)
        {
            return TextCommandResult.Error("Nothing in active hands");
        }
        else
        {
            string key = (string)args[0];
            string type = (string)args[1];
            string value = (string)args[2];
            return getSetItemStackAttr(slot, key, type, value);
        }
    }

    private static TextCommandResult getSetItemStackAttr(ItemSlot slot, string key, string type, string value)
    {
        ItemStack stack = slot.Itemstack;

        if (type == null)
        {
            if (stack.Attributes.HasAttribute(key))
            {
                var attr = stack.Attributes[key];
                var attrtype = TreeAttribute.AttributeIdMapping[attr.GetAttributeId()];

                return TextCommandResult.Success(Lang.Get("Attribute {0} is of type {1} and has value {2}", attrtype, attr.ToString()));
            }
            else
            {
                return TextCommandResult.Error(Lang.Get("Attribute {0} does not exist"));
            }
        }

        switch (type)
        {
            case "int":
                stack.Attributes.SetInt(key, value.ToInt(0));
                break;
            case "bool":
                stack.Attributes.SetBool(key, value.ToBool(false));
                break;
            case "string":
                stack.Attributes.SetString(key, value);
                break;
            case "tree":
                stack.Attributes[key] = new JsonObject(JObject.Parse(value)).ToAttribute() as IAttribute;
                break;
            case "double":
                stack.Attributes.SetDouble(key, value.ToDouble(0));
                break;
            case "float":
                stack.Attributes.SetFloat(key, value.ToFloat(0));
                break;
            default:
                return TextCommandResult.Error("Invalid type");
        }

        slot.MarkDirty();
        return TextCommandResult.Success(string.Format("Stack Attribute {0}={1} set.", key, stack.Attributes[key].ToString()));
    }

}

