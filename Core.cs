using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Vintagestory.ServerMods
{
    public class Core : ModSystem
	{
        ICoreServerAPI sapi;
        ICoreAPI api;
        ICoreClientAPI capi;

        IShaderProgram prog;

        public override double ExecuteOrder()
        {
            return 0;
        }

        public override bool ShouldLoad(EnumAppSide side)
        {   
            return true;
        }

        public override void StartPre(ICoreAPI api)
        {
            GameVersion.EnsureEqualVersionOrKillExecutable(api, System.Diagnostics.FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion, GameVersion.OverallVersion, "VSEssentials");
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

            api.Event.BlockTexturesLoaded += Event_BlockTexturesLoaded;

            capi = api;
        }

        private void Event_BlockTexturesLoaded()
        {
            capi.Event.ReloadShader += LoadShader;
            LoadShader();
        }

        public bool LoadShader()
        {
            prog = capi.Shader.NewShaderProgram();

            prog.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
            prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);

            capi.Shader.RegisterFileShaderProgram("instanced", prog);

            return prog.Compile();
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
            api.RegisterBlockBehaviorClass("Decor", typeof(BlockBehaviorDecor));
        }



        private void RegisterDefaultBlockEntities()
        {
            api.RegisterBlockEntityClass("ParticleEmitter", typeof(BlockEntityParticleEmitter));
            api.RegisterBlockEntityClass("Transient", typeof(BlockEntityTransient));
            api.RegisterBlockEntityClass("Generic", typeof(BlockEntityGeneric));
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
            api.RegisterEntityBehaviorClass("goalai", typeof(EntityBehaviorGoalAI));
            api.RegisterEntityBehaviorClass("interpolateposition", typeof(EntityBehaviorInterpolatePosition));
            api.RegisterEntityBehaviorClass("despawn", typeof(EntityBehaviorDespawn));

            api.RegisterEntityBehaviorClass("grow", typeof(EntityBehaviorGrow));
            api.RegisterEntityBehaviorClass("multiply", typeof(EntityBehaviorMultiply));
            api.RegisterEntityBehaviorClass("multiplybase", typeof(EntityBehaviorMultiplyBase));
            api.RegisterEntityBehaviorClass("aimingaccuracy", typeof(EntityBehaviorAimingAccuracy));
            api.RegisterEntityBehaviorClass("emotionstates", typeof(EntityBehaviorEmotionStates));
            api.RegisterEntityBehaviorClass("repulseagents", typeof(EntityBehaviorRepulseAgents));
            api.RegisterEntityBehaviorClass("tiredness", typeof(EntityBehaviorTiredness));
            api.RegisterEntityBehaviorClass("nametag", typeof(EntityBehaviorNameTag));
            api.RegisterEntityBehaviorClass("placeblock", typeof(EntityBehaviorPlaceBlock));
            api.RegisterEntityBehaviorClass("deaddecay", typeof(EntityBehaviorDeadDecay));
            api.RegisterEntityBehaviorClass("floatupwhenstuck", typeof(EntityBehaviorFloatUpWhenStuck));
            api.RegisterEntityBehaviorClass("harvestable", typeof(EntityBehaviorHarvestable));
            api.RegisterEntityBehaviorClass("reviveondeath", typeof(EntityBehaviorReviveOnDeath));

            api.RegisterEntityBehaviorClass("mouthinventory", typeof(EntityBehaviorMouthInventory));
        }






    }
}
