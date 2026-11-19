using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent;


public class SystemCinematicCamera : ModSystem, IRenderer
{
    protected ICoreClientAPI capi;
    protected List<CameraPoint> cameraPoints;
    
    protected double previousPositionX;
    protected double previousPositionY;
    protected double previousPositionZ;
    protected double previousOrientationX;
    protected double previousOrientationY;
    protected double previousOrientationZ;
    protected double totalDistance;
    protected double totalTime;
    protected double currentLengthTraveled = 0;
    protected int currentPoint = 0;
    protected int currentLoop = 0;
    protected bool cameraLive;
    protected bool teleportBack = false;
    protected int quantityLoops = 0;
    protected bool closedPath;
    protected double alpha = 0.1;
    protected IAviWriter avi;
    protected double writeAccum;
    protected bool firstFrameDone;
    protected bool shouldDisableGui = true;

    // Camera path variables.
    protected BlockPos origin;
    protected EnumAutoCamAngleMode camAngleMode = EnumAutoCamAngleMode.Point;
    protected string videoFileName = "";
    protected readonly string prevPathsFile = Path.Combine(GamePaths.Cache, "campaths.txt");
    protected readonly CameraKeyframe[] keyframesToSample = new CameraKeyframe[SegmentSampleRate];
    protected readonly CameraKeyframe[] currentSegmentKeyframes = new CameraKeyframe[60];

    public const int SegmentSampleRate = 60;

    protected CameraPathRenderer cpRenderer;

    bool prevNoclip, prevFly;
    EnumGameMode prevMode;

    public SystemCinematicCamera()
    {
        cameraPoints = new List<CameraPoint>();
    }
    

    public override void StartClientSide(ICoreClientAPI api)
    {
        this.capi = api;
        cpRenderer = new CameraPathRenderer(this, capi, cameraPoints);

        CommandArgumentParsers parsers = api.ChatCommands.Parsers;
        api.ChatCommands
            .Create("cam")
            .WithPreCondition(CheckPrecondition)
            .BeginSubCommand("tp")
                .WithDescription("Whether to teleport the player back to where he was previously (default on)")
                .WithArgs(parsers.OptionalBool("teleportBack"))
                .HandleWith(OnCmdTp)
            .EndSubCommand()
            .BeginSubCommand("gui")
                .WithDescription("If set, will hide all guis during while the cam is on (default on)")
                .WithArgs(parsers.OptionalBool("shouldDisableGui"))
                .HandleWith(OnCmdGui)
            .EndSubCommand()
            .BeginSubCommand("loop")
                .WithDescription("If set then path be repeatedly played")
                .WithArgs(parsers.OptionalInt("quantityLoops", 9999999))
                .HandleWith(OnCmdLoop)
            .EndSubCommand()
            .BeginSubCommand("p")
                .WithDescription("Add a point to path")
                .HandleWith(OnCmdAddPoint)
            .EndSubCommand()
            .BeginSubCommand("up")
                .WithDescription("Update nth point")
                .WithArgs(parsers.OptionalInt("point_index"))
                .HandleWith(OnCmdUp)
            .EndSubCommand()
            .BeginSubCommand("goto")
                .WithDescription("Teleport to nth point")
                .WithArgs(parsers.OptionalInt("point_index"))
                .HandleWith(OnCmdGoto)
            .EndSubCommand()
            .BeginSubCommand("cp")
                .WithDescription("Close path")
                .HandleWith(OnCmdCp)
            .EndSubCommand()
            .BeginSubCommand("rp")
                .WithDescription("Remove the last point from the path")
                .HandleWith(OnCmdRemovePoint)
            .EndSubCommand()
            .BeginSubCommand("play").WithAlias("start").WithAlias("rec")
            .WithDescription("Play/start path or play and record to .avi file using rec command")
                .WithArgs(parsers.OptionalDouble("totalTime", 10))
                .HandleWith(OnCmdPlay)
            .EndSubCommand()
            .BeginSubCommand("stop")
                .WithDescription("Stop playing and recording")
                .HandleWith(OnCmdStop)
            .EndSubCommand()
            .BeginSubCommand("clear")
                .WithDescription("Remove all points from path")
                .HandleWith(OnCmdClear)
            .EndSubCommand()
            .BeginSubCommand("save")
                .WithDescription("Copy path points to clipboard")
                .HandleWith(OnCmdSave)
            .EndSubCommand()
            .BeginSubCommand("load")
                .WithDescription("Load point to the path, seperated by ,")
                .WithArgs(parsers.Word("points"))
                .HandleWith(OnCmdLoad)
            .EndSubCommand()
            .BeginSubCommand("loadold")
                .WithDescription("Load point to the path from a pre 1.20 point list, seperated by ,")
                .WithArgs(parsers.Word("points"))
                .HandleWith(OnCmdLoadOld)
            .EndSubCommand()
            .BeginSubCommand("alpha")
                .WithDescription("Set/Show alpha")
                .WithArgs(parsers.OptionalFloat("alpha"))
                .HandleWith(OnCmdAlpha)
            .EndSubCommand()
            .BeginSubCommand("angles")
                .WithDescription("Set camera angle mode [point, direction]")
                .WithArgs(parsers.OptionalWord("mode"))
                .HandleWith(OnCmdAngles)
            .EndSubCommand();

        
        api.Event.RegisterRenderer(this, EnumRenderStage.Done, "cinecam-done"/*, 0.98*/);
    }

