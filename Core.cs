using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Vintagestory.ServerMods
{

    /// <summary>
    /// This class contains core settings for the Vintagestory server
    /// </summary>
    public class Core : ModSystem
	{
        ICoreServerAPI sapi;
        ICoreAPI api;

        public override double ExecuteOrder()
        {
            return 0;
        }

        public override bool ShouldLoad(EnumAppSide side)
        {
            
            return true;
        }


        public override void StartClientSide(ICoreClientAPI api)
        {
            api.RegisterEntityRendererClass("Item", typeof(EntityItemRenderer));
            
            api.RegisterEntityRendererClass("BlockFalling", typeof(EntityBlockFallingRenderer));
            api.RegisterEntityRendererClass("Shape", typeof(EntityShapeRenderer));
            api.RegisterEntityRendererClass("SkinnableShape", typeof(EntitySkinnableShapeRenderer));

            //api.RegisterDialog("BlockEntityTextInput", typeof(GuiDialogBlockEntityTextInput));
            //api.RegisterDialog("BlockEntityStove", typeof(GuiDialogBlockEntityStove));
            //api.RegisterDialog("BlockEntityQuern", typeof(GuiDialogBlockEntityQuern));

        }


        public override void StartServerSide(ICoreServerAPI api)
        {
            this.sapi = api;
            
        }

        

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            

            RegisterDefaultBlocks();
            RegisterDefaultBlockBehaviors();
            RegisterDefaultCropBehaviors();
            RegisterDefaultItems();
            RegisterDefaultEntities();
            RegisterDefaultEntityBehaviors();
            RegisterDefaultBlockEntities();
        }
        

        private void RegisterDefaultBlocks()
        {
            
        }
        
        private void RegisterDefaultBlockBehaviors()
        {
            
        }



        private void RegisterDefaultBlockEntities()
        {
            api.RegisterBlockEntityClass("ParticleEmitter", typeof(BlockEntityParticleEmitter));
            api.RegisterBlockEntityClass("Transient", typeof(BlockEntityTransient));
        }


        private void RegisterDefaultCropBehaviors()
        {
            
        }


        private void RegisterDefaultItems()
        {
            
           
        }




        private void RegisterDefaultEntities()
        {    
            api.RegisterEntity("EntityBlockfalling", typeof(EntityBlockFalling));
        }


        private void RegisterDefaultEntityBehaviors()
        {
            api.RegisterEntityBehaviorClass("collectitems", typeof(EntityBehaviorCollectEntities));
            api.RegisterEntityBehaviorClass("health", typeof(EntityBehaviorHealth));
            api.RegisterEntityBehaviorClass("hunger", typeof(EntityBehaviorHunger));
            api.RegisterEntityBehaviorClass("breathe", typeof(EntityBehaviorBreathe));
            
            api.RegisterEntityBehaviorClass("playerphysics", typeof(EntityBehaviorPlayerPhysics));
            api.RegisterEntityBehaviorClass("controlledphysics", typeof(EntityBehaviorControlledPhysics));
            
            api.RegisterEntityBehaviorClass("taskai", typeof(EntityBehaviorTaskAI));
            api.RegisterEntityBehaviorClass("interpolateposition", typeof(EntityBehaviorInterpolatePosition));
            api.RegisterEntityBehaviorClass("despawn", typeof(EntityBehaviorDespawn));

            api.RegisterEntityBehaviorClass("grow", typeof(EntityBehaviorGrow));
            api.RegisterEntityBehaviorClass("multiply", typeof(EntityBehaviorMultiply));
            api.RegisterEntityBehaviorClass("aimingaccuracy", typeof(EntityBehaviorAimingAccuracy));
            api.RegisterEntityBehaviorClass("emotionstates", typeof(EntityBehaviorEmotionStates));
            api.RegisterEntityBehaviorClass("repulseagents", typeof(EntityBehaviorRepulseAgents));
            api.RegisterEntityBehaviorClass("tiredness", typeof(EntityBehaviorTiredness));
            api.RegisterEntityBehaviorClass("nametag", typeof(EntityBehaviorNameTag));
            api.RegisterEntityBehaviorClass("placeblock", typeof(EntityBehaviorPlaceBlock));
        }






    }
}
