using UnityEngine;

namespace Slime
{
    /// <summary>
    /// 移动端粒子数量优化配置
    /// 使用说明：
    /// 1. 在 Jobs_Simulation_PBF.cs 中修改 Width 参数
    /// 2. 移动端建议: Width = 10-12 (500-864粒子)
    /// 3. 桌面端默认: Width = 16 (2048粒子)
    /// </summary>
    [CreateAssetMenu(fileName = "SlimeConfig", menuName = "Slime/Performance Config")]
    public class SlimePerformanceConfig : ScriptableObject
    {
        [Header("粒子数量配置")]
        [Tooltip("粒子网格宽度。移动端: 10-12, 桌面: 14-16")]
        [Range(6, 20)]
        public int particleWidth = 16;
        
        [Header("模拟质量")]
        [Tooltip("PBF迭代次数。移动端: 2-3, 桌面: 4-5")]
        [Range(1, 8)]
        public int solverIterations = 4;
        
        [Tooltip("物理更新频率(秒)。移动端: 0.03-0.04, 桌面: 0.02")]
        [Range(0.016f, 0.05f)]
        public float fixedDeltaTime = 0.02f;
        
        [Header("渲染模式")]
        [Tooltip("移动端建议使用 Particles 模式")]
        public Slime_PBF.RenderMode defaultRenderMode = Slime_PBF.RenderMode.Surface;
        
        [Header("其他优化")]
        [Tooltip("是否关闭气泡特效")]
        public bool disableBubbles = false;
        
        [Tooltip("是否降低密度场分辨率")]
        public bool reduceDensityFieldResolution = false;

        public int GetParticleCount()
        {
            return particleWidth * particleWidth * particleWidth / 2;
        }

        public void ApplySettings()
        {
            Time.fixedDeltaTime = fixedDeltaTime;
            Debug.Log($"[SlimeConfig] Particles: {GetParticleCount()}, FixedDeltaTime: {fixedDeltaTime}");
        }
    }
}
