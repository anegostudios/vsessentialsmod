using System;
using System.Linq;
using System.Text;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;


namespace Vintagestory.GameContent
{
    public enum EnumDirection
    {
        Backwards = -1,
        Stationary = 0,
        Forwards = 1
    }

    /// <summary>
    /// Controls how a gait should be used. Also uses properties from <see cref="AnimationMetaData"/>
    /// </summary>
    [DocumentAsJson]
    public class GaitMeta : AnimationMetaData
    {
        /// <summary>
        /// Determines how fast entity can turn left or right during the gait
        /// </summary>
        [DocumentAsJson("Optional", "1")]
        public float YawMultiplier = 1f;

        /// <summary>
        /// The speed of entity that is multiplied by <see cref="ControlMeta.MoveSpeedMultiplier"/> during the gait
        /// </summary>
        [DocumentAsJson("Optional", "0")]
        public float MoveSpeed = 0f;    

        /// <summary>
        /// Used to move backwards from <see cref="EntityBehaviorRideable"/>
        /// </summary>
        [DocumentAsJson("Optional", "False")]
        public bool Backwards = false;

        /// <summary> 
        /// If true, the animal may use this gait of its own volition
        /// </summary>
        [DocumentAsJson("Optional", "True")]
        public bool Natural = true;

        /// <summary>
        /// Currently unused
        /// </summary>
        [DocumentAsJson("Unused")]
        public float StaminaCost = 0f;

        /// <summary>
        /// Currently unused. Used as a fallback when the current gait is no longer valid, i.e. when fatigued
        /// </summary>
        [DocumentAsJson("Unused", "None")]
        public string? FallbackGaitCode;

        /// <summary>
        /// Sound played during the gait
        /// </summary>
        [Obsolete("Use SoundCode instead")]
        public AssetLocation? Sound;

        /// <summary>
        /// Sound played during the gait
        /// </summary>
        [DocumentAsJson("Optional", "None")]
        public string? SoundCode;

        /// <summary>
        /// The environment that this gait should be used in
        /// </summary>
        [DocumentAsJson("Optional", "Land")]
        public EnumHabitat Environment = EnumHabitat.Land;

        public EnumDirection Direction => MoveSpeed == 0f ? EnumDirection.Stationary : Backwards ? EnumDirection.Backwards : EnumDirection.Forwards;
        public bool HasMotion => MoveSpeed > 0f;
        public bool HasForwardMotion => MoveSpeed > 0f && !Backwards;
        public bool HasBackwardMotion => MoveSpeed > 0f && Backwards;

        /// <summary>
        /// The gait's environment typically has this much drag. May miss special cases.
        /// </summary>
        public float ExpectedDragFactor => Environment switch
        {
            EnumHabitat.Land => 0.7f,
            EnumHabitat.Sea => GlobalConstants.WaterDrag,
            EnumHabitat.Underwater => GlobalConstants.WaterDrag,
            EnumHabitat.Air => GlobalConstants.AirDragFlying,
            _ => 0.7f
        };
        /// <summary>
        /// If an entity stayed at this gait for a while, this is our best guess for how big entity.Pos.Motion will be
        /// </summary>
        public float EquilibriumMoveSpeed => MoveSpeed * ExpectedDragFactor / (1 - ExpectedDragFactor);
    }

