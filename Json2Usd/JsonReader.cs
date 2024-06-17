using System.IO;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using System.Linq;
using UnityStandardAssets.Characters.FirstPerson;
using System;
using MessagePack.Resolvers;
using MessagePack.Formatters;
using MessagePack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Thor.Procedural;
using Thor.Procedural.Data;
using UnityEngine.AI;
using UnityEditor;
using USD.NET;
using Unity.Formats.USD;
using TestPrefabExporter;
public class JsonReader : MonoBehaviour
{
    public string folderPath = @"C:/Users/zhiyuan/Desktop/house_plans";
    public string outputFolder = @"C:/Users/zhiyuan/Desktop/exported_usd";
    public Dictionary<string, UnityEngine.Object> assetMap;


    [ContextMenu("ReadJson")]
    public void ReadJsonFile()
    {
        Debug.Log("Start!");

        assetMap = LoadAllAssets();

        // Get all JSON files in the folder
        string[] jsonFiles = Directory.GetFiles(folderPath, "*.json");

        foreach (string jsonFilePath in jsonFiles){
            
            // Read file contents
            string jsonData = File.ReadAllText(jsonFilePath);

            // Deserialize JSON to ProceduralHouse class
            ProceduralHouse house = JsonConvert.DeserializeObject<ProceduralHouse>(jsonData);

            var windowsAndDoors = house.doors.Select(d => d as WallRectangularHole).Concat(house.windows);
            
            var holes = windowsAndDoors
                .SelectMany(hole => new List<(string, WallRectangularHole)> { (hole.wall0, hole), (hole.wall1, hole) })
                .Where(pair => !String.IsNullOrEmpty(pair.Item1))
                .ToDictionary(pair => pair.Item1, pair => pair.Item2);

            var walls = house.walls.Select(w => polygonWallToSimpleWall(w, holes));

            var worldGO = new GameObject("World");
            var structureGO = new GameObject("Structure");
            structureGO.transform.parent = worldGO.transform;

            var wallsGO = createWalls(walls, assetMap, house.proceduralParameters, "Walls");
            wallsGO.transform.parent = structureGO.transform;

            var ceilingGO = createCeilingGameObject(house, walls, assetMap);
            ceilingGO.transform.parent = structureGO.transform;

            var floorsGo = createFloorGameObject(house, assetMap);
            floorsGo.transform.parent = structureGO.transform;

            var objectsGO = new GameObject("Objects");
            objectsGO.transform.parent = worldGO.transform;

            foreach (var obj in house.objects) {
                spawnObjectHierarchy(obj, assetMap);
            }
            // spawnWindowsAndDoors(windowsAndDoors, assetMap);

            var doorsToWalls = windowsAndDoors.Select(
                door => (
                    door,
                    wall0: walls.First(w => w.id == door.wall0),
                    wall1: walls.FirstOrDefault(w => w.id == door.wall1)
                )
            ).ToDictionary(d => d.door.id, d => (d.wall0, d.wall1));
            var count = 0;
            foreach (WallRectangularHole holeCover in windowsAndDoors) {

                // var coverPrefab = assetMap.getAsset(holeCover.assetId);
                GameObject coverPrefab = assetMap[holeCover.assetId]as GameObject;

                // string prefabPath = PrefabExporter.LogPrefabPath(holeCover.assetId);
                // GameObject coverPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                (Wall wall0, Wall wall1) wall;
                var wallExists = doorsToWalls.TryGetValue(holeCover.id, out wall);


                if (wallExists) {
                    var p0p1 = wall.wall0.p1 - wall.wall0.p0;

                    var p0p1_norm = p0p1.normalized;
                    var normal = Vector3.Cross(Vector3.up, p0p1_norm);
                    Vector3 pos;

                    var holeBB = getHoleBoundingBox(holeCover);
       
                    bool positionFromCenter = true;
                    Vector3 assetOffset = holeCover.assetPosition;
                    pos = wall.wall0.p0 + (p0p1_norm * (assetOffset.x)) + Vector3.up * (assetOffset.y); 
                
                    var rotY = getWallDegreesRotation(new Wall { p0 = wall.wall0.p1, p1 = wall.wall0.p0 });
                    var rotation = Quaternion.AngleAxis(rotY, Vector3.up);

                    var go = spawnSimObjPrefab(
                        prefab: coverPrefab,
                        id: holeCover.id,
                        assetId: holeCover.assetId,
                        position: pos,
                        rotation: rotation,
                        // collisionDetectionMode: collDet,
                        kinematic: true,
                        materialProperties: holeCover.material,
                        positionBoundingBoxCenter: positionFromCenter,
                        scale: holeCover.scale
                    );

                    var canOpen = go.GetComponentInChildren<CanOpen_Object>();
                    if (canOpen != null) {
                        canOpen.SetOpennessImmediate(holeCover.openness);
                    }
                    count++;
                }
            }

            foreach (var obj in house.objects) {
                AdjustChildrenPositions(obj);
            }


            //convert to z up and right handed system 
            // Quaternion worldRotation = Quaternion.Euler(-90, 0, 0);
            // worldGO.transform.localRotation = worldRotation * worldGO.transform.localRotation;

            // Vector3 worldScale = worldGO.transform.localScale;
            // worldScale.z *= -1;
            // worldGO.transform.localScale = worldScale;
            
            string subfolder = Path.GetFileNameWithoutExtension(jsonFilePath);
            string fileName = "house_" + subfolder;

            // Use Path.Combine to combine folder paths and file names
            string fullPathWithoutExtension = Path.Combine(outputFolder, subfolder, fileName);

            //Add or change file extension using Path.ChangeExtension
            string outputPath = Path.ChangeExtension(fullPathWithoutExtension, ".usda");

            // Initialize the export scene
            var scene = ExportHelpers.InitForSave(outputPath);

            // test 
            GameObject targetObject = GameObject.Find("Vase|surface|6|22");
            if (targetObject != null)
            {
                // 获取对象的坐标
                Vector3 position = targetObject.transform.position;
                Debug.Log("Target Object Position: " + position.ToString("F6"));
            }

            // Export selected game objects
            ExportHelpers.ExportGameObjects(new GameObject[] {worldGO}, scene, BasisTransformation.SlowAndSafe);
            // delete the world
            // DestroyImmediate(worldGO);
        }     
        Debug.Log("finished!"); 

    }


