using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorDrunkTyping : EntityBehavior
    {
        ICoreAPI api;

        public EntityBehaviorDrunkTyping(Entity entity) : base(entity)
        {
        }

        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            api = entity.World.Api;
        }

        public override void OnEntityLoaded()
        {
            var capi = entity.Api as ICoreClientAPI;
            if (capi == null) return;

            // capi.World.Player is not initialized yet
            bool isself = (entity as EntityPlayer)?.PlayerUID == capi.Settings.String["playeruid"];

            if (isself)
            {
                capi.Event.RegisterEventBusListener(onChatKeyDownPre, 1, "chatkeydownpre");
                capi.Event.RegisterEventBusListener(onChatKeyDownPost, 1, "chatkeydownpost");
            }
        }

        public override string PropertyName()
        {
            return "drunktyping";
        }

        bool isCommand;

        private void onChatKeyDownPre(string eventName, ref EnumHandling handling, IAttribute data)
        {
            var treeAttr = data as TreeAttribute;
            string text = (treeAttr["text"] as StringAttribute).value;
            isCommand = text.Length > 0 && (text[0] == '.' || text[0] == '/');
        }

        private void onChatKeyDownPost(string eventName, ref EnumHandling handling, IAttribute data)
        {
            var treeAttr = data as TreeAttribute;
            int keyCode = (treeAttr["key"] as IntAttribute).value;
            string text = (treeAttr["text"] as StringAttribute).value;

            // User is trying to cheese the system
            if (isCommand && text.Length > 0 && text[0] != '.' && text[0] != '/')
            {
                string newtext = text[0] + "";
                for (int i = 1; i < text.Length; i++)
                {
                    newtext = slurText(newtext);
                    newtext += text[i];
                }

                text = newtext;
                (treeAttr["text"] as StringAttribute).value = text;
                treeAttr.SetBool("scrolltoEnd", true);
            }
            else
            {
                if (keyCode != (int)GlKeys.BackSpace && keyCode != (int)GlKeys.Left && keyCode != (int)GlKeys.Right && keyCode != (int)GlKeys.Delete && keyCode != (int)GlKeys.LAlt && keyCode != (int)GlKeys.ControlLeft && (text.Length > 0 && text[0] != '.' && text[0] != '/'))
                {
                    text = slurText(text);
                    (treeAttr["text"] as StringAttribute).value = text;
                    treeAttr.SetBool("scrolltoEnd", true);
                }
            }

            
        }

        private string slurText(string text)
        {
            var rnd = api.World.Rand;
            float intox = entity.WatchedAttributes.GetFloat("intoxication");
            if (rnd.NextDouble() < intox)
            {
                switch (rnd.Next(9))
                {
                    // Flip last 2 chars
                    case 0:
                    case 1:
                        if (text.Length > 1)
                        {
                            text = text.Substring(0, text.Length - 2) + text[text.Length - 1] + text[text.Length - 2];
                        }

                        break;
                    // Repeat last char
                    case 2:
                    case 3:
                    case 4:
                        if (text.Length > 0)
                        {
                            text = text + text[text.Length - 1];
                        }

                        break;
                    // Add random letter left/right from the last pressed key
                    case 5:
                        if (text.Length > 0)
                        {
                            string[] keybLayout = new string[] { "1234567890-", "qwertyuiop[", "asdfghjkl;", "zxcvbnm,." };
                            var lastchar = text[text.Length - 1];

                            for (int i = 0; i < 3; i++)
                            {
                                int index = keybLayout[i].IndexOf(lastchar);
                                if (index >= 0)
                                {
                                    int rndoffset = rnd.Next(2) * 2 - 1;
                                    text = text + keybLayout[i][GameMath.Clamp(index + rndoffset, 0, keybLayout[i].Length)];
                                }
                            }
                        }

                        break;
                }
            }

            return text;
        }
    }
}
