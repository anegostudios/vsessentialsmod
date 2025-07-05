using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    public class AiTaskLookAround : AiTaskBase
    {
        public AiTaskLookAround(EntityAgent entity) : base(entity)
        {
        }


        public int minduration;
        public int maxduration;
        public float turnSpeedMul = 0.75f;

        public long idleUntilMs;

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            this.minduration = (int)taskConfig["minduration"]?.AsInt(2000);
            this.maxduration = (int)taskConfig["maxduration"]?.AsInt(4000);
            this.turnSpeedMul = (float)taskConfig["turnSpeedMul"]?.AsFloat(0.75f);

            idleUntilMs = entity.World.ElapsedMilliseconds + minduration + entity.World.Rand.Next(maxduration - minduration);

            base.LoadConfig(taskConfig, aiConfig);
        }

        public override bool ShouldExecute()
        {
            return cooldownUntilMs < entity.World.ElapsedMilliseconds;
        }

        public override void StartExecute()
        {
            base.StartExecute();
            idleUntilMs = entity.World.ElapsedMilliseconds + minduration + entity.World.Rand.Next(maxduration - minduration);

            entity.ServerPos.Yaw = (float)GameMath.Clamp(
                entity.World.Rand.NextDouble() * GameMath.TWOPI,
                entity.ServerPos.Yaw - GameMath.PI / 4 * GlobalConstants.OverallSpeedMultiplier * turnSpeedMul,
                entity.ServerPos.Yaw + GameMath.PI / 4 * GlobalConstants.OverallSpeedMultiplier * turnSpeedMul
            );
        }

        public override bool ContinueExecute(float dt)
        {
            //Check if time is still valid for task.
            if (!IsInValidDayTimeHours(false)) return false;

            return entity.World.ElapsedMilliseconds < idleUntilMs;
        }
    }
}
