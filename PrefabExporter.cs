using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using USD.NET;
using Unity.Formats.USD;
using Newtonsoft.Json.Linq;


//TODO: input: Json file; Output: USD files
namespace TestPrefabExporter
{

    public class PrefabExporter:MonoBehaviour
    {
        // public string PrefabSoucePath = "";

        public string jsonFilePath = @"";

        // public string prefabName = "";

        public string exportFolder = "";

        /// <summary>
        /// 批处理prefab的方法
        /// </summary>
        [ContextMenu("BatchExportPrefab2usd")]
        public void BatchExportPrefab2usd()
        {   
            // 确保路径正确且文件存在
            if (File.Exists(jsonFilePath))
            {
                string jsonContent = File.ReadAllText(jsonFilePath);
                var jsonObject = JObject.Parse(jsonContent);

                // 初始化assetId列表
                List<string> assetIds = new List<string>();

                // 合并assetId列表
                assetIds.AddRange(ExtractAssetIds(jsonObject, "objects", true));
                assetIds.AddRange(ExtractAssetIds(jsonObject, "doors", false));
                assetIds.AddRange(ExtractAssetIds(jsonObject, "windows", false));

                foreach (string assetId in assetIds)
                {
                    string prefabPath = LogPrefabPath(assetId);
                    ExportPrefabAsusd(prefabPath, exportFolder);

                }

                // 输出所有assetId
                Debug.Log("Asset IDs: " + string.Join(", ", assetIds));
                // 输出列表长度
                Debug.Log("Total Asset IDs: " + assetIds.Count);
            }

            else
            {
                Debug.LogError("JSON file not found at the specified path: " + jsonFilePath);
            }

        }


        // string specifiedObjectName
        public static void ExportPrefabAsusd(string prefabPath, string exportFolder)
        {

            // global path to local path
            // string assetPath = PrefabSoucePath.Replace("\\","/").Replace(Application.dataPath, "Assets");
            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            // 使用游戏对象的名称创建导出文件的完整路径
            string fileName = go.name + ".usda";
            string subFolder = go.name;
            string filePath = Path.Combine(exportFolder, subFolder, fileName);

            // 初始化导出场景
            var scene = ExportHelpers.InitForSave(filePath);

            // 导出选定的游戏对象
            ExportHelpers.ExportGameObjects(new GameObject[] { go }, scene, BasisTransformation.SlowAndSafe);
        }

        // 通过Prefab名称获取路径
        public static string LogPrefabPath(string prefabName)
        {
            // 使用AssetDatabase来搜索所有预制体
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");

            foreach (string guid in prefabGuids)
            {
                // 将guid转换为预制体的路径
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // 获取预制体的文件名
                string prefabFileName = System.IO.Path.GetFileNameWithoutExtension(path);

                // 如果文件名与要查找的预制体名称相同，则返回路径
                if (prefabFileName == prefabName)
                {
                    Debug.Log("Prefab path found: " + path);
                    return path; // 停止循环
                }
            }

            // 如果未找到与名称匹配的预制体，则返回空字符串
            Debug.LogWarning("Prefab not found: " + prefabName);
            return "";
        }


        // 提取assetId的方法，返回一个包含assetId的列表
        private List<string> ExtractAssetIds(JObject jsonObject, string key, bool checkChildren)
        {
            var assetIds = new List<string>();
            var items = jsonObject[key] as JArray;

            if (items != null)
            {
                foreach (var item in items)
                {
                    string assetId = item["assetId"]?.ToString();
                    if (assetId != null)
                    {
                        assetIds.Add(assetId);
                    }

                    if (checkChildren)
                    {
                        var children = item["children"] as JArray;
                        if (children != null)
                        {
                            foreach (var child in children)
                            {
                                string childAssetId = child["assetId"]?.ToString();
                                if (childAssetId != null)
                                {
                                    assetIds.Add(childAssetId);
                                }
                            }
                        }
                    }
                }
            }
            return assetIds;
        }


    }
}
