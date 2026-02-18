using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Common.CommandAbbr;
using System;

namespace Vintagestory.GameContent
{
    public class CollectibleBuffDebug : ModSystem
    {
        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;
        public override double ExecuteOrder() => 1;

        public override void StartServerSide(ICoreServerAPI api)
        {
            var parsers = api.ChatCommands.Parsers;
            api.ChatCommands
                .GetOrCreate("debug")
                .BeginSub("cbuff")
                    .BeginSub("add")
                        .WithArgs(parsers.Word("code"), parsers.Word("statcode"), parsers.Float("multiplier"), parsers.OptionalFloat("flatchange"))
                        .HandleWith(cmdAddBuff)
                    .EndSub()
                    .BeginSub("clear")
                        .HandleWith(cmdClearBuffs)
                    .EndSub()
                .EndSub()
            ;
        }

        private TextCommandResult cmdAddBuff(TextCommandCallingArgs args)
        {
            var heldItemSlot = args.Caller?.Player?.InventoryManager.ActiveHotbarSlot;
            if (heldItemSlot == null || heldItemSlot.Empty) return TextCommandResult.Error("Hold something in your hands");

            var bh = heldItemSlot.Itemstack.Collectible.GetBehavior<CollectibleBehaviorBuffable>();
            if (bh == null) return TextCommandResult.Error("Held item is not buffable");

            bh.AddBuff(heldItemSlot.Itemstack, new AppliedCollectibleBuff()
            {
                Code = (string)args[0],
                StatCode = (string)args[1],
                Multiplier = (float)args[2],
                FlatChange = (float)args[3]
            });

            heldItemSlot.MarkDirty();

            return TextCommandResult.Success("Buff added");
        }

        private TextCommandResult cmdClearBuffs(TextCommandCallingArgs args)
        {
            var heldItemSlot = args.Caller?.Player?.InventoryManager.ActiveHotbarSlot;
            if (heldItemSlot == null || heldItemSlot.Empty) return TextCommandResult.Error("Hold something in your hands");

            var bh = heldItemSlot.Itemstack.Collectible.GetBehavior<CollectibleBehaviorBuffable>();
            if (bh == null) return TextCommandResult.Error("Held item is not buffable");

            bh.StoreItemBuffs(heldItemSlot.Itemstack, new List<AppliedCollectibleBuff>());
            heldItemSlot.MarkDirty();

            return TextCommandResult.Success("Buffs cleared");
        }
    }


    public class AppliedCollectibleBuff
    {
        public required string Code;
        public required string StatCode;
        /// <summary>
        /// -1 for permanent buff
        /// </summary>
        public int RemainingDurability;
        public float Multiplier = 1f;
        public float FlatChange = 0f;

        public override string ToString()
        {
            if (FlatChange != 0)
            {
                return Lang.Get("flatchangebuff-" + Code + "-" + StatCode, FlatChange, RemainingDurability);
            } else
            {
                return Lang.Get("mulbuff-" + Code + "-" + StatCode, Multiplier - 1, RemainingDurability);
            }            
        }

        public TreeAttribute ToAttribute()
        {
            TreeAttribute bufftree = new TreeAttribute();
            bufftree.SetString("code", Code);
            bufftree.SetString("statcode", StatCode);
            bufftree.SetInt("durability", RemainingDurability);
            bufftree.SetFloat("multiplier", Multiplier);
            bufftree.SetFloat("flatchange", FlatChange);
            return bufftree;
        }

        public static AppliedCollectibleBuff FromAttribute(TreeAttribute bufftree)
        {
            return new AppliedCollectibleBuff()
            {
                Code = bufftree.GetString("code"),
                StatCode = bufftree.GetString("statcode"),
                RemainingDurability = bufftree.GetInt("durability"),
                Multiplier = bufftree.GetFloat("multiplier"),
                FlatChange = bufftree.GetFloat("flatchange")
            };
        }
    }

    public class CollectibleBehaviorBuffable : CollectibleBehavior
    {
        public CollectibleBehaviorBuffable(CollectibleObject collObj) : base(collObj)
        {
        }


        public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot, IRecipeBase byRecipe, ref EnumHandling bhHandling)
        {
            foreach (var slot in allInputslots)
            {
                if (slot.Empty) continue;
                var stack = slot.Itemstack;

                var collobj = slot.Itemstack.Collectible;
                applyQuenchableBuffs(outputSlot.Itemstack, slot.Itemstack);
            }
        }

        public void applyQuenchableBuffs(ItemStack stack, ItemStack takeBuffsFromStack)
        {
            if (!takeBuffsFromStack.Attributes.HasAttribute("powervalue") && !takeBuffsFromStack.Attributes.HasAttribute("durationbonus")) return;

            var powervalue = takeBuffsFromStack.Attributes.GetFloat("powervalue");
            var durationbonus = takeBuffsFromStack.Attributes.GetFloat("durationbonus");

            if (powervalue > 0)
            {
                AddBuff(stack, new AppliedCollectibleBuff()
                {
                    Code = "hardened",
                    Multiplier = 1 + powervalue,
                    StatCode = "attackpower"
                });
                AddBuff(stack, new AppliedCollectibleBuff()
                {
                    Code = "hardened",
                    Multiplier = 1 + powervalue,
                    StatCode = "miningspeed"
                });
            }

            if (durationbonus > 0)
            {
                AddBuff(stack, new AppliedCollectibleBuff()
                {
                    Code = "hardened",
                    Multiplier = 1 + durationbonus,
                    StatCode = "maxdurability"
                });
            }
        }