    private static Wall polygonWallToSimpleWall(PolygonWall wall, Dictionary<string, WallRectangularHole> holes) {
        //wall.polygon.
        var polygons = wall.polygon.OrderBy(p => p.y);
        var maxY = wall.polygon.Max(p => p.y);
        WallRectangularHole val;
        var hole = holes.TryGetValue(wall.id, out val) ? val : null;
        var p0 = polygons.ElementAt(0);
        return new Wall() {
            id = wall.id,
            p0 = polygons.ElementAt(0),
            p1 = polygons.ElementAt(1),
            height = maxY - p0.y,
            material = wall.material,
            empty = wall.empty,
            roomId = wall.roomId,
            thickness = wall.thickness,
            hole = hole,
            layer = wall.layer,
        };
    }

    public static string DefaultHouseRootObjectName => "Floor";
    public static string DefaultRootStructureObjectName => "Structure";

    public static string DefaultRootWallsObjectName => "Walls";

    public static string DefaultCeilingRootObjectName => "Ceiling";

    public static string DefaultLightingRootName => "ProceduralLighting";
    public static string DefaultObjectsRootName => "Objects";


    public static void spawnObjectHierarchy(HouseObject houseObject, Dictionary<string, UnityEngine.Object> assetMap) {
        if (houseObject == null) {
            return;
        }

        var go = spawnHouseObject(assetMap, houseObject);


        if (houseObject.children != null) {
            foreach (var child in houseObject.children) {
                spawnObjectHierarchy(child, assetMap);
            }
        }
    }

    //generic function to spawn object in scene. No bounds or collision checks done
    public static GameObject spawnHouseObject(
        Dictionary<string, UnityEngine.Object> assetMap,
        HouseObject ho
    ) {
        Debug.Log(ho.assetId);
        GameObject go = assetMap[ho.assetId] as GameObject;
        // string prefabPath = PrefabExporter.LogPrefabPath(ho.assetId);
        // GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        // collisionDetectionMode = "Discrete";
        
        return spawnSimObjPrefab(
            prefab: go,
            id: ho.id,
            assetId: ho.assetId,
            position: ho.position,
            // ho.rotation,
            rotation: Quaternion.AngleAxis(ho.rotation.degrees, ho.rotation.axis),
            kinematic: ho.kinematic,
            positionBoundingBoxCenter: true,
            unlit: ho.unlit,
            materialProperties: ho.material,
            openness: ho.openness,
            isOn: ho.isOn,
            isDirty: ho.isDirty,
            layer: ho.layer
            // collisionDetectionMode: collisionDetectionMode
        );

    }


    public static GameObject createWalls(IEnumerable<Wall> walls, Dictionary<string, UnityEngine.Object> assetMap, ProceduralParameters proceduralParameters, string gameObjectId = "Structure") {
        var structure = new GameObject(gameObjectId);

        var wallsPerRoom = walls.GroupBy(w => w.roomId).Select(m => m.ToList()).ToList();

        var zip3 = wallsPerRoom.Select( walls => walls.Zip(
            walls.Skip(1).Concat(new Wall[] { walls.FirstOrDefault() }),
            (w0, w1) => (w0, w1)
        ).Zip(
            new Wall[] { walls.LastOrDefault() }.Concat(walls.Take(walls.Count() - 1)),
            (wallPair, w2) => (wallPair.w0, w2, wallPair.w1)
        )).ToList();

        // zip3 = zip3.Reverse().ToArray();

        var index = 0;
        foreach (var wallTuples in zip3) {
            foreach ((Wall w0, Wall w1, Wall w2) in wallTuples) {
                if (!w0.empty) {
                    var wallGO = createAndJoinWall(
                        index,
                        assetMap,
                        w0,
                        w1, 
                        w2,
                        squareTiling: proceduralParameters.squareTiling,
                        minimumBoxColliderThickness: proceduralParameters.minWallColliderThickness,
                        layer: (
                            String.IsNullOrEmpty(w0.layer)
                            ? LayerMask.NameToLayer("SimObjVisible")
                            : LayerMask.NameToLayer(w0.layer)
                        ), 
                        backFaces: false // TODO param from json
                    );

                    wallGO.transform.parent = structure.transform;
                    index++;
                }
            }
        }
        return structure;
    }