    /// <summary>
    /// Defines gaits and movement for an entity
    /// </summary>
    /// <example><code lang="json">
    /// "behaviors": [
    ///   {
    ///     "code": "gait",
    ///     "navigateUsingGaits": true,
    ///     "idleGaits": { "Land": "idle", "Sea": "swimidle" },
    ///     "gaits": [
    ///       "walkback": {
    ///           "movespeed": 0.011,
    ///           "backwards": true,
    ///           // "trot" sound needs to be defined in the "sounds" section of the entity file
    ///           "soundCode": "trot",
    ///           "animation": "walkback",
    ///           "animationSpeed": 2.55
    ///       },
    ///       "idle": {
    ///           "animation": "idle",
    ///       },
    ///       "walk": {
    ///           "movespeed": 0.019,
    ///           "soundCode": "trot",
    ///           "animation": "walk",
    ///           "animationSpeed": 1.8
    ///       },
    ///       "gallop": {
    ///           "movespeed": 0.1067,
    ///           "fallbackGaitCode": "walk",
    ///           "soundCode": "gallop",
    ///           "animation": "Run",
    ///           "weight": 10,
    ///           "animationSpeed": 1.2,
    ///           "blendMode": "Average",
    ///           "easeOutSpeed": 30
    ///       },
    ///       "swimforth": {
    ///           "movespeed": 0.015,
    ///           "fallbackGaitCode": "swimidle",
    ///           "environment": "Sea",
    ///           "animation": "Swim",
    ///           "animationSpeed": 1
    ///       },
    ///       "swimidle": {
    ///           "movespeed": 0.0,
    ///           "environment": "Sea",
    ///           "animation": "Swim",
    ///           "animationSpeed": 1
    ///       },
    ///       "swimback": {
    ///           "movespeed": 0.008,
    ///           "backwards": true,
    ///           "fallbackGaitCode": "swimidle",
    ///           "environment": "Sea",
    ///           "animation": "Swim",
    ///           "animationSpeed": 1
    ///       }
    ///     ]
    ///   }
    /// ]
    /// </code></example>
    [DocumentAsJson]
    public class EntityBehaviorGait : EntityBehavior
    {
        public override string PropertyName()
        {
            return "gait";
        }

        protected EntityAgent eagent = null!;

        [DocumentAsJson("Optional", "false")]
        public bool NavigateUsingGaits = false;

        /// <summary>
        /// All available gaits.
        /// </summary>
        [DocumentAsJson("Required", "Empty")]
        public FastSmallDictionary<string, GaitMeta> Gaits { get; private set; } = new(1);

        protected GaitMeta currentGait = null!;
        public GaitMeta CurrentGait
        {
            get => currentGait;
            set
            {
                GaitMeta prevGait = currentGait;
                currentGait = value;
                if (entity.WatchedAttributes.GetString("currentgait") != value.Code)
                {
                    entity.WatchedAttributes.SetString("currentgait", value.Code);
                }
                if (prevGait?.Code != currentGait.Code)
                {
                    OnGaitChangedFrom(prevGait);
                }
            }
        }

        protected string? currentTurnAnimation = null;
        /// <summary>
        /// Turning speed. Exact values are currently unused other than positive or negative. Units may possibly be radians/second, but don't count on this.
        /// </summary>
        public double AngularVelocity = 0.0;

        /// <summary>
        /// Movement speed is multiplied by this
        /// </summary>
        public double MoveSpeedModifier = 1;

        protected ILoadedSound? gaitSound;

        public event Action? GaitChangedForEnvironmentDelegate;

        protected IMountable? mountableSupplier;

        /// <summary>
        /// Per environment, the gait to use when standing still, treading water, hovering, or otherwise not doing anything in particular. Provided from json as gait codes.
        /// </summary>
        [DocumentAsJson("Required", "Empty")]
        public FastSmallDictionary<EnumHabitat, GaitMeta> IdleGaits { get; private set; } = new(1);

        public GaitMeta FallbackGait => CurrentGait.FallbackGaitCode == null ? IdleGaits[CurrentGait.Environment] : Gaits[CurrentGait.FallbackGaitCode];

        public float GetYawMultiplier() => CurrentGait?.YawMultiplier ?? 1f;

        public virtual void SetIdle() => CurrentGait = IdleGaits.TryGetValue(CurrentGait.Environment) ?? IdleGaits[EnumHabitat.Land];
        public bool IsIdle => IsIdleGait(CurrentGait);

        public virtual bool IsIdleGait(GaitMeta gait)
        {
            return gait == IdleGaits.TryGetValue(gait.Environment);
        }

        public GaitMeta CascadingFallbackGait(int n)
        {
            var result = CurrentGait;

            while (n > 0)
            {
                result = result.FallbackGaitCode == null ? IdleGaits[result.Environment] : Gaits[result.FallbackGaitCode];
                n--;
            }

            return result;
        }

