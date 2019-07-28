using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorInterpolatePosition : EntityBehavior, IRenderer
    {
        public double RenderOrder => 0;
        public int RenderRange => 9999;
        public void Dispose() { (entity.Api as ICoreClientAPI).Event.UnregisterRenderer(this, EnumRenderStage.Before); }

        double posDiffX, posDiffY, posDiffZ;
        double yawDiff, rollDiff, pitchDiff;
        

        long serverPosReceivedMs;
        

        public EntityBehaviorInterpolatePosition(Entity entity) : base(entity)
        {
            if (entity.World.Side == EnumAppSide.Server) throw new Exception("Not made for server side!");

            ICoreClientAPI capi = entity.Api as ICoreClientAPI;
            capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "interpolateposition");
        }


        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            // Lag. Stop extrapolation (extrapolation begins after 200ms)
            if (entity.World.ElapsedMilliseconds - serverPosReceivedMs > 400 && entity.ServerPos.BasicallySameAsIgnoreMotion(entity.Pos, 0.03f))
            {
                return;
            }
            // Don't interpolate for ourselves
            if (entity == ((IClientWorldAccessor)entity.World).Player.Entity) return;



            double percent = 8 * deltaTime;

            posDiffX = entity.ServerPos.X - entity.Pos.X;
            posDiffY = entity.ServerPos.Y - entity.Pos.Y;
            posDiffZ = entity.ServerPos.Z - entity.Pos.Z;
            rollDiff = entity.ServerPos.Roll - entity.Pos.Roll;
            yawDiff = entity.ServerPos.Yaw - entity.Pos.Yaw;
            pitchDiff = entity.ServerPos.Pitch - entity.Pos.Pitch;


            int signPX = Math.Sign(posDiffX);
            int signPY = Math.Sign(posDiffY);
            int signPZ = Math.Sign(posDiffZ);

            entity.Pos.X += GameMath.Clamp(posDiffX, -signPX * percent * posDiffX, signPX * percent * posDiffX);
            entity.Pos.Y += GameMath.Clamp(posDiffY, -signPY * percent * posDiffY, signPY * percent * posDiffY);
            entity.Pos.Z += GameMath.Clamp(posDiffZ, -signPZ * percent * posDiffZ, signPZ * percent * posDiffZ);
            
            int signR = Math.Sign(rollDiff);
            int signY = Math.Sign(yawDiff);
            int signP = Math.Sign(pitchDiff);

            // Dunno why the 0.7, but it's too fast otherwise
            entity.Pos.Roll += 0.7f * (float)GameMath.Clamp(rollDiff, -signR * percent * rollDiff, signR * percent * rollDiff);
            entity.Pos.Yaw += 0.7f * (float)GameMath.Clamp(GameMath.AngleRadDistance(entity.Pos.Yaw, entity.ServerPos.Yaw), -signY * percent * yawDiff, signY * percent * yawDiff);
            entity.Pos.Yaw = entity.Pos.Yaw % GameMath.TWOPI;


            entity.Pos.Pitch += 0.7f * (float)GameMath.Clamp(GameMath.AngleRadDistance(entity.Pos.Pitch, entity.ServerPos.Pitch), -signP * percent * pitchDiff, signP * percent * pitchDiff);
            entity.Pos.Pitch = entity.Pos.Pitch % GameMath.TWOPI;
        }


        
        public override void OnReceivedServerPos(bool isTeleport, ref EnumHandling handled)
        {
            // Don't interpolate for ourselves
            if (entity == ((IClientWorldAccessor)entity.World).Player.Entity) return;
            
            serverPosReceivedMs = entity.World.ElapsedMilliseconds;
            handled = EnumHandling.PreventDefault;

            if (isTeleport) serverPosReceivedMs = 0;
        }


        public override string PropertyName()
        {
            return "lerppos";
        }
    }
}