    protected TextCommandResult CheckPrecondition(TextCommandCallingArgs args)
    {
        IPlayer plr = args.Caller.Player;
        return plr == null || (plr.WorldData.CurrentGameMode != EnumGameMode.Creative && plr.WorldData.CurrentGameMode != EnumGameMode.Spectator)
            ? TextCommandResult.Error("Only available in creative or spectator mode")
            : TextCommandResult.Success();
    }

    protected TextCommandResult OnCmdAngles(TextCommandCallingArgs args)
    {
        camAngleMode = args.Parsers[0].IsMissing || (args[0] as string) == "point" ? EnumAutoCamAngleMode.Point : EnumAutoCamAngleMode.Direction;
        return TextCommandResult.Success("Camera angle mode is now " + camAngleMode);
    }

    protected TextCommandResult OnCmdAlpha(TextCommandCallingArgs args)
    {
        if (args.Parsers[0].IsMissing)
        {
            return TextCommandResult.Success("Current alpha is " + alpha);
        }

        alpha = (float)args[0];

        cpRenderer.UpdateModel(origin);
        return TextCommandResult.Success("Alpha set to " + alpha);
    }

    protected TextCommandResult OnCmdStop(TextCommandCallingArgs args)
    {
        StopAutoCamera();
        return TextCommandResult.Success();
    }

    protected TextCommandResult OnCmdPlay(TextCommandCallingArgs args)
    {
        if (cameraPoints.Count < 2)
        {
            capi.ShowChatMessage("Need at least 2 points!");
        }
        else
        {
            StartAutoCamera(args);
        }
        return TextCommandResult.Success();
    }

    protected TextCommandResult OnCmdLoop(TextCommandCallingArgs args)
    {
        quantityLoops = (int)args[0];
        return TextCommandResult.Success("Will loop " + quantityLoops + " times.");
    }

    protected TextCommandResult OnCmdGui(TextCommandCallingArgs args)
    {
        shouldDisableGui = (bool)args[0];
        return TextCommandResult.Success("Disable guis now " + (shouldDisableGui ? "on" : "off"));
    }

    protected TextCommandResult OnCmdTp(TextCommandCallingArgs args)
    {
        teleportBack = (bool)args[0];
        return TextCommandResult.Success("Teleport back to previous position now " + (teleportBack ? "on" : "off"));
    }


    protected TextCommandResult OnCmdLoad(TextCommandCallingArgs args)
    {
        int n = Load(args);
        return TextCommandResult.Success(string.Format("Camera points loaded: {0}", "" + n));
    }

