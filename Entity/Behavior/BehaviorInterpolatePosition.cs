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
        

        double posDiffX, posDiffY, posDiffZ;
        double rollDiff, pitchDiff;
        double headpitchDiff;



        float accum;
        bool serverposApplied;

        ICoreClientAPI capi;
        
        
        public EntityBehaviorInterpolatePosition(Entity entity) : base(entity)
        {
            if (entity.World.Side == EnumAppSide.Server) throw new Exception("Not made for server side!");

            capi = entity.Api as ICoreClientAPI;
            capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "interpolateposition");
        }


        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            // Don't interpolate for ourselves
            if (entity == capi.World.Player.Entity) return;
            if (capi.IsGamePaused) return;

            bool isMounted = (entity as EntityAgent)?.MountedOn != null;

            // If the client player is currently mounted on this entity and controlling it we don't interpolate the position
            // as the physics sim of this entity is now determined by the client
            if (entity is IMountableSupplier ims)
            {
                foreach (var seat in ims.MountPoints)
                {
                    if (seat.MountedBy == capi.World.Player.Entity && seat.CanControl)
                    {
                        return;
                    }
                }
            }

            float interval = 0.2f;
            accum += deltaTime;

            if (accum > interval * 2 || !(entity is EntityAgent))
            {
                posDiffX = entity.ServerPos.X - entity.Pos.X;
                posDiffY = entity.ServerPos.Y - entity.Pos.Y;
                posDiffZ = entity.ServerPos.Z - entity.Pos.Z;
                rollDiff = entity.ServerPos.Roll - entity.Pos.Roll;
                //yawDiff = entity.ServerPos.Yaw - entity.Pos.Yaw;
                pitchDiff = entity.ServerPos.Pitch - entity.Pos.Pitch;

                double posDiffSq = posDiffX * posDiffX + posDiffY * posDiffY + posDiffZ * posDiffZ;

                // "|| accum > 1" mitigates items at the edge of block constantly jumping up and down
                if (entity.ServerPos.BasicallySameAsIgnoreMotion(entity.Pos, 0.05) || (accum > 1 && posDiffSq < 0.1 * 0.1))
                {
                    if (!serverposApplied && !isMounted)
                    {
                        entity.Pos.SetPos(entity.ServerPos);
                    }

                    serverposApplied = true;

                    return;
                }
            }

            

            double percentPosx = Math.Abs(posDiffX) * deltaTime / interval;
            double percentPosy = Math.Abs(posDiffY) * deltaTime / interval;
            double percentPosz = Math.Abs(posDiffZ) * deltaTime / interval;

            double percentyawdiff = Math.Abs(GameMath.AngleRadDistance(entity.Pos.Yaw, entity.ServerPos.Yaw)) * deltaTime / interval;
            double percentheadyawdiff = Math.Abs(GameMath.AngleRadDistance(entity.Pos.HeadYaw, entity.ServerPos.HeadYaw)) * deltaTime / interval;

            double percentrolldiff = Math.Abs(rollDiff) * deltaTime / interval;
            double percentpitchdiff = Math.Abs(pitchDiff) * deltaTime / interval;
            double percentheadpitchdiff = Math.Abs(headpitchDiff) * deltaTime / interval;


            int signPX = Math.Sign(percentPosx);
            int signPY = Math.Sign(percentPosy);
            int signPZ = Math.Sign(percentPosz);

            if (!isMounted)
            {
                entity.Pos.X += GameMath.Clamp(posDiffX, -signPX * percentPosx, signPX * percentPosx);
                entity.Pos.Y += GameMath.Clamp(posDiffY, -signPY * percentPosy, signPY * percentPosy);
                entity.Pos.Z += GameMath.Clamp(posDiffZ, -signPZ * percentPosz, signPZ * percentPosz);
            }

            int signR = Math.Sign(percentrolldiff); 
            int signY = Math.Sign(percentyawdiff);
            int signP = Math.Sign(percentpitchdiff);

            int signHY = Math.Sign(percentheadyawdiff);
            int signHP = Math.Sign(percentheadpitchdiff);

            // Dunno why the 0.7, but it's too fast otherwise
            entity.Pos.Roll += 0.7f * (float)GameMath.Clamp(rollDiff, -signR * percentrolldiff, signR * percentrolldiff);
            entity.Pos.Yaw += 0.7f * (float)GameMath.Clamp(GameMath.AngleRadDistance(entity.Pos.Yaw, entity.ServerPos.Yaw), -signY * percentyawdiff, signY * percentyawdiff);
            entity.Pos.Yaw = entity.Pos.Yaw % GameMath.TWOPI;

            entity.Pos.Pitch += 0.7f * (float)GameMath.Clamp(GameMath.AngleRadDistance(entity.Pos.Pitch, entity.ServerPos.Pitch), -signP * percentpitchdiff, signP * percentpitchdiff);
            entity.Pos.Pitch = entity.Pos.Pitch % GameMath.TWOPI;

            entity.Pos.HeadYaw += 0.7f * (float)GameMath.Clamp(GameMath.AngleRadDistance(entity.Pos.HeadYaw, entity.ServerPos.HeadYaw), -signHY * percentheadyawdiff, signHY * percentheadyawdiff);
            entity.Pos.HeadYaw = entity.Pos.HeadYaw % GameMath.TWOPI;

            entity.Pos.HeadPitch += 0.7f * (float)GameMath.Clamp(GameMath.AngleRadDistance(entity.Pos.HeadPitch, entity.ServerPos.HeadPitch), -signHP * percentheadpitchdiff, signHP * percentheadpitchdiff);
            entity.Pos.HeadPitch = entity.Pos.HeadPitch % GameMath.TWOPI;

            if (entity is EntityAgent eagent)
            {
                double percentbodyyawdiff = Math.Abs(GameMath.AngleRadDistance(eagent.BodyYaw, eagent.BodyYawServer)) * deltaTime / interval;
                int signBY = Math.Sign(percentbodyyawdiff);
                eagent.BodyYaw += 0.7f * (float)GameMath.Clamp(GameMath.AngleRadDistance(eagent.BodyYaw, eagent.BodyYawServer), -signBY * percentbodyyawdiff, signBY * percentbodyyawdiff);
                eagent.BodyYaw = eagent.BodyYaw % GameMath.TWOPI;
            }
        }


        
        public override void OnReceivedServerPos(bool isTeleport, ref EnumHandling handled)
        {
            // Don't interpolate for ourselves
            if (entity == capi.World.Player.Entity) return;
            
            handled = EnumHandling.PreventDefault;

            posDiffX = entity.ServerPos.X - entity.Pos.X;      // radfast 5.3.24:  isTeleport is ignored because, if teleporting, entityPos will already have been set to match ServerPos
            posDiffY = entity.ServerPos.Y - entity.Pos.Y;
            posDiffZ = entity.ServerPos.Z - entity.Pos.Z;
            rollDiff = GameMath.AngleRadDistance(entity.Pos.Roll, entity.ServerPos.Roll);
            pitchDiff = GameMath.AngleRadDistance(entity.Pos.Pitch, entity.ServerPos.Pitch);
            headpitchDiff = GameMath.AngleRadDistance(entity.Pos.HeadPitch, entity.ServerPos.HeadPitch);

            serverposApplied = false;

            accum = 0;
        }


        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);
            (entity.Api as ICoreClientAPI).Event.UnregisterRenderer(this, EnumRenderStage.Before);
            Dispose();
        }

        public override string PropertyName()
        {
            return "lerppos";
        }

        public void Dispose()
        {
            
        }
    }
}
