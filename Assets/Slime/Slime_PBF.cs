using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;

namespace Slime
{
    public class Slime_PBF : MonoBehaviour
    {
        [System.Serializable]
        public enum RenderMode
        {
            Particles, // 直接实例化粒子网格 (更快, 显示内部结构)
            Surface,   // 通过密度场 + Marching Cubes 重建平滑表面
        }
        
        [System.Serializable]
        public enum ColliderSearchMode
        {
            Children,    // 只检测当前对象的子物体 (原来的方式)
            ByTag,       // 通过 Tag 查找整个场景中的碰撞体
            AllInScene,  // 场景中所有的 BoxCollider
        }
        
        private struct SlimeInstance
        {
            public bool Active;        // 是否有效
            public float3 Center;      // 对应连通分量的中心(网格坐标)
            public Vector3 Pos;        // 面部(表情)在表面上的一个投影点 (通过 RayInsectJob 射线检测得出)
            public Vector3 Dir;        // 面部朝向(由控制器速度或指向玩家的方向插和值)
            public float Radius;       // 当前块估计半径 (由包围盒尺寸推导)
            public int ControllerID;   // 关联的粒子控制器索引
        }
        

        [SerializeField, Range(0, 1)] private float bubbleSpeed = 0.2f;          // 气泡上升速度因子
        [SerializeField, Range(0, 100)] private float viscosityStrength = 1.0f; // 粒子粘度强度(越大越稠)
        [SerializeField, Range(0.1f, 100)] private float concentration = 10f;   // 汇聚到控制器中心的浓度(越大越紧)
        [SerializeField, Range(-10, 10)] private float gravity = -5f;           // 重力 (负值向下)
        [SerializeField, Range(0, 5)] private float threshold = 1f;             // Marching Cubes 生成表面的密度阈值
        [SerializeField] private bool useAnisotropic = true;                    // 是否使用各向异性核(使形状更平滑贴合)
        
        [Header("Collider Settings")]
        [SerializeField] private ColliderSearchMode colliderSearchMode = ColliderSearchMode.Children;
        [SerializeField] private string colliderTag = "Ground";                 // 当 ColliderSearchMode 为 ByTag 时使用的标签
        
        [Header("Rendering")]
        [SerializeField] private Mesh faceMesh;      // 小表情面部网格
        [SerializeField] private Material faceMat;   // 表情材质
        [SerializeField] private Material mat;       // 史莱姆表面材质
        [SerializeField] private Mesh particleMesh;  // 粒子表示用的简易网格(如四面体/小方块)
        [SerializeField] private Material particleMat; // 粒子材质(驱动实例化)
        [SerializeField] private Material bubblesMat;  // 气泡材质(单独 buffer)
        
        public Transform trans;        // 玩家或观察点(用于吸引/朝向)
        public RenderMode renderMode = RenderMode.Surface;
        public int blockNum;           // 当前密度场分配的 Block 数量
        public int bubblesNum;         // 活跃气泡数
        public float3 minPos;          // 密度场整体包围盒最小 (世界网格坐标)
        public float3 maxPos;          // 密度场整体包围盒最大

        public bool gridDebug;         // 调试: 画出网格单元
        public bool componentDebug;    // 调试: 连通分量/碰撞盒显示

        #region Buffers
        // 粒子核心数据与临时缓冲
        private NativeArray<Particle> _particles;            // 当前迭代后的粒子位置
        private NativeArray<Particle> _particlesTemp;        // 重建用的平均位置 (ComputeMeanPosJob)
        private NativeArray<float3> _posPredict;             // PBF 预测位置
        private NativeArray<float3> _posOld;                 // 上一帧位置 (用于速度估算)
        private NativeArray<float> _lambdaBuffer;            // PBF 拉格朗日乘子 λ
        private NativeArray<float3> _velocityBuffer;         // 最终速度
        private NativeArray<float3> _velocityTempBuffer;     // 约束迭代中间速度
        private NativeHashMap<int, int2> _lut;               // 网格哈希 -> 粒子排序范围
        private NativeArray<int2> _hashes;                   // 每粒子对应(哈希,原索引)
        private NativeArray<float4x4> _covBuffer;            // 各向异性协方差矩阵 (重建时计算, 着色器使用)
        private NativeArray<MyBoxCollider> _colliderBuffer;  // 场景中 BoxCollider 简化数据
        
        // 密度场网格数据
        private NativeArray<float3> _boundsBuffer;           // 粒子整体包围盒 min/max
        private NativeArray<float> _gridBuffer;              // 密度场主缓冲 (分块, 每块 4x4x4)
        private NativeArray<float> _gridTempBuffer;          // 模糊后的密度场
        private NativeHashMap<int3, int> _gridLut;           // block 坐标 -> gridBuffer 偏移
        private NativeArray<int4> _blockBuffer;              // block 列表 (xyz + color)
        private NativeArray<int> _blockColorBuffer;          // 各颜色段起始索引 (8 色分组)
        
        // 气泡数据 (冒泡特效)
        private NativeArray<Effects.Bubble> _bubblesBuffer;  // 气泡属性 (位置 半径 速度 生命周期)
        private NativeList<int> _bubblesPoolBuffer;          // 气泡对象池 (存放可复用索引)
        
        // 连通分量及控制器
        private NativeList<Effects.Component> _componentsBuffer; // 连通分量数据 (中心/包围盒/Cell 数)
        private NativeArray<int> _gridIDBuffer;                  // 每个网格 cell 的分量 ID
        private NativeList<ParticleController> _controllerBuffer;     // 当前分量对应的控制器
        private NativeList<ParticleController> _lastControllerBuffer; // 上一帧控制器 (用于平滑, 可拓展)
        