    protected TextCommandResult OnCmdLoadOld(TextCommandCallingArgs args)
    {
        int n = Load(args, GameMath.PIHALF);
        return TextCommandResult.Success(string.Format("Camera points loaded: {0}", "" + n));
    }

    protected int Load(TextCommandCallingArgs args, float yawOffset = 0)
    {
        string pointsJoined = args[0] as string;
        if (pointsJoined.Length == 0) return 0;
            
        int n = 0;
        if (pointsJoined[0] == '1')
        {
            n = loadLegacyPoints(yawOffset, pointsJoined);
        } else
        {
            var points = JsonUtil.FromString<CameraPath>(pointsJoined).Points;
            cameraPoints.Clear();
            cameraPoints.AddRange(points);

            if (cameraPoints.Count > 0)
            {
                origin = new BlockPos((int)points[0].x, (int)points[0].y, (int)points[0].z);

                foreach (var point in cameraPoints)
                {
                    if (point.fov <= 0) point.fov = capi.Render.MainCamera.Fov;
                }
            }
            n = cameraPoints.Count;
        }

        cpRenderer.UpdateModel(origin);
        return n;
    }

    private int loadLegacyPoints(float yawOffset, string pointsJoined)
    {
        string[] points = pointsJoined.Split(',');
        int n = (points.Length - 1) / 6;

        origin = capi.World.Player.Entity.Pos.AsBlockPos;

        for (int i = 0; i < n; i++)
        {
            CameraPoint point = new()
            {
                x = int.Parse(points[1 + (i * 6) + 0]) / 100f,
                y = int.Parse(points[1 + (i * 6) + 1]) / 100f,
                z = int.Parse(points[1 + (i * 6) + 2]) / 100f,
                pitch = int.Parse(points[1 + (i * 6) + 3]) / 1000f,
                yaw = (int.Parse(points[1 + (i * 6) + 4]) / 1000f) + yawOffset,
                roll = int.Parse(points[1 + (i * 6) + 5]) / 1000f,
                fov = capi.Render.MainCamera.Fov
            };

            if (cameraPoints.Count - 1 >= 0 && cameraPoints[cameraPoints.Count - 1].PositionEquals(point))
            {
                point.x += 0.001f;
            }

            cameraPoints.Add(point);
        }

        return n;
    }

    protected TextCommandResult OnCmdSave(TextCommandCallingArgs args)
    {
        string s = PointsToString();
        capi.Forms.SetClipboardText(s);
        return TextCommandResult.Success("Camera points copied to clipboard.");
    }

    protected string PointsToString()
    {
        return JsonUtil.ToString(new CameraPath() { Points = cameraPoints.Take(cameraPoints.Count).ToArray() });
    }


    protected TextCommandResult OnCmdAddPoint(TextCommandCallingArgs args)
    {
        if (cameraPoints.Count == 0)
        {
            origin = capi.World.Player.Entity.Pos.AsBlockPos;
        }

        CameraPoint point = CameraPoint.FromEntityPos(capi.World.Player.Entity.Pos, capi.Render.MainCamera.Fov);

        if (cameraPoints.Count - 1 >= 0 && cameraPoints[cameraPoints.Count - 1].PositionEquals(point))
        {
            point.x += 0.001f;
        }

        cameraPoints.Add(point);

        if (cameraPoints.Count > 1)
        {
            FixYaw(cameraPoints.Count);
        }

        closedPath = false;
        cpRenderer.UpdateModel(origin);
        return TextCommandResult.Success("Point added");
    }

    protected TextCommandResult OnCmdUp(TextCommandCallingArgs args)
    {
        int num = (int)args[0];
        if (num < 0 || num >= cameraPoints.Count)
        {
            return TextCommandResult.Success("Your supplied number is above the point count or negative");
        }

        CameraPoint point = CameraPoint.FromEntityPos(capi.World.Player.Entity.Pos, capi.Render.MainCamera.Fov);

        cameraPoints[num] = point;

        if (cameraPoints.Count > 1)
        {
            FixYaw(cameraPoints.Count);
        }

        cpRenderer.UpdateModel(origin);
        return TextCommandResult.Success("Point updated");
    }

