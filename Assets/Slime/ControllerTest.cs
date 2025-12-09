using UnityEngine;
using Unity.Mathematics;

namespace Slime
{
    public class ControllerTest : MonoBehaviour
    {
        [Range(1, 10)]public float speed = 4;
        [Range(1, 10)]public float jumpForce = 4;
        [SerializeField] private float groundCheckOffset = 0.1f; // 从底部向上的偏移
        [SerializeField] private float groundCheckDistance = 0.2f;
        [SerializeField] private LayerMask groundLayer = -1; // -1 means all layers
        
        [Header("史莱姆系统引用")]
        [SerializeField] private Slime_PBF slimePBF; // 可在 Inspector 中手动拖拽指定
        [Tooltip("如果留空，会尝试在同一对象或场景中查找")]
        
        [Header("史莱姆发射设置")]
        [SerializeField] private KeyCode shootKey = KeyCode.Mouse0; // 发射键（默认鼠标左键）
        [SerializeField, Range(1f, 100f)] private float launchSpeed = 30f; // 大幅增加默认速度
        [SerializeField, Range(0.5f, 10f)] private float arcHeight = 5f; // 增加弧度
        [SerializeField, Range(1, 50)] private int particlesPerBatch = 20; // 增加每批粒子数
        [SerializeField, Range(0.01f, 0.5f)] private float launchInterval = 0.05f; // 减少发射间隔，更连续
        [SerializeField, Range(10, 200)] private int minParticlesForSwitch = 50; // 切换控制的最少粒子数
        [SerializeField, Range(0.5f, 5f)] private float targetCheckRadius = 2f; // 目标区域半径
        [SerializeField] private KeyCode switchControlKey = KeyCode.Tab; // 切换控制键
        [SerializeField] private bool showShootDebug = true; // 显示发射调试信息
        
        [Header("发射力度增强")]
        [SerializeField, Range(1f, 20f)] private float velocityMultiplier = 10f; // 大幅增加倍增器
        [SerializeField] private bool useImpulseMode = true; // 使用冲量模式（更强的初速度）
        [SerializeField, Range(0f, 50f)] private float impulseStrength = 30f; // 大幅增加冲量
        
        [Header("发射时临时调整参数（关键！）")]
        [SerializeField] private bool reduceConcentrationDuringShooting = true; // 发射时降低浓度
        [SerializeField, Range(0f, 10f)] private float shootingConcentration = 0.5f; // 发射时的临时浓度（极低）
        [SerializeField, Range(0.1f, 5f)] private float concentrationRestoreTime = 2f; // 浓度恢复时间
        
        private Rigidbody _rb;
        private bool _isGrounded;
        private Collider _collider;
        private Camera _mainCamera;
        private Slime_PBF _slimePBF;
        
        // 发射状态
        private bool _isShooting;
        private Vector3 _targetWorldPos;
        private float _lastLaunchTime;
        private int _targetControllerID = -1; // 目标位置对应的控制器ID
        private float _originalConcentration; // 保存原始浓度
        private float _concentrationRestoreTimer;
        
        void Start()
        {
            _rb = GetComponent<Rigidbody>();
            _collider = GetComponent<Collider>();
            _mainCamera = Camera.main;
            
            // 优先使用 Inspector 中指定的引用
            if (slimePBF != null)
            {
                _slimePBF = slimePBF;
                Debug.Log($"<color=cyan>[ControllerTest] ✓ 使用手动指定的 Slime_PBF: {slimePBF.name}</color>");
            }
            else
            {
                // 尝试在同一对象上查找
                _slimePBF = GetComponent<Slime_PBF>();
                
                // 如果还是找不到，尝试在场景中查找
                if (_slimePBF == null)
                {
                    _slimePBF = FindFirstObjectByType<Slime_PBF>();
                    if (_slimePBF != null)
                    {
                        Debug.Log($"<color=yellow>[ControllerTest] ✓ 在场景中找到 Slime_PBF: {_slimePBF.name}</color>");
                    }
                }
            }
            
            if (_slimePBF == null)
            {
                Debug.LogError("<color=red>[ControllerTest] ⚠️ 未找到 Slime_PBF 组件！请在 Inspector 中手动指定或确保同一对象上有该组件</color>");
            }
            else
            {
                // 确保 Slime_PBF 的 trans 引用正确设置
                if (_slimePBF.trans == null)
                {
                    _slimePBF.trans = transform;
                    Debug.Log($"<color=cyan>[ControllerTest] ✓ 自动设置 Slime_PBF.trans 为当前对象</color>");
                }
                
                // 保存原始浓度
                _originalConcentration = GetConcentration();
                Debug.Log($"<color=cyan>[ControllerTest] 原始浓度: {_originalConcentration}</color>");
            }
        }