        // GPU 侧 ComputeBuffer (用于 DrawMeshInstancedProcedural / Shader)
        private ComputeBuffer _particlePosBuffer;   // 粒子位置/ID
        private ComputeBuffer _particleCovBuffer;   // 协方差矩阵
        private ComputeBuffer _bubblesDataBuffer;   // 气泡数据
        
        #endregion
        
        private float3 _lastMousePos; // (未使用, 可扩展鼠标拖拽)
        private bool _mouseDown;      // (未使用)
        private float3 _velocityY = float3.zero; // 跳跃用临时速度 (已注释逻辑)
        private Bounds _bounds;       // Unity Bounds 用于实例化绘制
        private Vector3 _velocity = Vector3.zero;  // 玩家刚体速度用于影响被控制的那块史莱姆方向/形态

        private LMarchingCubes _marchingCubes; // Marching Cubes 生成网格的工具类
        private Mesh _mesh;                    // 当前帧生成的网格
        
        private int batchCount = 64; // Job 并行批大小
        private bool _connect;       // 输入 P 键后连接所有分量朝向玩家
        private NativeList<SlimeInstance> _slimeInstances; // 每个连通分量对应一个可显示的史莱姆实例(带面部)
        private int _controlledInstance;                   // 当前“被玩家操控”的史莱姆索引
        private Stack<int> _instancePool;                  // SlimeInstance 空闲池 (复用避免扩容)

        void Awake()
        {
            // 关闭垂直同步 & 限制帧率 (保持模拟稳定)
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
        }

        void Start()
        {
            // 调试：确认 GridNum 值
            Debug.Log($"[Slime] GridNum = {PBF_Utils.GridNum}, GridBuffer Size = {PBF_Utils.GridSize * PBF_Utils.GridNum}");
            
            // 初始化粒子布局: 在一个 3D 半体积网格中摆放粒子 (形成初始团块)
            _particles = new NativeArray<Particle>(PBF_Utils.Num, Allocator.Persistent);
            float half = PBF_Utils.Width / 2.0f;
            for (int i = 0; i < PBF_Utils.Width / 2; i++)
            for (int j = 0; j < PBF_Utils.Width; j++)
            for (int k = 0; k < PBF_Utils.Width; k++)
            {
                var idx = i * PBF_Utils.Width * PBF_Utils.Width + j * PBF_Utils.Width + k;
                _particles[idx] = new Particle
                {
                    Position = new float3(k - half, j, i - half) * 0.5f,
                    ID = 0, // 初始控制器 ID 都为 0 (后面根据连通分量重新分配)
                };
            }

            int particleNum = PBF_Utils.Num;
            // 分配所有必须的 NativeArray / List
            _particlesTemp = new NativeArray<Particle>(particleNum, Allocator.Persistent);
            _posPredict = new NativeArray<float3>(particleNum, Allocator.Persistent);
            _posOld = new NativeArray<float3>(particleNum, Allocator.Persistent);
            _lambdaBuffer = new NativeArray<float>(particleNum, Allocator.Persistent);
            _velocityBuffer = new NativeArray<float3>(particleNum, Allocator.Persistent);
            _velocityTempBuffer = new NativeArray<float3>(particleNum, Allocator.Persistent);
            _boundsBuffer = new NativeArray<float3>(2, Allocator.Persistent);
            _gridBuffer = new NativeArray<float>(PBF_Utils.GridSize * PBF_Utils.GridNum, Allocator.Persistent);
            _gridTempBuffer = new NativeArray<float>(PBF_Utils.GridSize * PBF_Utils.GridNum, Allocator.Persistent);
            _gridLut = new NativeHashMap<int3, int>(PBF_Utils.GridNum, Allocator.Persistent);
            _covBuffer = new NativeArray<float4x4>(particleNum, Allocator.Persistent);
            _blockBuffer = new NativeArray<int4>(PBF_Utils.GridNum, Allocator.Persistent);
            _blockColorBuffer = new NativeArray<int>(9, Allocator.Persistent); // 8色 + 末尾 size
            
            // 初始化气泡池
            _bubblesBuffer  = new NativeArray<Effects.Bubble>(PBF_Utils.BubblesCount, Allocator.Persistent);
            _bubblesPoolBuffer = new NativeList<int>(PBF_Utils.BubblesCount, Allocator.Persistent);
            for (int i = 0; i < PBF_Utils.BubblesCount; ++i)
            {
                _bubblesBuffer[i] = new Effects.Bubble()
                {
                    LifeTime = -1, // -1 表示空闲
                };
                _bubblesPoolBuffer.Add(i);
            }

            _lut = new NativeHashMap<int, int2>(particleNum, Allocator.Persistent);
            _hashes = new NativeArray<int2>(particleNum, Allocator.Persistent);
            
            _componentsBuffer = new NativeList<Effects.Component>(16, Allocator.Persistent);
            _gridIDBuffer = new NativeArray<int>(PBF_Utils.GridSize * PBF_Utils.GridNum, Allocator.Persistent);
            _controllerBuffer = new NativeList<ParticleController>(16, Allocator.Persistent);
            // 初始默认一个控制器 (集中所有粒子)
            _controllerBuffer.Add(new ParticleController
            {
                Center = float3.zero,
                Radius = PBF_Utils.InvScale, // 初始半径使用 InvScale 方便归一
                Velocity = float3.zero,
                Concentration = concentration,
            });
            _lastControllerBuffer = new NativeList<ParticleController>(16, Allocator.Persistent);

            _marchingCubes = new LMarchingCubes();

            // 创建 GPU ComputeBuffer 并与材质绑定
            _particlePosBuffer = new ComputeBuffer(particleNum, sizeof(float) * 4); // Position.xyz + ID
            _particleCovBuffer = new ComputeBuffer(particleNum, sizeof(float) * 16); // 4x4 matrix
            _bubblesDataBuffer  = new ComputeBuffer(PBF_Utils.BubblesCount, sizeof(float) * 8); // Bubble 数据打包
            particleMat.SetBuffer("_ParticleBuffer", _particlePosBuffer);
            particleMat.SetBuffer("_CovarianceBuffer", _particleCovBuffer);
            bubblesMat.SetBuffer("_BubblesBuffer", _bubblesDataBuffer);

            // Slime (连通分量对应实例) 初始化
            _slimeInstances = new NativeList<SlimeInstance>(16,  Allocator.Persistent);
            _slimeInstances.Add(new SlimeInstance()
            {
                Center = Vector3.zero,
                Pos = Vector3.zero,
                Dir = Vector3.right,
                Radius = 1
            });
            _instancePool = new Stack<int>();
            
            // 收集场景中的 BoxCollider 转为简化数据用于 Job 碰撞
            CollectColliders();
        }

