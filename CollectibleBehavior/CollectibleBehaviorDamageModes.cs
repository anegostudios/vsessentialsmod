using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class CollectibleBehaviorDamageModes : CollectibleBehavior, IHeldAttackDamageModes
    {
        const string modeAttribute = "damageMode";
        const string modeRevisionAttribute = "damageModeRevision";
        const string iconCacheKey = "damageModeIcons";

        static readonly Dictionary<EnumDamageType, string> iconCodes = new()
        {
            [EnumDamageType.BluntAttack] = "blunt",
            [EnumDamageType.PiercingAttack] = "pierce",
            [EnumDamageType.SlashingAttack] = "slash"
        };

        HeldAttackDamageMode[]? modes;
        Dictionary<EnumDamageType, SkillItem>? sharedIcons;
        SkillItem[]? damageModeToolModes;

        public CollectibleBehaviorDamageModes(CollectibleObject collObj) : base(collObj) { }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);

            var entries = properties["modes"].AsArray();
            if (entries == null || entries.Length < 2) return;

            var damageTypes = new HashSet<EnumDamageType>();
            var damageModes = new HeldAttackDamageMode[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (!Enum.TryParse(entry["damageType"].AsString(), true, out EnumDamageType damageType)
                    || !iconCodes.ContainsKey(damageType)
                    || !damageTypes.Add(damageType)) return;

                string? readyAnimation = entry["heldRightReadyAnimation"].AsString();
                if (string.IsNullOrWhiteSpace(readyAnimation)) readyAnimation = null;

                string? hitAnimation = entry["heldTpHitAnimation"].AsString();
                if (string.IsNullOrWhiteSpace(hitAnimation)) hitAnimation = null;

                var combo = readCombo(entry["heldTpHitCombo"]);
                var aim = entry["heldTpHitComboAim"];
                var midCombo = readCombo(aim["mid"]);
                if (hitAnimation == null && combo == null && midCombo == null) return;

                damageModes[i] = new HeldAttackDamageMode(damageType, readyAnimation, hitAnimation, combo, readCombo(aim["high"]), midCombo, readCombo(aim["low"]));
            }

            modes = damageModes;
        }

        static string[]? readCombo(JsonObject property)
        {
            var combo = property.AsArray()?.Select(entry => entry.AsString("")).ToArray();
            return combo?.Length > 0 && !combo.Any(string.IsNullOrWhiteSpace) ? combo : null;
        }

        public override void OnLoaded(ICoreAPI api)
        {
            if (modes != null && !collObj.HasBehavior<CollectibleBehaviorAnimationAuthoritative>(true)) modes = null;
            if (modes == null || api is not ICoreClientAPI capi) return;

            sharedIcons = ObjectCacheUtil.GetOrCreate(capi, iconCacheKey, () => new Dictionary<EnumDamageType, SkillItem>());
            var toolModes = new SkillItem[modes.Length];
            for (int i = 0; i < modes.Length; i++)
            {
                var damageType = modes[i].DamageType;
                if (!sharedIcons.TryGetValue(damageType, out var icon))
                {
                    string code = iconCodes[damageType];
                    icon = new SkillItem
                    {
                        Code = new AssetLocation(code),
                        Name = Lang.Get("damagetype-" + damageType.ToString().ToLowerInvariant()),
                        Texture = capi.Gui.LoadSvgWithPadding(new AssetLocation($"textures/icons/{code}.svg"), 48, 48, 5, ColorUtil.WhiteArgb)
                    };
                    sharedIcons.Add(damageType, icon);
                }

                toolModes[i] = icon;
            }
            damageModeToolModes = toolModes;
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            if (sharedIcons == null || sharedIcons.Count == 0) return;

            foreach (var icon in sharedIcons.Values) icon.Dispose();
            sharedIcons.Clear();
            ObjectCacheUtil.Delete(api, iconCacheKey);
        }

        int getModeIndex(ItemStack stack)
        {
            var attributes = stack.Attributes;
            var prediction = stack.TempAttributes;
            if (prediction.TryGetLong(modeRevisionAttribute) is long revision)
            {
                if (revision > attributes.GetLong(modeRevisionAttribute)) attributes = prediction;
                else prediction.RemoveAttribute(modeRevisionAttribute);
            }

            int index = attributes.GetInt(modeAttribute);
            return (uint)index < (uint)(modes?.Length ?? 0) ? index : 0;
        }

        public HeldAttackDamageMode? GetDamageMode(ItemStack stack) => modes?[getModeIndex(stack)];

        public override SkillItem[]? GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel) => damageModeToolModes;

        public override bool IsToolModeToggle(ItemSlot slot, IClientPlayer forPlayer, BlockSelection? blockSelection) => modes != null;

        public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection? blockSelection) => getModeIndex(slot.Itemstack);

        public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection? blockSelection, int toolMode)
        {
            if ((uint)toolMode >= (uint)(modes?.Length ?? 0)) return;

            var stack = slot.Itemstack;
            bool server = byPlayer is IServerPlayer;
            var attributes = server ? stack.Attributes : stack.TempAttributes;
            long revision = attributes.GetLong(modeRevisionAttribute);
            if (!server) revision = Math.Max(revision, stack.Attributes.GetLong(modeRevisionAttribute));
            attributes.SetLong(modeRevisionAttribute, revision + 1);

            if (toolMode == 0) attributes.RemoveAttribute(modeAttribute);
            else attributes.SetInt(modeAttribute, toolMode);

            if (server) slot.MarkDirty();
        }

        public override EnumDamageType GetDamageType(ItemStack itemStack, EnumDamageType damageType, ref EnumHandling handling)
        {
            var mode = GetDamageMode(itemStack);
            if (mode == null) return damageType;

            handling = EnumHandling.PreventDefault;
            return mode.DamageType;
        }

        public override string? GetHeldReadyAnimation(ItemSlot slot, Entity forEntity, EnumHand hand, ref EnumHandling handling)
        {
            if (hand != EnumHand.Right) return null;

            var readyAnimation = GetDamageMode(slot.Itemstack!)?.HeldRightReadyAnimation;
            if (readyAnimation == null) return null;

            handling = EnumHandling.PreventDefault;
            return readyAnimation;
        }
    }
}