        void Update()
        {
            CheckGroundStatus();
            HandleMouseInteraction();
            HandleShootingInput();
            
            // 浓度恢复逻辑
            if (_concentrationRestoreTimer > 0)
            {
                _concentrationRestoreTimer -= Time.deltaTime;
                if (_concentrationRestoreTimer <= 0)
                {
                    SetConcentration(_originalConcentration);
                    if (showShootDebug)
                    {
                        Debug.Log($"<color=lime>[ControllerTest] ✓ 浓度已恢复到: {_originalConcentration}</color>");
                    }
                }
            }
            
            // 显示发射调试信息
            if (showShootDebug && _isShooting)
            {
                Debug.DrawLine(transform.position, _targetWorldPos, Color.cyan, 0.1f);
                Debug.DrawLine(_targetWorldPos, _targetWorldPos + Vector3.up * 2f, Color.green, 0.1f);
                
                // 绘制发射方向
                Vector3 direction = (_targetWorldPos - transform.position).normalized;
                Debug.DrawRay(transform.position, direction * 5f, Color.red, 0.1f);
            }
        }
        
        void FixedUpdate()
        {
            if (_isShooting && _slimePBF != null)
            {
                // 按间隔发射粒子批次
                if (Time.time - _lastLaunchTime >= launchInterval)
                {
                    LaunchParticleBatch();
                    _lastLaunchTime = Time.time;
                }
            }
        }
        
        private void CheckGroundStatus()
        {
            if (_collider == null)
            {
                _isGrounded = false;
                return;
            }
            
            // 从碰撞体底部稍微向上一点开始，向下发射射线
            Vector3 rayOrigin = _collider.bounds.center;
            rayOrigin.y = _collider.bounds.min.y + groundCheckOffset;
            
            float rayDistance = groundCheckDistance + groundCheckOffset;
            
            // 使用射线检测是否在地面上
            RaycastHit hit;
            _isGrounded = Physics.Raycast(rayOrigin, Vector3.down, out hit, rayDistance, groundLayer, QueryTriggerInteraction.Ignore);
            
            // 确保没有击中自己
            if (_isGrounded && hit.collider == _collider)
            {
                _isGrounded = false;
            }
            
            // 调试用：可视化射线和状态
            Debug.DrawRay(rayOrigin, Vector3.down * rayDistance, _isGrounded ? Color.green : Color.red);
        }
    
        private void HandleMouseInteraction()
        {
            var controlledRb = _rb;
            var velocity = speed * new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical")).normalized;
            velocity.y = controlledRb.linearVelocity.y;
        
            // 只有在地面上才能跳跃，防止连跳
            if (Input.GetKeyDown(KeyCode.Space) && _isGrounded)
            {
                velocity.y = jumpForce;
                Debug.Log("跳跃成功！");
            }
        
            controlledRb.linearVelocity = velocity;
        }
        