        private void CollectColliders()
        {
            BoxCollider[] colliders;
            
            switch (colliderSearchMode)
            {
                case ColliderSearchMode.Children:
                    // 原来的方式：只获取当前对象的子物体
                    colliders = GetComponentsInChildren<BoxCollider>();
                    Debug.Log($"[Slime] Collected {colliders.Length} colliders from children");
                    break;
                    
                case ColliderSearchMode.ByTag:
                    // 通过 Tag 查找场景中所有带指定标签的对象的碰撞体
                    var taggedObjects = GameObject.FindGameObjectsWithTag(colliderTag);
                    var colliderList = new System.Collections.Generic.List<BoxCollider>();
                    foreach (var obj in taggedObjects)
                    {
                        var collidersInObject = obj.GetComponentsInChildren<BoxCollider>();
                        colliderList.AddRange(collidersInObject);
                    }
                    colliders = colliderList.ToArray();
                    Debug.Log($"[Slime] Collected {colliders.Length} colliders with tag '{colliderTag}'");
                    break;
                    
                case ColliderSearchMode.AllInScene:
                    // 查找场景中所有的 BoxCollider
                    colliders = FindObjectsByType<BoxCollider>(FindObjectsSortMode.None);
                    Debug.Log($"[Slime] Collected {colliders.Length} colliders from entire scene");
                    break;
                    
                default:
                    colliders = new BoxCollider[0];
                    break;
            }
            
            _colliderBuffer = new NativeArray<MyBoxCollider>(colliders.Length, Allocator.Persistent);
            for (int i = 0; i < colliders.Length; ++i)
            {
                _colliderBuffer[i] = new MyBoxCollider()
                {
                    Center = colliders[i].bounds.center * PBF_Utils.InvScale,
                    Extent = colliders[i].bounds.extents * PBF_Utils.InvScale + Vector3.one, // 略扩一下避免穿透
                };
            }
        }

        private void OnDestroy()
        {
            // 释放所有 Native / ComputeBuffer
            if (_particles.IsCreated) _particles.Dispose();
            if (_particlesTemp.IsCreated) _particlesTemp.Dispose();
            if (_lut.IsCreated) _lut.Dispose();
            if (_hashes.IsCreated) _hashes.Dispose();
            if (_posPredict.IsCreated) _posPredict.Dispose();
            if (_posOld.IsCreated) _posOld.Dispose();
            if (_lambdaBuffer.IsCreated) _lambdaBuffer.Dispose();
            if (_velocityBuffer.IsCreated) _velocityBuffer.Dispose();
            if (_velocityTempBuffer.IsCreated) _velocityTempBuffer.Dispose();
            if (_boundsBuffer.IsCreated) _boundsBuffer.Dispose();
            if (_gridBuffer.IsCreated) _gridBuffer.Dispose();
            if (_gridTempBuffer.IsCreated) _gridTempBuffer.Dispose();
            if (_covBuffer.IsCreated) _covBuffer.Dispose();
            if (_gridLut.IsCreated) _gridLut.Dispose();
            if (_blockBuffer.IsCreated) _blockBuffer.Dispose();
            if (_blockColorBuffer.IsCreated) _blockColorBuffer.Dispose();
            if (_bubblesBuffer.IsCreated) _bubblesBuffer.Dispose();
            if (_bubblesPoolBuffer.IsCreated) _bubblesPoolBuffer.Dispose();
            if (_componentsBuffer.IsCreated) _componentsBuffer.Dispose();
            if (_gridIDBuffer.IsCreated) _gridIDBuffer.Dispose();
            if (_controllerBuffer.IsCreated) _controllerBuffer.Dispose();
            if (_lastControllerBuffer.IsCreated) _lastControllerBuffer.Dispose();
            if (_slimeInstances.IsCreated) _slimeInstances.Dispose();
            if (_colliderBuffer.IsCreated)  _colliderBuffer.Dispose();

            _marchingCubes.Dispose();
            _particlePosBuffer.Release();
            _particleCovBuffer.Release();
            _bubblesDataBuffer.Release();

        }

