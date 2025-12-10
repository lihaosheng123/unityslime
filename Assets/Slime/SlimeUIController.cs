<<<<<<< HEAD
using UnityEngine;
=======
﻿using UnityEngine;
>>>>>>> 5abb72d8189e8fdcf07b2d87fbf4b0b9c77edee9
using UnityEngine.UI;

namespace Slime
{
    public class SlimeUIController : MonoBehaviour
    {
<<<<<<< HEAD
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
=======
        // 按钮跳跃功能已移除
>>>>>>> 5abb72d8189e8fdcf07b2d87fbf4b0b9c77edee9
    }
}
