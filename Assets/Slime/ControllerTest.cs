using UnityEngine;
using Unity.Mathematics;

namespace Slime
{
    public class ControllerTest : MonoBehaviour
    {
<<<<<<< HEAD
        [Range(1, 10)] public float speed = 4;
        [Range(1, 10)] public float jumpForce = 4;
        
        private Rigidbody _rb;
        private Vector3 _targetPosition;
        private bool _hasTarget = false;
        private bool _isGrounded = false;
        
        [SerializeField] private LayerMask groundLayer = -1; // 地面层，用于检测是否着地
        [SerializeField] private float groundCheckDistance = 0.3f; // 地面检测距离
        
        // private int _controlledId = 0;
        // public GameObject prefan;
=======
        [Range(1, 10)]public float speed = 4;
        [Range(1, 10)]public float jumpForce = 4;
        [SerializeField] private float groundCheckOffset = 0.1f;
        [SerializeField] private float groundCheckDistance = 0.2f;
        [SerializeField] private LayerMask groundLayer = -1;
        
        [Header("史莱姆系统引用")]
        [SerializeField] private Slime_PBF slimePBF;
        [Tooltip("如果留空，会尝试在同一对象或场景中查找")]
        
        [Header("史莱姆发射设置")]
        [SerializeField] private KeyCode shootKey = KeyCode.Mouse0;
        [SerializeField, Range(1f, 100f)] private float launchSpeed = 50f; // 进一步增加速度
        [SerializeField, Range(0.5f, 10f)] private float arcHeight = 5f;
        [SerializeField, Range(1, 50)] private int particlesPerBatch = 20;
        [SerializeField, Range(0.01f, 0.5f)] private float launchInterval = 0.05f;
        [SerializeField, Range(10, 200)] private int minParticlesForSwitch = 50;
        [SerializeField, Range(0.5f, 5f)] private float targetCheckRadius = 2f;
        [SerializeField] private KeyCode switchControlKey = KeyCode.Tab;
        [SerializeField] private bool showShootDebug = true;
        
        [Header("发射力度增强")]
        [SerializeField, Range(1f, 30f)] private float velocityMultiplier = 15f; // 进一步增加倍增器
        [SerializeField] private bool useImpulseMode = true;
        [SerializeField, Range(0f, 100f)] private float impulseStrength = 50f; // 大幅增加冲量
        
        [Header("形状保持设置（新！）")]
        [SerializeField] private bool maintainOriginalShape = true; // 保持原史莱姆形状
        [SerializeField, Range(0f, 2f)] private float transitionConcentration = 0.1f; // 过渡控制器的低浓度
        [Tooltip("不修改原史莱姆浓度，只给飞行中的粒子使用低浓度控制器")]
        
        private Rigidbody _rb;
        private bool _isGrounded;
        private Collider _collider;
        private Camera _mainCamera;
        private Slime_PBF _slimePBF;
        
        private bool _isShooting;
        private Vector3 _targetWorldPos;
        private float _lastLaunchTime;
        private int _targetControllerID = -1;
        private int _transitionControllerID = -1; // 过渡控制器（低浓度，用于飞行中的粒子）
>>>>>>> 5abb72d8189e8fdcf07b2d87fbf4b0b9c77edee9
        
        void Start()
        {
            _rb = GetComponent<Rigidbody>();
            _collider = GetComponent<Collider>();
            _mainCamera = Camera.main;
            
            if (slimePBF != null)
            {
                _slimePBF = slimePBF;
                Debug.Log($"<color=cyan>[ControllerTest] ✓ 使用手动指定的 Slime_PBF: {slimePBF.name}</color>");
            }
            else
            {
                _slimePBF = GetComponent<Slime_PBF>();
                
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
                Debug.LogError("<color=red>[ControllerTest] ⚠️ 未找到 Slime_PBF 组件！</color>");
            }
            else
            {
                if (_slimePBF.trans == null)
                {
                    _slimePBF.trans = transform;
                    Debug.Log($"<color=cyan>[ControllerTest] ✓ 自动设置 Slime_PBF.trans</color>");
                }
            }
        }