        void Update()
        {
            HandleMouseInteraction();

            // 渲染模式选择：粒子 或 表面
            if (renderMode == RenderMode.Particles)
            {
                Graphics.DrawMeshInstancedProcedural(particleMesh, 0, particleMat, _bounds, PBF_Utils.Num);
            }
            else if (renderMode == RenderMode.Surface)
            {
                if (_mesh != null)
                {
                    // 使用更明确的参数以确保正确的深度和阴影
                    Graphics.DrawMesh(_mesh, 
                        Matrix4x4.TRS(_bounds.min, Quaternion.identity, Vector3.one), 
                        mat, 
                        0,           // layer: Default
                        null,        // camera: null (所有相机)
                        0,           // submeshIndex
                        null,        // properties
                        UnityEngine.Rendering.ShadowCastingMode.On,    // 投射阴影
                        true);       // 接收阴影
                }

                Graphics.DrawMeshInstancedProcedural(particleMesh, 0, bubblesMat, _bounds, PBF_Utils.BubblesCount);
            }

            // 浓度较高时才绘制"面部"表情 (避免太散的时候乱飞)
            if (concentration > 5)
            {
                foreach (var slime in _slimeInstances)
                {
                    if (!slime.Active) continue;

                    Graphics.DrawMesh(faceMesh, Matrix4x4.TRS(slime.Pos * PBF_Utils.Scale,
                        Quaternion.LookRotation(-slime.Dir),
                        0.2f * math.sqrt(slime.Radius * PBF_Utils.Scale) * Vector3.one), faceMat, 0);
                }
            }
        }

        private void FixedUpdate()
        {
            // 多次迭代提高稳定性 (2 次)
            for (int i = 0; i < 2; i++)
            {
                Profiler.BeginSample("Simulate");
                Simulate(); // 核心 PBF 流体步骤
                Profiler.EndSample();
            }

            Surface();      // 密度场构建 + 模糊 + Marching Cubes + 连通分量
            
            Control();      // 根据连通分量生成控制器并分配 SlimeInstance
            
            Bubbles();      // 气泡生成 + 更新
            
            bubblesNum = PBF_Utils.BubblesCount - _bubblesPoolBuffer.Length; // 统计活跃气泡
            
            if (renderMode == RenderMode.Particles)
            {
                _particlePosBuffer.SetData(_particles); // 仅粒子数据
                particleMat.SetInt("_Aniso", 0);        // 粒子查看时关闭各向异性
            }
            else
                _bubblesDataBuffer.SetData(_bubblesBuffer); // 表面模式补充气泡数据
            
            _bounds = new Bounds()
            {
                min = minPos * PBF_Utils.Scale,
                max = maxPos * PBF_Utils.Scale
            };
        }

        private void Surface()
        {
            Profiler.BeginSample("Render");

            // 1. 计算平均位置(用于协方差估计，更平滑)
            var handle = new Reconstruction.ComputeMeanPosJob
            {
                Lut = _lut,
                Ps = _particles,
                MeanPos = _particlesTemp,
            }.Schedule(_particles.Length, batchCount);

            // 2. 各向异性协方差矩阵 (可选)
            if (useAnisotropic)
            {
                handle = new Reconstruction.ComputeCovarianceJob
                {
                    Lut = _lut,
                    Ps = _particles,
                    MeanPos = _particlesTemp,
                    GMatrix = _covBuffer,
                }.Schedule(_particles.Length, batchCount, handle);
            }

            // 3. 计算整体包围盒
            new Reconstruction.CalcBoundsJob()
            {
                Ps = _particles,
                Bounds = _boundsBuffer,
            }.Schedule(handle).Complete();

            Profiler.EndSample();

            // 根据包围盒确定 Block 范围并清理之前的 Grid LUT
            _gridLut.Clear();
            float blockSize = PBF_Utils.CellSize * 4; // 每 block 覆盖 4x4x4 cells
            minPos = math.floor(_boundsBuffer[0] / blockSize) * blockSize;
            maxPos = math.ceil(_boundsBuffer[1] / blockSize) * blockSize;

            Profiler.BeginSample("Allocate");
            // 4. 清空网格密度
            handle = new Reconstruction.ClearGridJob
            {
                Grid = _gridBuffer,
                GridID = _gridIDBuffer,
            }.Schedule(_gridBuffer.Length, batchCount);

            // 5. 分配需要的 Block (只分配粒子影响范围内的块, 受 GridNum 限制)
            handle = new Reconstruction.AllocateBlockJob()
            {
                Ps = _particlesTemp,
                GridLut = _gridLut,
                MinPos = minPos,
                MaxBlocks = PBF_Utils.GridNum, // 限制最大 block 数量
            }.Schedule(handle);
            handle.Complete();

            var keys = _gridLut.GetKeyArray(Allocator.TempJob);
            blockNum = keys.Length;

            // 6. 给 Block 着色分组 (用于颜色分块并行或 Debug)
            new Reconstruction.ColorBlockJob()
            {
                Keys = keys,
                Blocks = _blockBuffer,
                BlockColors = _blockColorBuffer,
            }.Schedule().Complete();

            Profiler.EndSample();

            Profiler.BeginSample("Splat");

#if USE_SPLAT_SINGLE_THREAD
            // 单线程直接遍历所有粒子投影到密度场
            handle = new Reconstruction.DensityProjectionJob()
            {
                Ps = _particlesTemp,
                GMatrix = _covBuffer,
                Grid = _gridBuffer,
                GridLut = _gridLut,
                MinPos = minPos,
                UseAnisotropic = useAnisotropic,
            }.Schedule();
#elif USE_SPLAT_COLOR8
            // 分 8 个颜色组执行：可减少写冲突
            for (int i = 0; i < 8; i++)
            {
                int2 slice = new int2(_blockColorBuffer[i], _blockColorBuffer[i + 1]);
                int count = slice.y - slice.x;
                handle = new Reconstruction.DensitySplatColoredJob()
                {
                    ParticleLut = _lut,
                    ColorKeys = _blockBuffer.Slice(slice.x, count),
                    Ps = _particlesTemp,
                    GMatrix = _covBuffer,
                    Grid = _gridBuffer,
                    GridLut = _gridLut,
                    MinPos = minPos,
                    UseAnisotropic = useAnisotropic,
                }.Schedule(count, count, handle);
            }
#else
            // 并行按 block 投影 (推荐): 每个 block 独立汇总其 4x4x4 单元的密度
            handle = new Reconstruction.DensityProjectionParallelJob()
            {
                Ps = _particlesTemp,
                GMatrix = _covBuffer,
                GridLut = _gridLut,
                Grid = _gridBuffer,
                ParticleLut = _lut,
                Keys = keys,
                UseAnisotropic = useAnisotropic,
                MinPos = minPos,
            }.Schedule(keys.Length, batchCount);
#endif
            handle.Complete();
            Profiler.EndSample();

            Profiler.BeginSample("Blur");
            // 7. 模糊平滑 (对每 block 的 4x4x4 区域 + 邻域进行 6x6x6 卷积近似)
            new Reconstruction.GridBlurJob()
            {
                Keys = keys,
                GridLut = _gridLut,
                GridRead = _gridBuffer,
                GridWrite = _gridTempBuffer,
            }.Schedule(keys.Length, batchCount, handle).Complete();

            Profiler.EndSample();

            Profiler.BeginSample("Marching cubes");
            // 8. 将模糊后的密度场通过 Marching Cubes 重建 mesh
            _mesh = _marchingCubes.MarchingCubesParallel(keys, _gridLut, _gridTempBuffer, threshold, PBF_Utils.Scale * PBF_Utils.CellSize);
            Profiler.EndSample();
            
            Profiler.BeginSample("CCA");
            // 9. 连通性分析 -> 将密度场拆分为多个分量 (支持分裂与融合)
            _componentsBuffer.Clear();
            handle = new Effects.ConnectComponentBlockJob()
            {
                Keys = keys,
                Grid = _gridBuffer,
                GridLut = _gridLut,
                Components = _componentsBuffer,
                GridID = _gridIDBuffer,
                Threshold = 1e-4f, // 判定是否为空的密度阈值
            }.Schedule();
            
            // 10. 给粒子打上对应分量的 ID (后续吸引力及控制器分配用)
            handle = new Effects.ParticleIDJob()
            {
                GridLut = _gridLut,
                GridID = _gridIDBuffer,
                Particles = _particles,
                MinPos = minPos,
            }.Schedule(_particles.Length, batchCount, handle);
            
            handle.Complete();
            Profiler.EndSample();

            keys.Dispose();
        }