    protected TextCommandResult OnCmdGoto(TextCommandCallingArgs args)
    {
        int num = (int)args[0];
        if (num < 0 || num >= cameraPoints.Count)
        {
            return TextCommandResult.Success("Your supplied number is above the point count or negative");
        }

        CameraPoint p = cameraPoints[num];

        var eplr = capi.World.Player.Entity;

        eplr.Pos.X = p.x;
        eplr.Pos.Y = p.y;
        eplr.Pos.Z = p.z;
        capi.Input.MouseYaw = eplr.Pos.Yaw = p.yaw;
        capi.Input.MousePitch = eplr.Pos.Pitch = p.pitch;
        eplr.Pos.Roll = p.roll;

        capi.Render.MainCamera.Fov = p.fov;
        capi.Render.Reset3DProjection();

        return TextCommandResult.Success();
    }

    protected TextCommandResult OnCmdCp(TextCommandCallingArgs args)
    {
        if (cameraPoints.Count <= 1)
        {
            return TextCommandResult.Success("Requires at least 2 points");
        }

        cameraPoints.Add(cameraPoints[0].Clone());
        FixYaw(cameraPoints.Count);
        capi.ShowChatMessage("Path Closed");
        closedPath = true;
        cpRenderer.UpdateModel(origin);
        return TextCommandResult.Success();
    }

    protected void FixYaw(int pos)
    {
        double prevYaw = cameraPoints[pos - 2].yaw;
        double yaw = cameraPoints[pos - 1].yaw;

        double dist = yaw - prevYaw;

        if (dist > Math.PI)
        {
            cameraPoints[pos - 1].yaw -= GameMath.TWOPI;
        }
        if (dist < -Math.PI)
        {
            cameraPoints[pos - 1].yaw += GameMath.TWOPI;
        }
    }

    protected TextCommandResult OnCmdRemovePoint(TextCommandCallingArgs args)
    {
        if (cameraPoints.Count == 0)
        {
            return TextCommandResult.Error("No points to remove");
        }

        cameraPoints.RemoveAt(cameraPoints.Count - 1);
        closedPath = false;

        cpRenderer.UpdateModel(origin);
        return TextCommandResult.Success("Point removed");
    }

    protected TextCommandResult OnCmdClear(TextCommandCallingArgs args)
    {
        closedPath = false;
        cameraPoints.Clear();
        StopAutoCamera();
        cpRenderer.InitModel();
        return TextCommandResult.Success("Camera points cleared.");
    }