        public EntityBehaviorGait(Entity entity) : base(entity)
        {
            eagent = (EntityAgent)entity;
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            ArgumentNullException.ThrowIfNull(attributes);
            base.Initialize(properties, attributes);

            NavigateUsingGaits = attributes["navigateUsingGaits"].AsBool();
            JsonObject gaitsJson = attributes["gaits"];
            // Support older format for gaits
            if (gaitsJson.IsArray())
            {
                entity.Api.Logger.Warning("Gaits are being provided as an array for entity " + entity.Code + ". For better mod compat, provide them in object format instead.");
                GaitMeta?[]? gaitarray = attributes["gaits"].AsArray<GaitMeta>();
                ArgumentNullException.ThrowIfNull(gaitarray);
                foreach (GaitMeta? gait in gaitarray)
                {
                    ArgumentNullException.ThrowIfNull(gait);
                    gait.ClientSide = true; // Opt out of animation syncing
                    Gaits[gait.Code] = gait;
                }
            }
            // Mods are encouraged to switch to providing the gaits in this format instead
            else
            {
                Gaits = gaitsJson.AsObject<FastSmallDictionary<string, GaitMeta>>(Gaits);
                foreach ((string key, GaitMeta gait) in Gaits)
                {
                    gait.Code = key;
                    gait.ClientSide = true; // Opt out of animation syncing
                    gait.Init();
                }
            }

            FastSmallDictionary<EnumHabitat, string>? idleGaitCodes = attributes["idleGaits"].AsObject<FastSmallDictionary<EnumHabitat, string>>();
            if (idleGaitCodes != null)
            {
                foreach (var entry in idleGaitCodes)
                {
                    IdleGaits[entry.Key] = Gaits[entry.Value];
                }
            }
            else
            {
                string? idleGaitCode = attributes["idleGait"]?.AsString("idle");
                GaitMeta idle;
                if (idleGaitCode == null || !Gaits.TryGetValue(idleGaitCode, out idle)) throw new ArgumentException("JSON error. No idle gait for {0}", entity.Code);
                IdleGaits[EnumHabitat.Land] = idle;
            }

            // Sanity checks on the json data
            if (!IdleGaits.ContainsKey(EnumHabitat.Land))
            {
                throw new ArgumentException("JSON error. Gait behavior is missing idle land gait for entity " + entity.Code);
            }
            if (!IdleGaits.ContainsKey(EnumHabitat.Sea))
            {
                entity.Api.Logger.Warning("JSON error. Gait behavior is missing idle sea gait for entity " + entity.Code);
            }
            if (!Gaits.Values.Any(g => g.Environment == EnumHabitat.Land && g.Direction == EnumDirection.Forwards))
            {
                entity.Api.Logger.Warning("JSON error. Gait behavior is missing a forward land gait for entity " + entity.Code);
            }
            if (!Gaits.Values.Any(g => g.Environment == EnumHabitat.Sea && g.Direction == EnumDirection.Forwards))
            {
                entity.Api.Logger.Warning("JSON error. Gait behavior is missing a forward sea gait for entity " + entity.Code);
            }

            if (entity.Api.Side == EnumAppSide.Client)
            {
                // Only needed on the client, because on the server all changes will go through the CurrentGait property setter, which calls OnGaitChangedFrom directly
                entity.WatchedAttributes.RegisterModifiedListener("currentgait", OnGaitChanged);
                OnGaitChangedFrom(null);
            }
            else
            {
                // Server-only stand still on loaded, clients will be sent the already correct value. E.g. elk loaded on one client gallops into range of another.
                CurrentGait = IdleGaits[EnumHabitat.Land];
            }

            if (entity.Api is ICoreClientAPI capi)
            {
                capi.Event.PauseResume += OnGamePauseResume;
            }
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            mountableSupplier = entity.GetInterface<IMountable>();

            // Another sanity check here, since entity sounds aren't yet set up when Initialize() is called
            foreach (GaitMeta gait in Gaits.Values)
            {
                if (gait.SoundCode != null && entity.TryGetEntitySound(gait.SoundCode, entity.World.Rand) == null)
                {
                    entity.Api.Logger.Warning($"JSON Error: Entity {entity.Code} has sound {gait.SoundCode} set for gait {gait.Code}, but no such sound was found");
                }
            }
        }

        protected virtual void OnGaitChanged()
        {
            if (entity.Api is ICoreClientAPI capi && entity.IsControlledByClient(capi.World.Player.Entity))
            {
                // Client-authoritative, so keep our existing value
                if (entity.WatchedAttributes.GetString("currentgait") != currentGait.Code)
                {
                    entity.WatchedAttributes.SetString("currentgait", currentGait.Code);
                }
                return;
            }

            OnGaitChangedFrom(currentGait);
        }

