using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Slime
{
    /// <summary>
    /// 史莱姆分裂控制器 - 通过鼠标点击将粒子发射到目标位置形成新的史莱姆
    /// </summary>
    public class SlimeSplitController : MonoBehaviour
    {
        [Header("发射设置")]
        [SerializeField] private KeyCode splitKey = KeyCode.Mouse0; // 发射键（默认鼠标左键）
        [SerializeField, Range(0.1f, 10f)] private float launchSpeed = 3f; // 发射速度
        [SerializeField, Range(0.1f, 5f)] private float arcHeight = 1.5f; // 弧线高度
        [SerializeField, Range(1, 100)] private int particlesPerBatch = 5; // 每批发射的粒子数
        [SerializeField, Range(0.01f, 1f)] private float launchInterval = 0.1f; // 发射间隔
        
        [Header("目标位置设置")]
        [SerializeField] private LayerMask groundLayer = -1; // 地面层（用于射线检测）
        [SerializeField, Range(0.5f, 5f)] private float targetRadius = 1f; // 目标区域半径
        [SerializeField] private GameObject targetIndicatorPrefab; // 目标指示器预制体（可选）
        
        [Header("切换控制设置")]
        [SerializeField, Range(10, 500)] private int minParticlesForControl = 50; // 可切换控制的最少粒子数
        [SerializeField] private KeyCode switchControlKey = KeyCode.Tab; // 切换控制键
        
        [Header("调试")]
        [SerializeField] private bool showDebugInfo = true;
        
        private Slime_PBF _slimePBF;
        private Camera _mainCamera;
        private Vector3 _targetWorldPos;
        private bool _isShooting;
        private float _lastLaunchTime;
        private GameObject _targetIndicator;
        private int _particlesAtTarget; // 目标位置的粒子计数
        
        // 粒子发射状态
        private struct LaunchData
        {
            public int ParticleIndex;
            public float3 StartPos;
            public float3 TargetPos;
            public float LaunchTime;
            public float Duration;
            public bool Active;
        }
        
        private NativeList<LaunchData> _launchingParticles;
        
        void Start()
        {
            _slimePBF = GetComponent<Slime_PBF>();
            _mainCamera = Camera.main;
            
            if (_slimePBF == null)
            {
                Debug.LogError("[SlimeSplitController] 找不到 Slime_PBF 组件！");
                enabled = false;
                return;
            }
            
            _launchingParticles = new NativeList<LaunchData>(100, Allocator.Persistent);
            
            // 创建目标指示器
            if (targetIndicatorPrefab != null)
            {
                _targetIndicator = Instantiate(targetIndicatorPrefab);
                _targetIndicator.SetActive(false);
            }
        }
        
        void OnDestroy()
        {
            if (_launchingParticles.IsCreated)
                _launchingParticles.Dispose();
                
            if (_targetIndicator != null)
                Destroy(_targetIndicator);
        }
        
        void Update()
        {
            HandleMouseInput();
            UpdateTargetIndicator();
            
            // 显示调试信息
            if (showDebugInfo && _isShooting)
            {
                Debug.DrawLine(transform.position, _targetWorldPos, Color.cyan);
                Debug.DrawLine(_targetWorldPos, _targetWorldPos + Vector3.up * 2f, Color.green);
            }
        }
        
        private void HandleMouseInput()
        {
            // 检测鼠标按键
            if (Input.GetKeyDown(splitKey))
            {
                if (TryGetMouseWorldPosition(out Vector3 worldPos))
                {
                    StartShooting(worldPos);
                }
            }
            
            if (Input.GetKeyUp(splitKey))
            {
                StopShooting();
            }
            
            // 更新鼠标位置（按住时持续瞄准）
            if (_isShooting && Input.GetKey(splitKey))
            {
                if (TryGetMouseWorldPosition(out Vector3 worldPos))
                {
                    _targetWorldPos = worldPos;
                }
            }
            
            // 切换控制
            if (Input.GetKeyDown(switchControlKey))
            {
                TrySwitchControl();
            }
        }
        
        private bool TryGetMouseWorldPosition(out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            
            Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayer))
            {
                worldPos = hit.point;
                worldPos.y += 0.5f; // 稍微抬高避免地面穿透
                return true;
            }
            
            return false;
        }
        
        private void StartShooting(Vector3 targetPos)
        {
            _targetWorldPos = targetPos;
            _isShooting = true;
            _lastLaunchTime = Time.time;
            _particlesAtTarget = 0;
            
            if (_targetIndicator != null)
            {
                _targetIndicator.SetActive(true);
                _targetIndicator.transform.position = _targetWorldPos;
            }
            
            if (showDebugInfo)
            {
                Debug.Log($"[SlimeSplitController] 开始发射粒子到目标: {_targetWorldPos}");
            }
        }
        
        private void StopShooting()
        {
            _isShooting = false;
            
            if (_targetIndicator != null)
            {
                _targetIndicator.SetActive(false);
            }
            
            if (showDebugInfo)
            {
                Debug.Log($"[SlimeSplitController] 停止发射。目标位置粒子数: {_particlesAtTarget}");
            }
        }
        
        private void UpdateTargetIndicator()
        {
            if (_targetIndicator != null && _targetIndicator.activeSelf)
            {
                // 可以添加旋转、缩放等动画效果
                _targetIndicator.transform.Rotate(Vector3.up, 90f * Time.deltaTime);
            }
        }
        
        private void FixedUpdate()
        {
            if (!_isShooting)
                return;
                
            // 按间隔发射粒子批次
            if (Time.time - _lastLaunchTime >= launchInterval)
            {
                LaunchParticleBatch();
                _lastLaunchTime = Time.time;
            }
            
            // 更新正在飞行中的粒子（这个逻辑需要与 Slime_PBF 集成）
            UpdateLaunchingParticles();
        }
        
        private void LaunchParticleBatch()
        {
            // 注意：这里需要访问 Slime_PBF 的内部粒子数组
            // 由于访问限制，我们需要在 Slime_PBF 中添加公共接口
            
            if (showDebugInfo)
            {
                Debug.Log($"[SlimeSplitController] 发射粒子批次 ({particlesPerBatch} 个)");
            }
            
            // TODO: 实现粒子发射逻辑
            // 这需要修改 Slime_PBF 以暴露粒子操作接口
        }
        
        private void UpdateLaunchingParticles()
        {
            // 更新飞行中的粒子位置（抛物线轨迹）
            float currentTime = Time.time;
            
            for (int i = _launchingParticles.Length - 1; i >= 0; i--)
            {
                var data = _launchingParticles[i];
                if (!data.Active)
                    continue;
                    
                float elapsed = currentTime - data.LaunchTime;
                float t = math.clamp(elapsed / data.Duration, 0f, 1f);
                
                if (t >= 1f)
                {
                    // 粒子到达目标
                    data.Active = false;
                    _launchingParticles[i] = data;
                    _particlesAtTarget++;
                    continue;
                }
                
                // 计算抛物线轨迹位置
                float3 currentPos = CalculateArcPosition(data.StartPos, data.TargetPos, t);
                
                // TODO: 更新粒子实际位置（需要 Slime_PBF 接口）
                
                _launchingParticles[i] = data;
            }
        }
        
        private float3 CalculateArcPosition(float3 start, float3 target, float t)
        {
            // 线性插值水平位置
            float3 horizontal = math.lerp(start, target, t);
            
            // 添加抛物线高度（使用二次函数）
            float height = arcHeight * 4f * t * (1f - t); // 峰值在 t=0.5
            horizontal.y += height;
            
            return horizontal;
        }
        
        private void TrySwitchControl()
        {
            // 检查目标位置是否有足够的粒子形成新史莱姆
            if (_particlesAtTarget >= minParticlesForControl)
            {
                // TODO: 实现控制切换逻辑
                // 需要找到目标位置对应的史莱姆实例并切换控制
                
                if (showDebugInfo)
                {
                    Debug.Log($"[SlimeSplitController] 切换控制到目标史莱姆 (粒子数: {_particlesAtTarget})");
                }
            }
            else
            {
                if (showDebugInfo)
                {
                    Debug.LogWarning($"[SlimeSplitController] 目标位置粒子不足 ({_particlesAtTarget}/{minParticlesForControl})");
                }
            }
        }
        
        void OnDrawGizmos()
        {
            if (!showDebugInfo || !_isShooting)
                return;
                
            // 绘制目标区域
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(_targetWorldPos, targetRadius);
            
            // 绘制弧线预览
            Gizmos.color = Color.yellow;
            Vector3 start = transform.position;
            Vector3 end = _targetWorldPos;
            int segments = 20;
            
            for (int i = 0; i < segments; i++)
            {
                float t1 = (float)i / segments;
                float t2 = (float)(i + 1) / segments;
                
                Vector3 p1 = CalculateArcPosition(start, end, t1);
                Vector3 p2 = CalculateArcPosition(start, end, t2);
                
                Gizmos.DrawLine(p1, p2);
            }
        }
    }
}
