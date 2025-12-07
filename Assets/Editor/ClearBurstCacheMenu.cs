#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

public static class ClearBurstCacheMenu
{
    [MenuItem("Tools/Clear Burst Cache and Restart")]
    public static void ClearBurstCache()
    {
        string libraryPath = Path.Combine(Application.dataPath, "..", "Library");
        string burstCachePath = Path.Combine(libraryPath, "Burst");
        string burstDebugPath = Path.Combine(libraryPath, "BurstCache");
        
        int deletedCount = 0;
        
        if (Directory.Exists(burstCachePath))
        {
            try
            {
                Directory.Delete(burstCachePath, true);
                deletedCount++;
                Debug.Log($"[ClearBurstCache] Deleted: {burstCachePath}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ClearBurstCache] Failed to delete {burstCachePath}: {e.Message}");
            }
        }
        
        if (Directory.Exists(burstDebugPath))
        {
            try
            {
                Directory.Delete(burstDebugPath, true);
                deletedCount++;
                Debug.Log($"[ClearBurstCache] Deleted: {burstDebugPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ClearBurstCache] Failed to delete {burstDebugPath}: {e.Message}");
            }
        }
        
        if (deletedCount > 0)
        {
            Debug.Log("[ClearBurstCache] Burst cache cleared. Please restart Unity Editor!");
            EditorUtility.DisplayDialog("Burst Cache Cleared", 
                "Burst cache has been deleted.\n\nPlease restart Unity Editor for changes to take effect.", 
                "OK");
        }
        else
        {
            Debug.Log("[ClearBurstCache] No Burst cache found to delete.");
            EditorUtility.DisplayDialog("Burst Cache", 
                "No Burst cache found to delete.", 
                "OK");
        }
    }
}
#endif