    public static GameObject createAndJoinWall(
        int index,
        Dictionary<string, UnityEngine.Object> assetMap,
        Wall toCreate,
        Wall previous = null,
        Wall next = null,
        float visibilityPointInterval = 1 / 3.0f,
        float minimumBoxColliderThickness = 0.1f,
        bool globalVertexPositions = false,
        int layer = 8,
        bool squareTiling = false,
        bool backFaces = false
    ) 
    {
        var wallGO = new GameObject(toCreate.id);

        SetLayer<Transform>(wallGO, layer);

        var meshF = wallGO.AddComponent<MeshFilter>();

        Vector3 boxCenter = Vector3.zero;
        Vector3 boxSize = Vector3.zero;

        var generateBackFaces = backFaces;
        const float zeroThicknessEpsilon = 1e-4f;
        var colliderThickness = toCreate.thickness < zeroThicknessEpsilon ? minimumBoxColliderThickness : toCreate.thickness;

        var p0p1 = toCreate.p1 - toCreate.p0;

        var mesh = new Mesh();

        var p0p1_norm = p0p1.normalized;

        var normal = Vector3.Cross(p0p1_norm, Vector3.up);

        var center = toCreate.p0 + p0p1 * 0.5f + Vector3.up * toCreate.height * 0.5f + normal * toCreate.thickness * 0.5f;
        var width = p0p1.magnitude;

        Vector3 p0;
        Vector3 p1;
        var theta = -Mathf.Sign(p0p1_norm.z) * Mathf.Acos(Vector3.Dot(p0p1_norm, Vector3.right));

        if (globalVertexPositions) {

            p0 = toCreate.p0;
            p1 = toCreate.p1;

            boxCenter = center;
        } else {
            p0 = -(width / 2.0f) * Vector3.right - new Vector3(0.0f, toCreate.height / 2.0f, toCreate.thickness / 2.0f);
            p1 = (width / 2.0f) * Vector3.right - new Vector3(0.0f, toCreate.height / 2.0f, toCreate.thickness / 2.0f);

            normal = Vector3.forward;
            p0p1_norm = Vector3.right;
            
            wallGO.transform.position = center;

            wallGO.transform.rotation = Quaternion.AngleAxis(theta * 180.0f / Mathf.PI, Vector3.up);
        }

        var colliderOffset = Vector3.zero;//toCreate.thickness < zeroThicknessEpsilon ? normal * colliderThickness : Vector3.zero;

        boxCenter += colliderOffset;

        boxSize = new Vector3(width, toCreate.height, colliderThickness);

        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var uv = new List<Vector2>();
        var normals = new List<Vector3>();

        var min = p0;
        var max = p1 + new Vector3(0.0f, toCreate.height, 0.0f);

        IEnumerable<BoundingBox> colliderBoundingBoxes = new List<BoundingBox>();

        if (toCreate.hole != null) {

            if (toCreate.hole.holePolygon != null && toCreate.hole.holePolygon.Count != 2) {
                Debug.LogWarning($"Invalid `holePolygon` on object of id '{toCreate.hole.id}', only supported rectangle holes, 4 points in polygon. Using `boundingBox` instead.");
                if (toCreate.hole.holePolygon.Count < 2) {
                    throw new ArgumentException("$Invalid `holePolygon` on object of id '{toCreate.hole.id}', only supported rectangle holes, 4 points in polygon, polygon has {toCreate.hole.holePolygon.Count}.");
                }
            }

            var holeBB = getHoleBoundingBox(toCreate.hole);

            var dims = holeBB.max - holeBB.min;
            var offset = new Vector2(
                holeBB.min.x, holeBB.min.y
            );

            if (toCreate.hole.wall1 == toCreate.id) {
                offset = new Vector2(
                    width - holeBB.max.x, holeBB.min.y
                );
            }

            colliderBoundingBoxes = new List<BoundingBox>() {
                new BoundingBox() {min = p0, max =  p0
                        + p0p1_norm * offset.x
                        + Vector3.up * (toCreate.height)},
                    new BoundingBox() {
                    min = p0
                        + p0p1_norm * offset.x
                        + Vector3.up * (offset.y + dims.y),
                    max = p0
                        + p0p1_norm * (offset.x + dims.x)
                        + Vector3.up * (toCreate.height)},
                new BoundingBox() {
                    min = p0
                        + p0p1_norm * (offset.x + dims.x),
                    max = p1 + Vector3.up * (toCreate.height)},
                new BoundingBox() {
                    min = p0
                        + p0p1_norm * offset.x,
                    max = p0
                        + p0p1_norm * (offset.x + dims.x)
                        + Vector3.up * (offset.y)
                }
            };
            const float areaEps =0.0001f;
            colliderBoundingBoxes = colliderBoundingBoxes.Where(bb => Math.Abs(GetBBXYArea(bb)) > areaEps).ToList();

            
            vertices = new List<Vector3>() {
                    p0,
                    p0 + new Vector3(0.0f, toCreate.height, 0.0f),
                    p0 + p0p1_norm * offset.x
                        + Vector3.up * offset.y,
                    p0
                        + p0p1_norm * offset.x
                        + Vector3.up * (offset.y + dims.y),

                    p1 +  new Vector3(0.0f, toCreate.height, 0.0f),

                    p0
                        + p0p1_norm * (offset.x + dims.x)
                        + Vector3.up * (offset.y + dims.y),

                    p1,

                    p0
                    + p0p1_norm * (offset.x + dims.x)
                    + Vector3.up * offset.y

                };
            
            
            triangles = new List<int>() {
                    0, 1, 2, 1, 3, 2, 1, 4, 3, 3, 4, 5, 4, 6, 5, 5, 6, 7, 7, 6, 0, 0, 2, 7
            };


                // This would be for a left hand local axis space, so front being counter-clockwise of topdown polygon from inside the polygon
            // triangles = new List<int>() {
            //     7, 2, 0, 0, 6, 7, 7, 6, 5, 5, 6, 4, 5, 4, 3, 3, 4, 1, 2, 3, 1, 2, 1, 0
            // };
            
            if (toCreate.id == "wall_0_2") {
                
                Debug.Log($"---------- globalPos: {globalVertexPositions}, p0: {p0.ToString("F5")}, p1: {p0.ToString("F5")}, p0p1_norm: {p0p1_norm.ToString("F5")}, offset: {offset}");
            }
            var toRemove = new List<int>();
            // const float areaEps = 1e-4f;
            for (int i = 0; i < triangles.Count/3; i++) {
                var i0 = triangles[i*3];
                var i1 = triangles[ i*3 + 1];
                var i2 = triangles[ i*3 + 2];
                var area = TriangleArea(vertices, i0, i1, i2);
                
                if (area <= areaEps) {
                    toRemove.AddRange(new List<int>() { i*3, i*3 + 1, i*3 + 2 });
                }
            }
            var toRemoveSet = new HashSet<int>(toRemove);
            triangles = triangles.Where((t, i) => !toRemoveSet.Contains(i)).ToList();

            if (generateBackFaces) {
                triangles.AddRange(triangles.AsEnumerable().Reverse().ToList());
            }

        } else {

            vertices = new List<Vector3>() {
                    p0,
                    p0 + new Vector3(0.0f, toCreate.height, 0.0f),
                    p1 +  new Vector3(0.0f, toCreate.height, 0.0f),
                    p1
                };
            
            triangles = new List<int>() { 1, 2, 0, 2, 3, 0 };

            // Counter clockwise wall definition left hand rule
            // triangles = new List<int>() { 0, 3, 2, 0, 2, 1 };
            if (generateBackFaces) {
                triangles.AddRange(triangles.AsEnumerable().Reverse().ToList());
            }
        }

        normals = Enumerable.Repeat(-normal, vertices.Count).ToList();
        // normals = Enumerable.Repeat(normal, vertices.Count).ToList();//.Concat(Enumerable.Repeat(-normal, vertices.Count)).ToList();

        uv = vertices.Select(v =>
            new Vector2(Vector3.Dot(p0p1_norm, v - p0) / width, v.y / toCreate.height))
        .ToList();

        mesh.vertices = vertices.ToArray();
        mesh.uv = uv.ToArray();
        mesh.normals = normals.ToArray();
        mesh.triangles = triangles.ToArray();
        meshF.sharedMesh = mesh;
        var meshRenderer = wallGO.AddComponent<MeshRenderer>();

        if (toCreate.hole != null) {
            // var meshCollider  = wallGO.AddComponent<MeshCollider>();
            // meshCollider.sharedMesh = mesh;
            
            var holeColliders = new GameObject($"Colliders");

            
            
            holeColliders.transform.parent = wallGO.transform;
            holeColliders.transform.localPosition = Vector3.zero;
            holeColliders.transform.localRotation = Quaternion.identity;

            var i = 0;
            foreach (var boundingBox in colliderBoundingBoxes) {

                var colliderObj = new GameObject($"Collider_{i}");
                colliderObj.transform.parent = holeColliders.transform;
                colliderObj.transform.localPosition = Vector3.zero;
                colliderObj.transform.localRotation = Quaternion.identity;
                colliderObj.tag = "SimObjPhysics";
                colliderObj.layer = 8;
                var boxCollider = colliderObj.AddComponent<BoxCollider>();
                boxCollider.center = boundingBox.center();
                boxCollider.size = boundingBox.size() + Vector3.forward * colliderThickness;

            }
            
        }
        else {
            var boxC = wallGO.AddComponent<BoxCollider>();
            boxC.center = boxCenter;
            boxC.size = boxSize;
        }

        var dimensions = new Vector2(p0p1.magnitude, toCreate.height);
        var prev_p0p1 = previous.p1 - previous.p0;


        var prevOffset = getWallMaterialOffset(previous.id).GetValueOrDefault(Vector2.zero);
        var offsetX = (prev_p0p1.magnitude / previous.material.tilingDivisorX.GetValueOrDefault(1.0f)) - Mathf.Floor(prev_p0p1.magnitude / previous.material.tilingDivisorX.GetValueOrDefault(1.0f)) + prevOffset.x;

        // var mat = Resources.Load<Material>($"QuickMaterials/Ceramics/{toCreate.material.name}");
        var obj = assetMap[toCreate.material.name];
        if (obj is Material mat)
        {
            meshRenderer.material = generatePolygonMaterial(
                mat, 
                dimensions, 
                toCreate.material, 
                offsetX, 
                0.0f, 
                squareTiling: squareTiling
            );
        }
        else
        {
            // 转换失败的处理
            Debug.LogError("The asset is not a Material.");
        }




        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;

        meshF.sharedMesh.RecalculateBounds();

        return wallGO;
    }

