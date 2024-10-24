using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public delegate BlockPos PositionProviderDelegate();
    public delegate InventoryBase InventorySupplierDelegate();

    public interface ICollectibleResolveOnLoad
    {
        void ResolveOnLoad(ItemSlot slot, IWorldAccessor worldForResolve, bool resolveImports);
    }

    public class InWorldContainer
    {
        protected RoomRegistry roomReg;
        protected Room room;
        protected ICoreAPI Api;

        public Room Room => room;

        /// <summary>
        /// On the server, we calculate the temperature only once each tick, to save repeating the same costly calculation.  A value -999 or less signifies not fresh and requires re-calculation
        /// </summary>
        protected float temperatureCached = -1000f;

        public InventoryBase Inventory => inventorySupplier();

        protected PositionProviderDelegate positionProvider;
        protected Action onRequireSyncToClient;
        public InventorySupplierDelegate inventorySupplier;

        string treeAttrKey;

        InventoryBase prevInventory;

        public InWorldContainer(InventorySupplierDelegate inventorySupplier, string treeAttrKey)
        {
            this.inventorySupplier = inventorySupplier;
            this.treeAttrKey = treeAttrKey;
        }

        public void Init(ICoreAPI Api, PositionProviderDelegate positionProvider, Action onRequireSyncToClient)
        {
            this.Api = Api;
            this.positionProvider = positionProvider;
            this.onRequireSyncToClient = onRequireSyncToClient;

            roomReg = Api.ModLoader.GetModSystem<RoomRegistry>();
            LateInit();
        }

        bool didInit = false;

        public void Reset()
        {
            didInit = false;
        }
        public void LateInit()
        {
            if (Inventory == null || didInit) return;

            if (prevInventory != null && Inventory != prevInventory) // New inventory instance? Do remove the events
            {
                prevInventory.OnAcquireTransitionSpeed -= Inventory_OnAcquireTransitionSpeed;
                if (Api.Side == EnumAppSide.Client)
                {
                    prevInventory.OnInventoryOpened -= Inventory_OnInventoryOpenedClient;
                }
            }

            didInit = true;
            Inventory.ResolveBlocksOrItems();
            Inventory.OnAcquireTransitionSpeed += Inventory_OnAcquireTransitionSpeed;
            if (Api.Side == EnumAppSide.Client)
            {
                Inventory.OnInventoryOpened += Inventory_OnInventoryOpenedClient;
            }

            prevInventory = Inventory;
        }

        private void Inventory_OnInventoryOpenedClient(IPlayer player)
        {
            OnTick(1);
        }

        public virtual void OnTick(float dt)
        {
            if (Api.Side == EnumAppSide.Client)
            {
                // We don't have to do this client side. The item stack renderer already updates those states for us
                return;
            }

            temperatureCached = -1000f;     // reset the cached temperature; it will be updated by the first perishable in the loop below, if there is one
            if (!HasTransitionables()) return;   // Skip the room check if this container currently has no transitionables

            room = roomReg.GetRoomForPosition(positionProvider());
            if (room.AnyChunkUnloaded != 0) return;

            foreach (ItemSlot slot in Inventory)
            {
                if (slot.Itemstack == null) continue;

                AssetLocation codeBefore = slot.Itemstack.Collectible.Code;
                slot.Itemstack.Collectible.UpdateAndGetTransitionStates(Api.World, slot);

                if (slot.Itemstack?.Collectible.Code != codeBefore)
                {
                    onRequireSyncToClient();
                }
            }
            temperatureCached = -1000f;      // reset the cached temperature in case any code needs to call GetPerishRate() between ticks of this entity
        }

        protected virtual bool HasTransitionables()
        {
            foreach (ItemSlot slot in Inventory)
            {
                ItemStack stack = slot.Itemstack;
                if (stack == null) continue;

                if (stack.Collectible.RequiresTransitionableTicking(Api.World, stack)) return true;
            }
            return false;
        }

        protected virtual float Inventory_OnAcquireTransitionSpeed(EnumTransitionType transType, ItemStack stack, float baseMul)
        {
            float positionAwarePerishRate = Api != null && transType == EnumTransitionType.Perish ? GetPerishRate() : 1;
            if (transType == EnumTransitionType.Dry || transType == EnumTransitionType.Melt) positionAwarePerishRate = 0.25f;

            return baseMul * positionAwarePerishRate;
        }


        public virtual float GetPerishRate()
        {
            BlockPos sealevelpos = positionProvider().Copy();
            sealevelpos.Y = Api.World.SeaLevel;

            float temperature = temperatureCached;
            if (temperature < -999f)
            {
                temperature = Api.World.BlockAccessor.GetClimateAt(sealevelpos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, Api.World.Calendar.TotalDays).Temperature;
                if (Api.Side == EnumAppSide.Server) temperatureCached = temperature;   // Cache the temperature for the remainder of this tick
            }

            if (room == null)
            {
                room = roomReg.GetRoomForPosition(positionProvider());
            }

            float soilTempWeight = 0f;
            float skyLightProportion = (float)room.SkylightCount / Math.Max(1, room.SkylightCount + room.NonSkylightCount);   // avoid any risk of divide by zero

            if (room.IsSmallRoom)
            {
                soilTempWeight = 1f;
                // If there's too much skylight, it's less cellar-like
                soilTempWeight -= 0.4f * skyLightProportion;
                // If non-cooling blocks exceed cooling blocks, it's less cellar-like
                soilTempWeight -= 0.5f * GameMath.Clamp((float)room.NonCoolingWallCount / Math.Max(1, room.CoolingWallCount), 0f, 1f);
            }

            int lightlevel = Api.World.BlockAccessor.GetLightLevel(positionProvider(), EnumLightLevelType.OnlySunLight);

            // light level above 12 makes it additionally warmer, especially when part of a cellar or a greenhouse
            float lightImportance = 0.1f;
            // light in small fully enclosed rooms has a big impact
            if (room.IsSmallRoom) lightImportance += 0.3f * soilTempWeight + 1.75f * skyLightProportion;
            // light in large most enclosed rooms (e.g. houses, greenhouses) has medium impact
            else if (room.ExitCount <= 0.1f * (room.CoolingWallCount + room.NonCoolingWallCount)) lightImportance += 1.25f * skyLightProportion;
            // light outside rooms (e.g. chests on world surface) has low impact but still warms them above base air temperature
            else lightImportance += 0.5f * skyLightProportion;
            lightImportance = GameMath.Clamp(lightImportance, 0f, 1.5f);
            float airTemp = temperature + GameMath.Clamp(lightlevel - 11, 0, 10) * lightImportance;


            // Lets say deep soil temperature is a constant 5°C
            float cellarTemp = 5;

            // How good of a cellar it is depends on how much rock or soil was used on he cellars walls
            float hereTemp = GameMath.Lerp(airTemp, cellarTemp, soilTempWeight);

            // For fairness lets say if its colder outside, use that temp instead
            hereTemp = Math.Min(hereTemp, airTemp);

            // Some neat curve to turn the temperature into a spoilage rate
            // http://fooplot.com/#W3sidHlwZSI6MCwiZXEiOiJtYXgoMC4xLG1pbigyLjUsM14oeC8xOS0xLjIpKS0wLjEpIiwiY29sb3IiOiIjMDAwMDAwIn0seyJ0eXBlIjoxMDAwLCJ3aW5kb3ciOlsiLTIwIiwiNDAiLCIwIiwiMyJdLCJncmlkIjpbIjIuNSIsIjAuMjUiXX1d
            // max(0.1, min(2.5, 3^(x/15 - 1.2))-0.1)
            float rate = Math.Max(0.1f, Math.Min(2.4f, (float)Math.Pow(3, hereTemp / 19 - 1.2) - 0.1f));

            return rate;
        }

        public void ReloadRoom()
        {
            room = roomReg.GetRoomForPosition(positionProvider());
        }



        public void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
        {
            foreach (var slot in Inventory)
            {
                slot.Itemstack?.Collectible.OnStoreCollectibleMappings(Api.World, slot, blockIdMapping, itemIdMapping);
            }
        }

        public void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
        {
            foreach (var slot in Inventory)
            {
                if (slot.Itemstack == null) continue;

                if (!slot.Itemstack.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve))
                {
                    slot.Itemstack = null;
                }
                else
                {
                    slot.Itemstack.Collectible.OnLoadCollectibleMappings(worldForResolve, slot, oldBlockIdMapping, oldItemIdMapping, resolveImports);
                }

                if (slot.Itemstack?.Collectible is IResolvableCollectible resolvable)
                {
                    resolvable.Resolve(slot, worldForResolve, resolveImports);
                }
            }
        }

        public void ToTreeAttributes(ITreeAttribute tree)
        {
            if (Inventory != null)
            {
                ITreeAttribute invtree = new TreeAttribute();
                Inventory.ToTreeAttributes(invtree);
                tree[treeAttrKey] = invtree;
            }
        }

        public void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            Inventory.FromTreeAttributes(tree.GetTreeAttribute(treeAttrKey));
        }

    }
}