        void Update()
        {
<<<<<<< HEAD
            HandleMouseClick();
            HandleKeyboardInput();
            CheckGroundStatus();
=======
            CheckGroundStatus();
            HandleMouseInteraction();
            HandleShootingInput();
            
            if (showShootDebug && _isShooting)
            {
                Debug.DrawLine(transform.position, _targetWorldPos, Color.cyan, 0.1f);
                Debug.DrawLine(_targetWorldPos, _targetWorldPos + Vector3.up * 2f, Color.green, 0.1f);
                Vector3 direction = (_targetWorldPos - transform.position).normalized;
                Debug.DrawRay(transform.position, direction * 5f, Color.red, 0.1f);
            }
>>>>>>> 5abb72d8189e8fdcf07b2d87fbf4b0b9c77edee9
        }
        
        void FixedUpdate()
        {
<<<<<<< HEAD
            MoveTowardsTarget();
        }
        
        // 检测是否着地
        private void CheckGroundStatus()
        {
            _isGrounded = Physics.Raycast(transform.position, Vector3.down, groundCheckDistance, groundLayer);
        }
        
        // 处理鼠标点击
        private void HandleMouseClick()
        {
            if (Input.GetMouseButtonDown(0))
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    _targetPosition = hit.point;
                    _hasTarget = true;
=======
            if (_isShooting && _slimePBF != null)
            {
                if (Time.time - _lastLaunchTime >= launchInterval)
                {
                    LaunchParticleBatch();
                    _lastLaunchTime = Time.time;
>>>>>>> 5abb72d8189e8fdcf07b2d87fbf4b0b9c77edee9
                }
            }
        }
        
<<<<<<< HEAD
        // 处理键盘输入（跳跃）
        private void HandleKeyboardInput()
        {
            // 空格键或J键跳跃
            if ((Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.J)) && _isGrounded)
            {
                Jump();
            }
        }
        
        // 向目标位置移动
        private void MoveTowardsTarget()
        {
            if (!_hasTarget) return;
            
            Vector3 direction = (_targetPosition - transform.position);
            direction.y = 0; // 只在水平方向移动
            
            float distance = direction.magnitude;
            
            // 如果接近目标，停止移动
            if (distance < 0.5f)
            {
                _hasTarget = false;
                _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
                return;
            }
            
            // 计算移动速度
            Vector3 velocity = direction.normalized * speed;
            velocity.y = _rb.linearVelocity.y; // 保持垂直速度
            
            _rb.linearVelocity = velocity;
        }
        
        // 跳跃
        private void Jump()
        {
            _rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        }
        
