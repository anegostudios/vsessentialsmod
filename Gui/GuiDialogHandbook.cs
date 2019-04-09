using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using VintagestoryAPI.Util;

namespace Vintagestory.GameContent
{
    // Concept:
    // 1. Pressing H opens the GuiDialogKnowledgeBase
    // 2. Top of the dialog has a search field to search for blocks and items
    //    While hovering an itemstack in an itemslot it will pre-search the info of that item
    // The block/item detail page contains
    // - An icon of the block item
    // - Name and description
    // - Where it can be found (Dropped by: Block x, Monster y)
    // - In which recipes in can be used (Grip recipe X, Smithing recipe z)

    // By default every item and block in the creative inventory can be found through search
    // but can be explicitly made to be included or excluded using item/block attributes
    public class GuiDialogHandbook : GuiDialog
    {
        List<StacklistElement> stackListElements = new List<StacklistElement>();
        ItemStack[] stacks;

        Stack<ItemStack> browseHistory = new Stack<ItemStack>();

        IInventory creativeInv = null;
        string currentSearchText;


        GuiComposer overviewGui;
        GuiComposer detailViewGui;

        double listHeight = 500;

        public override string ToggleKeyCombinationCode => "knowledgebase";

        public GuiDialogHandbook(ICoreClientAPI capi) : base(capi)
        {
            IPlayerInventoryManager invm = capi.World.Player.InventoryManager;
            creativeInv = invm.GetOwnInventory(GlobalConstants.creativeInvClassName);

            InitCacheAndStacks();
            initOverviewGui();
        }


        void initOverviewGui()
        {
            ElementBounds searchFieldBounds = ElementBounds.Fixed(GuiStyle.ElementToDialogPadding - 2, 45, 300, 30);
            ElementBounds stackListBounds = ElementBounds.Fixed(0, 0, 400, listHeight).FixedUnder(searchFieldBounds, 5);

            ElementBounds clipBounds = stackListBounds.ForkBoundingParent();
            ElementBounds insetBounds = stackListBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);

            ElementBounds scrollbarBounds = insetBounds.CopyOffsetedSibling(3 + stackListBounds.fixedWidth + 7).WithFixedWidth(20);

            ElementBounds closeButtonBounds = ElementBounds
                .FixedSize(0, 0)
                .FixedUnder(clipBounds, 2 * 5 + 8)
                .WithAlignment(EnumDialogArea.RightFixed)
                .WithFixedPadding(20, 4)
                .WithFixedAlignmentOffset(2, 0)
            ;

            // 2. Around all that is 10 pixel padding
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(insetBounds, stackListBounds, scrollbarBounds, closeButtonBounds);

            // 3. Finally Dialog
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);


            overviewGui = capi.Gui
                .CreateCompo("handbook-overview", dialogBounds)
                .AddShadedDialogBG(bgBounds, true)
                .AddDialogTitleBar(Lang.Get("Survival Handbook"), OnTitleBarClose)
                .AddTextInput(searchFieldBounds, FilterItemsBySearchText, CairoFont.WhiteSmallishText(), "searchField")
                .BeginChildElements(bgBounds)
                    .BeginClip(clipBounds)
                        .AddInset(insetBounds, 3)
                        .AddStacklist(stackListBounds, onLeftClickStack, stackListElements, "stacklist")
                    .EndClip()
                    .AddVerticalScrollbar(OnNewScrollbarvalueOverviewPage, scrollbarBounds, "scrollbar")
                    .AddSmallButton(Lang.Get("Close Handbook"), OnButtonClose, closeButtonBounds)
                .EndChildElements()
                .Compose()
            ;

