using UnityEngine;

namespace Slime
{
    /// <summary>
    /// 性能配置管理器 - 根据平台自动调整性能参数
    /// </summary>
    public class PerformanceManager : MonoBehaviour
    {
        [System.Serializable]
        public enum PerformancePreset
        {
            Auto,      // 自动检测
            Mobile,    // 移动端优化
            Desktop,   // 桌面端
            High       // 高性能
        }

        [Header("性能预设")]
        [SerializeField] private PerformancePreset preset = PerformancePreset.Auto;
        
        [Header("自动优化")]
        [SerializeField] private bool autoOptimize = true;
        [SerializeField] private float targetFrameRate = 30f; // 移动端目标帧率
        
        [Header("渲染优化")]
        [SerializeField] private bool reduceShadows = true;
        [SerializeField] private bool reducePostProcessing = true;
        
        private Slime_PBF _slimePBF;
        private bool _isOptimized = false;

        void Awake()
        {
            _slimePBF = FindFirstObjectByType<Slime_PBF>();
            
            // 设置目标帧率
            if (IsMobilePlatform())
            {
                Application.targetFrameRate = (int)targetFrameRate;
                QualitySettings.vSyncCount = 0; // 关闭垂直同步
            }
            else
            {
                Application.targetFrameRate = 60;
            }
        }

        void Start()
        {
            if (autoOptimize)
            {
                ApplyOptimizations();
            }
        }

        public void ApplyOptimizations()
        {
            if (_isOptimized) return;

            PerformancePreset activePreset = preset;
            if (preset == PerformancePreset.Auto)
            {
                activePreset = IsMobilePlatform() ? PerformancePreset.Mobile : PerformancePreset.Desktop;
            }

            switch (activePreset)
            {
                case PerformancePreset.Mobile:
                    ApplyMobileOptimizations();
                    break;
                case PerformancePreset.Desktop:
                    ApplyDesktopOptimizations();
                    break;
                case PerformancePreset.High:
                    ApplyHighPerformanceSettings();
                    break;
            }

            _isOptimized = true;
            Debug.Log($"[Performance] Applied {activePreset} optimizations");
        }

        private void ApplyMobileOptimizations()
        {
            // 降低质量设置
            QualitySettings.shadowDistance = 20f;
            QualitySettings.shadowResolution = ShadowResolution.Low;
            QualitySettings.pixelLightCount = 1;
            
            if (reduceShadows)
            {
                QualitySettings.shadows = ShadowQuality.Disable;
            }

            // 降低物理更新频率
            Time.fixedDeltaTime = 0.03f; // 从 0.02 降到 0.03 (33fps物理)

            // 优化史莱姆参数
            if (_slimePBF != null)
            {
                // 将渲染模式改为粒子模式（性能更好）
                _slimePBF.renderMode = Slime_PBF.RenderMode.Particles;
                
                Debug.Log("[Performance] Mobile optimizations: Particle mode, reduced quality");
            }
        }

        private void ApplyDesktopOptimizations()
        {
            QualitySettings.shadowDistance = 50f;
            QualitySettings.shadowResolution = ShadowResolution.Medium;
            QualitySettings.pixelLightCount = 2;
            
            Time.fixedDeltaTime = 0.02f;
            
            if (_slimePBF != null)
            {
                // 桌面可以使用表面渲染
                _slimePBF.renderMode = Slime_PBF.RenderMode.Surface;
            }
            
            Debug.Log("[Performance] Desktop optimizations applied");
        }

        private void ApplyHighPerformanceSettings()
        {
            QualitySettings.shadowDistance = 100f;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.pixelLightCount = 4;
            
            Time.fixedDeltaTime = 0.02f;
            
            if (_slimePBF != null)
            {
                _slimePBF.renderMode = Slime_PBF.RenderMode.Surface;
            }
            
            Debug.Log("[Performance] High performance settings applied");
        }

        private bool IsMobilePlatform()
        {
#if UNITY_ANDROID || UNITY_IOS
            return true;
#elif UNITY_EDITOR
            return false;
#else
            return Application.isMobilePlatform;
#endif
        }

        // 运行时调整
        public void SetPreset(PerformancePreset newPreset)
        {
            preset = newPreset;
            _isOptimized = false;
            ApplyOptimizations();
        }

        // UI 显示当前 FPS
        void OnGUI()
        {
            if (!Debug.isDebugBuild) return;

            float fps = 1.0f / Time.deltaTime;
            float ms = Time.deltaTime * 1000f;
            
            GUIStyle style = new GUIStyle();
            style.fontSize = 24;
            style.normal.textColor = fps < 20 ? Color.red : (fps < 30 ? Color.yellow : Color.green);
            
            GUI.Label(new Rect(10, 10, 300, 30), $"FPS: {fps:F1} ({ms:F1}ms)", style);
        }
    }
}