        protected virtual void OnGaitChangedFrom(GaitMeta? prevGait)
        {
            currentGait = Gaits[entity.WatchedAttributes.GetString("currentgait")];
            if (currentGait.Code == prevGait?.Code) return;

#pragma warning disable CS0618 // Type or member is obsolete (gait.Sound)
            ICoreClientAPI? capi = entity.Api as ICoreClientAPI;
            if (capi != null && (CurrentGait.Sound != prevGait?.Sound || CurrentGait.SoundCode != prevGait?.SoundCode))
            {
                gaitSound?.Stop();
                gaitSound?.Dispose();
                gaitSound = null;

                if (CurrentGait.SoundCode != null)
                {
                    SoundAttributes? fetchedSound = entity.TryGetEntitySound(CurrentGait.SoundCode, entity.World.Rand);
                    if (fetchedSound != null)
                    {
                        SoundAttributes entitySound = fetchedSound.Value;
                        gaitSound = capi.World.LoadSound(new SoundParams()
                        {
                            Location = entitySound.Location,
                            DisposeOnFinish = false,
                            Position = entity.Pos.XYZ.ToVec3f(),
                            ShouldLoop = true,
                            Range = entitySound.Range,
                            SoundType = entitySound.Type,
                            Volume = entitySound.Volume.nextFloat(1f, entity.World.Rand),
                            Pitch = entitySound.Pitch.nextFloat(1f, entity.World.Rand),
                        });
                    }
                }
                else if (CurrentGait.Sound != null)
                {
                    gaitSound = capi.World.LoadSound(new SoundParams()
                    {
                        Location = CurrentGait.Sound!.Clone().WithPathPrefix("sounds/"),
                        DisposeOnFinish = false,
                        Position = entity.Pos.XYZ.ToVec3f(),
                        ShouldLoop = true
                    });
                }
#pragma warning restore CS0618 // Type or member is obsolete (gait.Sound)

                gaitSound?.Start();
            }

            if (prevGait?.Animation != null && CurrentGait.Animation != prevGait.Animation)
            {
                RunningAnimation? prev = eagent.AnimManager.Animator?.GetAnimationState(prevGait.Animation);
                var onEnd = prev?.Animation?.OnAnimationEnd;
                // Stop repeating or malformed animations, let others ease out or play til end
                if (onEnd == null || onEnd == EnumEntityAnimationEndHandling.Repeat)
                {
                    eagent.AnimManager.StopAnimation(prevGait.Animation);
                }
            }

            // Even if the animation is already playing from the previous gait, update in case this one has different metadata
            if (CurrentGait.Animation != null)
            {
                eagent.AnimManager.StartAnimation(CurrentGait);
            }

            if (CurrentGait.MoveSpeed == 0)
            {
                eagent.Controls.StopAllMovement();
                // On swimming entities this causes a tick where controls.Up is set false and PModuleGravity is applied, causing extra
                // bobbing motion. The effect looks nice on ridden elk so I'll keep it.
                // Don't immediately set the walk and fly vectors to 0. They will decrease naturally from drag, which looks nicer.

                if (currentTurnAnimation != null)
                {
                    eagent.StopAnimation(currentTurnAnimation);
                    currentTurnAnimation = null;
                }
            }
        }

        protected virtual void OnGamePauseResume(bool isPaused)
        {
            if (isPaused)
            {
                gaitSound?.Pause();
            }
            else
            {
                if (gaitSound?.IsPaused == true) gaitSound?.Start();
            }
        }

        protected float notOnGroundAccum;
        public override void OnGameTick(float dt)
        {
            if (!entity.Alive)
            {
                eagent.Controls.StopAllMovement();
                eagent.AnimManager.StopAnimation(currentGait.Animation);
                gaitSound?.Stop();
                gaitSound?.Dispose();
                gaitSound = null;
                return;
            }

            if (entity.Api is ICoreClientAPI capi && entity.IsControlledByClient(capi.World.Player.Entity)
                || entity.Api.Side == EnumAppSide.Server && entity.IsControlledByServer(mountableSupplier))
            {
                UpdateGaitForEnvironment();
                Move(dt);
            }

            if (entity.Api.Side != EnumAppSide.Client) return;

            if (entity.OnGround) notOnGroundAccum = 0;
            else notOnGroundAccum += dt;

            if (gaitSound != null)
            {
                gaitSound.SetPosition((float)entity.Pos.X, (float)entity.Pos.Y, (float)entity.Pos.Z);

                if (notOnGroundAccum > 0.2)
                {
                    if (!gaitSound.IsPaused) gaitSound.Pause();
                }
                else if (gaitSound.IsPaused)
                {
                    gaitSound.Start();
                }
            }

            UpdateTurnAnimation();
        }