    private static float getWallDegreesRotation(Wall wall) {
        var p0p1 = wall.p1 - wall.p0;

        var p0p1_norm = p0p1.normalized;

        var theta = -Mathf.Sign(p0p1_norm.z) * Mathf.Acos(Vector3.Dot(p0p1_norm, Vector3.right));
        return theta * 180.0f / Mathf.PI;
    }
    private static Vector2? getWallMaterialOffset(string wallId) {
        var wallGO = GameObject.Find(wallId);
        if (wallGO == null) {
            return null;
        }
            var renderer = wallGO.GetComponent<MeshRenderer>();
            if (renderer == null) {
                return null;
            }
        return renderer.sharedMaterial.mainTextureOffset;
    }



    private static Material generatePolygonMaterial(Material sharedMaterial, Vector2 dimensions, MaterialProperties materialProperties = null, float offsetX = 0.0f, float offsetY = 0.0f, bool squareTiling = false) {
        // optimization do not copy when not needed
        // Almost never happens because material continuity requires tilings offsets for every following wall
        if (materialProperties == null && materialProperties.color == null && !materialProperties.tilingDivisorX.HasValue && !materialProperties.tilingDivisorY.HasValue && offsetX == 0.0f && offsetY == 0.0f && !materialProperties.unlit) {
            return sharedMaterial;
        }

        var materialCopy = new Material(sharedMaterial);

        if (materialProperties.color != null) {
            materialCopy.color = materialProperties.color.toUnityColor();
        }

        var tilingX = dimensions.x / materialProperties.tilingDivisorX.GetValueOrDefault(1.0f);
        var tilingY = dimensions.y / materialProperties.tilingDivisorY.GetValueOrDefault(1.0f);

        if (squareTiling) {
            tilingX = Math.Max(tilingX, tilingY);
            tilingY = tilingX;
        }

        materialCopy.mainTextureScale = new Vector2(tilingX, tilingY);
        materialCopy.mainTextureOffset = new Vector2(offsetX, offsetY);
                
        if (materialProperties.unlit) {
            var shader = Shader.Find("Unlit/Color");
            materialCopy.shader = shader;
        }

        if (materialProperties.metallic != null) {
            materialCopy.SetFloat("_Metallic", materialProperties.metallic.GetValueOrDefault(0.0f));
        }

        if (materialProperties.metallic != null) {
            materialCopy.SetFloat("_Glossiness", materialProperties.smoothness.GetValueOrDefault(0.0f));
        }

        return materialCopy;
    }


