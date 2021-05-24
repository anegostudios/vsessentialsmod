using System;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.Essentials;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorGoalAI : EntityBehavior
    {
        public AiGoalManager goalManager;
        public PathTraverserBase PathTraverser;

        public EntityBehaviorGoalAI(Entity entity) : base(entity)
        {
            goalManager = new AiGoalManager(entity);
        }


        public override void Initialize(EntityProperties properties, JsonObject aiconfig)
        {
            if (!(entity is EntityAgent))
            {
                entity.World.Logger.Error("The goal ai currently only works on entities inheriting from EntityAgent. Will ignore loading goals for entity {0} ", entity.Code);
                return;
            }

            PathTraverser = new StraightLineTraverser(entity as EntityAgent);

            JsonObject[] goals = aiconfig["aigoals"]?.AsArray();
            if (goals == null) return;

            foreach (JsonObject goalConfig in goals)
            {
                string goalCode = goalConfig["code"]?.AsString();
                Type goalType = null;
                if (!AiGoalRegistry.GoalTypes.TryGetValue(goalCode, out goalType))
                {
                    entity.World.Logger.Error("Goal with code {0} for entity {1} does not exist. Ignoring.", goalCode, entity.Code);
                    continue;
                }

                AiGoalBase goal = (AiGoalBase)Activator.CreateInstance(goalType, (EntityAgent)entity);
                goal.LoadConfig(goalConfig, aiconfig);

                goalManager.AddGoal(goal);
            }
        }


        public override void OnGameTick(float deltaTime)
        {
            // AI is only running for active entities
            if (entity.State != EnumEntityState.Active) return;

            PathTraverser.OnGameTick(deltaTime);

            goalManager.OnGameTick(deltaTime);

            entity.World.FrameProfiler.Mark("entity-ai");
        }


        public override void OnStateChanged(EnumEntityState beforeState, ref EnumHandling handled)
        {
            goalManager.OnStateChanged(beforeState);
        }


        public override void Notify(string key, object data)
        {
            goalManager.Notify(key, data);
        }

        public override string PropertyName()
        {
            return "goalai";
        }
    }
}