        public virtual EnumHabitat GetTargetEnvironment()
        {
            if (eagent.Controls.IsFlying)
            {
                return EnumHabitat.Air;
            }
            return entity.Swimming ? EnumHabitat.Sea : EnumHabitat.Land;
        }

        public virtual GaitMeta? ChooseGait(EnumHabitat environment, EnumDirection direction, float moveSpeed)
        {
            GaitMeta? closest = null;
            foreach (GaitMeta gait in Gaits.Values)
            {
                if (!gait.Natural || gait.Environment != environment || gait.Direction != direction) continue;

                if (closest == null || (Math.Abs(gait.MoveSpeed - moveSpeed) < Math.Abs(closest.MoveSpeed - moveSpeed)))
                {
                    closest = gait;
                }
            }

            return closest;
        }

        public virtual void UpdateGaitForEnvironment()
        {
            EnumHabitat targetEnvironment = GetTargetEnvironment();
            if (CurrentGait.Environment == targetEnvironment) return;

            CurrentGait = ChooseGait(targetEnvironment, CurrentGait.Direction, CurrentGait.MoveSpeed)
                ?? IdleGaits.TryGetValue(targetEnvironment)
                ?? throw new Exception("Error changing gait for " + entity.Code + ". No gait found for target environment " + targetEnvironment + " and direction " + CurrentGait.Direction);

            GaitChangedForEnvironmentDelegate?.Invoke();
        }

        public void SetMotion(float dt, EntityControls controls, float desiredYaw, float nowMoveSpeed, GaitMeta? preferredGait)
        {
            if (!entity.Alive)
            {
                return;
            }

            float yawDist = GameMath.AngleRadDistance(entity.Pos.Yaw, desiredYaw);
            bool backwards = Math.Abs(yawDist) > GameMath.PIHALF;

            EnumHabitat targetEnvironment = GetTargetEnvironment();
            if (preferredGait != null && preferredGait.Environment == targetEnvironment && (preferredGait.Direction == EnumDirection.Backwards) == backwards)
            {
                CurrentGait = preferredGait;
            }
            else
            {
                GaitMeta? nextGait = ChooseGait(targetEnvironment, backwards ? EnumDirection.Backwards : EnumDirection.Forwards, nowMoveSpeed);
                if (nextGait == null)
                {
                    backwards = !backwards;
                    nextGait = ChooseGait(targetEnvironment, backwards ? EnumDirection.Backwards : EnumDirection.Forwards, nowMoveSpeed);
                }

                if (nextGait != null) CurrentGait = nextGait;
            }
            MoveSpeedModifier = Math.Min(1, nowMoveSpeed / Math.Max(CurrentGait.MoveSpeed, 0.001f)); // Careful not to divide by 0

            eagent.Controls.Forward = !backwards;
            eagent.ServerControls.Forward = !backwards;
            eagent.Controls.Backward = backwards;
            eagent.ServerControls.Backward = backwards;

            float minTurnAnglePerSec = 250;
            float maxTurnAnglePerSec = 450;
            ITreeAttribute? pathfinder = entity.Properties.Server?.Attributes?.GetTreeAttribute("pathfinder");
            if (pathfinder != null)
            {
                minTurnAnglePerSec = (float)pathfinder.GetDecimal("minTurnAnglePerSec", 250);
                maxTurnAnglePerSec = (float)pathfinder.GetDecimal("maxTurnAnglePerSec", 450);
            }

            float curTurnRadPerSec = minTurnAnglePerSec + (float)entity.World.Rand.NextDouble() * (maxTurnAnglePerSec - minTurnAnglePerSec);
            curTurnRadPerSec *= GameMath.DEG2RAD;

            float turnSpeed = curTurnRadPerSec * GetYawMultiplier() * dt * GlobalConstants.OverallSpeedMultiplier;
            AngularVelocity = GameMath.Clamp(yawDist, -turnSpeed, turnSpeed);

            entity.Pos.Yaw += (float)AngularVelocity;
            entity.Pos.Yaw = entity.Pos.Yaw % GameMath.TWOPI;
        }