    protected void StartAutoCamera(TextCommandCallingArgs args)
    {
        if (cameraPoints.Count == 0)
        {
            capi.ShowChatMessage("No points defined. Enter points with \".cam p\" Command.");
            return;
        }

        var plr = capi.World.Player;
        prevMode = plr.WorldData.CurrentGameMode;
        prevNoclip = plr.WorldData.NoClip;
        prevFly = plr.WorldData.FreeMove;

        plr.WorldData.CurrentGameMode = EnumGameMode.Spectator;
        plr.WorldData.NoClip = true;
        plr.WorldData.FreeMove = true;


        currentPoint = 0;
        totalDistance = CalculateApproximateDistances();

        if (args.SubCmdCode == "rec")
        {
            string path = capi.Settings.String["videoFileTarget"] ?? GamePaths.Videos;

            try
            {
                if (new DirectoryInfo(path).Parent != null && !Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                avi = capi.Render.CreateAviWriter(capi.Settings.Float["recordingFrameRate"], capi.Settings.String["recordingCodec"]);

                string time = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";

                avi.Open(Path.Combine(path, videoFileName = $"{time}.avi"), capi.Render.FrameWidth, capi.Render.FrameHeight);
            }
            catch (Exception e)
            {
                capi.ShowChatMessage("Could not start recording: " + e.Message);
                return;
            }

            if (capi.Settings.Float["gameTickFrameRate"] > 0)
            {
                capi.DeltaTimeLimiter = 1f / capi.Settings.Float["gameTickFrameRate"];
            }
        }

        totalTime = (double)args[0];

        firstFrameDone = false;
        currentPoint = 0;
        currentLoop = 0;
        currentLengthTraveled = 0;

        EntityPos pos = capi.World.Player.Entity.Pos;

        previousPositionX = pos.X;
        previousPositionY = pos.Y;
        previousPositionZ = pos.Z;
        previousOrientationX = pos.Pitch;
        previousOrientationY = pos.Yaw;
        previousOrientationZ = pos.Roll;

        if (shouldDisableGui)
        {
            capi.HideGuis = true;
        }

        capi.Render.AllowCameraControl = false;
        capi.Render.MainCamera.UpdateCameraPos = false;

        PrecalcCurrentPoint();
        SetupCameraKeyframesForCurrentPoint();

        cameraLive = true;
    }

    protected void StopAutoCamera()
    {
        if (shouldDisableGui)
        {
            capi.HideGuis = false;
        }

        capi.Render.AllowCameraControl = true;
        capi.Render.MainCamera.UpdateCameraPos = true;

        if (cameraLive)
        {
            var plr = capi.World.Player;
            plr.WorldData.CurrentGameMode = prevMode;
            plr.WorldData.NoClip = prevNoclip;
            plr.WorldData.FreeMove = prevFly;

            if (teleportBack)
            {
                EntityPos pos = capi.World.Player.Entity.Pos;
                pos.X = previousPositionX;
                pos.Y = previousPositionY;
                pos.Z = previousPositionZ;
                pos.Pitch = (float)previousOrientationX;
                pos.Yaw = (float)previousOrientationY;
                pos.Roll = (float)previousOrientationZ;
            }
        }

        if (avi != null)
        {
            avi.Close();
            avi = null;

            string path = capi.Settings.String["videoFileTarget"] ?? GamePaths.Videos;
            capi.ShowChatMessage(videoFileName + " saved to " + path);
        }

        cameraLive = false;

        capi.DeltaTimeLimiter = -1;

        // This triggers the fov changed watcher, which will reset the fov to the previous value
        capi.Render.MainCamera.Fov = capi.Settings.Int["fieldOfView"] * GameMath.DEG2RAD;
        capi.Settings.Int["fieldOfView"] = capi.Settings.Int["fieldOfView"];
    }


    // OnFinalizeFrame
    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (!cameraLive) return;
        if (capi.IsGamePaused) return;

        UpdateAvi(dt);

        if (currentPoint == cameraPoints.Count - 1)
        {
            StopAutoCamera();
            File.AppendAllText(prevPathsFile, PointsToString() + "\r\n");
            capi.ShowChatMessage("Camera path saved to Cache folder, in case you lose it");
            return;
        }

        double travelSpeed = totalDistance / totalTime;
        currentLengthTraveled += travelSpeed * dt;
        double percentTraveled = currentLengthTraveled / points[1].distance;

        if (percentTraveled > 1)
        {
            double rollOverDistance = currentLengthTraveled - points[1].distance;
            currentPoint++;

            if (currentPoint != cameraPoints.Count - 1 || currentLoop < quantityLoops)
            {
                if (currentLoop < quantityLoops && currentPoint == cameraPoints.Count - 1)
                {
                    currentPoint = 0;
                    currentLoop++;
                }

                PrecalcCurrentPoint();
                SetupCameraKeyframesForCurrentPoint();

                currentLengthTraveled = rollOverDistance;
                percentTraveled = currentLengthTraveled / points[1].distance;
            }
        }

        AdvanceTo(Math.Min(1, percentTraveled));
    }

