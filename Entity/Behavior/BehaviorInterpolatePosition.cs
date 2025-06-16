using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

#nullable disable

public struct PositionSnapshot
{
    public double x;
    public double y;
    public double z;

    public float interval;

    public bool isTeleport;

    public PositionSnapshot(Vec3d pos, float interval, bool isTeleport)
    {
        x = pos.X;
        y = pos.Y;
        z = pos.Z;

        this.interval = interval;
        this.isTeleport = isTeleport;
    }

    public PositionSnapshot(EntityPos pos, float interval, bool isTeleport)
    {
        x = pos.X;
        y = pos.Y;
        z = pos.Z;

        this.interval = interval;
        this.isTeleport = isTeleport;
    }
}

public class EntityBehaviorInterpolatePosition : EntityBehavior, IRenderer
{
    public ICoreClientAPI capi;
    public EntityAgent agent;
    public IMountable mountableSupplier;

    public EntityBehaviorInterpolatePosition(Entity entity) : base(entity)
    {
        if (entity.World.Side == EnumAppSide.Server) throw new Exception($"Remove server interpolation behavior from {entity.Code.Path}.");

        capi = entity.Api as ICoreClientAPI;
        capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "interpolateposition");
        agent = entity as EntityAgent;
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        mountableSupplier = entity.GetInterface<IMountable>();
    }

    public float dtAccum = 0;

    // Will lerp from pL to pN.
    public PositionSnapshot pL;
    public PositionSnapshot pN;

    public Queue<PositionSnapshot> positionQueue = new();

    public float currentYaw;
    public float targetYaw;

    public float currentPitch;
    public float targetPitch;

    public float currentRoll;
    public float targetRoll;

    public float currentHeadYaw;
    public float targetHeadYaw;

    public float currentHeadPitch;
    public float targetHeadPitch;

    public float currentBodyYaw;
    public float targetBodyYaw;

    public void PushQueue(PositionSnapshot snapshot)
    {
        positionQueue.Enqueue(snapshot);
        queueCount++;
    }

    // Interval at what things should be received.
    public float interval = 1 / 15f;
    public int queueCount;

    public void PopQueue(bool clear)
    {
        dtAccum -= pN.interval;

        if (dtAccum < 0) dtAccum = 0;
        if (dtAccum > 1) dtAccum = 0;

        pL = pN;
        pN = positionQueue.Dequeue();
        queueCount--;

        // Clear flooded queue.
        if (clear && queueCount > 1) PopQueue(true);

        if (mountableSupplier?.IsBeingControlled() == true) return;

        entity.ServerPos.SetPos(pN.x, pN.y, pN.z);
        physics?.HandleRemotePhysics(Math.Max(pN.interval, interval), pN.isTeleport);
    }

    public IRemotePhysics physics;

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        currentYaw = entity.ServerPos.Yaw;
        targetYaw = entity.ServerPos.Yaw;

        PushQueue(new PositionSnapshot(entity.ServerPos, 0, false));

        targetYaw = entity.ServerPos.Yaw;
        targetPitch = entity.ServerPos.Pitch;
        targetRoll = entity.ServerPos.Roll;

        currentYaw = entity.ServerPos.Yaw;
        currentPitch = entity.ServerPos.Pitch;
        currentRoll = entity.ServerPos.Roll;

        if (agent != null)
        {
            targetHeadYaw = entity.ServerPos.HeadYaw;
            targetHeadPitch = entity.ServerPos.HeadPitch;
            targetBodyYaw = agent.BodyYawServer;

            currentHeadYaw = entity.ServerPos.HeadYaw;
            currentHeadPitch = entity.ServerPos.HeadPitch;
            currentBodyYaw = agent.BodyYawServer;
        }

        foreach (EntityBehavior behavior in entity.SidedProperties.Behaviors)
        {
            if (behavior is IRemotePhysics)
            {
                physics = behavior as IRemotePhysics;
                break;
            }
        }
    }

    /// <summary>
    /// Called when the client receives a new position.
    /// Move the positions forward and reset the accumulation.
    /// </summary>
    public override void OnReceivedServerPos(bool isTeleport, ref EnumHandling handled)
    {
        float tickInterval = entity.Attributes.GetInt("tickDiff", 1) * interval;

        PushQueue(new PositionSnapshot(entity.ServerPos, tickInterval, isTeleport));

        if (isTeleport)
        {
            dtAccum = 0;
            positionQueue.Clear();
            queueCount = 0;

            PushQueue(new PositionSnapshot(entity.ServerPos, tickInterval, false));
            PushQueue(new PositionSnapshot(entity.ServerPos, tickInterval, false));

            PopQueue(false);
            PopQueue(false);
        }

        targetYaw = entity.ServerPos.Yaw;
        targetPitch = entity.ServerPos.Pitch;
        targetRoll = entity.ServerPos.Roll;

        if (agent != null)
        {
            targetHeadYaw = entity.ServerPos.HeadYaw;
            targetHeadPitch = entity.ServerPos.HeadPitch;
            targetBodyYaw = agent.BodyYawServer;
        }

        if (queueCount > 20)
        {
            PopQueue(true);
        }
    }

    public int wait = 0;
    public float targetSpeed = 0.6f;


    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (capi.IsGamePaused) return;

        if (queueCount < wait)
        {
            if (mountableSupplier == null)
            {
                entity.Pos.Yaw = LerpRotation(ref currentYaw, targetYaw, dt);
                entity.Pos.Pitch = LerpRotation(ref currentPitch, targetPitch, dt);
                entity.Pos.Roll = LerpRotation(ref currentRoll, targetRoll, dt);

                if (agent != null)
                {
                    entity.Pos.HeadYaw = LerpRotation(ref currentHeadYaw, targetHeadYaw, dt);
                    entity.Pos.HeadPitch = LerpRotation(ref currentHeadPitch, targetHeadPitch, dt);
                    agent.BodyYaw = LerpRotation(ref currentBodyYaw, targetBodyYaw, dt);
                }
            }

            return;
        }

        dtAccum += dt * targetSpeed;

        while (dtAccum > pN.interval)
        {
            if (queueCount > 0)
            {
                // This is the most convenient place to check this.
                // It should be done when the client creates it's own player somewhere.
                if (entity == capi.World.Player.Entity)
                {
                    capi.Event.UnregisterRenderer(this, EnumRenderStage.Before);
                    return;
                }

                PopQueue(false);
                wait = 0;
            }
            else
            {
                wait = 1;

                break;
            }
        }

        float speed = (queueCount * 0.2f) + 0.8f;
        targetSpeed = GameMath.Lerp(targetSpeed, speed, dt * 4);

        // Don't set position if the player is controlling the mount.
        if (mountableSupplier != null)
        {
            foreach (IMountableSeat seat in mountableSupplier.Seats)
            {
                // Set position of other entities.
                if (seat.Passenger != capi.World.Player.Entity)
                {
                    seat.Passenger?.Pos.SetFrom(seat.SeatPosition);
                }
                else
                {
                    if (mountableSupplier.Controller == capi.World.Player.Entity)
                    {
                        currentYaw = entity.Pos.Yaw;
                        currentPitch = entity.Pos.Pitch;
                        currentRoll = entity.Pos.Roll;
                        return;
                    }
                }
            }
        }

        float delta = dtAccum / pN.interval;
        if (wait != 0) delta = 1;

        entity.Pos.Yaw = LerpRotation(ref currentYaw, targetYaw, dt);
        entity.Pos.Pitch = LerpRotation(ref currentPitch, targetPitch, dt);
        entity.Pos.Roll = LerpRotation(ref currentRoll, targetRoll, dt);

        if (agent != null)
        {
            entity.Pos.HeadYaw = LerpRotation(ref currentHeadYaw, targetHeadYaw, dt);
            entity.Pos.HeadPitch = LerpRotation(ref currentHeadPitch, targetHeadPitch, dt);
            agent.BodyYaw = LerpRotation(ref currentBodyYaw, targetBodyYaw, dt);
        }

        // Only set position if not mounted.
        if (agent == null || agent.MountedOn == null)
        {
            entity.Pos.X = GameMath.Lerp(pL.x, pN.x, delta);
            entity.Pos.Y = GameMath.Lerp(pL.y, pN.y, delta);
            entity.Pos.Z = GameMath.Lerp(pL.z, pN.z, delta);
        }
    }

    // I don't like doing it like this but AI turns instantly so using the actual lerp is bad.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float LerpRotation(ref float current, float target, float dt)
    {
        float pDiff = Math.Abs(GameMath.AngleRadDistance(current, target)) * dt / 0.1f;
        int signY = Math.Sign(pDiff);
        current += 0.6f * Math.Clamp(GameMath.AngleRadDistance(current, target), -signY * pDiff, signY * pDiff);
        current %= GameMath.TWOPI;
        
        return current;
    }

    public override string PropertyName()
    {
        return "entityinterpolation";
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Before);
    }

    public void Dispose()
    {

    }

    public double RenderOrder => 0;
    public int RenderRange => 9999;
}
