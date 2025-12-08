using UnityEngine;
using UnityEngine.UI;

namespace Slime
{
    public class SlimeUIController : MonoBehaviour
    {
        [SerializeField] private Button jumpButton;
        [SerializeField] private ControllerTest slimeController;

        void Start()
        {
            if (jumpButton != null)
            {
                jumpButton.onClick.AddListener(OnJumpButtonClick);
            }
            
            // 如果没有手动分配，尝试自动查找
            if (slimeController == null)
            {
                slimeController = FindFirstObjectByType<ControllerTest>();
            }
        }

        private void OnJumpButtonClick()
        {
            if (slimeController != null)
            {
                slimeController.TryJump();
            }
        }

        void OnDestroy()
        {
            if (jumpButton != null)
            {
                jumpButton.onClick.RemoveListener(OnJumpButtonClick);
            }
        }
    }
}