            overviewGui.GetScrollbar("scrollbar").SetHeights(
                (float)listHeight, (float)overviewGui.GetStacklist("stacklist").insideBounds.fixedHeight
            );

        }

        void initDetailGui() { 
            ElementBounds textBounds = ElementBounds.Fixed(9, 45, 400, 30 + listHeight + 17);
            
            ElementBounds clipBounds = textBounds.ForkBoundingParent();
            ElementBounds insetBounds = textBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);

            ElementBounds scrollbarBounds = clipBounds.CopyOffsetedSibling(textBounds.fixedWidth + 7, -6, 0, 6).WithFixedWidth(20);

            ElementBounds closeButtonBounds = ElementBounds
                .FixedSize(0, 0)
                .FixedUnder(clipBounds, 2 * 5 + 5)
                .WithAlignment(EnumDialogArea.RightFixed)
                .WithFixedPadding(20, 4)
                .WithFixedAlignmentOffset(-12, 1)
            ;
            ElementBounds backButtonBounds = ElementBounds
                .FixedSize(0, 0)
                .FixedUnder(clipBounds, 2 * 5 + 5)
                .WithAlignment(EnumDialogArea.LeftFixed)
                .WithFixedPadding(20, 4)
                .WithFixedAlignmentOffset(4, 1)
            ;
            ElementBounds overviewButtonBounds = ElementBounds
                .FixedSize(0, 0)
                .FixedUnder(clipBounds, 2 * 5 + 5)
                .WithAlignment(EnumDialogArea.CenterFixed)
                .WithFixedPadding(20, 4)
                .WithFixedAlignmentOffset(0, 1)
            ;

            ElementBounds bgBounds = insetBounds.ForkBoundingParent(5, 40, 36, 52).WithFixedPadding(GuiStyle.ElementToDialogPadding / 2);
            bgBounds.WithChildren(insetBounds, textBounds, scrollbarBounds, backButtonBounds, closeButtonBounds);

            // 3. Finally Dialog
            ElementBounds dialogBounds = bgBounds.ForkBoundingParent().WithAlignment(EnumDialogArea.CenterMiddle);
            dialogBounds.WithFixedAlignmentOffset(3, 3);
            RichTextComponentBase[] cmps = browseHistory.Peek().Collectible.GetHandbookInfo(browseHistory.Peek(), capi, stacks, OpenDetailPageFor);


            detailViewGui = capi.Gui
                .CreateCompo("handbook-detail", dialogBounds)
                .AddShadedDialogBG(bgBounds, true)
                .AddDialogTitleBar("Survival Handbook", OnTitleBarClose)
                .BeginChildElements(bgBounds)
                    .BeginClip(clipBounds)
                        .AddInset(insetBounds, 3)
                        .AddRichtext(cmps, textBounds, "richtext")
                    .EndClip()
                    .AddVerticalScrollbar(OnNewScrollbarvalueDetailPage, scrollbarBounds, "scrollbar")
                    .AddSmallButton("Back", OnButtonBack, backButtonBounds)
                    .AddSmallButton("Overview", OnButtonOverview, overviewButtonBounds)
                    .AddSmallButton("Close", OnButtonClose, closeButtonBounds)
                .EndChildElements()
                .Compose()
            ;

            GuiElementRichtext richtextelem = detailViewGui.GetRichtext("richtext");
            detailViewGui.GetScrollbar("scrollbar").SetHeights(
                (float)listHeight, (float)richtextelem.Bounds.fixedHeight
            );
        }

        private bool OnButtonOverview()
        {
            browseHistory.Clear();
            return true;
        }

        public void OpenDetailPageFor(ItemStack stack)
        {
            capi.Gui.PlaySound("menubutton_press");

            if (browseHistory.Count > 0 && stack.Equals(capi.World, browseHistory.Peek(), GlobalConstants.IgnoredStackAttributes)) return;

            browseHistory.Push(stack);
            initDetailGui();
        }

        private void OnLinkClicked()
        {
            
        }

        private bool OnButtonBack()
        {
            browseHistory.Pop();
            if (browseHistory.Count > 0) initDetailGui();
            return true;
        }

        private void OnRenderStack(Context ctx, ImageSurface surface, ElementBounds currentBounds)
        {
            
        }

        private void onLeftClickStack(int index)
        {
            browseHistory.Push(stackListElements[index].Stack);
            initDetailGui();
        }



        private void OnNewScrollbarvalueOverviewPage(float value)
        {
            GuiElementStacklist stacklist = overviewGui.GetStacklist("stacklist");

            stacklist.insideBounds.fixedY = 3 - value;
            stacklist.insideBounds.CalcWorldBounds();
        }

        private void OnNewScrollbarvalueDetailPage(float value)
        {
            GuiElementRichtext richtextElem = detailViewGui.GetRichtext("richtext");

            richtextElem.Bounds.fixedY = 3 - value;
            richtextElem.Bounds.CalcWorldBounds();
        }

        private void OnTitleBarClose()
        {
            TryClose();
        }

        private bool OnButtonClose()
        {
            TryClose();
            return true;
        }



        public void InitCacheAndStacks()
        {
            for (int i = 0; i < capi.World.Blocks.Length; i++)
            {
                AddCollectible(capi.World.Blocks[i]);
            }

            for (int i = 0; i < capi.World.Items.Length; i++)
            {
                AddCollectible(capi.World.Items[i]);
            }

            stacks = new ItemStack[stackListElements.Count];
            for (int i = 0; i < stacks.Length; i++)
            {
                stacks[i] = stackListElements[i].Stack;
            }
        }

        private void AddCollectible(CollectibleObject obj)
        {
            if (obj?.Code == null) return;

            bool inCreativeTab = obj.CreativeInventoryTabs != null && obj.CreativeInventoryTabs.Length > 0;
            bool inCreativeTabStack = obj.CreativeInventoryStacks != null && obj.CreativeInventoryStacks.Length > 0;
            bool explicitlyIncluded = obj.Attributes?["handbook"]?["include"].AsBool() == true;
            bool explicitlyExcluded = obj.Attributes?["handbook"]?["exclude"].AsBool() == true;

            if (explicitlyExcluded) return;
            if (!explicitlyIncluded && !inCreativeTab && !inCreativeTabStack) return;

            List<ItemStack> stacks = new List<ItemStack>();

            if (inCreativeTabStack)
            {
                for (int i = 0; i < obj.CreativeInventoryStacks.Length; i++)
                {
                    for (int j = 0; j < obj.CreativeInventoryStacks[i].Stacks.Length; j++)
                    {
                        ItemStack stack = obj.CreativeInventoryStacks[i].Stacks[j].ResolvedItemstack;
                        stack.ResolveBlockOrItem(capi.World);

                        stack = stack.Clone();
                        stack.StackSize = stack.Collectible.MaxStackSize;
                        
                        if (!stacks.Any((stack1) => stack1.Equals(stack)))
                        {
                            stackListElements.Add(new StacklistElement()
                            {
                                Stack = stack,
                                TextCache = stack.GetName() + " " + stack.GetDescription(capi.World, false),
                                Visible = true
                            });
                        }
                    }
                }
            } else
            {
                ItemStack stack = new ItemStack(obj);
                stack.StackSize = stack.Collectible.MaxStackSize;

                stackListElements.Add(new StacklistElement()
                {
                    Stack = stack,
                    TextCache = stack.GetName() + " " + stack.GetDescription(capi.World, false),
                    Visible = true
                });
            }
        }




        public void FilterItemsBySearchText(string text)
        {
            currentSearchText = text;

            text = text.ToLowerInvariant();

            for (int i = 0; i < stackListElements.Count; i++)
            {
                stackListElements[i].Visible = text.Length == 0 || stackListElements[i].TextCache.CaseInsensitiveContains(text);
            }

            GuiElementStacklist stacklist = overviewGui.GetStacklist("stacklist");
            stacklist.CalcTotalHeight();
            overviewGui.GetScrollbar("scrollbar").SetHeights(
                (float)listHeight, (float)stacklist.insideBounds.fixedHeight
            );
        }

        public override void OnRenderGUI(float deltaTime)
        {
            if (browseHistory.Count == 0)
            {
                SingleComposer = overviewGui;
            }
            else
            {
                SingleComposer = detailViewGui;
            }

            base.OnRenderGUI(deltaTime);
        }


        public override bool RequiresUngrabbedMouse()
        {
            return true;
        }

        public override bool CaptureAllInputs()
        {
            return false;
        }

        public override void Dispose()
        {
            overviewGui?.Dispose();
            detailViewGui?.Dispose();
        }


    }
}