    public static void SetLayer<T>(GameObject go, int layer) where T : Component {
        if (go.GetComponent<T>() != null) {
            go.layer = layer;
        }
        foreach (Transform child in go.transform) {
            SetLayer<T>(child.gameObject, layer);
        }
    }
    private static BoundingBox getHoleBoundingBox(WallRectangularHole hole) {
        if (hole.holePolygon == null || hole.holePolygon.Count < 2) {
            throw new ArgumentException($"Invalid `holePolygon` for object id: '{hole.id}'. Minimum 2 vertices indicating first min and second max of hole bounding box.");
        }
        return new BoundingBox() {
            min = hole.holePolygon[0],
            max = hole.holePolygon[1]
        };
    }
    private static float TriangleArea(List<Vector3> vertices, int index0, int index1, int index2) {
        Vector3 a = vertices[index0];
        Vector3 b = vertices[index1];
        Vector3 c = vertices[index2];
        Vector3 cross = Vector3.Cross(a-b, a-c);
        float area = cross.magnitude * 0.5f;
        return area;
    }

    private static float GetBBXYArea(BoundingBox bb) {
        var diff = bb.max - bb.min;
        return diff.x * diff.y;
    }

    private static Mesh GenerateFloorMesh(IEnumerable<Vector3> floorPolygon, float yOffset = 0.0f, bool clockWise = false) {

        // Get indices for creating triangles
        var m_points = floorPolygon.Select(p => new Vector2(p.x, p.z)).ToArray();

        var triangleIndices = TriangulateVertices();

        // Get array of vertices for floor
        var floorVertices = m_points.Select(p => new Vector3(p.x, yOffset, p.y)).ToArray();

        // Create the mesh
        var floor = new Mesh();
        floor.vertices = floorVertices;
        floor.triangles = triangleIndices;
        floor.RecalculateNormals();
        floor.RecalculateBounds();

        // Get UVs for mesh's vertices
        floor.uv = GenerateUVs();
        return floor;

        int[] TriangulateVertices() {
            List<int> indices = new List<int>();

            int n = m_points.Length;
            if (n < 3) {
                return indices.ToArray();
            }

            int[] V = new int[n];
            if (Area() > 0) {
                for (int v = 0; v < n; v++) {
                    V[v] = v;
                }
            } else {
                for (int v = 0; v < n; v++) {
                    V[v] = (n - 1) - v;
                }
            }

            int nv = n;
            int count = 2 * nv;
            for (int v = nv - 1; nv > 2;) {
                if ((count--) <= 0) {
                    return indices.ToArray();
                }

                int u = v;
                if (nv <= u) {
                    u = 0;
                }

                v = u + 1;
                if (nv <= v) {
                    v = 0;
                }

                int w = v + 1;
                if (nv <= w) {
                    w = 0;
                }

                if (Snip(u, v, w, nv, V)) {
                    int a, b, c, s, t;
                    a = V[u];
                    b = V[v];
                    c = V[w];

                    if (!clockWise) { 
                        indices.Add(a);
                        indices.Add(b);
                        indices.Add(c);
                    }
                    else {
                            indices.Add(c);
                            indices.Add(b);
                        indices.Add(a);
                    }
                    for (s = v, t = v + 1; t < nv; s++, t++) {
                        V[s] = V[t];
                    }

                    nv--;
                    count = 2 * nv;
                }
            }

            indices.Reverse();
            return indices.ToArray();
        }

        float Area() {
            int n = m_points.Length;
            float A = 0.0f;
            for (int p = n - 1, q = 0; q < n; p = q++) {
                Vector2 pval = m_points[p];
                Vector2 qval = m_points[q];
                A += pval.x * qval.y - qval.x * pval.y;
            }
            return (A * 0.5f);
        }

        bool Snip(int u, int v, int w, int n, int[] V) {
            int p;
            Vector2 A = m_points[V[u]];
            Vector2 B = m_points[V[v]];
            Vector2 C = m_points[V[w]];
            if (Mathf.Epsilon > (((B.x - A.x) * (C.y - A.y)) - ((B.y - A.y) * (C.x - A.x)))) {
                return false;
            }

            for (p = 0; p < n; p++) {
                if ((p == u) || (p == v) || (p == w)) {
                    continue;
                }

                Vector2 P = m_points[V[p]];
                if (InsideTriangle(A, B, C, P)) {
                    return false;
                }
            }

            return true;
        }

        bool InsideTriangle(Vector2 A, Vector2 B, Vector2 C, Vector2 P) {
            float ax, ay, bx, by, cx, cy, apx, apy, bpx, bpy, cpx, cpy;
            float cCROSSap, bCROSScp, aCROSSbp;

            ax = C.x - B.x; ay = C.y - B.y;
            bx = A.x - C.x; by = A.y - C.y;
            cx = B.x - A.x; cy = B.y - A.y;
            apx = P.x - A.x; apy = P.y - A.y;
            bpx = P.x - B.x; bpy = P.y - B.y;
            cpx = P.x - C.x; cpy = P.y - C.y;

            aCROSSbp = ax * bpy - ay * bpx;
            cCROSSap = cx * apy - cy * apx;
            bCROSScp = bx * cpy - by * cpx;

            return ((aCROSSbp >= 0.0f) && (bCROSScp >= 0.0f) && (cCROSSap >= 0.0f));
        }

        Vector2[] GenerateUVs() {
            Vector2[] uvArray = new Vector2[m_points.Length];
            float texelDensity = 5f;

            for (int i = 0; i < m_points.Length; i++) {
                uvArray[i] = (m_points[i] - m_points[0]) / texelDensity;
            }

            return uvArray;
        }
    }