        protected virtual void Move(float dt)
        {
            EntityControls controls = eagent.Controls;

            double cosYaw = Math.Cos(entity.Pos.Yaw);
            double sinYaw = Math.Sin(entity.Pos.Yaw);
            controls.WalkVector.Set(sinYaw, 0, cosYaw);
            controls.WalkVector.Mul(CurrentGait.MoveSpeed * GlobalConstants.OverallSpeedMultiplier * (int)CurrentGait.Direction * MoveSpeedModifier);

            // Make it walk along the wall, but not walk into the wall, which causes it to climb
            if (entity.Properties.RotateModelOnClimb && controls.IsClimbing && entity.ClimbingOnFace != null && entity.Alive)
            {
                BlockFacing facing = entity.ClimbingOnFace;
                if (Math.Sign(facing.Normali.X) == Math.Sign(controls.WalkVector.X))
                {
                    controls.WalkVector.X = 0;
                }

                if (Math.Sign(facing.Normali.Z) == Math.Sign(controls.WalkVector.Z))
                {
                    controls.WalkVector.Z = 0;
                }
            }

            if (entity.Swimming)
            {
                controls.FlyVector.Set(controls.WalkVector);

                Vec3d pos = entity.Pos.XYZ;
                Block inblock = entity.World.BlockAccessor.GetBlockRaw((int)pos.X, (int)(pos.Y), (int)pos.Z, BlockLayersAccess.Fluid);
                Block aboveblock = entity.World.BlockAccessor.GetBlockRaw((int)pos.X, (int)(pos.Y + 1), (int)pos.Z, BlockLayersAccess.Fluid);
                float waterY = (int)pos.Y + inblock.LiquidLevel / 8f + (aboveblock.IsLiquid() ? 9 / 8f : 0);
                float bottomSubmergedness = waterY - (float)pos.Y;

                // 0 = at swim line
                // 1 = completely submerged
                float swimlineSubmergedness = GameMath.Clamp(bottomSubmergedness - ((float)entity.SwimmingOffsetY), 0, 1);
                swimlineSubmergedness = Math.Min(1, swimlineSubmergedness + 0.075f);
                controls.FlyVector.Y = GameMath.Clamp(controls.FlyVector.Y, 0.02f, 0.04f) * swimlineSubmergedness*3;

                if (entity.CollidedHorizontally)
                {
                    controls.FlyVector.Y = Math.Max(0.05f, controls.FlyVector.Y);
                }

                eagent.Pos.Motion.Y += (swimlineSubmergedness-0.1)/300.0;

                controls.Up = controls.FlyVector.Y > 0;
            }
        }

        protected virtual void UpdateTurnAnimation()
        {
            string? nowTurnAnim = null;
            if (!CurrentGait.HasBackwardMotion)
            {
                if (AngularVelocity > 0.001)
                {
                    nowTurnAnim = "turn-left";
                }
                else if (AngularVelocity < -0.001)
                {
                    nowTurnAnim = "turn-right";
                }
            }

            if (nowTurnAnim != currentTurnAnimation)
            {
                if (currentTurnAnimation != null)
                {
                    eagent.StopAnimation(currentTurnAnimation);
                }
                if (nowTurnAnim == null)
                {
                    currentTurnAnimation = null;
                }
                else
                {
                    var anim = (CurrentGait.HasMotion == false ? "idle-" : "") + nowTurnAnim;
                    currentTurnAnimation = anim;
                    eagent.StartAnimation(anim);
                }
            }
        }

        public override void GetInfoText(StringBuilder infotext)
        {
            if (entity.Api is ICoreClientAPI capi && capi.Settings.Bool["extendedDebugInfo"])
            {
                infotext.AppendLine("<font color=\"#bbbbbb\">Gait: " + entity.WatchedAttributes.GetString("currentgait") + "</font>");
            }
        }
    }
}
