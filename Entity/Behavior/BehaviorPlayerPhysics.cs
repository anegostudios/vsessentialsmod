using System;
using System.Collections.Generic;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorPlayerPhysics : EntityBehaviorControlledPhysics
    {
        EntityPlayer eplr;

        public EntityBehaviorPlayerPhysics(Entity entity) : base(entity)
        {
            eplr = entity as EntityPlayer;
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
            accum1s += deltaTime;
            if (accum1s > 1.5f)
            {
                accum1s = 0;
                updateWindForce();
            }

            accumulator += deltaTime;

            if (accumulator > 1)
            {
                accumulator = 1;
            }

            float frameTime = GlobalConstants.PhysicsFrameTime;
            bool isSelf = entity.World.Side == EnumAppSide.Client && (entity.World as IClientWorldAccessor).Player.Entity == entity;
            smoothStepping = isSelf;

            if (isSelf)
            {
                frameTime = 1 / 60f;
            }

            collisionTester.NewTick();
            while (accumulator >= frameTime)
            {
                TickEntityPhysicsPre(entity, frameTime);
                accumulator -= frameTime;
            }

            entity.PhysicsUpdateWatcher?.Invoke(accumulator, prevPos);
        }

        public override void TickEntityPhysicsPre(Entity entity, float dt)
        {
            prevPos.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
            EntityControls controls = eplr.Controls;

            string playerUID = entity.WatchedAttributes.GetString("playerUID");
            IPlayer player = entity.World.PlayerByUid(playerUID);
            if (entity.World is IServerWorldAccessor && ((IServerPlayer)player).ConnectionState != EnumClientState.Playing) return;

            if (player != null)
            {
                IClientWorldAccessor clientWorld = entity.World as IClientWorldAccessor;

                // Tyron Nov. 10 2020: This line of code was from August 2020 where it fixed jitter of other players related to climbing ladders?
                // Cannot repro. But what I can repro is this line breaks player animations. They are stuck in a permanent land pose, so disabled for now.
                //if (clientWorld != null && clientWorld.Player.ClientId != player.ClientId) return;

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
                pos.Yaw = (entity.Api as ICoreClientAPI).Input.MouseYaw;

                if (entity.Swimming)
                {
                    float prevPitch = pos.Pitch;
                    pos.Pitch = cplr.CameraPitch;
                    controls.CalcMovementVectors(pos, dt);
                    pos.Yaw = prevYaw;
                    pos.Pitch = prevPitch;
                }
                else
                {
                    controls.CalcMovementVectors(pos, dt);
                    pos.Yaw = prevYaw;
                }

                float desiredYaw = (float)Math.Atan2(controls.WalkVector.X, controls.WalkVector.Z) - GameMath.PIHALF;
                
                float yawDist = GameMath.AngleRadDistance(eplr.WalkYaw, desiredYaw);
                eplr.WalkYaw += GameMath.Clamp(yawDist, -6 * dt * GlobalConstants.OverallSpeedMultiplier, 6 * dt * GlobalConstants.OverallSpeedMultiplier);
                eplr.WalkYaw = GameMath.Mod(eplr.WalkYaw, GameMath.TWOPI);

                if (entity.Swimming)
                {
                    float desiredPitch = -(float)Math.Sin(pos.Pitch);
                    float pitchDist = GameMath.AngleRadDistance(eplr.WalkPitch, desiredPitch);
                    eplr.WalkPitch += GameMath.Clamp(pitchDist, -2 * dt * GlobalConstants.OverallSpeedMultiplier, 2 * dt * GlobalConstants.OverallSpeedMultiplier);
                    eplr.WalkPitch = GameMath.Mod(eplr.WalkPitch, GameMath.TWOPI);
                } else
                {
                    eplr.WalkPitch = 0;
                }
            } else
            {
                float prevYaw = pos.Yaw;
                controls.CalcMovementVectors(pos, dt);
                pos.Yaw = prevYaw;
            }
            
            TickEntityPhysics(pos, controls, dt);
        }

    }
}