    private static GameObject createFloorGameObject(ProceduralHouse house, Dictionary<string, UnityEngine.Object> assetMap) {
        // change if MinY is not 0
        float wallsMinY = 0;
        var floorGameObject = ProceduralTools.createSimObjPhysicsGameObject(
            name: "Floor",
            position: new Vector3(0, wallsMinY, 0) ,
            withRigidBody: false
        );

        for (int i = 0; i < house.rooms.Count(); i++) {
            var room = house.rooms.ElementAt(i);
            var subFloorGO = ProceduralTools.createSimObjPhysicsGameObject(room.id);
            var mesh = GenerateFloorMesh(room.floorPolygon);

            subFloorGO.GetComponent<MeshFilter>().mesh = mesh;
            var meshRenderer = subFloorGO.GetComponent<MeshRenderer>();



            // var floorMat = Resources.Load<Material>($"QuickMaterials/Wood/{room.floorMaterial.name}");
            var obj = assetMap[room.floorMaterial.name];

            // 尝试将 obj 转换为 Material
            if (obj is Material floorMat)
            {
                if (floorMat == null) {
                    Debug.Log("Shared material cannot be null");}


                var dimensions = getAxisAlignedWidthDepth(room.floorPolygon);


                meshRenderer.material = generatePolygonMaterial(
                    sharedMaterial: floorMat,
                    materialProperties: room.floorMaterial,
                    dimensions: dimensions, 
                    squareTiling: house.proceduralParameters.squareTiling 
                );
            }
            else
            {
                // 转换失败的处理
                Debug.LogError("The asset is not a Material.");
            }



            if (!String.IsNullOrEmpty(room.layer)) {
                SetLayer<MeshRenderer>(subFloorGO, LayerMask.NameToLayer(room.layer));
            }
            subFloorGO.transform.parent = floorGameObject.transform;
        }
        
        return floorGameObject;
    }

    private static Vector2 getAxisAlignedWidthDepth(IEnumerable<Vector3> polygon) {
            // TODO: include rotation in json for floor and ceiling to compute the real scale not axis aligned scale

        if (polygon.Count() > 1) {
            var maxX = polygon.Max(p => p.x);
            var maxZ = polygon.Max(p => p.z);

            var minX = polygon.Min(p => p.x);
            var minZ = polygon.Min(p => p.z);
        
            var width =  maxX - minX;
            var depth = maxZ - minZ;
            return new Vector2(width, depth);
        }
        return Vector2.zero;
    }

    // private static void spawnWindowsAndDoors<T>(T windowsAndDoors, Dictionary<string, UnityEngine.Object> assetMap)
    // {
    //     var doorsToWalls = windowsAndDoors.Select(
    //         door => (
    //             door,
    //             wall0: walls.First(w => w.id == door.wall0),
    //             wall1: walls.FirstOrDefault(w => w.id == door.wall1)
    //         )
    //     ).ToDictionary(d => d.door.id, d => (d.wall0, d.wall1));
    //     var count = 0;
    //     foreach (WallRectangularHole holeCover in windowsAndDoors) {

    //         GameObject coverPrefab = assetMap[holeCover.assetId]as GameObject;
    //         // string prefabPath = PrefabExporter.LogPrefabPath(holeCover.assetId);
    //         // GameObject coverPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

    //         (Wall wall0, Wall wall1) wall;
    //         var wallExists = doorsToWalls.TryGetValue(holeCover.id, out wall);


    //         if (wallExists) {
    //             var p0p1 = wall.wall0.p1 - wall.wall0.p0;

    //             var p0p1_norm = p0p1.normalized;
    //             var normal = Vector3.Cross(Vector3.up, p0p1_norm);
    //             Vector3 pos;

    //             var holeBB = getHoleBoundingBox(holeCover);

    //             bool positionFromCenter = true;
    //             Vector3 assetOffset = holeCover.assetPosition;
    //             pos = wall.wall0.p0 + (p0p1_norm * (assetOffset.x)) + Vector3.up * (assetOffset.y); 
            
    //             var rotY = getWallDegreesRotation(new Wall { p0 = wall.wall0.p1, p1 = wall.wall0.p0 });
    //             var rotation = Quaternion.AngleAxis(rotY, Vector3.up);

    //             var go = spawnSimObjPrefab(
    //                 prefab: coverPrefab,
    //                 id: holeCover.id,
    //                 assetId: holeCover.assetId,
    //                 position: pos,
    //                 rotation: rotation,
    //                 // collisionDetectionMode: collDet,
    //                 kinematic: true,
    //                 materialProperties: holeCover.material,
    //                 positionBoundingBoxCenter: positionFromCenter,
    //                 scale: holeCover.scale
    //             );

    //             var canOpen = go.GetComponentInChildren<CanOpen_Object>();
    //             if (canOpen != null) {
    //                 canOpen.SetOpennessImmediate(holeCover.openness);
    //             }

    //             count++;
    //         }
    //     }
    // }
    public static void AdjustChildrenPositions(
        HouseObject houseObject
    ) {
        if (houseObject == null) {
            return;
        }

        // 检查 houseObject 是否有子对象
        if (houseObject.children != null && houseObject.children.Count > 0) {
            // 获取当前对象
            var parentGO = GameObject.Find(houseObject.id); // 根据 id 查找已生成的 GameObject
            if (parentGO == null) {
                Debug.LogError($"Parent object {houseObject.id} not found in the scene.");
                return;
            }
            // 在子对象中查找 ReceptacleTriggerBox
            var receptacleTriggerBoxGO = parentGO.transform.Find("ReceptacleTriggerBox") ??
                                        parentGO.transform.Find("ReceptacleTriggerBox (1)") ??
                                        parentGO.transform.Find("ReceptacleTriggerBox (2)");

            if (receptacleTriggerBoxGO == null) {
                Debug.LogError($"ReceptacleTriggerBox not found in {houseObject.id}'s children.");
                return;
            }

            var receptacleCollider = receptacleTriggerBoxGO.GetComponent<BoxCollider>();
            if (receptacleCollider == null) {
                Debug.LogError($"ReceptacleTriggerBox {houseObject.id} does not have a BoxCollider component.");
                return;
            }

            float bottomY = receptacleCollider.bounds.min.y;

            foreach (var child in houseObject.children) {
                // 处理子对象位置
                ProcessChildPosition(child, bottomY);

                // 递归调用 AdjustChildrenPositions 处理子对象的子对象
                AdjustChildrenPositions(child);
            }
        }
    }