        private void Simulate()
        {
            // PBF 迭代：构建空间哈希 -> 应用外力 -> 约束求解 -> 更新速度
            _lut.Clear();
            new Simulation_PBF.ApplyForceJob
            {
                Ps = _particles,
                Velocity = _velocityBuffer,
                PsNew = _particlesTemp,
                Controllers = _controllerBuffer,
                Gravity = new float3(0, gravity, 0),
            }.Schedule(_particles.Length, batchCount).Complete();

            new Simulation_PBF.HashJob
            {
                Ps = _particlesTemp,
                Hashes = _hashes,
            }.Schedule(_particles.Length, batchCount).Complete();

            _hashes.SortJob(new PBF_Utils.Int2Comparer()).Schedule().Complete();

            new Simulation_PBF.BuildLutJob
            {
                Hashes = _hashes,
                Lut = _lut
            }.Schedule().Complete();

            new Simulation_PBF.ShuffleJob
            {
                Hashes = _hashes,
                PsRaw = _particles,
                PsNew = _particlesTemp,
                Velocity = _velocityBuffer,
                PosOld = _posOld,
                PosPredict = _posPredict,
                VelocityOut = _velocityTempBuffer,
            }.Schedule(_particles.Length, batchCount).Complete();

            new Simulation_PBF.ComputeLambdaJob
            {
                Lut = _lut,
                PosPredict = _posPredict,
                Lambda = _lambdaBuffer,
            }. Schedule(_particles.Length, batchCount).Complete();

            new Simulation_PBF.ComputeDeltaPosJob
            {
                Lut = _lut,
                PosPredict = _posPredict,
                Lambda = _lambdaBuffer,
                PsNew = _particles,
            }.Schedule(_particles.Length, batchCount).Complete();

            new Simulation_PBF.UpdateJob
            {
                Ps = _particles,
                PosOld = _posOld,
                Colliders = _colliderBuffer,
                Velocity = _velocityTempBuffer,
            }.Schedule(_particles.Length, batchCount).Complete();

            new Simulation_PBF.ApplyViscosityJob
            {
                Lut = _lut,
                PosPredict = _posPredict,
                VelocityR = _velocityTempBuffer,
                VelocityW = _velocityBuffer,
                ViscosityStrength = viscosityStrength,
            }.Schedule(_particles.Length, batchCount).Complete();
        }

        private void Control()
        {
            // 根据连通分量生成控制器 (每个史莱姆块一个控制器)
            _controllerBuffer.Clear();

            for (int i = 0; i < _componentsBuffer.Length; i++)
            {
                var component = _componentsBuffer[i];
                float3 extent = component.BoundsMax - component.Center;
                // 估计半径: 用三个轴尺寸之和近似 -> 越大越分散
                float radius = math.max(1, (extent.x + extent.y + extent.z) * PBF_Utils.CellSize * 0.6f);
                float3 center = minPos + component.Center * PBF_Utils.CellSize;
                if (extent.y < 3)
                    center.y += extent.y * PBF_Utils.Scale * PBF_Utils.CellSize; // 提升很扁的块的中心高度避免过低
                float3 toMain = 5 * math.normalize((float3)trans.position * PBF_Utils.InvScale - center);
                _controllerBuffer.Add(new ParticleController()
                {
                    Center = center,
                    Radius = radius,
                    Velocity = _connect ? toMain : float3.zero, // P 键激活后所有块向玩家聚合
                    Concentration = concentration,
                });
            }
            if (_controllerBuffer.Length == 1) _connect = false; // 只有一个分量时自动取消聚合
            
            RearrangeInstances(); // 将控制器映射到 SlimeInstance (面部)
        }

