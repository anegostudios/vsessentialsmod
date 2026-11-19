using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

#nullable disable

namespace Vintagestory.GameContent
{
    public struct SelectionBoxConfig
    {
        [DocumentAsJson("Required")]
        public required string Name;

        [DocumentAsJson("Optional", "0")]
        public double MinTraining;

        public bool ValidFor(Entity entity)
        {
            return entity.WatchedAttributes.GetDouble("training") >= MinTraining;
        }
    }

    /// <summary>
    /// Allows to define interactable hitboxes directly from entity shape elements.
    /// <br/>Uses the "selectionboxes" code
    /// </summary>
    /// <example><code lang="json">
    ///"behaviors": [
    /// {
    ///  "code": "selectionboxes",
    ///  "selectionBoxes": ["HeadAP", "FaceAP", "NeckAP", "MidAP", "MidUnderAP", "RearAP", "RearSideAP", "RFrontAP", "LFrontAP", "TempGear1AP", "TempGear2AP", "TempGear3AP"]
    /// },
    ///],
    /// </code></example>
    [DocumentAsJson]
    public class EntityBehaviorSelectionBoxes : EntityBehavior, IRenderer
    {
        protected ICoreClientAPI capi;
        protected Matrixf mvmat = new Matrixf();
        [ThreadStatic]
        protected static Matrixf mvMatInv;
        protected bool debug = false;
        protected bool rendererRegistered=false;

        public AttachmentPointAndPose[] selectionBoxes = Array.Empty<AttachmentPointAndPose>();

        /// <summary>
        /// <!--<jsonalias>SelectionBoxes</jsonalias>-->
        /// A list of attachment points from entity shape used to define hitboxes
        /// </summary>
        [DocumentAsJson("Required")]
        protected SelectionBoxConfig[] selectionBoxConfigs;

        public EntityBehaviorSelectionBoxes(Entity entity) : base(entity) { }
        public double RenderOrder => 1;
        public int RenderRange => 24;
        public void Dispose() { }

        public WireframeCube BoxWireframe;


        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            capi = entity.Api as ICoreClientAPI;
            if (capi != null)
            {
                debug = capi.Settings.Bool["debugEntitySelectionBoxes"];
            }

            setupWireframe();
            entity.trickleDownRayIntersects = true;
            entity.requirePosesOnServer = true;

            JsonObject[] jsonBoxes = attributes["selectionBoxes"].AsArray();
            if (jsonBoxes == null || jsonBoxes.Length == 0)
            {
                entity.World.Logger.Warning(entity.Code + " EntityBehaviorSelectionBoxes, missing selectionBoxes. Will ignore.");
                selectionBoxConfigs = [];
                return;
            }

            selectionBoxConfigs = jsonBoxes.Select(jsonBox => jsonBox.Token is JValue ? new SelectionBoxConfig() { Name = jsonBox.AsString() } : jsonBox.AsObject<SelectionBoxConfig>() ).ToArray();

            entity.WatchedAttributes.RegisterModifiedListener("training", loadSelectionBoxes);
        }

        public override void OnTesselated()
        {
            loadSelectionBoxes();
        }

        protected virtual void loadSelectionBoxes()
        {
            List<AttachmentPointAndPose> list = new List<AttachmentPointAndPose>();

            foreach (var config in selectionBoxConfigs)
            {
                if (!config.ValidFor(entity)) continue;

                var apap = entity.AnimManager?.Animator?.GetAttachmentPointPose(config.Name);
                if (apap == null)
                {
                    entity.World.Logger.VerboseDebug($"EntityBehaviorSelectionBoxes for entity '{entity.Code}', selection box with code '{apap?.AttachPoint?.Code}' defined, but the shape file does not contain such attachment point. Will ignore.");
                    continue;
                }

                var dapap = new AttachmentPointAndPose()
                {
                    AnimModelMatrix = apap.AnimModelMatrix,
                    AttachPoint = apap.AttachPoint,
                    CachedPose = apap.CachedPose
                };

                list.Add(dapap);
            }

            selectionBoxes = list.ToArray();
        }

        protected float accum = 0;
        public override void OnGameTick(float deltaTime)
        {
            if (capi != null && (accum += deltaTime) >= 1)
            {
                accum = 0;
                debug = capi.Settings.Bool["debugEntitySelectionBoxes"];
                setupWireframe();
            }
            base.OnGameTick(deltaTime);
        }

        protected virtual void setupWireframe()
        {
            if (!rendererRegistered)
            {
                if (capi != null)
                {
                    capi.Event.RegisterRenderer(this, EnumRenderStage.AfterOIT, "selectionboxesbhdebug");
                    BoxWireframe = WireframeCube.CreateUnitCube(capi, ColorUtil.WhiteArgb);
                }
                rendererRegistered = true;
            }
        }