        private void HandleShootingInput()
        {
            if (_slimePBF == null)
                return;
            
            // 检测鼠标按键开始发射
            if (Input.GetKeyDown(shootKey))
            {
                if (TryGetMouseWorldPosition(out Vector3 worldPos))
                {
                    StartShooting(worldPos);
                }
            }
            
            // 释放鼠标停止发射
            if (Input.GetKeyUp(shootKey))
            {
                StopShooting();
            }
            
            // 按住时持续瞄准
            if (_isShooting && Input.GetKey(shootKey))
            {
                if (TryGetMouseWorldPosition(out Vector3 worldPos))
                {
                    _targetWorldPos = worldPos;
                }
            }
            
            // Tab键切换到目标史莱姆
            if (Input.GetKeyDown(switchControlKey))
            {
                TrySwitchToTarget();
            }
        }
        
        private bool TryGetMouseWorldPosition(out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            
            if (_mainCamera == null)
                return false;
            
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
            _lastLaunchTime = Time.time - launchInterval; // 立即开始发射
            
            // 🔥 关键：临时降低浓度，让粒子能飞出去
            if (reduceConcentrationDuringShooting)
            {
                _originalConcentration = GetConcentration();
                SetConcentration(shootingConcentration);
                _concentrationRestoreTimer = concentrationRestoreTime;
                
                if (showShootDebug)
                {
                    Debug.Log($"<color=yellow>[ControllerTest] ⚡ 临时降低浓度: {_originalConcentration} → {shootingConcentration}</color>");
                }
            }
            
            // 在目标位置创建新的控制器
            _targetControllerID = _slimePBF.CreateControllerAtPosition(_targetWorldPos, targetCheckRadius);
            
            if (showShootDebug)
            {
                Debug.Log($"<color=cyan>[ControllerTest] 🎯 开始发射到: {_targetWorldPos}</color>");
                Debug.Log($"<color=cyan>目标控制器ID: {_targetControllerID}, 距离: {Vector3.Distance(transform.position, _targetWorldPos):F2}m</color>");
            }
        }
        
        private void StopShooting()
        {
            _isShooting = false;
            
            if (showShootDebug && _slimePBF != null)
            {
                int particleCount = _slimePBF.CountParticlesInSphere(_targetWorldPos, targetCheckRadius);
                Debug.Log($"<color=yellow>[ControllerTest] ⏹ 停止发射。目标粒子数: {particleCount}</color>");
            }
        }
        
        private void LaunchParticleBatch()
        {
            // 优先从发射源位置（角色当前位置）获取粒子
            Vector3 shootOrigin = _slimePBF.trans != null ? _slimePBF.trans.position : transform.position;
            
            // 使用非常大的搜索半径
            int[] particlesToLaunch = _slimePBF.GetNearestParticles(shootOrigin, particlesPerBatch * 3);
            
            // 如果还是没找到，尝试从所有粒子中获取
            if (particlesToLaunch.Length == 0)
            {
                int currentControllerID = _slimePBF.GetControlledInstanceID();
                if (currentControllerID >= 0)
                {
                    particlesToLaunch = _slimePBF.GetParticlesInController(currentControllerID, particlesPerBatch);
                }
            }
            
            if (particlesToLaunch.Length == 0)
            {
                if (showShootDebug)
                {
                    Debug.LogWarning($"<color=red>[ControllerTest] ⚠️ 位置 {shootOrigin} 没有粒子！</color>");
                }
                StopShooting();
                return;
            }
            
            // 限制实际发射数量
            int launchCount = Mathf.Min(particlesToLaunch.Length, particlesPerBatch);
            System.Array.Resize(ref particlesToLaunch, launchCount);
            
            Vector3 startPos = shootOrigin;
            Vector3 targetPos = _targetWorldPos;
            Vector3 direction = (targetPos - startPos).normalized;
            
            Vector3 velocity;
            
            if (useImpulseMode)
            {
                // 冲量模式：超强速度
                velocity = direction * impulseStrength;
                velocity.y += arcHeight;
            }
            else
            {
                float distance = Vector3.Distance(startPos, targetPos);
                float time = distance / launchSpeed;
                
                Vector3 horizontalDir = new Vector3(direction.x, 0, direction.z).normalized;
                float horizontalDist = Vector3.Distance(
                    new Vector3(startPos.x, 0, startPos.z),
                    new Vector3(targetPos.x, 0, targetPos.z)
                );
                
                float vx = horizontalDist / time;
                float vy = arcHeight / (time * 0.5f);
                
                velocity = horizontalDir * vx + Vector3.up * vy;
            }
            
            // 应用速度倍增器
            velocity *= velocityMultiplier;
            
            // 🚀 施加速度并立即分配到新控制器
            _slimePBF.ApplyVelocityToParticles(particlesToLaunch, velocity);
            _slimePBF.SetParticleController(particlesToLaunch, _targetControllerID);
            
            if (showShootDebug)
            {
                Debug.Log($"<color=green>[发射] 🚀 {particlesToLaunch.Length} 个粒子，速度: {velocity.magnitude:F1} m/s，方向: {direction}</color>");
            }
        }
        
