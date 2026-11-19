using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using static Vintagestory.API.Common.EntityAgent;

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
                .WithArgs(parsers.OptionalWord("emote"), parsers.OptionalWord("stop"))
                .HandleWith(args =>
                {
                    var entity = args.Caller.Entity as EntityAgent;
                    var emotes = entity.Properties.Attributes["emotes"].AsArray<string>();
                    string emote = (string)args[0];
                    string stop = args.ArgCount > 1 ? (string)args[1] : null;

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

                    if (stop == "stop")
                    {
                        api.Network.BroadcastEntityPacket(entity.EntityId, (int)EntityServerPacketId.EmoteStop, SerializerUtil.Serialize(emote));
                    } else
                    {
                        api.Network.BroadcastEntityPacket(entity.EntityId, (int)EntityServerPacketId.Emote, SerializerUtil.Serialize(emote));
                    }

                    

                    return TextCommandResult.Success();
                });


            cmdapi
                .GetOrCreate("dev")
                .RequiresPrivilege(Privilege.controlserver)
                .BeginSubCommand("playanim")
                    .WithDescription("Play an animation on your player")
                    .WithArgs(parsers.Word("anim"))
                    .HandleWith(args =>
                    {
                        var entity = args.Caller.Entity as EntityAgent;
                        string animationcode = (string)args[0];
                        string stop = args.ArgCount > 1 ? (string)args[1] : null;

                        api.Network.BroadcastEntityPacket(entity.EntityId, (int)EntityServerPacketId.Emote, SerializerUtil.Serialize(animationcode));
                        return TextCommandResult.Success();
                    })
                .EndSubCommand()
            ;

            cmdapi
                .GetOrCreate("dev")
                .RequiresPrivilege(Privilege.controlserver)
                .BeginSubCommand("stopanim")
                    .WithDescription("Play an animation on your player")
                    .WithArgs(parsers.Word("anim"))
                    .HandleWith(args =>
                    {
                        var entity = args.Caller.Entity as EntityAgent;
                        string animationcode = (string)args[0];
                        api.Network.BroadcastEntityPacket(entity.EntityId, (int)EntityServerPacketId.EmoteStop, SerializerUtil.Serialize(animationcode));
                        return TextCommandResult.Success();
                    })
                .EndSubCommand()
            ;
        }

    }


}