        public virtual void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (capi.HideGuis) return;

            int hitindex = getHitIndex();
            if (hitindex < 0 && capi.World.Player.CurrentEntitySelection?.Entity != this.entity) return;
            var eplr = capi.World.Player.Entity;

            if (debug)
            {
                for (int i = 0; i < selectionBoxes.Length; i++)
                {
                    if (hitindex != i) Render(eplr, i, ColorUtil.WhiteArgbVec);
                }

                // Render selected cube last to be on top
                if (hitindex >= 0)
                {
                    Render(eplr, hitindex, new Vec4f(1, 0, 0, 1));
                }
            } else
            {
                if (hitindex >= 0)
                {
                    Render(eplr, hitindex, new Vec4f(0,0,0,0.5f));
                }
            }
        }

        protected virtual void Render(EntityPlayer eplr, int i, Vec4f color)
        {
            var apap = selectionBoxes[i];
            var pos = entity.Pos;

            mvmat.Identity();
            mvmat.Set(capi.Render.CameraMatrixOrigin);

            IMountable ims;
            ims = entity.GetInterface<IMountable>();
            IMountableSeat seat;
            if (ims != null && (seat = ims.GetSeatOfMountedEntity(eplr)) != null)
            {
                var offset = seat.SeatPosition.XYZ - seat.MountSupplier.Position.XYZ;
                mvmat.Translate(-(float)offset.X, -(float)offset.Y, -(float)offset.Z);
            }
            else
            {
                mvmat.Translate(pos.X - eplr.CameraPos.X, pos.InternalY - eplr.CameraPos.Y, pos.Z - eplr.CameraPos.Z);
            }

            applyBoxTransform(mvmat, apap);

            BoxWireframe.Render(capi, mvmat, 1.6f, color);
        }

        protected virtual int getHitIndex()
        {
            var eplr = capi.World.Player.Entity;
            // Lets pretend our 0/0/0 is at the center point of our creatures feet
            // This means our picking ray starts at playerPos + eyeHeight - creaturePos
            var pickingray = Ray.FromAngles(eplr.Pos.XYZ + eplr.LocalEyePos - entity.Pos.XYZ, eplr.Pos.Pitch, eplr.Pos.Yaw, capi.World.Player.WorldData.PickingRange);
            return getHitIndex(pickingray);
        }

        protected virtual void applyBoxTransform(Matrixf mvmat, AttachmentPointAndPose apap)
        {
            var esr = entity.Properties.Client.Renderer as EntityShapeRenderer;

            mvmat.RotateY(GameMath.PIHALF + entity.Pos.Yaw);

            if (esr != null)
            {
                mvmat.Translate(0, entity.SelectionBox.Y2 / 2, 0);
                mvmat.RotateX(esr.xangle);
                mvmat.RotateY(esr.yangle);
                mvmat.RotateZ(esr.zangle);
                mvmat.Translate(0, -entity.SelectionBox.Y2 / 2, 0f);
            }

            mvmat.Translate(0, 0.7f, 0);
            mvmat.RotateX(esr?.nowSwivelRad ?? 0);
            mvmat.Translate(0, -0.7f, 0);

            float s = entity.Properties.Client.Size;
            mvmat.Scale(s, s, s);

            mvmat.Translate(-0.5f, 0, -0.5f);  // Center the box around entity pos (entity pos = center position of the model)

            mvmat.Mul(apap.AnimModelMatrix);

            var selem = apap.AttachPoint.ParentElement;
            float sizex = (float)(selem.To[0] - selem.From[0]) / 16f;
            float sizey = (float)(selem.To[1] - selem.From[1]) / 16f;
            float sizez = (float)(selem.To[2] - selem.From[2]) / 16f;
            mvmat.Scale(sizex, sizey, sizez);
        }

        public override bool IntersectsRay(Ray ray, AABBIntersectionTest intersectionTester, out double intersectionDistance, ref int selectionBoxIndex, ref EnumHandling handled)
        {
            var pickingray = new Ray(ray.origin - entity.Pos.XYZ, ray.dir);

            int index = getHitIndex(pickingray);
            if (index >= 0)
            {
                intersectionDistance = hitPositionAABBSpace.Length();
                intersectionTester.hitPosition = hitPositionAABBSpace.AddCopy(entity.Pos.XYZ);
                selectionBoxIndex = 1 + index;
                handled = EnumHandling.PreventDefault;

                return true;
            }

            intersectionDistance = double.MaxValue;
            return false;
        }


        protected static Cuboidd standardbox = new Cuboidd(0, 0, 0, 1, 1, 1);
        protected Vec3d hitPositionOBBSpace;
        protected Vec3d hitPositionAABBSpace;


        protected virtual int getHitIndex(Ray pickingray)
        {
            int foundIndex = -1;
            double foundDistance = double.MaxValue;

            mvMatInv ??= new Matrixf();
            var rayLengthSq = pickingray.Length * pickingray.Length;
            Vec4d rayOrigin = new Vec4d(pickingray.origin.X, pickingray.origin.Y, pickingray.origin.Z, 1);
            Vec4d rayDirection = new Vec4d(pickingray.dir.X, pickingray.dir.Y, pickingray.dir.Z, 0);
            for (int i = 0; i < selectionBoxes.Length; i++)
            {
                var apap = selectionBoxes[i];

                mvmat.Identity();
                applyBoxTransform(mvmat, apap);
                mvMatInv.Set(mvmat.Values).Invert();

                // Here's the big trick: Instead of transforming the box into world space, we inverse transform the picking ray into the box's local space.
                // This way, we can use a simple AABB intersection test in the box's local space.
                var obbSpaceOrigin = mvMatInv.TransformVector(rayOrigin);
                var obbSpaceDirection = mvMatInv.TransformVector(rayDirection);
                Ray obbSpaceRay = new Ray(obbSpaceOrigin.XYZ, obbSpaceDirection.XYZ);

                if (AABBIntersectionTest.Testintersection(standardbox, obbSpaceRay, ref hitPositionOBBSpace))
                {
                    var tf = mvmat.TransformVector(new Vec4d(hitPositionOBBSpace.X, hitPositionOBBSpace.Y, hitPositionOBBSpace.Z, 1));

                    double dist = (tf.XYZ - pickingray.origin).LengthSq();
                    if (foundIndex >= 0 && foundDistance < dist || rayLengthSq < dist) continue;

                    hitPositionAABBSpace = tf.XYZ;
                    foundDistance = dist;
                    foundIndex = i;
                }
            }

            return foundIndex;
        }

        public virtual Vec3d GetCenterPosOfBox(int selectionBoxIndex)
        {
            if (selectionBoxIndex >= selectionBoxes.Length) return null;
            var apap = selectionBoxes[selectionBoxIndex];
            mvmat.Identity();
            applyBoxTransform(mvmat, apap);

            var pe = apap.AttachPoint.ParentElement;
            Vec4d centerPos = new Vec4d((pe.To[0] - pe.From[0]) / 2/16, (pe.To[1] - pe.From[1]) / 2/16, (pe.To[2] - pe.From[2]) / 2/16, 1);

            Vec3d basePos = entity.Pos.XYZ;

            var eplr = capi.World.Player.Entity;
            if (eplr?.MountedOn?.Entity == entity)
            {
                var mpos = eplr.MountedOn.SeatPosition.XYZ - entity.Pos.XYZ;
                basePos = new Vec3d(eplr.CameraPos.X - mpos.X, eplr.CameraPos.Y - mpos.Y, eplr.CameraPos.Z - mpos.Z);
            }

            return mvmat.TransformVector(centerPos).XYZ.Add(basePos);
        }

        public virtual bool IsAPCode(EntitySelection es, string apcode)
        {
            var ebs = entity.GetBehavior<EntityBehaviorSelectionBoxes>();
            if (ebs != null)
            {
                int seleBox = es?.SelectionBoxIndex - 1 ?? -1;
                if (seleBox >= 0 && ebs.selectionBoxes.Length > seleBox)
                {
                    string apCode = ebs.selectionBoxes[seleBox].AttachPoint.Code;
                    return apCode == apcode;
                }
            }

            return false;
        }

        public override void GetInfoText(StringBuilder infotext)
        {
            int hitindex = getHitIndex();
            if (hitindex >= 0)
            {
                if (capi.Settings.Bool["extendedDebugInfo"]) infotext.AppendLine("<font color=\"#bbbbbb\">looking at AP " + selectionBoxes[hitindex].AttachPoint.Code + "</font>");
                infotext.AppendLine(API.Config.Lang.GetMatching("creature-" + entity.Code.ToShortString() + "-selectionbox-" + selectionBoxes[hitindex].AttachPoint.Code));
            }

            base.GetInfoText(infotext);
        }


        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);

            capi?.Event.UnregisterRenderer(this, EnumRenderStage.AfterOIT);
            BoxWireframe?.Dispose();
        }


        public override string PropertyName() => "selectionboxes";
    }

}
