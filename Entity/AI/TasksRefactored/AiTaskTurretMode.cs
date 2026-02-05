using Newtonsoft.Json;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent;

public enum EnumTurretStateR
{
    Idle,
    TurretMode,
    TurretModeLoad,
    TurretModeHold,
    TurretModeFired,
    TurretModeReload,
    TurretModeUnload,
    Stop
}

/// <summary>
/// <br/>
/// <br/>
/// Changes 1.21.0-pre.1 => 1.21.0-pre.2<br/>
/// - sensingRange => seekingRange<br/>
/// - shootSound default value "sounds/creature/bowtorn/release" => ""<br/>
/// - drawSound default value "sounds/creature/bowtorn/draw" => ""<br/>
/// - reloadSound default value "sounds/creature/bowtorn/reload" => ""<br/>
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AiTaskTurretModeConfig : AiTaskShootAtEntityConfig
{
    [JsonProperty] public float AbortRange = 14f;
    [JsonProperty] public float FiringRangeMin = 14f;
    [JsonProperty] public float FiringRangeMax = 26f;
    [JsonProperty] public float FinishedAnimationProgress = 0.95f;
    [JsonProperty] public string LoadAnimation = "load";
    [JsonProperty] public string TurretAnimation = "turret";
    [JsonProperty] public string LoadFromTurretAnimation = "load-fromturretpose";
    [JsonProperty] public string HoldAnimation = "hold";
    [JsonProperty] public string FireAnimation = "fire";
    [JsonProperty] public string UnloadAnimation = "unload";
    [JsonProperty] public string ReloadAnimation = "reload";
    [JsonProperty] public AssetLocation? DrawSound = null;
    [JsonProperty] public AssetLocation? ReloadSound = null;

    public override void Init(EntityAgent entity)
    {
        base.Init(entity);

        if (DrawSound != null)
        {
            DrawSound = DrawSound.WithPathPrefixOnce("sounds/");
        }

        if (ReloadSound != null)
        {
            ReloadSound = ReloadSound.WithPathPrefixOnce("sounds/");
        }
    }
}

public class AiTaskTurretModeR : AiTaskShootAtEntityR
{
    private AiTaskTurretModeConfig Config => GetConfig<AiTaskTurretModeConfig>();

    protected int searchWaitMs = 2000;
    protected EnumTurretStateR currentState;
    protected IProjectile? prevProjectile;
    protected float currentStateTime;


    protected virtual bool inFiringRange
    {
        get
        {
            double range = targetEntity?.Pos.DistanceTo(entity.Pos) ?? double.MaxValue;
            return range >= Config.FiringRangeMin && range <= Config.FiringRangeMax;
        }
    }
    protected virtual bool inSensingRange => (targetEntity?.Pos.DistanceTo(entity.Pos) ?? float.MaxValue) <= GetSeekingRange();
    protected virtual bool inAbortRange => (targetEntity?.Pos.DistanceTo(entity.Pos) ?? float.MaxValue) <= Config.AbortRange;


