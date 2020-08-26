using System;
using System.Collections.Generic;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorPlayerPhysics : EntityBehaviorControlledPhysics
    {
        public EntityBehaviorPlayerPhysics(Entity entity) : base(entity)
        {
            
        }

        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            base.Initialize(properties, typeAttributes);
        }


        public override void OnGameTick(float deltaTime)
        {
            if (!duringRenderFrame)
            {
                onPhysicsTick(deltaTime);
            }
        }

        public override void onPhysicsTick(float deltaTime)
        {
            accumulator += deltaTime;

            if (accumulator > 1)
            {
                accumulator = 1;
            }

            float frameTime = GlobalConstants.PhysicsFrameTime;
            bool isSelf = entity.World.Side == EnumAppSide.Client && (entity.World as IClientWorldAccessor).Player.Entity == entity;
            if (isSelf)
            {
                frameTime = 1 / 60f;
            }

            while (accumulator >= frameTime)
            {
                prevPos.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
                
                GameTick(entity, frameTime);
                accumulator -= frameTime;
            }

            entity.PhysicsUpdateWatcher?.Invoke(accumulator, prevPos);
        }

        public override void GameTick(Entity entity, float dt)
        {
            EntityPlayer entityplayer = entity as EntityPlayer;
            EntityControls controls = entityplayer.Controls;

            string playerUID = entity.WatchedAttributes.GetString("playerUID");
            IPlayer player = entity.World.PlayerByUid(playerUID);
            if (entity.World is IServerWorldAccessor && ((IServerPlayer)player).ConnectionState != EnumClientState.Playing) return;

            if (player != null)
            {
                IClientWorldAccessor clientWorld = entity.World as IClientWorldAccessor;

                if (clientWorld != null && clientWorld.Player.ClientId != player.ClientId) return;

                // We pretend the entity is flying to disable gravity so that EntityBehaviorInterpolatePosition system 
                // can work better
                controls.IsFlying = player.WorldData.FreeMove || (clientWorld != null && clientWorld.Player.ClientId != player.ClientId);
                controls.NoClip = player.WorldData.NoClip;
                controls.MovespeedMultiplier = player.WorldData.MoveSpeedMultiplier;
            }

            EntityPos pos = entity.World is IServerWorldAccessor ? entity.ServerPos : entity.Pos;

            


            if (controls.TriesToMove && player is IClientPlayer)
            {
                IClientPlayer cplr = player as IClientPlayer;

                float prevYaw = pos.Yaw;

                if (entity.Swimming)
                {
                    float prevPitch = pos.Pitch;
                    pos.Yaw = cplr.CameraYaw;
                    pos.Pitch = cplr.CameraPitch;
                    controls.CalcMovementVectors(pos, dt);
                    pos.Yaw = prevYaw;
                    pos.Pitch = prevPitch;
                }
                else
                {
                    pos.Yaw = cplr.CameraYaw;
                    controls.CalcMovementVectors(pos, dt);
                    pos.Yaw = prevYaw;
                }

                float desiredYaw = (float)Math.Atan2(controls.WalkVector.X, controls.WalkVector.Z) - GameMath.PIHALF;
                
                float yawDist = GameMath.AngleRadDistance(entityplayer.WalkYaw, desiredYaw);
                entityplayer.WalkYaw += GameMath.Clamp(yawDist, -8 * dt * GlobalConstants.OverallSpeedMultiplier, 8 * dt * GlobalConstants.OverallSpeedMultiplier);
                entityplayer.WalkYaw = GameMath.Mod(entityplayer.WalkYaw, GameMath.TWOPI);

                if (entity.Swimming)
                {
                    float desiredPitch = -(float)Math.Sin(pos.Pitch); // (float)controls.FlyVector.Y * GameMath.PI;
                    float pitchDist = GameMath.AngleRadDistance(entityplayer.WalkPitch, desiredPitch);
                    entityplayer.WalkPitch += GameMath.Clamp(pitchDist, -2 * dt * GlobalConstants.OverallSpeedMultiplier, 2 * dt * GlobalConstants.OverallSpeedMultiplier);
                    entityplayer.WalkPitch = GameMath.Mod(entityplayer.WalkPitch, GameMath.TWOPI);
                } else
                {
                    entityplayer.WalkPitch = 0;
                }
            } else
            {

                controls.CalcMovementVectors(pos, dt);
            }
            
            TickEntityPhysics(pos, controls, dt);
        }

    }
}