        private void RearrangeInstances()
        {
            // 维护 _slimeInstances 与 _controllerBuffer 的对应关系, 复用或创建实例
            if (_slimeInstances.Length - _instancePool.Count > _controllerBuffer.Length)
            {
                // 情况一: 现有实例多于控制器 -> 回收多余实例
                var used = new NativeArray<bool>(_slimeInstances.Length, Allocator.Temp);
                for (int controllerID = 0; controllerID < _controllerBuffer.Length; controllerID++)
                {
                    var controller = _controllerBuffer[controllerID];
                    var center = controller.Center;
                    int instanceID = -1;
                    float minDst = float.MaxValue;
                    for (int j = 0; j < _slimeInstances.Length; j++)
                    {
                        var slime = _slimeInstances[j];
                        if (used[j] || !slime.Active) continue;
                        var pos = slime.Center;
                        float dst = math.lengthsq(center - pos);
                        if (dst < minDst)
                        {
                            minDst = dst;
                            instanceID = j;
                        }
                    }
                    
                    used[instanceID] = true;
                    UpdateInstanceController(instanceID, controllerID);
                }

                for (int i = 0; i < _slimeInstances.Length; i++)
                {
                    var slime = _slimeInstances[i];
                    if (used[i] || !slime.Active) continue;
                    slime.Active = false;
                    _slimeInstances[i] = slime;
                    _instancePool.Push(i); // 回收到池
                }
                used.Dispose();

                // 若当前被控制的实例失效, 重新选择最近的一个
                if (!_slimeInstances[_controlledInstance].Active)
                {
                    float3 pos = trans.position * PBF_Utils.InvScale;
                    float minDst = float.MaxValue;
                    for (int i = 0; i < _slimeInstances.Length; i++)
                    {
                        var slime = _slimeInstances[i];
                        if (!slime.Active) continue;

                        float dst = math.lengthsq(pos - slime.Center);
                        if (dst < minDst)
                        {
                            minDst = dst;
                            _controlledInstance = i;
                        }
                    }

                    int controllerID = _slimeInstances[_controlledInstance].ControllerID;
                    UpdateInstanceController(_controlledInstance, controllerID);
                }
            }
            else
            {
                // 情况二: 控制器数量多于或等于实例 -> 尝试复用并新增实例
                var used = new NativeArray<bool>(_controllerBuffer.Length, Allocator.Temp);
                for (int instanceID = 0; instanceID < _slimeInstances.Length; instanceID++)
                {
                    var slime = _slimeInstances[instanceID];
                    if (!slime.Active)  continue;
                    var pos = slime.Center;
                    int controllerID = -1;
                    float minDst = float.MaxValue;
                    for (int j = 0; j < _controllerBuffer.Length; j++)
                    {
                        if (used[j]) continue;
                        var cl = _controllerBuffer[j];
                        var center = cl.Center;
                        float dst = math.lengthsq(center - pos);
                        if (dst < minDst)
                        {
                            minDst = dst;
                            controllerID = j;
                        }
                    }
                    used[controllerID] = true;
                    UpdateInstanceController(instanceID, controllerID);
                }
                
                // 为剩余未使用的控制器创建新的 SlimeInstance (面部投影点通过射线找表面)
                for (int i = 0; i < _controllerBuffer.Length; i++)
                {
                    if (used[i]) continue;
                    var controller = _controllerBuffer[i];
                    float3 dir = math.normalizesafe(
                        math.lengthsq(controller.Velocity) < 1e-3f
                            ? (float3)trans.position - controller.Center
                            : controller.Velocity,
                        new float3(1, 0, 0));
                    new Effects.RayInsectJob
                    {
                        GridLut = _gridLut,
                        Grid = _gridBuffer,
                        Result = _boundsBuffer,
                        Threshold = threshold,
                        Pos = controller.Center,
                        Dir = dir,
                        MinPos = minPos,
                    }.Schedule().Complete();
                    
                    float3 newPos = _boundsBuffer[0];
                    if (!math.all(math.isfinite(newPos)))
                        newPos = controller.Center + dir * controller.Radius * 0.5f;
                    
                    SlimeInstance slime = new SlimeInstance()
                    {
                        Active = true,
                        Center =  controller.Center,
                        Radius = controller.Radius,
                        Dir = dir,
                        Pos = newPos,
                        ControllerID = i,
                    };
                    if (_instancePool.Count > 0)
                        _slimeInstances[_instancePool.Pop()] = slime;
                    else
                        _slimeInstances.Add(slime);
                }
                used.Dispose();
            }
        }