        private void TrySwitchToTarget()
        {
            if (_slimePBF == null || _targetControllerID < 0)
            {
                Debug.LogWarning("[ControllerTest] 没有目标控制器");
                return;
            }
            
            int particleCount = _slimePBF.CountParticlesInSphere(_targetWorldPos, targetCheckRadius);
            
            if (particleCount >= minParticlesForSwitch)
            {
                var instances = _slimePBF.GetAllSlimeInstances();
                
                float minDist = float.MaxValue;
                int targetInstanceID = -1;
                
                foreach (var instance in instances)
                {
                    float dist = Vector3.Distance(instance.position, _targetWorldPos);
                    if (dist < minDist && instance.particleCount >= minParticlesForSwitch)
                    {
                        minDist = dist;
                        targetInstanceID = instance.id;
                    }
                }
                
                if (targetInstanceID >= 0)
                {
                    _slimePBF.SwitchToInstance(targetInstanceID);
                    transform.position = _targetWorldPos;
                    
                    if (showShootDebug)
                    {
                        Debug.Log($"<color=lime>[切换成功] ✅ ID: {targetInstanceID}, 粒子: {particleCount}</color>");
                    }
                }
                else
                {
                    Debug.LogWarning("[ControllerTest] 找不到目标实例");
                }
            }
            else
            {
                if (showShootDebug)
                {
                    Debug.LogWarning($"<color=orange>[ControllerTest] ⚠️ 粒子不足: {particleCount}/{minParticlesForSwitch}</color>");
                }
            }
        }
        
        // 通过反射获取/设置 Slime_PBF 的私有 concentration 字段
        private float GetConcentration()
        {
            var field = typeof(Slime_PBF).GetField("concentration", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                return (float)field.GetValue(_slimePBF);
            }
            return 10f; // 默认值
        }
        
        private void SetConcentration(float value)
        {
            var field = typeof(Slime_PBF).GetField("concentration", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(_slimePBF, value);
                
                if (showShootDebug)
                {
                    Debug.Log($"<color=yellow>[ControllerTest] 设置浓度 = {value}</color>");
                }
            }
        }
        
        private void OnDrawGizmos()
        {
            if (!showShootDebug || !_isShooting)
                return;
            
            // 目标区域
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(_targetWorldPos, targetCheckRadius);
            
            // 发射源范围
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, 2f);
            
            // 显示 Slime_PBF 粒子中心位置
            if (_slimePBF != null && _slimePBF.trans != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(_slimePBF.trans.position, 1.5f);
                Gizmos.DrawLine(transform.position, _slimePBF.trans.position);
            }
            
            if (Application.isPlaying)
            {
                // 轨迹预览
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
        
        private Vector3 CalculateArcPosition(Vector3 start, Vector3 target, float t)
        {
            // 线性插值水平位置
            Vector3 horizontal = Vector3.Lerp(start, target, t);
            
            // 添加抛物线高度（使用二次函数）
            float height = arcHeight * 4f * t * (1f - t); // 峰值在 t=0.5
            horizontal.y += height;
            
            return horizontal;
        }
    }
}
