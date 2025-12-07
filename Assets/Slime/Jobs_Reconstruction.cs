using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Slime
{
    public class Reconstruction
    {
        [BurstCompile]
        public struct CalcBoundsJob : IJob
        {
            [ReadOnly] public NativeArray<Particle> Ps; // 粒子数组
            [WriteOnly] public NativeArray<float3> Bounds; // 输出: Bounds[0]=min, Bounds[1]=max

            public void Execute()
            {
                // 计算所有粒子位置的包围盒 (忽略 index0 也可, 当前从1开始略微减少极值问题)
                float3 min = new float3(float.MaxValue);
                float3 max = new float3(float.MinValue);
                for (int i = 1; i < Ps.Length; ++i)
                {
                    min = math.min(min, Ps[i].Position);
                    max = math.max(max, Ps[i].Position);
                }

                Bounds[0] = min;
                Bounds[1] = max;
            }
        }

        [BurstCompile]
        public struct ComputeMeanPosJob : IJobParallelFor
        {
            [ReadOnly] public NativeHashMap<int, int2> Lut; // 网格哈希 -> 粒子范围
            [ReadOnly] public NativeArray<Particle> Ps;      // 原始粒子

            [WriteOnly] public NativeArray<Particle> MeanPos; // 输出: 平滑后位置 (存回同结构)

            public void Execute(int i)
            {
                // 对粒子位置做局部加权平均 (核密度加权) 得到平滑中心, 用于后续协方差计算更稳定
                Particle p = Ps[i];
                float3 pos = p.Position;
                int3 coord = PBF_Utils.GetCoord(pos);
                float rho = 0.0f;      // 权重累加 (近似密度)
                float3 posSum = float3.zero; // 加权位置和
                for (int dz = -1; dz <= 1; ++dz)
                for (int dy = -1; dy <= 1; ++dy)
                for (int dx = -1; dx <= 1; ++dx)
                {
                    int key = PBF_Utils.GetKey(coord + math.int3(dx, dy, dz));
                    if (!Lut.ContainsKey(key)) continue;
                    int2 range = Lut[key];
                    for (int j = range.x; j < range.y; j++)
                    {
                        float3 neighborPos = Ps[j].Position;
                        float3 dir = pos - neighborPos;
                        float r2 = math.lengthsq(dir);
                        if (r2 > PBF_Utils.h2) continue;

                        float w = PBF_Utils.SmoothingKernelPoly6(r2); // Poly6 核权重
                        rho += w;
                        posSum += neighborPos * w;
                    }
                }

                p.Position = rho > 1e-5f ? posSum / rho : pos; // 避免除0
                MeanPos[i] = p;
            }
        }

        [BurstCompile]
        public struct ComputeCovarianceJob : IJobParallelFor
        {
            [ReadOnly] public NativeHashMap<int, int2> Lut;    // 网格索引
            [ReadOnly] public NativeArray<Particle> Ps;        // 原始粒子位置
            [ReadOnly] public NativeArray<Particle> MeanPos;   // 平滑中心

            [WriteOnly] public NativeArray<float4x4> GMatrix;  // 输出协方差矩阵(用于各向异性核)

            public void Execute(int i)
            {
                float3 pos = Ps[i].Position;
                int3 coord = PBF_Utils.GetCoord(pos);

                float3 meanPos = MeanPos[i].Position;
                float rho = 0.0f;
                float3x3 cov = float3x3.zero; // 协方差累加 (未归一)

                for (int dz = -1; dz <= 1; ++dz)
                for (int dy = -1; dy <= 1; ++dy)
                for (int dx = -1; dx <= 1; ++dx)
                {
                    int key = PBF_Utils.GetKey(coord + math.int3(dx, dy, dz));
                    if (!Lut.ContainsKey(key)) continue;
                    int2 range = Lut[key];
                    for (int j = range.x; j < range.y; j++)
                    {
                        float3 neighborPos = Ps[j].Position;
                        float3 dir = neighborPos - meanPos; // 去平均
                        float r2 = math.lengthsq(dir);
                        if (r2 > PBF_Utils.h2) continue;

                        float w = PBF_Utils.SmoothingKernelPoly6(r2);
                        cov += OutDot(dir) * w;
                        rho += w;
                    }
                }

                // 归一化 & 单位尺度化
                cov = rho > 1e-5f ? cov / rho : float3x3.identity;
                cov /= (cov.c0.x + cov.c1.y + cov.c2.z) / 3.0f;

                // 特征分解 (Jacobi) -> 将协方差拉伸/压缩到限制范围, 避免过度扁平/奇异
                Eigen.EVD_Jacobi(cov, out float3 lambda, out float3x3 V);
                float3 lambdaClamped = 1.0f / math.max(lambda, 0.1f); // 防止特征值过小导致爆炸
                cov = math.mul(V,
                    math.mul(new float3x3(lambdaClamped.x, 0, 0, 0, lambdaClamped.y, 0, 0, 0, lambdaClamped.z),
                        math.transpose(V)));
                cov /= (cov.c0.x + cov.c1.y + cov.c2.z) / 3.0f; // 再次归一化主对角线平均

                // 写入 4x4 (最后一行一列用于齐次兼容/着色器方便取)
                GMatrix[i] = new float4x4(
                    cov.c0.x, cov.c0.y, cov.c0.z, 0,
                    cov.c1.x, cov.c1.y, cov.c1.z, 0,
                    cov.c2.x, cov.c2.y, cov.c2.z, 0,
                    0, 0, 0, 1
                );
            }

            private float3x3 OutDot(float3 a)
            {
                // 外积 a * a^T
                return new float3x3(
                    a.x * a.x, a.x * a.y, a.x * a.z,
                    a.y * a.x, a.y * a.y, a.y * a.z,
                    a.z * a.x, a.z * a.y, a.z * a.z
                );
            }
        }

        [BurstCompile]
        public struct ClearGridJob : IJobParallelFor
        {
            [WriteOnly] public NativeArray<float> Grid;  // 密度值
            [WriteOnly] public NativeArray<int> GridID;  // 连通分量 ID 初始化

            public void Execute(int i)
            {
                Grid[i] = 0;
                GridID[i] = -1; // -1 表示未访问/空
            }
        }

        [BurstCompile]
        public struct AllocateBlockJob : IJob
        {
            [ReadOnly] public NativeArray<Particle> Ps; // 平滑后粒子 (MeanPos)
            public NativeHashMap<int3, int> GridLut;    // block坐标 -> 偏移
            public float3 MinPos;                       // 当前整体 min (世界网格坐标)
            public int MaxBlocks;                       // 最大 block 数量限制

            public void Execute()
            {
                // 遍历粒子，找到影响范围内的 block (扩展 2 cell) 并分配网格偏移
                int ptr = 0;
                foreach (var p in Ps)
                {
                    // 如果已达上限, 停止分配
                    if (ptr >= MaxBlocks)
                        break;
                    
                    int3 coord = (int3)math.floor((p.Position - MinPos) / PBF_Utils.CellSize);
                    int3 blockMin = (coord - 2) >> 2; // 以 4 为单位的 Block 范围
                    int3 blockMax = (coord + 2) >> 2;
                    for (int bz = blockMin.z; bz <= blockMax.z; ++bz)
                    for (int by = blockMin.y; by <= blockMax.y; ++by)
                    for (int bx = blockMin.x; bx <= blockMax.x; ++bx)
                    {
                        if (ptr >= MaxBlocks)
                            break;
                        
                        int3 key = new int3(bx, by, bz);
                        if (GridLut.ContainsKey(key)) continue;
                        var offset = ptr * PBF_Utils.GridSize; // 每 block 分配 4x4x4 = 64 单元
                        GridLut.TryAdd(key, offset);
                        ptr++;
                    }
                }
            }
        }

        [BurstCompile]
        public struct ColorBlockJob : IJob
        {
            [ReadOnly] public NativeArray<int3> Keys; // 所有 block 坐标
            public NativeArray<int4> Blocks;          // 输出: xyz + color(0..7)
            [WriteOnly] public NativeArray<int> BlockColors; // 每种颜色起始索引, 第8位置为总数

            public void Execute()
            {
                // 给 block 分配 3bit 颜色(奇偶性)用于后续可能的分组并行或调试彩色显示
                int blockNum = Keys.Length;
                for (int i = 0; i < blockNum; i++)
                {
                    int3 key = Keys[i];
                    int color = (key.x & 1) | (key.y & 1) << 1 | (key.z & 1) << 2; // 立方体坐标奇偶模式
                    Blocks[i] = new int4(key, color);
                }

                Blocks.Slice(0, blockNum).Sort(new PBF_Utils.BlockComparer());
                int cur = -1;
                for (int i = 0; i < blockNum; i++)
                {
                    int color = Blocks[i].w;
                    if (color == cur) continue;
                    BlockColors[color] = i; // 记录每种颜色第一出现的位置
                    cur = color;
                }

                BlockColors[8] = blockNum; // 末尾记录总数方便切片
            }
        }

        [BurstCompile]
        public struct DensitySplatColoredJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Particle> Ps; // 平滑后粒子
            [ReadOnly] public NativeArray<float4x4> GMatrix; // 各向异性矩阵
            [ReadOnly] public NativeHashMap<int3, int> GridLut; // block -> 偏移
            [ReadOnly] public NativeHashMap<int, int2> ParticleLut; // 网格哈希 -> 粒子范围
            [ReadOnly] public NativeSlice<int4> ColorKeys; // 当前颜色的 block 切片
            [NativeDisableParallelForRestriction] public NativeArray<float> Grid; // 写入密度
            public bool UseAnisotropic;
            public float3 MinPos;

            public void Execute(int i)
            {
                // 针对同颜色的 block 并行; 每次构建一个临时 8x8x8 的大块, 最后再写回真正的 4x4x4 子块
                int3 block = ColorKeys[i].xyz;
                int3 basePos = PBF_Utils.GetCoord(MinPos);
                int3 blockMin = (block * 2) + basePos; // 查找粒子用的较大范围
                int3 blockMax = (block * 2 + 1) + basePos;
                var tempBlock = new NativeArray<float>(8 * 8 * 8, Allocator.Temp); // 先在更大局部缓存
                int3 tempBlockMin = block * 4 - 2; // 对应实际坐标

                for (int z = blockMin.z; z <= blockMax.z; ++z)
                for (int y = blockMin.y; y <= blockMax.y; ++y)
                for (int x = blockMin.x; x <= blockMax.x; ++x)
                {
                    int coordIdx = PBF_Utils.GetKey(new int3(x, y, z));
                    if (!ParticleLut.ContainsKey(coordIdx))
                        continue;
                    int2 range = ParticleLut[coordIdx];
                    for (int j = range.x; j < range.y; j++)
                    {
                        Particle p = Ps[j];
                        float3 relativePos = p.Position - MinPos;
                        int3 centerCoord = (int3)math.floor(relativePos / PBF_Utils.CellSize);
                        for (int dz = -2; dz <= 2; ++dz)
                        for (int dy = -2; dy <= 2; ++dy)
                        for (int dx = -2; dx <= 2; ++dx)
                        {
                            int3 coord = centerCoord + new int3(dx, dy, dz);
                            if (math.any(coord - tempBlockMin < 0) || math.any(coord - tempBlockMin >= 8))
                                continue; // 超出临时缓存

                            float3 cellCenter = (0.5f + (float3)coord) * PBF_Utils.CellSize;
                            float3 dir = cellCenter - relativePos;

                            if (UseAnisotropic)
                                dir = math.mul((float3x3)GMatrix[j], dir); // 各向异性变换

                            float r2 = math.lengthsq(dir);
                            if (r2 > PBF_Utils.h2) continue;

                            tempBlock[GetTempIndex(coord - tempBlockMin)] += PBF_Utils.SmoothingKernelPoly6(r2);
                        }
                    }
                }

                // 将 8x8x8 压缩写入实际 4x4x4 Block (对应多个子块映射)
                for (int gz = 0; gz < 4; ++gz)
                for (int gy = 0; gy < 4; ++gy)
                for (int gx = 0; gx < 4; ++gx)
                {
                    int3 key = (tempBlockMin + new int3(gx * 2, gy * 2, gz * 2)) >> 2;
                    if (!GridLut.ContainsKey(key))
                        continue;
                    var offset = GridLut[key];

                    for (int lz = 0; lz < 2; ++lz)
                    for (int ly = 0; ly < 2; ++ly)
                    for (int lx = 0; lx < 2; ++lx)
                    {
                        int3 localCoord = new int3(gx * 2 + lx, gy * 2 + ly, gz * 2 + lz);
                        int3 coord = tempBlockMin + localCoord - key * 4;
                        Grid[offset + GetLocalIndex(coord)] += tempBlock[GetTempIndex(localCoord)];
                    }
                }

                tempBlock.Dispose();
            }

            private int GetLocalIndex(int3 coord)
            {
                return (coord.x & 3) + 4 * ((coord.y & 3) + 4 * (coord.z & 3)); // 4x4x4 压缩索引
            }

            private int GetTempIndex(int3 coord)
            {
                return coord.x + 8 * (coord.y + 8 * coord.z); // 8x8x8 索引
            }
        }

        [BurstCompile]
        public struct DensityProjectionJob : IJob
        {
            [ReadOnly] public NativeArray<Particle> Ps;
            [ReadOnly] public NativeArray<float4x4> GMatrix;
            [ReadOnly] public NativeHashMap<int3, int> GridLut;
            public NativeArray<float> Grid;
            public bool UseAnisotropic;
            public float3 MinPos;

            public void Execute()
            {
                // 简单单线程方式: 遍历所有粒子并对其影响的局部 5x5x5 cell 写密度
                for (int i = 0; i < Ps.Length; ++i)
                {
                    Particle p = Ps[i];
                    float3 relativePos = p.Position - MinPos;
                    int3 centerCoord = (int3)math.floor(relativePos / PBF_Utils.CellSize);
                    for (int dz = -2; dz <= 2; ++dz)
                    for (int dy = -2; dy <= 2; ++dy)
                    for (int dx = -2; dx <= 2; ++dx)
                    {
                        int3 coord = centerCoord + new int3(dx, dy, dz);

                        int3 key = coord / 4; // 找对应 block
                        var offset = GridLut[key];

                        float3 cellCenter = (0.5f + (float3)coord) * PBF_Utils.CellSize;
                        float3 dir = cellCenter - relativePos;

                        if (UseAnisotropic)
                            dir = math.mul((float3x3)GMatrix[i], dir);

                        float r2 = math.lengthsq(dir);
                        if (r2 > PBF_Utils.h2) continue;

                        float density = PBF_Utils.SmoothingKernelPoly6(r2);
                        Grid[offset + GetLocalIndex(coord)] += density;
                    }
                }
            }

            private int GetLocalIndex(int3 coord)
            {
                return (coord.x & 3) + 4 * ((coord.y & 3) + 4 * (coord.z & 3));
            }
        }

        [BurstCompile]
        public struct DensityProjectionParallelJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Particle> Ps;
            [ReadOnly] public NativeArray<float4x4> GMatrix;
            [ReadOnly] public NativeHashMap<int3, int> GridLut;
            [ReadOnly] public NativeHashMap<int, int2> ParticleLut;
            [ReadOnly] public NativeSlice<int3> Keys; // block 列表

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<float> Grid;

            public bool UseAnisotropic;
            public float3 MinPos;

            public void Execute(int i)
            {
                // 并行按 block 做局部汇总: 每个 block 独立构建一个临时 4x4x4 缓存, 避免原子写冲突
                int3 block = Keys[i];
                int3 basePos = PBF_Utils.GetCoord(MinPos);
                int3 blockMin = (block * 2) + basePos; // 查找粒子范围缩小计算量

                var blockTemp = new NativeArray<float>(PBF_Utils.GridSize, Allocator.Temp);

                var offset = GridLut[block];
                for (int z = -1; z < 3; ++z)
                for (int y = -1; y < 3; ++y)
                for (int x = -1; x < 3; ++x)
                {
                    int key = PBF_Utils.GetKey(blockMin + math.int3(x, y, z));
                    if (!ParticleLut.ContainsKey(key))
                        continue;
                    int2 range = ParticleLut[key];
                    for (int j = range.x; j < range.y; j++)
                    {
                        Particle p = Ps[j];
                        float3 relativePos = p.Position - MinPos;
                        for (int gz = math.max(0, z * 2 - 2); gz < math.min(4, z * 2 + 4); ++gz)
                        for (int gy = math.max(0, y * 2 - 2); gy < math.min(4, y * 2 + 4); ++gy)
                        for (int gx = math.max(0, x * 2 - 2); gx < math.min(4, x * 2 + 4); ++gx)
                        {
                            int3 coord = (block << 2) + new int3(gx, gy, gz);
                            float3 cellCenter = (0.5f + (float3)coord) * PBF_Utils.CellSize;
                            float3 dir = cellCenter - relativePos;

                            if (UseAnisotropic)
                                dir = math.mul((float3x3)GMatrix[j], dir);

                            float r2 = math.lengthsq(dir);
                            if (r2 > PBF_Utils.h2) continue;

                            float density = PBF_Utils.SmoothingKernelPoly6(r2);
                            blockTemp[GetLocalIndex(gx, gy, gz)] += density;
                        }
                    }
                }

                // 写回主 Grid 缓冲
                for (int j = 0; j < PBF_Utils.GridSize; j++)
                    Grid[offset + j] = blockTemp[j];

                blockTemp.Dispose();
            }

            private int GetLocalIndex(int x, int y, int z)
            {
                return (x & 3) + 4 * ((y & 3) + 4 * (z & 3));
            }
        }

        [BurstCompile]
        public struct GridBlurJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int3> Keys;           // block列表
            [ReadOnly] public NativeHashMap<int3, int> GridLut; // block -> 偏移
            [ReadOnly] public NativeArray<float> GridRead;      // 原始密度

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<float> GridWrite;                // 模糊后密度

            public void Execute(int i)
            {
                int3 key = Keys[i];

                if (!GridLut.ContainsKey(key))
                    return;

                // 为当前 block 构建一个 6x6x6 的邻域缓存 (包含周围一圈) 便于做局部模糊
                var block = new NativeArray<float>(6 * 6 * 6, Allocator.Temp);

                int3 minCoord = key * 4 - 1; // 左下角偏移
                for (int dz = -1; dz <= 1; ++dz)
                for (int dy = -1; dy <= 1; ++dy)
                for (int dx = -1; dx <= 1; ++dx)
                {
                    int3 nKey = key + new int3(dx, dy, dz);
                    if (!GridLut.ContainsKey(nKey)) continue;

                    int nOff = GridLut[nKey];
                    for (int j = 0; j < PBF_Utils.GridSize; j++)
                    {
                        int3 coord = (nKey << 2) + GetLocalCoord(j) - minCoord; // 映射到 6x6x6 缓冲坐标
                        if (math.any(coord < 0) || math.any(coord >= 6))
                            continue;

                        block[GetBlockIndex(coord)] = GridRead[nOff + j];
                    }
                }

                int offset = GridLut[key];
                for (int j = 0; j < PBF_Utils.GridSize; j++)
                {
                    int3 coord = GetLocalCoord(j) + 1; // 中心偏移一格对应 block 内坐标

                    float sum = 0;
                    float weight = 0;
                    for (int dz = -1; dz <= 1; ++dz)
                    for (int dy = -1; dy <= 1; ++dy)
                    for (int dx = -1; dx <= 1; ++dx)
                    {
                        int3 nCoord = coord + new int3(dx, dy, dz);

                        sum += block[GetBlockIndex(nCoord)];
                        weight += 1 - 0.5f * math.length(new float3(dx, dy, dz)); // 简单权重: 距离越近权重越高
                    }

                    GridWrite[offset + j] = sum / weight; // 平均化 -> 平滑密度
                }

                block.Dispose();
            }

            private static int3 GetLocalCoord(int index)
            {
                // 将 0..63 的索引解码成 (x,y,z) in [0,3]
                return new int3(index & 3, (index >> 2) & 3, (index >> 4) & 3);
            }

            private static int GetBlockIndex(int3 coord)
            {
                // 6x6x6 缓冲的线性索引
                return coord.x + 6 * (coord.y + 6 * coord.z);
            }
        }
    }
}