        private void UpdateInstanceController(int instanceID, int controllerID)
        {
            // 根据控制器信息更新 SlimeInstance 的位置/朝向/半径，并计算面部投影点
            var slime = _slimeInstances[instanceID];
            var controller = _controllerBuffer[controllerID];
            
            if (instanceID == _controlledInstance)
                controller.Velocity = _velocity * PBF_Utils.InvScale; // 将玩家刚体速度转为控制器速度

            slime.ControllerID = controllerID;
            float speed = 0.1f; // 插值速度(越大越跟手但抖动增加)
            slime.Radius = math.lerp(slime.Radius, controller.Radius, speed);
            slime.Center = math.lerp(slime.Center, controller.Center, speed);
            Vector3 vec = controller.Velocity;
            if (vec.sqrMagnitude > 1e-4f)
            {
                var newDir = Vector3.Slerp(slime.Dir, vec.normalized, speed);
                newDir.y = math.clamp(newDir.y, -0.2f, 0.5f); // 限制竖直方向避免面部翻转过度
                slime.Dir = newDir.normalized;
            }
            else
                slime.Dir = Vector3.Slerp(slime.Dir, new Vector3(slime.Dir.x, 0, slime.Dir.z), speed);
            
            // 使用 RayInsectJob 沿 slime.Dir 从中心向外射线, 找到表面交点作为面部位置
            new Effects.RayInsectJob
            {
                GridLut = _gridLut,
                Grid = _gridBuffer,
                Result = _boundsBuffer,
                Threshold = threshold,
                Pos = controller.Center,
                Dir = slime.Dir,
                MinPos = minPos,
            }.Schedule().Complete();
            
            float3 newPos = _boundsBuffer[0];
            if (math.all(math.isfinite(newPos)))
                slime.Pos = Vector3.Lerp(slime.Pos + vec * PBF_Utils.DeltaTime, newPos, 0.1f);
            else
                slime.Pos = controller.Center;
            
            _slimeInstances[instanceID] = slime;
            
            if (instanceID == _controlledInstance)
            {
                controller.Center = trans.position * PBF_Utils.InvScale; // 主控制块跟随玩家 transform
                _controllerBuffer[controllerID] = controller;
            }
        }

        private void Bubbles()
        {
            // 气泡生成 + 与流体速度耦合 + 生命周期更新回收
            var handle = new Effects.GenerateBubblesJobs()
            {
                GridLut = _gridLut,
                Keys = _blockBuffer,
                Grid = _gridBuffer,
                BubblesStack = _bubblesPoolBuffer,
                BubblesBuffer = _bubblesBuffer,
                Speed = 0.01f * bubbleSpeed,
                Threshold = threshold * 1.2f,
                BlockCount = blockNum,
                MinPos = minPos,
                Seed = (uint)Time.frameCount,
            }.Schedule();

            handle = new Effects.BubblesViscosityJob()
            {
                Lut = _lut,
                Particles = _particles,
                VelocityR = _velocityBuffer,
                BubblesBuffer = _bubblesBuffer,
                Controllers = _controllerBuffer,
                ViscosityStrength = viscosityStrength / 50, // 气泡更轻, 粘度缩小
            }.Schedule(_bubblesBuffer.Length, batchCount, handle);

            handle = new Effects.UpdateBubblesJob()
            {
                GridLut = _gridLut,
                Grid = _gridBuffer,
                BubblesStack = _bubblesPoolBuffer,
                BubblesBuffer = _bubblesBuffer,
                Threshold = threshold * 1.2f,
                MinPos = minPos,
            }.Schedule(handle);
            
            handle.Complete();
        }

        void HandleMouseInteraction()
        {
            // 输入处理: P 键聚合，R 键重新选择控制的史莱姆块
            if (Input.GetKeyDown(KeyCode.P))
                _connect = true;
            
            if (Input.GetKeyDown(KeyCode.R))
            {
                for (int i = 0; i < _slimeInstances.Length; i++)
                {
                    if (!_slimeInstances[i].Active) continue;
                    _controlledInstance = i;
                    trans.position = _slimeInstances[i].Center * PBF_Utils.Scale;
                    break;
                }
            }
            
            // 这里可以扩展 WASD 等位移控制, 当前仅通过 Rigidbody 速度驱动朝向
            _velocity = trans.GetComponent<Rigidbody>().linearVelocity;
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying)
                return;
            
            // 总体包围盒
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(_bounds.center, _bounds.size);
            if (gridDebug)
            {
                // 显示所有 Block 的 4x4x4 区域
                Gizmos.color = Color.blue;
                for (var i = 0; i < blockNum; i++)
                {
                    var block = _blockBuffer[i];
                    Vector3 blockMinPos = new Vector3(block.x, block.y, block.z) * PBF_Utils.CellSize * 0.4f +
                                          _bounds.min;
                    Vector3 size = new Vector3(PBF_Utils.CellSize, PBF_Utils.CellSize, PBF_Utils.CellSize) * 0.4f;
                    Gizmos.DrawWireCube(blockMinPos + size * 0.5f, size);
                }
            }

            if (componentDebug)
            {
                // 连通分量包围盒
                Gizmos.color = Color.green;
                for (var i = 0; i < _componentsBuffer.Length; i++)
                {
                    var c = _componentsBuffer[i];
                    var size = (c.BoundsMax - c.BoundsMin) * PBF_Utils.Scale * PBF_Utils.CellSize;
                    var center = c.Center * PBF_Utils.Scale * PBF_Utils.CellSize;
                    Gizmos.DrawWireCube(_bounds.min + (Vector3)center, size);
                }
                
                // 每个史莱姆实例半径 & 面部位置
                for (var i = 0; i < _slimeInstances.Length; i++)
                {
                    var slime = _slimeInstances[i];
                    if (!slime.Active) continue;
                    Gizmos.DrawWireSphere(slime.Center * PBF_Utils.Scale, slime.Radius * PBF_Utils.Scale);
#if UNITY_EDITOR
                    UnityEditor.Handles.Label(slime.Center * PBF_Utils.Scale, $"id:{i}");
#endif
                    if (_connect)
                        Gizmos.DrawLine(slime.Center * PBF_Utils.Scale + new float3(0, 0.1f, 0), trans.position + new Vector3(0, 0.1f, 0));
                }

                Gizmos.color = Color.cyan;
                // BoxCollider 显示
                for (var i = 0; i < _colliderBuffer.Length; i++)
                {
                    var c = _colliderBuffer[i];
                    Gizmos.DrawWireCube(c.Center * PBF_Utils.Scale, c.Extent * PBF_Utils.Scale * 2);
                }
            }
        }
        
        #region Public API for Particle Control
        