        // 公共方法，供UI按钮调用
        public void TryJump()
        {
            if (_isGrounded)
            {
                Jump();
            }
=======
        private void CheckGroundStatus()
        {
            if (_collider == null)
            {
                _isGrounded = false;
                return;
            }
            
            Vector3 rayOrigin = _collider.bounds.center;
            rayOrigin.y = _collider.bounds.min.y + groundCheckOffset;
            float rayDistance = groundCheckDistance + groundCheckOffset;
            
            RaycastHit hit;
            _isGrounded = Physics.Raycast(rayOrigin, Vector3.down, out hit, rayDistance, groundLayer, QueryTriggerInteraction.Ignore);
            
            if (_isGrounded && hit.collider == _collider)
            {
                _isGrounded = false;
            }
            
            Debug.DrawRay(rayOrigin, Vector3.down * rayDistance, _isGrounded ? Color.green : Color.red);
>>>>>>> 5abb72d8189e8fdcf07b2d87fbf4b0b9c77edee9
        }
    
        private void HandleMouseInteraction()
        {
            var controlledRb = _rb;
            var velocity = speed * new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical")).normalized;
            velocity.y = controlledRb.linearVelocity.y;
        
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
            
            if (Input.GetKeyDown(shootKey))
            {
                if (TryGetMouseWorldPosition(out Vector3 worldPos))
                {
                    StartShooting(worldPos);
                }
            }
            
            if (Input.GetKeyUp(shootKey))
            {
                StopShooting();
            }
            
            if (_isShooting && Input.GetKey(shootKey))
            {
                if (TryGetMouseWorldPosition(out Vector3 worldPos))
                {
                    _targetWorldPos = worldPos;
                }
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
                worldPos.y += 0.5f;
                return true;
            }
            
            return false;
        }
        
        private void StartShooting(Vector3 targetPos)
        {
            _targetWorldPos = targetPos;
            _isShooting = true;
            _lastLaunchTime = Time.time - launchInterval;
            
            if (maintainOriginalShape)
            {
                // 🔑 创建低浓度过渡控制器
                Vector3 midPoint = (transform.position + _targetWorldPos) / 2f;
                _transitionControllerID = _slimePBF.CreateControllerAtPosition(
                    midPoint, 
                    Vector3.Distance(transform.position, _targetWorldPos) / 2f,
                    transitionConcentration  // 直接传入低浓度
                );
                
                if (showShootDebug)
                {
                    Debug.Log($"<color=yellow>[ControllerTest] ✓ 创建过渡控制器 ID: {_transitionControllerID}, 浓度: {transitionConcentration}</color>");
                }
            }
            
            // 创建目标控制器（正常浓度）
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
            Vector3 shootOrigin = _slimePBF.trans != null ? _slimePBF.trans.position : transform.position;
            
            int[] particlesToLaunch = _slimePBF.GetNearestParticles(shootOrigin, particlesPerBatch * 3);
            
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
            
            int launchCount = Mathf.Min(particlesToLaunch.Length, particlesPerBatch);
            System.Array.Resize(ref particlesToLaunch, launchCount);
            
            Vector3 startPos = shootOrigin;
            Vector3 targetPos = _targetWorldPos;
            Vector3 direction = (targetPos - startPos).normalized;
            
            Vector3 velocity;
            
            if (useImpulseMode)
            {
                // 🚀 超强冲量模式 - 确保速度足够大
                velocity = direction * impulseStrength;
                velocity.y += arcHeight * 2; // 增加垂直分量
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
            
            velocity *= velocityMultiplier;
            
            // ⚡ 关键：每帧连续多次施加速度，确保粒子获得足够动量
            for (int repeat = 0; repeat < 3; repeat++)
            {
                _slimePBF.ApplyVelocityToParticles(particlesToLaunch, velocity);
            }
            
            if (maintainOriginalShape && _transitionControllerID >= 0)
            {
                // 立即分配到过渡控制器
                _slimePBF.SetParticleController(particlesToLaunch, _transitionControllerID);
                
                // 延迟分配到目标控制器
                StartCoroutine(DelayedAssignToTarget(particlesToLaunch, 0.5f));
            }
            else
            {
                // 直接分配到目标控制器
                _slimePBF.SetParticleController(particlesToLaunch, _targetControllerID);
            }
            
            if (showShootDebug)
            {
                Debug.Log($"<color=green>[发射] 🚀 {particlesToLaunch.Length} 个粒子，速度: {velocity.magnitude:F1} m/s，方向: {velocity.normalized}</color>");
            }
        }
        
        private System.Collections.IEnumerator DelayedAssignToTarget(int[] particleIndices, float delay)
        {
            yield return new WaitForSeconds(delay);
            
            // 粒子飞出去后，重新分配到目标控制器
            _slimePBF.SetParticleController(particleIndices, _targetControllerID);
            
            if (showShootDebug)
            {
                Debug.Log($"<color=lime>[延迟分配] ✓ {particleIndices.Length} 个粒子现在归属目标控制器</color>");
            }
        }
        
        private void OnDrawGizmos()
        {
            if (!showShootDebug || !_isShooting)
                return;
            
            // 目标区域
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(_targetWorldPos, targetCheckRadius);
            
            // 发射源
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, 2f);
            
            // 过渡控制器位置（如果启用）
            if (maintainOriginalShape)
            {
                Vector3 midPoint = (transform.position + _targetWorldPos) / 2f;
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(midPoint, 1f);
                Gizmos.DrawLine(transform.position, midPoint);
                Gizmos.DrawLine(midPoint, _targetWorldPos);
            }
            
            if (_slimePBF != null && _slimePBF.trans != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(_slimePBF.trans.position, 1.5f);
            }
            
            if (Application.isPlaying)
            {
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
            Vector3 horizontal = Vector3.Lerp(start, target, t);
            float height = arcHeight * 4f * t * (1f - t);
            horizontal.y += height;
            return horizontal;
        }
    }
}