    public AiTaskTurretModeR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        baseConfig = LoadConfig<AiTaskTurretModeConfig>(entity, taskConfig, aiConfig);
    }

    public override void AfterInitialize()
    {
        base.AfterInitialize();

        entity.AnimManager.OnAnimationStopped += AnimManager_OnAnimationStopped;
    }

    public override bool ShouldExecute()
    {
        return base.ShouldExecute() && !inAbortRange;
    }

    public override void StartExecute()
    {
        base.StartExecute();

        currentState = EnumTurretStateR.Idle;
        currentStateTime = 0;
    }

    public override bool ContinueExecute(float dt)
    {
        if (!base.ContinueExecuteChecks(dt)) return false;
        if (targetEntity == null) return false;

        currentStateTime += dt;
        UpdateState();
        AdjustYaw(dt);

        return currentState != EnumTurretStateR.Stop;
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);

        entity.StopAnimation(Config.TurretAnimation);
        entity.StopAnimation(Config.HoldAnimation);
        prevProjectile = null;
    }

    #region FSM
    // When in sensing range
    // Enter turret mode
    // When in firing range and in turret mode
    // Load bolt
    //   If target too close: Unload
    //   If target too far: Wait 2-3 seconds. If still too far. Unload
    //   If outside sensing range exit turret mode. Stop task.
    // Fire bolt
    // Reload
    // If target too close or outside sensing range - Stop here.
    // Load bolt from turret pose

    protected virtual void UpdateState()
    {
        currentState = currentState switch
        {
            EnumTurretStateR.Idle => Idle(currentState),
            EnumTurretStateR.TurretMode => TurretMode(currentState),
            EnumTurretStateR.TurretModeLoad => TurretModeLoad(currentState),
            EnumTurretStateR.TurretModeHold => TurretModeHold(currentState),
            EnumTurretStateR.TurretModeFired => TurretModeFired(currentState),
            EnumTurretStateR.TurretModeReload => TurretModeReload(currentState),
            EnumTurretStateR.TurretModeUnload => TurretModeUnload(currentState),
            EnumTurretStateR.Stop => currentState,
            _ => currentState,
        };
    }

    protected virtual EnumTurretStateR Idle(EnumTurretStateR state)
    {
        if (inFiringRange)
        {
            entity.StartAnimation(Config.LoadAnimation);
            currentStateTime = 0;
            return EnumTurretStateR.TurretMode;
        }

        if (inSensingRange)
        {
            entity.StartAnimation(Config.TurretAnimation);
            currentStateTime = 0;
            return EnumTurretStateR.TurretMode;
        }

        return state;
    }
    protected virtual EnumTurretStateR TurretMode(EnumTurretStateR state)
    {
        if (!IsAnimationFinished(Config.TurretAnimation)) return state;

        if (inAbortRange)
        {
            Abort();
            return state;
        }

        if (inFiringRange)
        {
            entity.StopAnimation(Config.TurretAnimation);
            entity.StartAnimation(Config.LoadFromTurretAnimation);
            if (Config.DrawSound != null)
            {
                entity.World.PlaySoundAt(Config.DrawSound, entity, null, Config.RandomizePitch, Config.SoundRange, Config.SoundVolume);
            }
            currentStateTime = 0;
            return EnumTurretStateR.TurretModeLoad;
        }

        if (currentStateTime > 5)
        {
            entity.StopAnimation(Config.TurretAnimation);
            return EnumTurretStateR.Stop;
        }

        return state;
    }
    protected virtual EnumTurretStateR TurretModeLoad(EnumTurretStateR state)
    {
        if (!IsAnimationFinished(Config.LoadAnimation)) return state;
        entity.StartAnimation(Config.HoldAnimation);
        currentStateTime = 0;
        return EnumTurretStateR.TurretModeHold;
    }
    protected virtual EnumTurretStateR TurretModeHold(EnumTurretStateR state)
    {
        if (inFiringRange || inAbortRange)
        {
            if (currentStateTime > 1.25)
            {
                SetOrAdjustDispersion();

                ShootProjectile();

                entity.StopAnimation(Config.HoldAnimation);
                entity.StartAnimation(Config.FireAnimation);

                return EnumTurretStateR.TurretModeFired;
            }
            return state;
        }

        if (currentStateTime > 2)
        {
            entity.StopAnimation(Config.HoldAnimation);
            entity.StartAnimation(Config.UnloadAnimation);

            return EnumTurretStateR.TurretModeUnload;
        }

        return state;
    }
    protected virtual EnumTurretStateR TurretModeFired(EnumTurretStateR state)
    {
        if (targetEntity == null)
        {
            stopTask = true;
            return EnumTurretStateR.Stop;
        }

        float range = Config.SeekingRange;
        if (inAbortRange || !targetEntity.Alive || targetEntity is EntityPlayer player && !TargetablePlayerMode(player) || !HasDirectContact(targetEntity, range, range / 2f))
        {
            Abort();
            return state;
        }

        if (inSensingRange)
        {
            entity.StartAnimation(Config.ReloadAnimation);
            if (Config.ReloadSound != null)
            {
                entity.World.PlaySoundAt(Config.ReloadSound, entity, null, Config.RandomizePitch, Config.SoundRange, Config.SoundVolume);
            }
            return EnumTurretStateR.TurretModeReload;
        }

        return state;
    }
    protected virtual EnumTurretStateR TurretModeReload(EnumTurretStateR state)
    {
        if (!IsAnimationFinished(Config.ReloadAnimation)) return state;

        if (inAbortRange)
        {
            Abort();
            return state;
        }

        if (Config.DrawSound != null)
        {
            entity.World.PlaySoundAt(Config.DrawSound, entity, null, Config.RandomizePitch, Config.SoundRange, Config.SoundVolume);
        }

        return EnumTurretStateR.TurretModeLoad;
    }
    protected virtual EnumTurretStateR TurretModeUnload(EnumTurretStateR state)
    {
        if (!IsAnimationFinished(Config.UnloadAnimation)) return state;

        return EnumTurretStateR.Stop;
    }
    #endregion

    protected virtual void Abort()
    {
        currentState = EnumTurretStateR.Stop;

        entity.StopAnimation(Config.HoldAnimation);
        entity.StopAnimation(Config.TurretAnimation);

        AiTaskManager taskManager = entity.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager ?? throw new InvalidOperationException("Failed to get task manager");
        AiTaskStayInRangeR? stayInRangeTask = taskManager.GetTask<AiTaskStayInRangeR>();

        if (stayInRangeTask != null && targetEntity != null)
        {
            stayInRangeTask.TargetEntity = targetEntity;
            taskManager.ExecuteTask<AiTaskStayInRangeR>();
        }
    }

    protected virtual bool IsAnimationFinished(string animationCode)
    {
        RunningAnimation? animation = entity.AnimManager.GetAnimationState(animationCode);
        if (animation == null) return false;
        return !animation.Running || animation.AnimProgress >= Config.FinishedAnimationProgress;
    }

    protected virtual void AnimManager_OnAnimationStopped(string anim)
    {
        if (!active || targetEntity == null) return;
        UpdateState();
    }
}