    // 处理子对象的位置
    private static void ProcessChildPosition(HouseObject child, float parentBottomY) {
        // 获取子对象
        var childGO = GameObject.Find(child.id);
        if (childGO == null) {
            Debug.LogError($"Child object {child.id} not found in the scene.");
            return;
        }

        // 在子对象中查找 BoundingBox
        var boundingBoxGO = childGO.transform.Find("BoundingBox");
        if (boundingBoxGO == null) {
            Debug.LogError($"BoundingBox not found in {child.id}'s children.");
            return;
        }

        var boundingBoxCollider = boundingBoxGO.GetComponent<BoxCollider>();
        if (boundingBoxCollider == null) {
            Debug.LogError($"BoundingBox {child.id} does not have a BoxCollider component.");
            return;
        }

        // 计算子对象的底面高度
        float childHeight = childGO.transform.position.y;
        float boundingBoxCenterY = boundingBoxCollider.center.y;
        float boundingBoxSizeY = boundingBoxCollider.size.y;
        float childBottomY = childHeight + boundingBoxCenterY - (boundingBoxSizeY / 2);

        // 计算向下移动的距离
        float distanceToMove = childBottomY - parentBottomY;

        // 调整子对象的位置
        childGO.transform.position = new Vector3(
            childGO.transform.position.x,
            childGO.transform.position.y - distanceToMove,
            childGO.transform.position.z
        );

        Debug.Log($"Moved child object {child.id} to contact ReceptacleTriggerBox bottom.");
    }


    private static GameObject createCeilingGameObject(ProceduralHouse house, IEnumerable<Wall> walls, Dictionary<string, UnityEngine.Object> assetMap) {
            // TODO: include rotation in json for floor and ceiling to compute the real scale not axis aligned scale

            // generate ceiling
        string ceilingMaterialId = house.proceduralParameters.ceilingMaterial.name;
        var ceilingParent = new GameObject(DefaultCeilingRootObjectName);

        // OLD rectangular ceiling, may be usefull to have as a feature, much faster
        // var ceilingGameObject = createSimObjPhysicsGameObject(DefaultCeilingRootObjectName, new Vector3(0, wallsMaxY + wallsMaxHeight, 0), "Structure", 0);
        // var ceilingMesh = ProceduralTools.GetRectangleFloorMesh(new List<RectangleRoom> { roomCluster }, 0.0f, house.proceduralParameters.ceilingBackFaces);
        // var k = house.rooms.SelectMany(r =>  r.floorPolygon.Select(p => new Vector3(p.x, p.y + wallsMaxY + wallsMaxHeight, p.z)).ToList()).ToList();

        var ceilingMeshes = house.rooms.Select(r => GenerateFloorMesh(r.floorPolygon, yOffset:  0.0f, clockWise: true)).ToArray();

        for (int i = 0; i < house.rooms.Count(); i++) {
            var ceilingMesh = ceilingMeshes[i];
            var room = house.rooms[i];
            var floorName = house.rooms[i].id;

            var wallPoints = walls.SelectMany(w => new List<Vector3>() { w.p0, w.p1 });
            var wallsMinY = wallPoints.Count() > 0? wallPoints.Min(p => p.y) : 0.0f;
            var wallsMaxY =  wallPoints.Count() > 0? wallPoints.Max(p => p.y) : 0.0f;
            var wallsMaxHeight =  walls.Count() > 0? walls.Max(w => w.height) : 0.0f;
            var ceilingGameObject = ProceduralTools.createSimObjPhysicsGameObject($"{DefaultCeilingRootObjectName}_{floorName}", new Vector3(0, wallsMaxY + wallsMaxHeight, 0), "Structure", 0);

            StructureObject so = ceilingGameObject.AddComponent<StructureObject>();
            so.WhatIsMyStructureObjectTag = StructureObjectTag.Ceiling;               

            ceilingGameObject.GetComponent<MeshFilter>().mesh = ceilingMesh;
            var ceilingMeshRenderer = ceilingGameObject.GetComponent<MeshRenderer>();
            var dimensions = getAxisAlignedWidthDepth(ceilingMesh.vertices);

            string roomCeilingMaterialId = ceilingMaterialId;
            MaterialProperties ceilingMaterialProperties = null;
            if (room.ceilings.Count > 0) {
                ceilingMaterialProperties = room.ceilings[0].material;
                if (!string.IsNullOrEmpty(room.ceilings[0].material.name)) {
                    roomCeilingMaterialId = room.ceilings[0].material.name;
                }
            }
            
            
            // var mat = Resources.Load<Material>($"QuickMaterials/Walls/{ceilingMaterialId}");
            var obj = assetMap[ceilingMaterialId];

            // 尝试将 obj 转换为 Material
            if (obj is Material mat)
            {
                if (mat == null) {
                    Debug.Log("Ceiling material cannot be null");}

                ceilingMeshRenderer.material = generatePolygonMaterial(
                    mat,
                    dimensions,
                    house.proceduralParameters.ceilingMaterial,
                    0.0f,
                    0.0f,
                    squareTiling: house.proceduralParameters.squareTiling
                );
            }
            else
            {
                // 转换失败的处理
                Debug.LogError("The asset is not a Material.");
            }


            ceilingMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;

            // tagObjectNavmesh(ceilingGameObject, "Not Walkable");

            ceilingGameObject.transform.parent = ceilingParent.transform;
            
        }
        return ceilingParent;
        
        
        
    }