    protected void SetupCameraKeyframesForCurrentPoint()
    {
        for (int i = 0; i < SegmentSampleRate; i++)
        {
            double dt = i / ((float)SegmentSampleRate - 1);

            double x = GameMath.CPCatmullRomSplineLerp(tStart + (dt * (tEnd - tStart)), pointsX, time);
            double y = GameMath.CPCatmullRomSplineLerp(tStart + (dt * (tEnd - tStart)), pointsY, time);
            double z = GameMath.CPCatmullRomSplineLerp(tStart + (dt * (tEnd - tStart)), pointsZ, time);

            double pitch = GameMath.CPCatmullRomSplineLerp(tStart + (dt * (tEnd - tStart)), pointsPitch, time);
            double yaw = GameMath.CPCatmullRomSplineLerp(tStart + (dt * (tEnd - tStart)), pointsYaw, time);
            double roll = GameMath.CPCatmullRomSplineLerp(tStart + (dt * (tEnd - tStart)), pointsRoll, time);
            double fov = GameMath.CPCatmullRomSplineLerp(tStart + (dt * (tEnd - tStart)), pointsFov, time);

            keyframesToSample[i] = new CameraKeyframe()
            {
                X = x,
                Y = y,
                Z = z,
                Pitch = pitch,
                Yaw = yaw,
                Roll = roll,
                FovRad = fov
            };
        }

        CameraKeyframe[] currentKeyframes = currentSegmentKeyframes;
        currentKeyframes[0] = keyframesToSample[0];
        currentKeyframes[SegmentSampleRate - 1] = keyframesToSample[^1];

        // Convert keyframesToSample into evenly spaced keyframes in currentKeyFrames.
        double segmentLength = 0;
        for (int i = 1; i < keyframesToSample.Length; i++)
        {
            CameraKeyframe prev = keyframesToSample[i - 1];
            CameraKeyframe curr = keyframesToSample[i];
            double dx = curr.X - prev.X;
            double dy = curr.Y - prev.Y;
            double dz = curr.Z - prev.Z;
            segmentLength += Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }
        double distancePerKeyframe = segmentLength / (SegmentSampleRate - 1);
        double accumulatedDistance = 0;
        int currentKeyframeIndex = 1;

        for (int i = 1; i < keyframesToSample.Length; i++)
        {
            CameraKeyframe prev = keyframesToSample[i - 1];
            CameraKeyframe curr = keyframesToSample[i];
            double dx = curr.X - prev.X;
            double dy = curr.Y - prev.Y;
            double dz = curr.Z - prev.Z;
            double dist = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            accumulatedDistance += dist;
            while (accumulatedDistance >= distancePerKeyframe && currentKeyframeIndex < SegmentSampleRate)
            {
                double overshoot = accumulatedDistance - distancePerKeyframe;
                double ratio = (dist - overshoot) / dist;
                currentSegmentKeyframes[currentKeyframeIndex] = new CameraKeyframe()
                {
                    X = prev.X + (dx * ratio),
                    Y = prev.Y + (dy * ratio),
                    Z = prev.Z + (dz * ratio),
                    Pitch = prev.Pitch + ((curr.Pitch - prev.Pitch) * ratio),
                    Yaw = prev.Yaw + ((curr.Yaw - prev.Yaw) * ratio),
                    Roll = prev.Roll + ((curr.Roll - prev.Roll) * ratio),
                    FovRad = prev.FovRad + ((curr.FovRad - prev.FovRad) * ratio)
                };
                currentKeyframeIndex++;
                accumulatedDistance -= distancePerKeyframe;
            }
        }
    }

