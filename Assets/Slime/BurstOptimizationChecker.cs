using UnityEngine;
using Unity.Jobs;

namespace Slime
{
    /// <summary>
    /// Burst编译器性能检查和优化管理器
    /// 确保Burst编译器在移动端正确工作
    /// </summary>
    public class BurstOptimizationChecker : MonoBehaviour
    {
        [Header("Burst 状态检查")]
        [SerializeField] private bool showBurstStatus = true;
        
        void Start()
        {
            CheckBurstStatus();
            CheckJobsSystemSettings();
        }

        private void CheckBurstStatus()
        {
#if UNITY_BURST
            Debug.Log("[Burst] ✓ Burst编译器已启用");
#else
            Debug.LogWarning("[Burst] ✗ Burst编译器未启用！性能会大幅下降");
#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Debug.LogWarning("[Jobs] 开启了安全检查模式，会降低性能。发布版本会自动关闭。");
#endif
        }

        private void CheckJobsSystemSettings()
        {
            // Jobs系统工作线程数
            int workerThreads = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount;
            Debug.Log($"[Jobs] 工作线程数: {workerThreads}");
            
            // 移动端建议减少工作线程
            if (Application.isMobilePlatform && workerThreads > 2)
            {
                Debug.Log("[Jobs] 移动端检测到多个工作线程，这是正常的");
            }
        }

        void OnGUI()
        {
            if (!showBurstStatus || !Debug.isDebugBuild) return;

            GUIStyle style = new GUIStyle();
            style.fontSize = 20;
            style.normal.textColor = Color.white;

            int y = 50;
            
#if UNITY_BURST
            GUI.Label(new Rect(10, y, 400, 30), "Burst: 已启用 ✓", style);
#else
            style.normal.textColor = Color.red;
            GUI.Label(new Rect(10, y, 400, 30), "Burst: 未启用 ✗", style);
#endif
            
            y += 30;
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(10, y, 400, 30), $"Jobs线程: {Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount}", style);
        }
    }
}