    public static GameObject spawnSimObjPrefab(
        GameObject prefab,
        string id,
        string assetId,
        Vector3 position,
        Quaternion rotation,
        // FlexibleRotation rotation,
        bool kinematic = false,
        bool positionBoundingBoxCenter = false,
        bool unlit = false,
        MaterialProperties materialProperties = null,
        float? openness = null,
        bool? isOn = null,
        bool? isDirty = null,
        string layer = null,
        Vector3? scale = null
        // CollisionDetectionMode collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative
    ) {
        var go = prefab;

        var spawned = GameObject.Instantiate(original: go); //, position, Quaternion.identity); //, position, rotation);

        if (!String.IsNullOrEmpty(layer)) {
            SetLayer<MeshRenderer>(spawned, LayerMask.NameToLayer(layer));
        }

        if (openness.HasValue) {
            var canOpen = spawned.GetComponentInChildren<CanOpen_Object>();
            if (canOpen != null) {
                canOpen.SetOpennessImmediate(openness.Value);
            }
        }

        if (isOn.HasValue) {
            var canToggle = spawned.GetComponentInChildren<CanToggleOnOff>();
            if (canToggle != null) {
                if (isOn.Value != canToggle.isOn) {
                    canToggle.Toggle();
                }
            }
        }

        if (isDirty.HasValue) {
            var dirt = spawned.GetComponentInChildren<Dirty>();
            if (dirt != null) {
                if (isDirty.Value != dirt.IsDirty()) {
                    dirt.ToggleCleanOrDirty();
                }
            }
        }

        spawned.transform.parent = GameObject.Find(DefaultObjectsRootName).transform;
        if (spawned.transform.parent  == null) {
            Debug.Log("spawned.transform.parent  is null and cannot be instantiated.");
        }

        // scale the object
        if (scale.HasValue) {
            spawned.transform.localScale = scale.Value;
            Transform[] children = new Transform[spawned.transform.childCount];
            for (int i = 0; i < spawned.transform.childCount; i++) {
                children[i] = spawned.transform.GetChild(i);
            }

            // detach all children
            spawned.transform.DetachChildren();
            spawned.transform.localScale = Vector3.one;
            foreach (Transform t in children) {
                t.SetParent(spawned.transform);
            }
            spawned.GetComponent<SimObjPhysics>().ContextSetUpBoundingBox(forceCacheReset: true);
        }

        // var rotaiton = Quaternion.AngleAxis(rotation.degrees, rotation.axis);
        if (positionBoundingBoxCenter) {
            var simObj = spawned.GetComponent<SimObjPhysics>();
            var box = simObj.AxisAlignedBoundingBox;
            // box.enabled = true;
            var centerObjectSpace = prefab.transform.TransformPoint(box.center);

            spawned.transform.position = rotation * (spawned.transform.localPosition - box.center) + position;
            spawned.transform.rotation = rotation;
        } else {
            spawned.transform.position = position;
            spawned.transform.rotation = rotation;
        }

        var toSpawn = spawned.GetComponent<SimObjPhysics>();
        // Rigidbody rb = spawned.GetComponent<Rigidbody>();
        // rb.isKinematic = kinematic;
        // if (!kinematic) {
        //     rb.collisionDetectionMode = collisionDetectionMode;
        // }
        toSpawn.objectID = id;
        toSpawn.name = id;
        toSpawn.assetID = assetId;


        Shader unlitShader = null;
        if (unlit) {
            unlitShader = Shader.Find("Unlit/Color");
        }

        if (materialProperties != null) {
            var materials = toSpawn.GetComponentsInChildren<MeshRenderer>().Select(
                mr => mr.material
            );
            foreach (var mat in materials) {
                if (materialProperties.color != null) {
                    mat.color = new Color(
                        materialProperties.color.r, 
                        materialProperties.color.g, 
                        materialProperties.color.b, 
                        materialProperties.color.a
                    );
                }
                if (unlit) {
                    mat.shader = unlitShader;
                }
                if (materialProperties?.metallic != null) { 
                    mat.SetFloat("_Metallic", materialProperties.metallic.GetValueOrDefault(0.0f));
                }
                if (materialProperties?.smoothness != null) { 
                    mat.SetFloat("_Glossiness", materialProperties.smoothness.GetValueOrDefault(0.0f));
                }
            }
        }

        // TODO (speed up): move to room creator class
        // var sceneManager = GameObject.FindObjectOfType<PhysicsSceneManager>();
        // sceneManager.AddToObjectsInScene(toSpawn);
        toSpawn.transform.SetParent(GameObject.Find(DefaultObjectsRootName).transform);

        SimObjPhysics[] childSimObjects = toSpawn.transform.gameObject.GetComponentsInChildren<SimObjPhysics>();
        int childNumber = 0;
        for (int i = 0; i < childSimObjects.Length; i++) {
            if (childSimObjects[i].objectID == id) {
                // skip the parent object that's ID has already been assigned
                continue;
            }
            childSimObjects[i].objectID = $"{id}___{childNumber++}";
        }

        return toSpawn.transform.gameObject;
    }
    #if UNITY_EDITOR

    private Dictionary<string, UnityEngine.Object> LoadAllAssets()
    {
        Dictionary<string, UnityEngine.Object> map = new Dictionary<string, UnityEngine.Object>();

        // 加载材料
        LoadAssetsOfType("t:Material", map);
        // 加载预制体
        LoadAssetsOfType("t:Prefab", map);

        return map;
    }

    private void LoadAssetsOfType(string typeFilter, Dictionary<string, UnityEngine.Object> map)
    {
        string[] assetPaths = AssetDatabase.FindAssets(typeFilter, new[] {"Assets"});
        foreach (string guid in assetPaths)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset != null) 
            {
                string key = asset.name;
                if (!map.ContainsKey(key)) {
                    map.Add(key, asset);
                } else {
                    // 如果发现重复的资源名称，可以选择记录警告或忽略新找到的资源
                    Debug.LogWarning("Duplicate asset name found, ignored: " + key);
                }
            }
        }
        Debug.Log($"Loaded {map.Count} assets of type {typeFilter}");
    }
    #endif

}