        /// <summary>
        /// 给指定粒子施加外部速度（用于发射）
        /// </summary>
        public void ApplyVelocityToParticles(int[] particleIndices, float3 velocity)
        {
            if (!_velocityBuffer.IsCreated || particleIndices == null)
                return;
                
            foreach (int idx in particleIndices)
            {
                if (idx >= 0 && idx < _velocityBuffer.Length)
                {
                    _velocityBuffer[idx] = velocity;
                }
            }
        }
        
        /// <summary>
        /// 获取距离指定世界位置最近的N个粒子索引
        /// </summary>
        public int[] GetNearestParticles(Vector3 worldPos, int count)
        {
            if (!_particles.IsCreated || count <= 0)
                return new int[0];
                
            float3 pos = (float3)worldPos * PBF_Utils.InvScale;
            
            // 简单距离排序（可以优化为基于网格的查询）
            var distances = new List<(int index, float distSq)>();
            
            for (int i = 0; i < _particles.Length; i++)
            {
                float distSq = math.lengthsq(_particles[i].Position - pos);
                distances.Add((i, distSq));
            }
            
            distances.Sort((a, b) => a.distSq.CompareTo(b.distSq));
            
            int resultCount = Mathf.Min(count, distances.Count);
            int[] result = new int[resultCount];
            for (int i = 0; i < resultCount; i++)
            {
                result[i] = distances[i].index;
            }
            
            return result;
        }
        
        /// <summary>
        /// 获取指定控制器ID内的粒子索引
        /// </summary>
        public int[] GetParticlesInController(int controllerID, int maxCount = -1)
        {
            if (!_particles.IsCreated)
                return new int[0];
                
            var result = new List<int>();
            
            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i].ID == controllerID)
                {
                    result.Add(i);
                    if (maxCount > 0 && result.Count >= maxCount)
                        break;
                }
            }
            
            return result.ToArray();
        }
        
        /// <summary>
        /// 设置粒子的目标控制器ID
        /// </summary>
        public void SetParticleController(int[] particleIndices, int controllerID)
        {
            if (!_particles.IsCreated || particleIndices == null)
                return;
                
            foreach (int idx in particleIndices)
            {
                if (idx >= 0 && idx < _particles.Length)
                {
                    var p = _particles[idx];
                    p.ID = controllerID;
                    _particles[idx] = p;
                }
            }
        }
        
        /// <summary>
        /// 在指定位置创建新的控制器
        /// </summary>
        public int CreateControllerAtPosition(Vector3 worldPos, float radius = 2f)
        {
            float3 pos = (float3)worldPos * PBF_Utils.InvScale;
            
            _controllerBuffer.Add(new ParticleController
            {
                Center = pos,
                Radius = radius,
                Velocity = float3.zero,
                Concentration = concentration,
            });
            
            return _controllerBuffer.Length - 1;
        }
        
        /// <summary>
        /// 在指定位置创建新的控制器（可自定义浓度）
        /// </summary>
        public int CreateControllerAtPosition(Vector3 worldPos, float radius = 2f, float customConcentration = -1f)
        {
            float3 pos = (float3)worldPos * PBF_Utils.InvScale;
            
            _controllerBuffer.Add(new ParticleController
            {
                Center = pos,
                Radius = radius,
                Velocity = float3.zero,
                Concentration = customConcentration < 0 ? concentration : customConcentration,
            });
            
            return _controllerBuffer.Length - 1;
        }
        
        /// <summary>
        /// 获取当前控制的实例ID
        /// </summary>
        public int GetControlledInstanceID()
        {
            return _controlledInstance;
        }
        
        /// <summary>
        /// 切换到指定的史莱姆实例
        /// </summary>
        public void SwitchToInstance(int instanceID)
        {
            if (instanceID >= 0 && instanceID < _slimeInstances.Length && _slimeInstances[instanceID].Active)
            {
                _controlledInstance = instanceID;
                trans.position = _slimeInstances[instanceID].Center * PBF_Utils.Scale;
                
                if (showDebugInfo)
                {
                    Debug.Log($"[Slime_PBF] 切换控制到实例 {instanceID}");
                }
            }
        }
        
        /// <summary>
        /// 统计指定区域内的粒子数量
        /// </summary>
        public int CountParticlesInSphere(Vector3 worldCenter, float worldRadius)
        {
            if (!_particles.IsCreated)
                return 0;
                
            float3 center = (float3)worldCenter * PBF_Utils.InvScale;
            float radius = worldRadius * PBF_Utils.InvScale;
            float radiusSq = radius * radius;
            int count = 0;
            
            for (int i = 0; i < _particles.Length; i++)
            {
                if (math.lengthsq(_particles[i].Position - center) <= radiusSq)
                {
                    count++;
                }
            }
            
            return count;
        }
        
        /// <summary>
        /// 获取所有史莱姆实例的信息（用于选择切换目标）
        /// </summary>
        public (int id, Vector3 position, int particleCount, bool active)[] GetAllSlimeInstances()
        {
            if (!_slimeInstances.IsCreated)
                return new (int, Vector3, int, bool)[0];
                
            var result = new List<(int, Vector3, int, bool)>();
            
            for (int i = 0; i < _slimeInstances.Length; i++)
            {
                var slime = _slimeInstances[i];
                if (!slime.Active)
                    continue;
                    
                // 统计该实例的粒子数
                int particleCount = 0;
                for (int j = 0; j < _particles.Length; j++)
                {
                    if (_particles[j].ID == slime.ControllerID)
                        particleCount++;
                }
                
                result.Add((i, slime.Center * PBF_Utils.Scale, particleCount, slime.Active));
            }
            
            return result.ToArray();
        }
        
        private bool showDebugInfo = false; // 添加调试开关
        
        #endregion
    }
}
