using UnityEngine;

namespace Slime
{
    public class ControllerTest : MonoBehaviour
    {
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
        
        void Start()
        {
            _rb = GetComponent<Rigidbody>();
        }

        void Update()
        {
            HandleMouseClick();
            HandleKeyboardInput();
            CheckGroundStatus();
        }
        
        void FixedUpdate()
        {
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
                }
            }
        }
        
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
        }
    
        private void HandleMouseInteraction()
        {
            var controlledRb = _rb;
            var velocity = speed * new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical")).normalized;
            velocity.y = controlledRb.linearVelocity.y;
        
            if (Input.GetKeyDown(KeyCode.Space))
                velocity += new Vector3(0, 4, 0);
        
            controlledRb.linearVelocity = velocity;
        }
    }
}
