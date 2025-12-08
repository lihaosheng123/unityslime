using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Slime
{
    // 粒子核心结构: Position + 所属控制器/分量 ID
    public struct Particle 
    {
        public float3 Position;
        public int ID; // 连通分量或控制器索引
    }
    
    // 控制器: 每一个连通的史莱姆块由一个控制器驱动 (中心 半径 速度 汇聚浓度)
    public struct ParticleController
    {
        public float3 Center;
        public float Radius;
        public float3 Velocity;
        public float Concentration; // 汇聚强度, 控制 ApplyForceJob 吸向中心的力度
    }
    
    // 简化的 BoxCollider 用于 Job 中进行碰撞修正
    public struct MyBoxCollider
    {
        public float3 Center;
        public float3 Extent; // 半尺寸
    }

    public static class PBF_Utils
    {
        // 基础尺寸/数量设置
        public const int Width = 10;                      // 初始体素立方宽度
        public const int Num = Width * Width * Width / 2; // 粒子总数 (占半个体积)
        public const int BubblesCount = 2048;             // 气泡池大小
        public const float PredictStep = 0.02f;           // 半步预测 (粒子位置更新步长)
        public const float DeltaTime = 0.02f;             // 固定时间步 (与 FixedUpdate 匹配)
        public const float TargetDensity = 1.5f;          // PBF 目标密度 (约束求解使用)
        public const int GridSize = 4 * 4 * 4;            // 每 block 内的 cell 数量 (4x4x4)
        public const int GridNum = 2048;                  // 最大 block 数 (容量上限) - 增大以支持分散粒子
            
        public const float h = 1.0f;                      // 核支持半径
        public const float h2 = h * h;                    // h^2 预计算
        public const float CellSize = 0.5f * h;           // 密度网格的 cell 尺寸 (分辨率)
        public const float Mass = 1.0f;                   // 每粒子质量 (可拓展)
        public const float Scale = 0.1f;                  // 世界缩放用于渲染 (把模拟坐标缩小)
        public const float InvScale = 10f;                // Scale 的倒数 (快速转换)
    
        // 排序比较器 (哈希 -> 构建空间查找 LUT)
        public struct Int2Comparer : IComparer<int2>
        {
            public int Compare(int2 lhs, int2 rhs) => lhs.x - rhs.x;
        }
        public struct BlockComparer : IComparer<int4>
        {
            public int Compare(int4 lhs, int4 rhs) => lhs.w - rhs.w;
        }

        // 将 3D 网格坐标编码为整型哈希 (x,y,z 各 10bit)
        public static int GetKey(int3 coord)
        {
            unchecked
            {
                int key = coord.x & 1023;
                key = (key << 10) | (coord.y & 1023);
                key = (key << 10) | (coord.z & 1023);
                return key;
            }
        }

        // 将位置换算成离散网格坐标 (按 h 分辨率)
        public static int3 GetCoord(float3 pos)
        {
            return (int3)math.floor(pos / h);
        }
    
        // Poly6 核常数: 315/(64π h^9) 这里展开成 h2/h 次幂等用于 r2 形式
        private const float KernelPoly6 = 315 / (64 * math.PI * h2 * h2 * h2 * h2 * h);

        // 标准 Poly6 核 (使用 r^2 减少开根)
        public static float SmoothingKernelPoly6(float r2)
        {
            if (r2 < h2)
            {
                float v = h2 - r2;
                return v * v * v * KernelPoly6;
            }
            return 0;
        }
    
        // 可变半径版本 Poly6 (用于一些基于局部半径的扩展)
        public static float SmoothingKernelPoly6(float dst, float radius)
        {
            if (dst < radius)
            {
                float scale = 315 / (64 * math.PI * math.pow(radius, 9));
                float v = radius * radius - dst * dst;
                return v * v * v * scale;
            }
            return 0;
        }
    
        // Spiky 核梯度相关常数 (Pow3 表示 (h-r)^3)
        private const float Spiky3 = 15 / (h2*h2*h2 * math.PI);
        public static float DerivativeSpikyPow3(float r)
        {
            if (r <= h)
            {
                float v = h - r;
                return -v * v * 3 * Spiky3; // 导数近似: -3 (h-r)^2 * 常数
            }
            return 0;
        }
        public static float DerivativeSpikyPow3(float dst, float radius)
        {
            if (dst <= radius)
            {
                float scale = 45 / (math.pow(radius, 6) * math.PI);
                float v = radius - dst;
                return -v * v * scale;
            }
            return 0;
        }
        public static float SpikyKernelPow3(float r)
        {
            if (r < h)
            {
                float v = h - r;
                return v * v * v * Spiky3;
            }
            return 0;
        }
        private static float SpikyKernelPow3(float dst, float radius)
        {
            if (dst < radius)
            {
                float scale = 15 / (math.PI * math.pow(radius, 6));
                float v = radius - dst;
                return v * v * v * scale;
            }
            return 0;
        }
    }

    public static class Simulation_PBF
    {
        // 1. 为每个粒子生成网格哈希 (支持邻域查询)
        [BurstCompile]
        public struct HashJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Particle> Ps;
            [WriteOnly] public NativeArray<int2> Hashes; // (hash, 原索引)

            public void Execute(int i)
            {
                int3 gridPos = PBF_Utils.GetCoord(Ps[i].Position);
                int hash = PBF_Utils.GetKey(gridPos);
                Hashes[i] = math.int2(hash, i);
            }
        }

        // 2. 基于排序后的哈希构建 LUT (key -> [start,end))
        [BurstCompile]
        public struct BuildLutJob : IJob
        {
            [ReadOnly] public NativeArray<int2> Hashes;
            public NativeHashMap<int, int2> Lut;

            public void Execute()
            {
                int currentKey = Hashes[0].x;
                int start = 0;
                for (int i = 1; i < Hashes.Length; ++i)
                {
                    if (Hashes[i].x == currentKey) continue;
                    Lut.TryAdd(currentKey, new int2(start, i)); // [start,i)
                    currentKey = Hashes[i].x;
                    start = i;
                }

                Lut.TryAdd(currentKey, new int2(start, Hashes.Length));
            }
        }

        // 3. 按哈希排序结果重排粒子相关的临时数组 (紧密访问, 利于邻域查询)
        [BurstCompile]
        public struct ShuffleJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int2> Hashes;
            [ReadOnly] public NativeArray<Particle> PsRaw; // 原始粒子
            [ReadOnly] public NativeArray<Particle> PsNew; // 受外力后预测位置
            [ReadOnly] public NativeArray<float3> Velocity; // 原始速度

            [WriteOnly] public NativeArray<float3> PosOld;     // 上一帧位置
            [WriteOnly] public NativeArray<float3> PosPredict; // 当前预测位置
            [WriteOnly] public NativeArray<float3> VelocityOut;// 对应排序后的速度

            public void Execute(int i)
            {
                int id = Hashes[i].y; // 原索引
                PosPredict[i] = PsNew[id].Position;
                PosOld[i] = PsRaw[id].Position;
                VelocityOut[i] = Velocity[id];
            }
        }

        // 4. 外力与控制器吸引: 重力 + 向控制器中心的汇聚 + 控制器速度影响
        [BurstCompile]
        public struct ApplyForceJob : IJobParallelFor
        {
            [ReadOnly] public NativeList<ParticleController> Controllers; // 连通分量控制器
            [ReadOnly] public NativeArray<Particle> Ps;                   // 旧粒子
            [WriteOnly] public NativeArray<Particle> PsNew;               // 输出预测粒子
            public NativeArray<float3> Velocity;                          // 读写速度
            public float3 Gravity;

            public void Execute(int i)
            {
                Particle p = Ps[i];

                var velocity = Velocity[i] * 0.99f + Gravity * PBF_Utils.DeltaTime; // 简单阻尼
                if (p.ID >= 0 && p.ID < Controllers.Length)
                {
                    ParticleController cl = Controllers[p.ID];
                    // 将吸引点略微抬高: 避免所有粒子挤成一层
                    float3 toCenter = cl.Center + new float3(0, cl.Radius * 0.05f, 0) - p.Position;
                    float len = math.length(toCenter);

                    if (len < cl.Radius)
                    {
                        // 在块内部: 速度向控制器速度与原速度插值 + 向中心推
                        velocity = math.lerp(cl.Velocity, velocity, math.lerp(1, len * 0.1f, cl.Concentration * 0.002f));
                        velocity += cl.Concentration * PBF_Utils.DeltaTime * math.min(1, len) *
                                    math.normalizesafe(toCenter);
                    }
                }

                p.Position += velocity * PBF_Utils.PredictStep;
                PsNew[i] = p;
                Velocity[i] = velocity;
            }
        }

        // 5. 计算 λ (密度约束): 根据邻域粒子估计当前密度偏差 c, 求解 λ = -c/(Σ|∇c|^2 + eps)
        [BurstCompile]
        public struct ComputeLambdaJob : IJobParallelFor
        {
            [ReadOnly] public NativeHashMap<int, int2> Lut;
            [ReadOnly] public NativeArray<float3> PosPredict;
            [WriteOnly] public NativeArray<float> Lambda;

            public void Execute(int i)
            {
                float3 pos = PosPredict[i];
                int3 coord = PBF_Utils.GetCoord(pos);
                float rho = 0.0f;
                float3 grad_i = float3.zero; // 粒子 i 的梯度和
                float sigmaGrad = 0.0f;     // 所有邻域梯度平方和
                for (int dz = -1; dz <= 1; ++dz)
                for (int dy = -1; dy <= 1; ++dy)
                for (int dx = -1; dx <= 1; ++dx)
                {
                    int key = PBF_Utils.GetKey(coord + math.int3(dx, dy, dz));
                    if (!Lut.ContainsKey(key)) continue;
                    int2 range = Lut[key];
                    for (int j = range.x; j < range.y; j++)
                    {
                        if (i == j)
                            continue;

                        float3 dir = pos - PosPredict[j];
                        float r2 = math.lengthsq(dir);
                        if (r2 > PBF_Utils.h2) continue;

                        float r = math.sqrt(r2);
                        rho += PBF_Utils.SmoothingKernelPoly6(r2) / PBF_Utils.TargetDensity; // 归一密度贡献
                        float3 grad_j = PBF_Utils.DerivativeSpikyPow3(r) / PBF_Utils.TargetDensity * math.normalize(dir); // 密度梯度
                        sigmaGrad += math.lengthsq(grad_j);
                        grad_i += grad_j;
                    }
                }

                sigmaGrad += math.dot(grad_i, grad_i);
                float c = math.max(-0.2f, rho / PBF_Utils.TargetDensity - 1.0f); // 稳定: 限制最小值
                Lambda[i] = -c / (sigmaGrad + 1e-5f);
            }
        }

        // 6. 根据 λ 计算位置修正 Δp, 并施加张力校正 (Tensile) 防止粒子拉丝
        [BurstCompile]
        public struct ComputeDeltaPosJob : IJobParallelFor
        {
            [ReadOnly] public NativeHashMap<int, int2> Lut;
            [ReadOnly] public NativeArray<float3> PosPredict;
            [ReadOnly] public NativeArray<float> Lambda;
            [WriteOnly] public NativeArray<Particle> PsNew;
            private const float TensileDq = 0.25f * PBF_Utils.h; // 张力参考距离
            private const float TensileK = 0.1f;                 // 张力强度

            public void Execute(int i)
            {
                float3 position = PosPredict[i];
                float3 dp = float3.zero; // 修正累计
                float W_dp = PBF_Utils.SmoothingKernelPoly6(TensileDq * TensileDq);

                float lambda = Lambda[i];
                int3 coord = PBF_Utils.GetCoord(position);
                for (int dz = -1; dz <= 1; ++dz)
                for (int dy = -1; dy <= 1; ++dy)
                for (int dx = -1; dx <= 1; ++dx)
                {
                    int key = PBF_Utils.GetKey(coord + math.int3(dx, dy, dz));
                    if (!Lut.ContainsKey(key)) continue;
                    int2 range = Lut[key];
                    for (int j = range.x; j < range.y; j++)
                    {
                        if (i == j)
                            continue;

                        float3 dir = position - PosPredict[j];
                        float r2 = math.dot(dir, dir);
                        if (r2 >= PBF_Utils.h2) continue;

                        float r = math.sqrt(r2);
                        float3 w_spiky = PBF_Utils.SpikyKernelPow3(r) * math.normalize(dir); // 梯度近似
                        float corr = PBF_Utils.SmoothingKernelPoly6(r2) / W_dp;              // 张力校正因子
                        float s_corr = -TensileK * corr * corr * corr * corr;                // 4 次幂防止过强
                        dp += (lambda + Lambda[j] + s_corr) * w_spiky;                        // λ_i + λ_j + 张力
                    }
                }

                dp /= PBF_Utils.TargetDensity;

                PsNew[i] = new Particle
                {
                    Position = position - dp, // 写入修正后的位置
                    ID = 0,                    // 分量 ID 后续会重新写 (ParticleIDJob)
                };
            }
        }

        // 7. 更新粒子速度 + 简单碰撞 (地面 + BoxCollider)
        public struct UpdateJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<MyBoxCollider> Colliders;
            [ReadOnly] public NativeArray<float3> PosOld; // 旧位置
            [WriteOnly] public NativeArray<float3> Velocity; // 输出速度
            public NativeArray<Particle> Ps; // 可写粒子位置

            public void Execute(int i)
            {
                Particle p = Ps[i];

                // 地面约束: 不低于 y=1
                p.Position.y = math.max(1f, p.Position.y);
                foreach (var box in Colliders)
                {
                    float3 dir = p.Position - box.Center;
                    float3 vec = math.abs(dir);
                    if (math.all(vec < box.Extent))
                    {
                        // 在碰撞盒内: 推向最近的面
                        float3 remain = box.Extent - vec;
                        bool3 pushAxis = new bool3(false, false, false);
                        int axis = 0;
                        if (remain.y < remain[axis]) axis = 1;
                        if (remain.z < remain[axis]) axis = 2;
                        pushAxis[axis] = true;
                        p.Position = math.select(p.Position, box.Center + math.sign(dir) * box.Extent, pushAxis);
                    }
                }
                float3 vel = (p.Position - PosOld[i]) / PBF_Utils.DeltaTime; // 后向差分速度
                Velocity[i] = math.min(30, math.length(vel)) * math.normalizesafe(vel); // 限制最大速度
                Ps[i] = p;
            }
        }

        // 8. 最后应用粘度: 邻域速度差加权平滑 (拉近彼此速度)
        [BurstCompile]
        public struct ApplyViscosityJob : IJobParallelFor
        {
            [ReadOnly] public NativeHashMap<int, int2> Lut;
            [ReadOnly] public NativeArray<float3> PosPredict;
            [ReadOnly] public NativeArray<float3> VelocityR; // 参考速度 (更新前)
            [WriteOnly] public NativeArray<float3> VelocityW; // 写入新速度
            public float ViscosityStrength;

            public void Execute(int i)
            {
                float3 pos = PosPredict[i];
                int3 coord = PBF_Utils.GetCoord(pos);
                float3 viscosityForce = float3.zero;
                float3 vel = VelocityR[i];
                for (int dz = -1; dz <= 1; ++dz)
                for (int dy = -1; dy <= 1; ++dy)
                for (int dx = -1; dx <= 1; ++dx)
                {
                    int key = PBF_Utils.GetKey(coord + math.int3(dx, dy, dz));
                    if (!Lut.ContainsKey(key)) continue;
                    int2 range = Lut[key];
                    for (int j = range.x; j < range.y; j++)
                    {
                        if (i == j)
                            continue;

                        float3 dir = pos - PosPredict[j];
                        float r2 = math.lengthsq(dir);
                        if (r2 > PBF_Utils.h2) continue;

                        viscosityForce += (VelocityR[j] - vel) * PBF_Utils.SmoothingKernelPoly6(r2);
                    }
                }

                VelocityW[i] = vel + viscosityForce / PBF_Utils.TargetDensity * ViscosityStrength * PBF_Utils.DeltaTime;
            }
        }
    }
}