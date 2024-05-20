using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class AssetMap : MonoBehaviour
{
    // 用于存储材料和预制体的字典
    public Dictionary<string, Object> assetMap;

    void Start()
    {
        Debug.Log("Start!");
        assetMap = LoadAllAssets();
        Debug.Log("work!");
    }

    #if UNITY_EDITOR
    private Dictionary<string, Object> LoadAllAssets()
    {
        Dictionary<string, Object> map = new Dictionary<string, Object>();

        // 加载材料
        LoadAssetsOfType("t:Material", map);
        // 加载预制体
        LoadAssetsOfType("t:Prefab", map);

        return map;
    }

    private void LoadAssetsOfType(string typeFilter, Dictionary<string, Object> map)
    {
        string[] assetPaths = AssetDatabase.FindAssets(typeFilter, new[] {"Assets"});
        foreach (string path in assetPaths)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(path);
            Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);

            if (asset != null)
            {
                // 确保名称唯一
                string key = asset.name;
                if (typeFilter == "t:Prefab") key += "_Prefab";
                if (!map.ContainsKey(key))
                {
                    map.Add(key, asset);
                }
                else
                {
                    Debug.LogWarning("Duplicate asset name found: " + key);
                }
            }
        }
        Debug.Log($"Loaded {map.Count} assets of type {typeFilter}");
    }
    #endif
}