    public void AdvanceTo(double percent)
    {
        int keyframeIndex = (int)(percent * (SegmentSampleRate - 1));

        EntityPos pos = capi.World.Player.Entity.Pos;
        Vec3d camPos = capi.World.Player.Entity.CameraPos;

        CameraKeyframe outKeyframe;

        if (keyframeIndex >= SegmentSampleRate - 1)
        {
            outKeyframe = currentSegmentKeyframes[SegmentSampleRate - 1];
        }
        else
        {
            CameraKeyframe current = currentSegmentKeyframes[keyframeIndex];
            CameraKeyframe next = currentSegmentKeyframes[keyframeIndex + 1];
            outKeyframe = new()
            {
                X = current.X + ((next.X - current.X) * ((percent * (SegmentSampleRate - 1)) - keyframeIndex)),
                Y = current.Y + ((next.Y - current.Y) * ((percent * (SegmentSampleRate - 1)) - keyframeIndex)),
                Z = current.Z + ((next.Z - current.Z) * ((percent * (SegmentSampleRate - 1)) - keyframeIndex)),
                Pitch = current.Pitch + ((next.Pitch - current.Pitch) * ((percent * (SegmentSampleRate - 1)) - keyframeIndex)),
                Yaw = current.Yaw + ((next.Yaw - current.Yaw) * ((percent * (SegmentSampleRate - 1)) - keyframeIndex)),
                Roll = current.Roll + ((next.Roll - current.Roll) * ((percent * (SegmentSampleRate - 1)) - keyframeIndex)),
                FovRad = current.FovRad + ((next.FovRad - current.FovRad) * ((percent * (SegmentSampleRate - 1)) - keyframeIndex))
            };
        }

        double prevX = pos.X;
        double prevY = pos.Y;
        double prevZ = pos.Z;

        camPos.X = pos.X = outKeyframe.X;
        camPos.Y = pos.Y = outKeyframe.Y;
        camPos.Z = pos.Z = outKeyframe.Z;
        camPos.Add(
            capi.Render.MainCamera.CameraShakeStrength * GameMath.Cos(capi.Render.MainCamera.deltaSum * 100) / 10.0,
            -capi.Render.MainCamera.CameraShakeStrength * GameMath.Cos(capi.Render.MainCamera.deltaSum * 100) / 10.0,
            capi.Render.MainCamera.CameraShakeStrength * GameMath.Sin(capi.Render.MainCamera.deltaSum * 100) / 10.0
        );
        capi.Render.MainCamera.Fov = (float)outKeyframe.FovRad;

        if (camAngleMode == EnumAutoCamAngleMode.Point)
        {
            capi.Input.MousePitch = pos.Pitch = (float)outKeyframe.Pitch;
            capi.Input.MouseYaw = pos.Yaw = (float)outKeyframe.Yaw;
            pos.Roll = (float)outKeyframe.Roll;            
        }
        else
        {
            double dx = pos.X - prevX;
            double dy = pos.Y - prevY;
            double dz = pos.Z - prevZ;
            double length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            if (length > 0)
            {
                double np = GameMath.Asin(dy / length);
                double ny = Math.Atan2(dx, dz) + GameMath.PIHALF; ;

                pos.Pitch += (float)Math.Min(0.03, np - (pos.Pitch % GameMath.TWOPI));
                pos.Yaw += (float)Math.Min(0.03, ny - (pos.Yaw % GameMath.TWOPI));
            }
        }
    }


    protected readonly double[] time = new double[4];
    protected readonly CameraPoint[] points = new CameraPoint[4];
    protected readonly double[] pointsX = new double[4];
    protected readonly double[] pointsY = new double[4];
    protected readonly double[] pointsZ = new double[4];
    protected readonly double[] pointsPitch = new double[4];
    protected readonly double[] pointsYaw = new double[4];
    protected readonly double[] pointsRoll = new double[4];
    protected readonly double[] pointsFov = new double[4];
    protected double tStart, tEnd;

    public double RenderOrder => 0.98;

    public int RenderRange => -1;

