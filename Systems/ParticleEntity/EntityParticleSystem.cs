using System;
using System.Collections.Generic;
using System.Threading;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EPCounter
    {
        Dictionary<string, int> epcount = new Dictionary<string, int>();

        public Dictionary<string, int> Dict => epcount;

        public int this[string key]
        {
            get
            {
                if (epcount.TryGetValue(key, out int count))
                {
                    return count;
                }
                return 0;
            }
        }

        public void Inc(string key)
        {
            if (epcount.TryGetValue(key, out int count))
            {
                epcount[key] = count + 1;
            } else
            {
                epcount[key] = 1;
            }
        }

        public void Dec(string key)
        {
            if (epcount.TryGetValue(key, out int count))
            {
                epcount[key] = count - 1;
            }
            else
            {
                epcount[key] = -1;
            }
        }

        public void Clear()
        {
            epcount.Clear();
        }
    }

    public class EntityParticleSystem : ModSystem, IRenderer
    {
        protected MeshRef particleModelRef;

        protected MeshData[] updateBuffers; // Multi threaded
        protected Vec3d[] cameraPos;
        protected float[] tickTimes;
        protected float[][] velocities;
        protected int writePosition = 1;
        protected int readPosition = 0;
        protected object advanceCountLock = new object();
        protected int advanceCount = 0;
        protected Random rand = new Random();
        protected ICoreClientAPI capi;
        protected float currentGamespeed;
        protected ParticlePhysics partPhysics;
        protected EnumParticleModel ModelType = EnumParticleModel.Cube;

        int poolSize = 5000;
        int quantityAlive = 0;

        public event Action<float> OnSimTick;

        int offthreadid;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public MeshRef Model
        {
            get { return particleModelRef; }
        }

        public IBlockAccessor BlockAccess => partPhysics.BlockAccess;

        public double RenderOrder => 1;
        public int RenderRange => 50;

        EPCounter counter = new EPCounter();
        public EPCounter Count => counter;



        public virtual MeshData LoadModel()
        {
            float size = 1 / 32f;

            MeshData modeldata = CubeMeshUtil.GetCubeOnlyScaleXyz(size, size, new Vec3f());
            modeldata.WithNormals();
            modeldata.Rgba = null;

            for (int i = 0; i < 4 * 6; i++)
            {
                BlockFacing face = BlockFacing.ALLFACES[i / 4];
                modeldata.AddNormal(face);
            }

            return modeldata;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            this.capi = api;
            capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "reeps-op");

            var thread = TyronThreadPool.CreateDedicatedThread(new ThreadStart(onThreadStart), "entityparticlesim");
            offthreadid = thread.ManagedThreadId;
            capi.Event.LeaveWorld += () => isShuttingDown = true;
            thread.Start();

            partPhysics = new ParticlePhysics(api.World.GetLockFreeBlockAccessor());
            partPhysics.PhysicsTickTime = 0.125f;
            MeshData particleModel = LoadModel();

            // vec3 pos, vec2 size
            particleModel.CustomFloats = new CustomMeshDataPartFloat()
            {
                Instanced = true,
                StaticDraw = false,
                Values = new float[poolSize * (3 + 1)],
                InterleaveSizes = new int[] { 3, 1 },
                InterleaveStride = (3 + 1) * 4,
                InterleaveOffsets = new int[] { 0, 12 },
                Count = poolSize * (3 + 1)
            };

            // vec4 rgba * 2 (each channel = 1 byte), vec4 direction
            particleModel.CustomBytes = new CustomMeshDataPartByte()
            {
                Conversion = DataConversion.NormalizedFloat,
                Instanced = true,
                StaticDraw = false,
                Values = new byte[poolSize * (8 + 4)],
                InterleaveSizes = new int[] { 4, 4, 4 },
                InterleaveStride = 4 + 4 + 4,
                InterleaveOffsets = new int[] { 0, 4, 8 },
                Count = poolSize * (8 + 4)
            };

            particleModel.Flags = new int[poolSize];
            particleModel.FlagsInstanced = true;

            particleModelRef = api.Render.UploadMesh(particleModel);

            updateBuffers = new MeshData[5];
            cameraPos = new Vec3d[5];
            tickTimes = new float[5];
            velocities = new float[5][];

            for (int i = 0; i < 5; i++)
            {
                tickTimes[i] = partPhysics.PhysicsTickTime;
                velocities[i] = new float[3 * poolSize];
                cameraPos[i] = new Vec3d();
                updateBuffers[i] = genUpdateBuffer();
            }
        }

        bool isShuttingDown = false;
        private void onThreadStart()
        {
            while (!isShuttingDown)
            {
                Thread.Sleep(10);

                if (capi.IsGamePaused)
                {
                    continue;
                }

                var cpos = capi.World.Player?.Entity?.CameraPos.Clone();
                if (cpos != null)
                {
                    OnNewFrameOffThread(0.01f, cpos);
                }
            }
        }

        MeshData genUpdateBuffer()
        {
            MeshData updateBuffer = new MeshData();
            updateBuffer.CustomFloats = new CustomMeshDataPartFloat()
            {
                Values = new float[poolSize * (3 + 1)],
                Count = poolSize * (3 + 1),
            };
            updateBuffer.CustomBytes = new CustomMeshDataPartByte()
            {
                Values = new byte[poolSize * 12],
                Count = poolSize * 12,
            };

            updateBuffer.Flags = new int[poolSize];
            updateBuffer.FlagsInstanced = true;
            return updateBuffer;
        }



        public EntityParticle FirstAlive;
        public EntityParticle LastAlive;

        public void SpawnParticle(EntityParticle eparticle)
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != offthreadid) throw new InvalidOperationException("Only in the entityparticle thread");

            eparticle.Prev = null;
            eparticle.Next = null;

            if (FirstAlive == null)
            {
                FirstAlive = eparticle;
                LastAlive = eparticle;
            } else
            {
                eparticle.Prev = LastAlive;
                LastAlive.Next = eparticle;

                LastAlive = eparticle;
            }

            eparticle.OnSpawned(partPhysics);
            counter.Inc(eparticle.Type);
            quantityAlive++;
        }

        protected void KillParticle(EntityParticle entityParticle)
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != offthreadid) throw new InvalidOperationException("Only in the entityparticle thread");

            var prevParticle = entityParticle.Prev;
            var nextParticle = entityParticle.Next;

            if (prevParticle != null) prevParticle.Next = nextParticle;
            if (nextParticle != null) nextParticle.Prev = prevParticle;

            if (FirstAlive == entityParticle) FirstAlive = (EntityParticle)nextParticle;
            if (LastAlive == entityParticle) LastAlive = (EntityParticle)prevParticle ?? FirstAlive;

            entityParticle.Prev = null;
            entityParticle.Next = null;

            quantityAlive--;
            counter.Dec(entityParticle.Type);
        }


        float accumPhysics;


        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            var prog = capi.Shader.GetProgram((int)EnumShaderProgram.Particlescube);
            prog.Use();

            capi.Render.GlToggleBlend(true); // let see what happens if we do this. yolo.

            capi.Render.GlPushMatrix();
            capi.Render.GlLoadMatrix(capi.Render.CameraMatrixOrigin);

            prog.Uniform("rgbaFogIn", capi.Ambient.BlendedFogColor);
            prog.Uniform("rgbaAmbientIn", capi.Ambient.BlendedAmbientColor);
            prog.Uniform("fogMinIn", capi.Ambient.BlendedFogMin);
            prog.Uniform("fogDensityIn", capi.Ambient.BlendedFogDensity);
            prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
            prog.UniformMatrix("modelViewMatrix", capi.Render.CurrentModelviewMatrix);

            OnNewFrame(dt, capi.World.Player.Entity.CameraPos);

            capi.Render.RenderMeshInstanced(Model, quantityAlive);

            prog.Stop();

            capi.Render.GlPopMatrix();
        }

        public void OnNewFrame(float dt, Vec3d cameraPos)
        {
            if (capi.IsGamePaused) return;

            accumPhysics += dt;

            float ticktime = tickTimes[readPosition];

            if (accumPhysics >= ticktime)
            {
                lock (advanceCountLock)
                {
                    // Particle Simulation runs slow, don't advance to next buffer
                    if (advanceCount > 0)
                    {
                        readPosition = (readPosition + 1) % updateBuffers.Length;

                        advanceCount--;

                        accumPhysics -= ticktime;
                        ticktime = tickTimes[readPosition];
                    }
                }

                if (accumPhysics > 1)
                {
                    accumPhysics = 0;
                }
            }

            float step = dt / ticktime;

            MeshData buffer = updateBuffers[readPosition];
            float[] velocity = velocities[readPosition];

            int cnt = quantityAlive = buffer.VerticesCount;

            float camdX = (float)(this.cameraPos[readPosition].X - cameraPos.X);
            float camdY = (float)(this.cameraPos[readPosition].Y - cameraPos.Y);
            float camdZ = (float)(this.cameraPos[readPosition].Z - cameraPos.Z);

            this.cameraPos[readPosition].X -= camdX;
            this.cameraPos[readPosition].Y -= camdY;
            this.cameraPos[readPosition].Z -= camdZ;

            float[] nowFloats = buffer.CustomFloats.Values;


            for (int i = 0; i < cnt; i++)
            {
                int a = i * (3 + 1) + 0;
                nowFloats[a] += camdX + velocity[i * 3 + 0] * step;
                a++;
                nowFloats[a] += camdY + velocity[i * 3 + 1] * step;
                a++;
                nowFloats[a] += camdZ + velocity[i * 3 + 2] * step;
            }

            capi.Render.UpdateMesh(particleModelRef, buffer);
        }


        public void OnNewFrameOffThread(float dt, Vec3d cameraPos)
        {
            if (capi.IsGamePaused) return;

            // Particle Simulation runs too fast, skip a frame
            lock (advanceCountLock)
            {
                if (advanceCount >= updateBuffers.Length - 1)
                {
                    return;
                }
            }

            OnSimTick?.Invoke(dt);

            currentGamespeed = capi.World.Calendar.SpeedOfTime / 60f * 5f;


            ParticleBase particle = FirstAlive;
            ParticleBase nextparticle;
            int posPosition = 0;
            int rgbaPosition = 0;
            int flagPosition = 0;

            MeshData updateBuffer = updateBuffers[writePosition];
            float[] velocity = velocities[writePosition];
            Vec3d curCamPos = this.cameraPos[writePosition].Set(cameraPos);


            partPhysics.PhysicsTickTime = 0.125f / 8f;

            float pdt = Math.Max(partPhysics.PhysicsTickTime, dt);
            float spdt = pdt * currentGamespeed;
            int j = 0;

            while (particle != null)
            {
                double x = particle.Position.X;
                double y = particle.Position.Y;
                double z = particle.Position.Z;

                particle.TickNow(spdt, spdt, capi, partPhysics);

                if (!particle.Alive)
                {
                    nextparticle = particle.Next;
                    KillParticle((EntityParticle)particle);
                    particle = nextparticle;
                    continue;
                }

                velocity[j * 3 + 0] = particle.prevPosDeltaX = (float)(particle.Position.X - x);
                velocity[j * 3 + 1] = particle.prevPosDeltaY = (float)(particle.Position.Y - y);
                velocity[j * 3 + 2] = particle.prevPosDeltaZ = (float)(particle.Position.Z - z);

                j++;
                particle.UpdateBuffers(updateBuffer, curCamPos, ref posPosition, ref rgbaPosition, ref flagPosition);
                particle = particle.Next;
            }

            // Only update as much positions as there are alive particles
            updateBuffer.CustomFloats.Count = j * (3 + 1);
            updateBuffer.CustomBytes.Count = j * (4 + 4 + 4);
            updateBuffer.VerticesCount = j;


            tickTimes[writePosition] = Math.Min(pdt, 1);
            writePosition = (writePosition + 1) % updateBuffers.Length;

            lock (advanceCountLock)
            {
                advanceCount++;
            }
        }

        public override void Dispose()
        {
            particleModelRef.Dispose();
        }

        public void Clear()
        {
            FirstAlive = null;
            LastAlive = null;
            quantityAlive = 0;
            counter.Clear();
        }
    }
}
