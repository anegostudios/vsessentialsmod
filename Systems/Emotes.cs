using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.GameContent
{
    public class ModSystemEmotes : ModSystem
    {
        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            var cmdapi = api.ChatCommands;
            var parsers = api.ChatCommands.Parsers;

            cmdapi
                .Create("emote")
                .RequiresPrivilege(Privilege.chat)
                .WithDescription("Execute an emote on your player")
                .WithArgs(parsers.OptionalWord("emote"))
                .HandleWith(args =>
                {
                    var entity = args.Caller.Entity as EntityAgent;
                    var emotes = entity.Properties.Attributes["emotes"].AsArray<string>();
                    string emote = (string)args[0];

                    if (emote == null || !emotes.Contains(emote))
                    {
                        return TextCommandResult.Error(Lang.Get("Choose emote: {0}", string.Join(", ", emotes)));
                    }

                    if ((emote != "shakehead") && !entity.RightHandItemSlot.Empty)
                    {
                        string anim = entity.RightHandItemSlot.Itemstack.Collectible.GetHeldTpIdleAnimation(entity.RightHandItemSlot, entity, EnumHand.Right);

                        if (anim != null)
                        {
                            return TextCommandResult.Error("Only with free hands");
                        }
                    }

                    api.Network.BroadcastEntityPacket(entity.EntityId, 197, SerializerUtil.Serialize(emote));

                    return TextCommandResult.Success();
                })
                ;
        }
    }


}