    public FastVec3d[] GenerateCameraPath()
    {
        if (cameraPoints.Count <= 1) return [];

        FastVec3d[] path = new FastVec3d[(cameraPoints.Count-1) * 31];

        for (currentPoint = 0; currentPoint < cameraPoints.Count - 1; currentPoint++)
        {
            PrecalcCurrentPoint();

            for (int i = 0; i <= 30; i++)
            {
                double dt = i / 30f;

                double x = GameMath.CPCatmullRomSplineLerp(tStart + (dt * (tEnd - tStart)), pointsX, time);
                double y = GameMath.CPCatmullRomSplineLerp(tStart + (dt * (tEnd - tStart)), pointsY, time);
                double z = GameMath.CPCatmullRomSplineLerp(tStart + (dt * (tEnd - tStart)), pointsZ, time);

                path[currentPoint * 31 + i] = new FastVec3d(x, y, z);
            }
        }

        return path;
    }

    protected void PrecalcCurrentPoint()
    {
        points[1] = cameraPoints[currentPoint];
        points[2] = cameraPoints[currentPoint + 1];

        // Extrapolate point before start if at start
        points[0] = currentPoint > 0
            ? cameraPoints[currentPoint - 1]
            : closedPath ? cameraPoints[cameraPoints.Count - 2] : points[1].ExtrapolateFrom(points[2], 1);

        // Extrapolate point after end if at end
        points[3] = currentPoint < cameraPoints.Count - 2
            ? cameraPoints[currentPoint + 2]
            : closedPath ? cameraPoints[1] : points[2].ExtrapolateFrom(points[1], 1);

        time[0] = 0;
        time[1] = 1;
        time[2] = 2;
        time[3] = 3;

        // Alpha calculation
        double total = 0;
        for (int i = 1; i < 4; i++)
        {
            double dx = points[i].x - points[i - 1].x;
            double dy = points[i].y - points[i - 1].y;
            double dz = points[i].z - points[i - 1].z;

            double dt = Math.Pow((dx * dx) + (dy * dy) + (dz * dz), alpha);
            total += dt;

            time[i] = total;
        }

        // Easier access for splineLerp 
        for (int i = 0; i < 4; i++)
        {
            pointsX[i] = points[i].x;
            pointsY[i] = points[i].y;
            pointsZ[i] = points[i].z;
            pointsPitch[i] = points[i].pitch;
            pointsYaw[i] = points[i].yaw;
            pointsRoll[i] = points[i].roll;
            pointsFov[i] = points[i].fov;
        }

        tStart = time[1];
        tEnd = time[2];
    }

    protected void UpdateAvi(float dt)
    {
        if (avi == null)
        {
            return;
        }

        if (!firstFrameDone)
        {
            // Skip first frame, because screen is not redrawn yet.
            firstFrameDone = true;
            return;
        }

        writeAccum += dt;

        float frameRate = capi.Settings.Float["recordingFrameRate"];

        if (writeAccum >= 1f / frameRate)
        {
            writeAccum -= 1f / frameRate;
            avi.AddFrame();
        }
    }

    protected double CalculateApproximateDistances()
    {
        double totalDistance = 0;
        Vec3d cur = new();
        Vec3d prev = new();

        int currentPointBefore = currentPoint;

        for (currentPoint = 0; currentPoint < cameraPoints.Count - 1; currentPoint++)
        {
            PrecalcCurrentPoint();

            CameraPoint point = cameraPoints[currentPoint];

            prev.X = point.x;
            prev.Y = point.y;
            prev.Z = point.z;

            double distance = 0;

            for (int i = 0; i < 60; i++)
            {
                double dt = i / 60f;

                cur.X = GameMath.CPCatmullRomSplineLerp(tStart + (dt * (tEnd - tStart)), pointsX, time);
                cur.Y = GameMath.CPCatmullRomSplineLerp(tStart + (dt * (tEnd - tStart)), pointsY, time);
                cur.Z = GameMath.CPCatmullRomSplineLerp(tStart + (dt * (tEnd - tStart)), pointsZ, time);

                distance += Math.Sqrt(cur.SquareDistanceTo(prev));

                prev.X = cur.X;
                prev.Y = cur.Y;
                prev.Z = cur.Z;
            }

            point.distance = distance;

            totalDistance += point.distance;
        }

        currentPoint = currentPointBefore;

        return totalDistance;
    }
}

