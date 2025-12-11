using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Slime
{
    /// <summary>
    /// 快速应用材质颜色到选中模型
    /// </summary>
    public class MaterialApplier : MonoBehaviour
    {
        [SerializeField]
        private MaterialConfig materialConfig;

        [SerializeField, Range(0, 99)]
        private int materialPresetIndex = 0;

        [SerializeField]
        private Renderer[] targetRenderers;

        /// <summary>
        /// 应用材质配置到当前对象的渲染器
        /// </summary>
        public void ApplyMaterialConfig()
        {
            if (materialConfig == null)
            {
                Debug.LogError("<color=red>[MaterialApplier] 未设置 MaterialConfig！</color>");
                return;
            }

            if (targetRenderers == null || targetRenderers.Length == 0)
            {
                // 尝试自动获取
                targetRenderers = GetComponentsInChildren<Renderer>();
                if (targetRenderers.Length == 0)
                {
                    Debug.LogError("<color=red>[MaterialApplier] 未找到任何 Renderer 组件！</color>");
                    return;
                }
            }

            MaterialPropertyConfig config = materialConfig.GetMaterial(materialPresetIndex);
            if (config == null) return;

            // 记录原始 Shader
            string originalShaderName = "";
            int successCount = 0;

            foreach (var renderer in targetRenderers)
            {
                Material[] materials = renderer.materials;
                for (int i = 0; i < materials.Length; i++)
                {
                    // 记录第一个材质的原始 Shader
                    if (i == 0 && successCount == 0)
                    {
                        originalShaderName = materials[i].shader.name;
                    }

                    materialConfig.ApplyConfig(materials[i], config);
                    successCount++;
                }
                renderer.materials = materials;
            }

            Debug.Log($"<color=green>[MaterialApplier] ✓ 已应用材质 '{config.name}' 到 {targetRenderers.Length} 个渲染器（{successCount} 个材质）</color>");
            Debug.Log($"<color=yellow>[MaterialApplier] Shader 已覆盖: {originalShaderName} → Toon Shader</color>");
        }

        /// <summary>
        /// 快速设置所有材质为指定配置
        /// </summary>
        public void SetAllMaterialsToConfig(int configIndex)
        {
            materialPresetIndex = configIndex;
            ApplyMaterialConfig();
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// 编辑器菜单和快捷方式
    /// </summary>
    public static class MaterialEditorTools
    {
        [MenuItem("Tools/Material/Apply Config to Selected")]
        public static void ApplyConfigToSelected()
        {
            foreach (var obj in Selection.gameObjects)
            {
                var applier = obj.GetComponent<MaterialApplier>();
                if (applier != null)
                {
                    applier.ApplyMaterialConfig();
                }
                else
                {
                    Debug.LogWarning($"<color=orange>{obj.name} 没有 MaterialApplier 组件</color>");
                }
            }
        }

        [MenuItem("Tools/Material/Create Material Config")]
        public static void CreateMaterialConfig()
        {
            // 确保 Materials 文件夹存在
            string materialsFolder = "Assets/Materials";
            if (!System.IO.Directory.Exists(materialsFolder))
            {
                System.IO.Directory.CreateDirectory(materialsFolder);
                AssetDatabase.Refresh();
                Debug.Log($"<color=cyan>[MaterialConfig] 已创建文件夹: {materialsFolder}</color>");
            }

            string path = "Assets/Materials/NewMaterialConfig.asset";
            int counter = 1;
            while (System.IO.File.Exists(path))
            {
                path = $"Assets/Materials/NewMaterialConfig_{counter++}.asset";
            }

            var config = ScriptableObject.CreateInstance<MaterialConfig>();
            
            // 初始化默认材质（所有预设都使用 Toon Shader）
            config.materials = new MaterialPropertyConfig[]
            {
                new MaterialPropertyConfig { name = "White", baseColor = Color.white, description = "纯白色" },
                new MaterialPropertyConfig { name = "Red", baseColor = new Color(1, 0.2f, 0.2f), description = "红色" },
                new MaterialPropertyConfig { name = "Green", baseColor = new Color(0.2f, 1, 0.2f), description = "绿色" },
                new MaterialPropertyConfig { name = "Blue", baseColor = new Color(0.2f, 0.2f, 1), description = "蓝色" },
                new MaterialPropertyConfig { name = "Yellow", baseColor = new Color(1, 1, 0.2f), description = "黄色" },
                new MaterialPropertyConfig { name = "Black", baseColor = Color.black, description = "纯黑色" },
                new MaterialPropertyConfig { name = "Gray", baseColor = new Color(0.5f, 0.5f, 0.5f), description = "灰色" },
                new MaterialPropertyConfig { name = "Cyan", baseColor = new Color(0.2f, 1, 1), description = "青色" },
                new MaterialPropertyConfig { name = "Magenta", baseColor = new Color(1, 0.2f, 1), description = "洋红色" },
                new MaterialPropertyConfig { name = "Orange", baseColor = new Color(1, 0.5f, 0.2f), description = "橙色" },
            };

            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"<color=cyan>[MaterialConfig] 已创建: {path}</color>");
            Debug.Log($"<color=green>[MaterialConfig] ✓ 所有预设将使用 Toon Shader 覆盖原有 Shader</color>");
            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Tools/Material/Setup Scene for Material Management")]
        public static void SetupSceneForMaterialManagement()
        {
            // 查找所有有 Renderer 的对象
            var renderers = Object.FindObjectsOfType<Renderer>();
            int setupCount = 0;

            foreach (var renderer in renderers)
            {
                var parent = renderer.transform;
                
                // 只在最顶层的有 Renderer 的对象上添加
                if (parent.GetComponent<MaterialApplier>() == null)
                {
                    var applier = parent.gameObject.AddComponent<MaterialApplier>();
                    setupCount++;
                }
            }

            Debug.Log($"<color=green>[Setup] 已为 {setupCount} 个对象添加 MaterialApplier 组件</color>");
        }
    }

    /// <summary>
    /// Inspector 自定义编辑器
    /// </summary>
    [CustomEditor(typeof(MaterialApplier))]
    public class MaterialApplierEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            MaterialApplier applier = (MaterialApplier)target;

            GUILayout.Space(10);
            GUILayout.Label("快速操作", EditorStyles.boldLabel);

            if (GUILayout.Button("应用材质配置", GUILayout.Height(35)))
            {
                applier.ApplyMaterialConfig();
            }

            GUILayout.Space(5);

            // 显示 MaterialConfig 中的所有预设快捷按钮
            var config = serializedObject.FindProperty("materialConfig").objectReferenceValue as MaterialConfig;
            if (config != null && config.materials != null && config.materials.Length > 0)
            {
                GUILayout.Label("快速预设", EditorStyles.boldLabel);
                
                int buttonsPerRow = 3;
                int buttonIndex = 0;

                for (int i = 0; i < config.materials.Length; i++)
                {
                    if (buttonIndex % buttonsPerRow == 0)
                    {
                        GUILayout.BeginHorizontal();
                    }

                    var mat = config.materials[i];
                    
                    // 创建彩色按钮
                    GUI.backgroundColor = mat.baseColor;
                    if (GUILayout.Button($"{mat.name}\n({i})", GUILayout.Height(50)))
                    {
                        applier.SetAllMaterialsToConfig(i);
                    }
                    GUI.backgroundColor = Color.white;

                    buttonIndex++;
                    if (buttonIndex % buttonsPerRow == 0)
                    {
                        GUILayout.EndHorizontal();
                    }
                }

                if (buttonIndex % buttonsPerRow != 0)
                {
                    GUILayout.EndHorizontal();
                }
            }
        }
    }
#endif
}
