#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Slime
{
    /// <summary>
    /// 材质管理编辑器窗口 - 快速查看和应用所有材质预设
    /// </summary>
    public class MaterialManagerWindow : EditorWindow
    {
        private MaterialConfig materialConfig;
        private Vector2 scrollPosition = Vector2.zero;
        private int selectedMaterialIndex = 0;

        [MenuItem("Window/Material Manager")]
        public static void ShowWindow()
        {
            GetWindow<MaterialManagerWindow>("材质管理器");
        }

        private void OnGUI()
        {
            GUILayout.Label("材质管理器", EditorStyles.boldLabel);
            GUILayout.Space(10);

            // 材质配置选择
            materialConfig = EditorGUILayout.ObjectField(
                "材质配置",
                materialConfig,
                typeof(MaterialConfig),
                false
            ) as MaterialConfig;

            if (materialConfig == null)
            {
                GUILayout.Label("请先创建 MaterialConfig 资源（Tools > Material > Create Material Config）", EditorStyles.helpBox);
                return;
            }

            GUILayout.Space(10);
            GUILayout.Label($"总计 {materialConfig.materials.Length} 个材质预设", EditorStyles.boldLabel);
            GUILayout.Space(10);

            // 滚动列表
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));

            for (int i = 0; i < materialConfig.materials.Length; i++)
            {
                var mat = materialConfig.materials[i];
                
                GUILayout.BeginHorizontal(EditorStyles.helpBox);

                // 颜色预览
                GUI.backgroundColor = mat.baseColor;
                GUILayout.Box("", GUILayout.Width(40), GUILayout.Height(40));
                GUI.backgroundColor = Color.white;

                GUILayout.BeginVertical();
                GUILayout.Label($"[{i}] {mat.name}", EditorStyles.boldLabel);
                if (!string.IsNullOrEmpty(mat.description))
                {
                    GUILayout.Label(mat.description, EditorStyles.wordWrappedLabel);
                }
                GUILayout.Label($"RGB({mat.baseColor.r:F2}, {mat.baseColor.g:F2}, {mat.baseColor.b:F2})", EditorStyles.miniLabel);
                GUILayout.EndVertical();

                GUILayout.Space(10);

                // 快速应用按钮
                if (GUILayout.Button("应用到选中\n对象", GUILayout.Width(80), GUILayout.Height(40)))
                {
                    ApplyToSelected(i);
                }

                GUILayout.EndHorizontal();
                GUILayout.Space(5);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(10);

            // 批量操作
            GUILayout.Label("批量操作", EditorStyles.boldLabel);
            
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("应用到全部选中对象", GUILayout.Height(30)))
            {
                ApplyToAllSelected();
            }
            if (GUILayout.Button("编辑配置", GUILayout.Height(30)))
            {
                EditorGUIUtility.PingObject(materialConfig);
                Selection.activeObject = materialConfig;
            }
            GUILayout.EndHorizontal();
        }

        private void ApplyToSelected(int materialIndex)
        {
            var selectedObjects = Selection.gameObjects;
            if (selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("警告", "请先选择至少一个对象", "确定");
                return;
            }

            foreach (var obj in selectedObjects)
            {
                var applier = obj.GetComponent<MaterialApplier>();
                if (applier == null)
                {
                    applier = obj.AddComponent<MaterialApplier>();
                }
                applier.SetAllMaterialsToConfig(materialIndex);
            }

            EditorUtility.DisplayDialog("成功", $"已应用材质预设 #{materialIndex} 到 {selectedObjects.Length} 个对象", "确定");
        }

        private void ApplyToAllSelected()
        {
            EditorUtility.DisplayDialog("提示", "请在左侧预设列表中点击具体预设来应用", "确定");
        }
    }
}
#endif