        public List<AppliedCollectibleBuff> GetItemBuffs(ItemStack stack)
        {
            List<AppliedCollectibleBuff> appliedbuffs = new List<AppliedCollectibleBuff>();
            var buffs = stack.Attributes.GetTreeAttribute("buffs");
            if (buffs == null) return appliedbuffs;

            foreach (var keyval in buffs)
            {
                var bufftree = keyval.Value as TreeAttribute;
                if (bufftree == null) continue;
                appliedbuffs.Add(AppliedCollectibleBuff.FromAttribute(bufftree));
            }

            return appliedbuffs;
        }


        public void StoreItemBuffs(ItemStack stack, List<AppliedCollectibleBuff> buffs)
        {
            TreeAttribute buffstree = new TreeAttribute();
            for (int i = 0; i < buffs.Count; i++)
            {
                buffstree[i.ToString()] = buffs[i].ToAttribute();
            }

            stack.Attributes["buffs"] = buffstree;
        }

        /// <summary>
        /// Adds a new buff to the item
        /// </summary>
        /// <param name="stack"></param>
        /// <param name="buff"></param>
        /// <param name="addOnDuplicate">If true, adds the buff values to any existing ones, if the buff code matches</param>
        public void AddBuff(ItemStack stack, AppliedCollectibleBuff buff, bool addOnDuplicate = false)
        {
            var buffstree = stack.Attributes.GetTreeAttribute("buffs");
            if (buffstree == null)
            {
                buffstree = new TreeAttribute();
                stack.Attributes["buffs"] = buffstree;
            }
            int index = 0;
            while (buffstree.HasAttribute(index.ToString()))
            {
                if (addOnDuplicate)
                {
                    var bufftree = buffstree[index.ToString()] as TreeAttribute;
                    if (bufftree?.GetString("code") == buff.Code)
                    {
                        bufftree.SetInt("durability", bufftree.GetInt("durability") + buff.RemainingDurability);
                        bufftree.SetFloat("multiplier", bufftree.GetFloat("multiplier") + (buff.Multiplier-1));
                        bufftree.SetFloat("flatchange", bufftree.GetFloat("flatchange") + buff.FlatChange);
                        return;
                    }
                }

                index++;
            }
            buffstree[index.ToString()] = buff.ToAttribute();
        }


        public void RemoveBuff(ItemStack stack, int index)
        {
            stack.Attributes.GetTreeAttribute("buffs")?.RemoveAttribute(index.ToString());
        }


        public override void OnDamageItem(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, ref int amount, ref EnumHandling bhHandling)
        {
            var buffs = GetItemBuffs(itemslot.Itemstack);
            bool modified = false;
            for (int i = 0; i < buffs.Count; i++)
            {
                var buff = buffs[i];
                if (buff.RemainingDurability > 0)
                {
                    buff.RemainingDurability--;
                    modified = true;
                    if (buff.RemainingDurability == 0)
                    {
                        buffs.RemoveAt(i);
                        i--;
                    }
                }
            }

            if (modified)
            {
                StoreItemBuffs(itemslot.Itemstack, buffs);
                itemslot.MarkDirty();
            }
        }

        public override int GetMaxDurability(ItemStack itemstack, int durability, ref EnumHandling bhHandling)
        {
            return (int)applyBuffs(itemstack, durability, "maxdurability", ref bhHandling);
        }

        public override int GetRemainingDurability(ItemStack itemstack, int durability, ref EnumHandling bhHandling)
        {
            return (int)applyBuffs(itemstack, durability, "durability", ref bhHandling);
        }

        public override float GetAttackPower(ItemStack itemstack, float attackPower, ref EnumHandling bhHandling)
        {
            return applyBuffs(itemstack, attackPower, "attackpower", ref bhHandling);
        }


        public override float GetDamageToEntity(float baseDamage, Entity entity, ItemStack itemStack, ref bool isCriticalHit, ref EnumHandling handling)
        {
            float critChance = applyBuffs(itemStack, 1, "critchance", ref handling) - 1;
            if (entity.World.Rand.NextDouble() < critChance)
            {
                baseDamage *= 2;
                isCriticalHit = true;
            }

            return baseDamage;
        }

        public override float GetMiningSpeed(ItemStack itemstack, BlockSelection blockSel, Block block, IPlayer forPlayer, ref EnumHandling bhHandling)
        {
            return applyBuffs(itemstack, 1f, "miningspeed", ref bhHandling);
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            var buffs = GetItemBuffs(inSlot.Itemstack);
            if (buffs.Count > 0) dsc.AppendLine();

            foreach (var buff in buffs)
            {
                bool positiveBuff = buff.Multiplier > 1 || buff.FlatChange > 0;

                dsc.Append(string.Format("<font color=\"{0}\">", positiveBuff ? "#00bb00" : "#bb0000"));
                dsc.Append(buff.ToString());
                dsc.AppendLine("</font>");
            }
        }


        private float applyBuffs(ItemStack itemstack, float currentvalue, string statcode, ref EnumHandling bhHandling)
        {
            var buffs = GetItemBuffs(itemstack);
            foreach (var buff in buffs)
            {
                if (buff.StatCode == statcode)
                {
                    currentvalue *= buff.Multiplier;
                    currentvalue += buff.FlatChange;
                    bhHandling = EnumHandling.Handled; // PreventDefault would cause this buff not to get applied on top of existing buffs (e.g. mining speeds of a pickaxe)
                }
            }

            return currentvalue;
        }

    }
}
