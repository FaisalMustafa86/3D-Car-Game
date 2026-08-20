using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally builds a large, district-based night city on a connected road
/// GRID, using the real Kenney "City Kit (Commercial)" building models (auto-
/// loaded from Assets). The map is zoned like a real city: a downtown skyscraper
/// cluster in the middle, mid-rise commercial rings around it, low-rise outskirts,
/// and scattered parks. Roads, sidewalks, curbs, streetlights, street props and
/// neon signs are still generated procedurally (with runtime textures) to dress
/// the streets and give the Tokyo-night glow.
///
/// Attach to an empty GameObject at the origin. In the Editor use the context
/// menu: "Load City Kit" (once, to pull in the models) then "Generate World".
/// "Generate World" auto-loads the kit if needed. Pair with NightAtmosphere.
/// </summary>
public class WorldGenerator : MonoBehaviour
{
    [Header("City Grid")]
    [Tooltip("Number of blocks across (X) and down (Z). 8x8 = a large city.")]
    [SerializeField] int gridCols = 8;
    [SerializeField] int gridRows = 8;
    [Tooltip("Size (m) of one square block (the buildable area between roads).")]
    [SerializeField] float blockSize = 48f;
    [Tooltip("Width (m) of the streets between blocks.")]
    [SerializeField] float roadWidth = 12f;
    [Tooltip("Width (m) of the sidewalk strip between the road and the building fronts.")]
    [SerializeField] float sidewalkWidth = 4f;
    [SerializeField] int seed = 12345;

    [Header("Buildings (Kenney City Kit)")]
    [Tooltip("Target real-world width (m) of a typical mid-rise building. Kenney "
           + "models are scaled uniformly so a typical building matches this.")]
    [SerializeField] float buildingScaleTarget = 14f;
    [Tooltip("Gap (m) left between neighbouring buildings on the same street edge.")]
    [SerializeField] float buildingGap = 1.5f;
    [Range(0f, 1f)]
    [SerializeField] float neonChance = 0.6f;

    [Header("Props")]
    [Range(0f, 1f)]
    [SerializeField] float propDensity = 0.4f;

    [Header("Streetlights")]
    [SerializeField] bool spawnStreetlights = true;
    [SerializeField] float lightSpacing = 26f;
    [SerializeField] float lightHeight = 6f;
    [SerializeField] float lightRange = 16f;
    [SerializeField] float lightIntensity = 2.6f;
    [SerializeField] Color lightColor = new Color(1f, 0.82f, 0.55f);

    // ---- Auto-loaded Kenney assets (populated by "Load City Kit") ----
    [Header("Loaded Assets (auto-filled)")]
    [SerializeField] GameObject[] midRise;      // building-a..n
    [SerializeField] GameObject[] skyscrapers;  // building-skyscraper-a..e
    [SerializeField] GameObject[] lowRise;      // low-detail-building-*
    [SerializeField] GameObject[] neonSigns;    // burger/soda/fries neon FBX
    [SerializeField] Material kitMaterial;      // shared URP material w/ colormap atlas
    [SerializeField] bool kitLoaded;

    const string KitRoot     = "Assets/kenney_city-kit-commercial_2.1/Models/FBX format";
    const string ColorMap    = KitRoot + "/Textures/colormap.png";
    const string NeonSignDir = "Assets/neon signs";
    const string GeneratedRootName = "GeneratedWorld";

    // ---- Cached procedural materials ----
    Material groundMat, roadMat, laneMat, sidewalkMat, curbMat;
    Material poleMat, metalMat, foliageMat, trashMat, coneMat, barrierMat, vendingMat, bulbMat;
    Material[] neonMats;

    // ---- Procedural textures (generated at runtime) ----
    Texture2D asphaltTex, concreteTex, groundTex, metalTex, hazardTex, trashTex;

    Transform root, roadRoot, buildingRoot, propRoot, lightRoot;

    // ---- Runtime model measurements (rebuilt each session, not serialized) ----
    struct ModelInfo { public Vector3 size; public Vector3 center; public float minY; }
    Dictionary<GameObject, ModelInfo> modelData;
    GameObject[] downtownSet;
    float unitScale = 1f;

    enum District { Downtown, Midtown, Outskirts }

    float Spacing => blockSize + roadWidth;
    float HalfSpanX => gridCols * Spacing * 0.5f;
    float HalfSpanZ => gridRows * Spacing * 0.5f;

    // ==================================================================

    [ContextMenu("Generate World")]
    public void Generate()
    {
        Clear();
        Random.State prev = Random.state;
        Random.InitState(seed);

#if UNITY_EDITOR
        if (!kitLoaded || midRise == null || midRise.Length == 0) LoadCityKit();
#endif
        EnsureModelData();
        downtownSet = Concat(skyscrapers, midRise);

        BuildMaterials();

        root = new GameObject(GeneratedRootName).transform;
        root.SetParent(transform, false);
        roadRoot     = Child(root, "Roads");
        buildingRoot = Child(root, "Buildings");
        propRoot     = Child(root, "Props");
        lightRoot    = Child(root, "Streetlights");

        BuildGround();
        BuildRoads();

        for (int i = 0; i < gridCols; i++)
            for (int j = 0; j < gridRows; j++)
                BuildBlock(i, j);

        if (spawnStreetlights) BuildStreetlights();

        Random.state = prev;
        Debug.Log($"[WorldGenerator] {gridCols}x{gridRows} district city generated.");
    }

    [ContextMenu("Clear World")]
    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform c = transform.GetChild(i);
            if (c != null && c.name == GeneratedRootName) DestroyThing(c.gameObject);
        }
    }

    Vector3 BlockCenter(int i, int j) => new Vector3(
        (i + 0.5f) * Spacing - HalfSpanX, 0f,
        (j + 0.5f) * Spacing - HalfSpanZ);

    // ==================================================================
    // Kenney City Kit loading (editor only)

#if UNITY_EDITOR
    [ContextMenu("Load City Kit")]
    public void LoadCityKit()
    {
        midRise     = LoadModels("building-", excludeSkyscraper: true);
        skyscrapers = LoadModels("building-skyscraper-", excludeSkyscraper: false);
        lowRise     = LoadModels("low-detail-building-", excludeSkyscraper: false);
        neonSigns   = LoadNeonSigns();

        var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(ColorMap);
        kitMaterial = MakeTexturedLit("CityKit", tex, 0.2f);

        kitLoaded = tex != null && midRise.Length > 0;
        modelData = null;   // force re-measure
        Debug.Log($"[WorldGenerator] City Kit loaded: {midRise.Length} mid-rise, "
                + $"{skyscrapers.Length} skyscrapers, {lowRise.Length} low-rise, "
                + $"{neonSigns.Length} neon signs, atlas={(tex != null ? "ok" : "MISSING")}.");
    }

    GameObject[] LoadModels(string prefix, bool excludeSkyscraper)
    {
        var list = new List<GameObject>();
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Model", new[] { KitRoot }))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!file.StartsWith(prefix)) continue;
            if (excludeSkyscraper && file.Contains("skyscraper")) continue;
            var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null) list.Add(go);
        }
        return list.ToArray();
    }

    GameObject[] LoadNeonSigns()
    {
        var list = new List<GameObject>();
        foreach (var name in new[] { "Burger", "Soda", "French+Fry" })
        {
            var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"{NeonSignDir}/{name}.fbx");
            if (go != null) list.Add(go);
        }
        return list.ToArray();
    }
#endif

    // Measure every model's bounds once (instantiate, read, destroy) so we can
    // place & scale them analytically. Not serialized — rebuilt per session.
    void EnsureModelData()
    {
        if (modelData != null && modelData.Count > 0) return;
        modelData = new Dictionary<GameObject, ModelInfo>();

        var all = new List<GameObject>();
        AddRange(all, midRise); AddRange(all, skyscrapers);
        AddRange(all, lowRise); AddRange(all, neonSigns);

        foreach (var p in all)
        {
            if (p == null || modelData.ContainsKey(p)) continue;
            var temp = Instantiate(p);
            temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            temp.transform.localScale = Vector3.one;
            Bounds b = GetBounds(temp);
            modelData[p] = new ModelInfo { size = b.size, center = b.center, minY = b.min.y };
            DestroyThing(temp);
        }

        // Uniform scale so a typical mid-rise footprint == buildingScaleTarget.
        float sum = 0f; int n = 0;
        if (midRise != null)
            foreach (var p in midRise)
                if (p != null && modelData.TryGetValue(p, out var mi))
                { sum += Mathf.Max(mi.size.x, mi.size.z); n++; }
        float refFoot = n > 0 ? sum / n : 1f;
        unitScale = refFoot > 0.001f ? buildingScaleTarget / refFoot : 1f;
    }

    // ==================================================================
    // Materials

    void BuildMaterials()
    {
        groundMat   = Lit("Ground",   new Color(0.04f, 0.04f, 0.05f), 0.1f, 0f);
        roadMat     = Lit("Road",     new Color(0.07f, 0.07f, 0.09f), 0.6f, 0.2f);
        laneMat     = Emissive("Lane", new Color(0.9f, 0.85f, 0.5f), 0.5f);
        sidewalkMat = Lit("Sidewalk", new Color(0.16f, 0.16f, 0.18f), 0.2f, 0f);
        curbMat     = Lit("Curb",     new Color(0.28f, 0.28f, 0.3f), 0.2f, 0f);
        poleMat     = Lit("Pole",     new Color(0.1f, 0.1f, 0.12f), 0.4f, 0.5f);
        metalMat    = Lit("Metal",    new Color(0.18f, 0.18f, 0.2f), 0.6f, 0.7f);
        foliageMat  = Lit("Foliage",  new Color(0.06f, 0.14f, 0.07f), 0.15f, 0f);
        trashMat    = Lit("Trash",    new Color(0.12f, 0.11f, 0.09f), 0.1f, 0f);
        coneMat     = Emissive("Cone", new Color(1f, 0.35f, 0.05f), 0.4f);
        barrierMat  = Lit("Barrier",  new Color(0.75f, 0.7f, 0.15f), 0.3f, 0.3f);
        vendingMat  = Emissive("Vending", new Color(0.9f, 0.95f, 1f), 0.5f);
        bulbMat     = Emissive("Bulb", lightColor, 2.5f);

        neonMats = new[]
        {
            Emissive("Neon_Pink",   new Color(1f, 0.12f, 0.6f),  4f),
            Emissive("Neon_Cyan",   new Color(0.1f, 0.9f, 1f),   4f),
            Emissive("Neon_Red",    new Color(1f, 0.15f, 0.15f), 4f),
            Emissive("Neon_Orange", new Color(1f, 0.55f, 0.1f),  4f),
            Emissive("Neon_Green",  new Color(0.35f, 1f, 0.45f), 4f),
            Emissive("Neon_Purple", new Color(0.65f, 0.25f, 1f), 4f),
            Emissive("Neon_Yellow", new Color(1f, 0.9f, 0.25f),  4f),
            Emissive("Neon_Blue",   new Color(0.25f, 0.4f, 1f),  4f),
        };

        // Surface grain.
        asphaltTex  = BuildAsphaltTexture();
        concreteTex = BuildConcreteTexture();
        groundTex   = BuildGroundTexture();
        metalTex    = BuildMetalTexture();
        hazardTex   = BuildHazardTexture();
        trashTex    = BuildTrashTexture();
        ApplyTexture(metalMat,   metalTex,    new Vector2(1f, 2f));
        ApplyTexture(barrierMat, hazardTex,   new Vector2(6f, 1f));
        ApplyTexture(trashMat,   trashTex,    Vector2.one);
        ApplyTexture(curbMat,    concreteTex, new Vector2(2f, 1f));
    }

    static Shader UrpLit => Shader.Find("Universal Render Pipeline/Lit");

    Material Lit(string name, Color color, float smoothness, float metallic)
    {
        Shader s = UrpLit;
        var m = new Material(s != null ? s : Shader.Find("Standard")) { name = "World_" + name };
        if (m.HasProperty("_BaseColor"))  m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color"))      m.SetColor("_Color", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", metallic);
        return m;
    }

    Material Emissive(string name, Color color, float intensity)
    {
        var m = Lit(name, color * 0.6f, 0.3f, 0f);
        if (m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        return m;
    }

    // A URP Lit material carrying a base texture (used for the Kenney atlas).
    Material MakeTexturedLit(string name, Texture2D tex, float smoothness)
    {
        Shader s = UrpLit;
        var m = new Material(s != null ? s : Shader.Find("Standard")) { name = "World_" + name };
        if (m.HasProperty("_BaseColor"))  m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", 0f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (tex != null)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        }
        m.enableInstancing = true;   // many buildings share one material → GPU instancing
        return m;
    }

    // ==================================================================
    // Ground + road grid

    void BuildGround()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(root, false);
        float sx = (gridCols * Spacing + roadWidth) * 1.3f;
        float sz = (gridRows * Spacing + roadWidth) * 1.3f;
        ground.transform.localScale = new Vector3(sx / 10f, 1f, sz / 10f);
        ground.transform.localPosition = new Vector3(0f, -0.02f, 0f);
        var gm = new Material(groundMat);
        ApplyTexture(gm, groundTex, new Vector2(sx / 22f, sz / 22f));
        ground.GetComponent<MeshRenderer>().sharedMaterial = gm;
    }

    void BuildRoads()
    {
        float lenZ = gridRows * Spacing + roadWidth;
        float lenX = gridCols * Spacing + roadWidth;

        for (int i = 0; i <= gridCols; i++)
        {
            float x = i * Spacing - HalfSpanX;
            var r = MakeTiledBox("Road_V", new Vector3(roadWidth, 0.08f, lenZ), roadMat, asphaltTex, 6f, roadRoot);
            r.transform.position = new Vector3(x, 0.03f, 0f);
            AddLaneDashes(new Vector3(x, 0.08f, -lenZ * 0.5f), Vector3.forward, lenZ);
        }
        for (int j = 0; j <= gridRows; j++)
        {
            float z = j * Spacing - HalfSpanZ;
            var r = MakeTiledBox("Road_H", new Vector3(lenX, 0.08f, roadWidth), roadMat, asphaltTex, 6f, roadRoot);
            r.transform.position = new Vector3(0f, 0.03f, z);
            AddLaneDashes(new Vector3(-lenX * 0.5f, 0.08f, z), Vector3.right, lenX);
        }
    }

    void AddLaneDashes(Vector3 start, Vector3 dir, float length)
    {
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
        for (float d = 4f; d < length - 4f; d += 6f)
        {
            var dash = MakeBox("Lane", new Vector3(0.22f, 0.02f, 2.5f), laneMat, roadRoot, false);
            dash.transform.position = start + dir * d;
            dash.transform.rotation = rot;
        }
    }

    // ==================================================================
    // Blocks + districts

    // District from distance-to-centre plus a stable per-block jitter, so the
    // downtown cluster sits in the middle and thins out toward the edges.
    District DistrictAt(int i, int j)
    {
        float cx = (gridCols - 1) * 0.5f, cy = (gridRows - 1) * 0.5f;
        float dx = cx > 0 ? (i - cx) / cx : 0f;
        float dy = cy > 0 ? (j - cy) / cy : 0f;
        float dist = Mathf.Sqrt(dx * dx + dy * dy) + (Hash(i, j) - 0.5f) * 0.25f;
        if (dist < 0.5f) return District.Downtown;
        if (dist < 0.9f) return District.Midtown;
        return District.Outskirts;
    }

    void BuildBlock(int i, int j)
    {
        Vector3 center = BlockCenter(i, j);
        float hb = blockSize * 0.5f;

        var pad = MakeTiledBox("Sidewalk", new Vector3(blockSize, 0.2f, blockSize), sidewalkMat, concreteTex, 8f, roadRoot);
        pad.transform.position = center + Vector3.up * 0.1f;
        AddCurb(center, hb);

        District dist = DistrictAt(i, j);
        float parkChance = dist == District.Outskirts ? 0.18f : dist == District.Midtown ? 0.07f : 0f;
        if (Random.value < parkChance) { BuildPark(center, hb); return; }

        GameObject[] set = dist == District.Downtown ? downtownSet
                         : dist == District.Midtown  ? midRise
                                                     : lowRise;
        if (set == null || set.Length == 0) set = (midRise != null && midRise.Length > 0) ? midRise : lowRise;
        if (set == null || set.Length == 0)
        {
            Debug.LogWarning("[WorldGenerator] No building models loaded. Run 'Load City Kit' from the component's context menu.");
            return;
        }

        EdgeRow(center, hb, Vector3.forward, Vector3.right,   set);
        EdgeRow(center, hb, Vector3.back,    Vector3.right,   set);
        EdgeRow(center, hb, Vector3.right,   Vector3.forward, set);
        EdgeRow(center, hb, Vector3.left,    Vector3.forward, set);
    }

    void AddCurb(Vector3 center, float hb)
    {
        foreach (var (dir, along) in new[] {
            (Vector3.forward, Vector3.right), (Vector3.back, Vector3.right),
            (Vector3.right, Vector3.forward), (Vector3.left, Vector3.forward) })
        {
            var curb = MakeBox("Curb",
                new Vector3(along == Vector3.right ? blockSize : 0.3f, 0.28f, along == Vector3.right ? 0.3f : blockSize),
                curbMat, roadRoot, false);
            curb.transform.position = center + dir * hb + Vector3.up * 0.14f;
        }
    }

    // Places a row of real building models along one edge, fronts set back by the
    // sidewalk and facing the street. Widths come from each model's measured size.
    void EdgeRow(Vector3 center, float hb, Vector3 faceDir, Vector3 alongDir, GameObject[] set)
    {
        float frontDist   = hb - sidewalkWidth;
        float cornerClear = 10f;
        float half        = hb - cornerClear;
        if (half <= 0f) return;

        float d = -half;
        int guard = 0;
        while (d < half && guard++ < 40)
        {
            var prefab = set[Random.Range(0, set.Length)];
            if (!modelData.TryGetValue(prefab, out var info)) break;

            float w = info.size.x * unitScale;
            if (w <= 0.2f) break;
            if (d + w > half) break;

            float alongPos = d + w * 0.5f;
            if (Random.value < 0.05f) { d += w + buildingGap; continue; }   // occasional alley

            Vector3 frontPt = center + faceDir * frontDist + alongDir * alongPos;
            PlaceBuilding(prefab, info, frontPt, faceDir);

            if (Random.value < propDensity)
                PlaceProp(center + faceDir * (hb - sidewalkWidth * 0.5f) + alongDir * alongPos, faceDir, alongDir);

            d += w + buildingGap;
        }
    }

    // Instantiates a Kenney model, scales it uniformly, faces it to the street,
    // sits it on the sidewalk, applies the atlas material, adds a box collider,
    // and maybe a neon sign.
    void PlaceBuilding(GameObject prefab, ModelInfo info, Vector3 frontPt, Vector3 faceDir)
    {
        float s = unitScale;
        float depth = info.size.z * s;
        Quaternion rot = Quaternion.LookRotation(faceDir, Vector3.up);

        var inst = Instantiate(prefab, buildingRoot);
        inst.name = prefab.name;
        inst.transform.rotation = rot;
        inst.transform.localScale = Vector3.one * s;

        // Analytic placement: centre the footprint set back by half its depth so
        // the front face lands on the sidewalk line; sit the base on the pad top.
        Vector3 groundCtr = frontPt - faceDir * (depth * 0.5f);
        Vector3 rc = rot * (info.center * s);
        inst.transform.position = new Vector3(
            groundCtr.x - rc.x,
            0.2f - info.minY * s,      // pad top is at y=0.2
            groundCtr.z - rc.z);

        ApplyKitMaterial(inst);

        var col = inst.AddComponent<BoxCollider>();   // so the car can't drive through
        col.center = info.center;
        col.size   = info.size;

        if (Random.value < neonChance)
            AddNeon(groundCtr, faceDir, rot, info.size.x * s, info.size.y * s, depth);
    }

    void ApplyKitMaterial(GameObject inst)
    {
        if (kitMaterial == null) return;
        foreach (var r in inst.GetComponentsInChildren<MeshRenderer>())
        {
            var ms = r.sharedMaterials;
            for (int i = 0; i < ms.Length; i++) ms[i] = kitMaterial;
            r.sharedMaterials = ms;
        }
    }

    void AddNeon(Vector3 ground, Vector3 faceDir, Quaternion rot, float w, float h, float depth)
    {
        int count = Random.Range(1, 4);
        for (int n = 0; n < count; n++)
        {
            var mat = neonMats[Random.Range(0, neonMats.Length)];
            bool vertical = Random.value < 0.5f;
            float sx = vertical ? Random.Range(0.6f, 1.1f) : Random.Range(2f, 4f);
            float sy = vertical ? Random.Range(3f, Mathf.Max(3.5f, h * 0.5f)) : Random.Range(0.7f, 1.4f);

            var sign = MakeBox("Neon", new Vector3(sx, sy, 0.25f), mat, buildingRoot, false);
            float px = Random.Range(-w * 0.35f, w * 0.35f);
            float py = Random.Range(2.5f, Mathf.Max(3f, h - 2f));
            sign.transform.position = ground + rot * (Vector3.right * px)
                                             + faceDir * (depth * 0.5f + 0.2f)
                                             + Vector3.up * py;
            sign.transform.rotation = rot;
        }
    }

    void BuildPark(Vector3 center, float hb)
    {
        var grass = MakeBox("Park", new Vector3(blockSize - sidewalkWidth * 2f, 0.22f, blockSize - sidewalkWidth * 2f), foliageMat, propRoot, false);
        grass.transform.position = center + Vector3.up * 0.11f;

        int trees = Random.Range(5, 11);
        for (int i = 0; i < trees; i++)
            MakeTree(center + new Vector3(Random.Range(-hb + 6f, hb - 6f), 0f, Random.Range(-hb + 6f, hb - 6f)));

        int benches = Random.Range(2, 5);
        for (int i = 0; i < benches; i++)
            MakeBench(center + new Vector3(Random.Range(-hb + 5f, hb - 5f), 0f, Random.Range(-hb + 5f, hb - 5f)),
                      Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
    }

    void MakeTree(Vector3 pos)
    {
        var trunk = MakeBox("Trunk", new Vector3(0.4f, 3f, 0.4f), trashMat, propRoot, false);
        trunk.transform.position = pos + Vector3.up * 1.5f;
        var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        canopy.name = "Canopy";
        canopy.transform.SetParent(propRoot, false);
        canopy.transform.localScale = new Vector3(3f, 3.2f, 3f);
        canopy.transform.position = pos + Vector3.up * 4f;
        StripCollider(canopy);
        Paint(canopy, foliageMat);
    }

    // ==================================================================
    // Props

    void PlaceProp(Vector3 pos, Vector3 faceDir, Vector3 alongDir)
    {
        Quaternion face = Quaternion.LookRotation(faceDir, Vector3.up);
        float r = Random.value;
        if (r < 0.22f)      MakeVendingMachine(pos, face);
        else if (r < 0.4f)  MakeTrashPile(pos);
        else if (r < 0.55f) MakeCone(pos);
        else if (r < 0.7f)  MakeUtilityPole(pos);
        else if (r < 0.82f) MakeBarrier(pos, Quaternion.LookRotation(alongDir, Vector3.up));
        else if (r < 0.92f) MakePlanter(pos, face);
        else                MakeBench(pos, face);
    }

    void MakeVendingMachine(Vector3 pos, Quaternion rot)
    {
        var body = MakeBox("Vending", new Vector3(1.1f, 1.9f, 0.75f), metalMat, propRoot, true);
        body.transform.position = pos + Vector3.up * 0.95f;
        body.transform.rotation = rot;
        var front = MakeBox("VendingGlow", new Vector3(0.9f, 1.5f, 0.05f), vendingMat, propRoot, false);
        front.transform.position = pos + Vector3.up * 1.05f + (rot * Vector3.forward) * 0.4f;
        front.transform.rotation = rot;
    }

    void MakeTrashPile(Vector3 pos)
    {
        int bags = Random.Range(2, 6);
        for (int i = 0; i < bags; i++)
        {
            var bag = MakeBox("Trash", new Vector3(Random.Range(0.4f, 0.7f), Random.Range(0.3f, 0.6f), Random.Range(0.4f, 0.7f)), trashMat, propRoot, false);
            bag.transform.position = pos + new Vector3(Random.Range(-0.6f, 0.6f), 0.25f, Random.Range(-0.6f, 0.6f));
            bag.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), Random.Range(-10f, 10f));
        }
    }

    void MakeCone(Vector3 pos)
    {
        var cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cone.name = "Cone";
        cone.transform.SetParent(propRoot, false);
        cone.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
        cone.transform.position = pos + Vector3.up * 0.35f;
        StripCollider(cone);
        Paint(cone, coneMat);
    }

    void MakeUtilityPole(Vector3 pos)
    {
        var pole = MakeBox("UtilityPole", new Vector3(0.22f, 8f, 0.22f), poleMat, propRoot, true);
        pole.transform.position = pos + Vector3.up * 4f;
        var arm = MakeBox("PoleArm", new Vector3(2.2f, 0.15f, 0.15f), poleMat, propRoot, false);
        arm.transform.position = pos + Vector3.up * 7.2f;
    }

    void MakeBarrier(Vector3 pos, Quaternion rot)
    {
        var rail = MakeBox("Guardrail", new Vector3(3f, 0.5f, 0.12f), barrierMat, propRoot, false);
        rail.transform.position = pos + Vector3.up * 0.6f;
        rail.transform.rotation = rot;
    }

    void MakePlanter(Vector3 pos, Quaternion rot)
    {
        var box = MakeBox("Planter", new Vector3(1.2f, 0.5f, 0.6f), curbMat, propRoot, false);
        box.transform.position = pos + Vector3.up * 0.25f;
        box.transform.rotation = rot;
        var bush = MakeBox("Bush", new Vector3(1f, 0.7f, 0.5f), foliageMat, propRoot, false);
        bush.transform.position = pos + Vector3.up * 0.75f;
        bush.transform.rotation = rot;
    }

    void MakeBench(Vector3 pos, Quaternion rot)
    {
        var seat = MakeBox("Bench", new Vector3(1.6f, 0.15f, 0.5f), metalMat, propRoot, false);
        seat.transform.position = pos + Vector3.up * 0.5f;
        seat.transform.rotation = rot;
        var back = MakeBox("BenchBack", new Vector3(1.6f, 0.5f, 0.1f), metalMat, propRoot, false);
        back.transform.position = pos + Vector3.up * 0.75f + (rot * Vector3.forward) * -0.2f;
        back.transform.rotation = rot;
    }

    // ==================================================================
    // Streetlights

    void BuildStreetlights()
    {
        float hb = blockSize * 0.5f;
        for (int i = 0; i < gridCols; i++)
        for (int j = 0; j < gridRows; j++)
        {
            Vector3 c = BlockCenter(i, j);
            foreach (var (dir, along) in new[] {
                (Vector3.forward, Vector3.right),
                (Vector3.right, Vector3.forward) })
            {
                for (float d = -blockSize * 0.5f + lightSpacing * 0.5f; d < blockSize * 0.5f; d += lightSpacing)
                    SpawnLight(c + dir * (hb - 0.5f) + along * d);
            }
        }
    }

    void SpawnLight(Vector3 basePos)
    {
        var pole = MakeBox("LightPole", new Vector3(0.18f, lightHeight, 0.18f), poleMat, lightRoot, false);
        pole.transform.position = basePos + Vector3.up * (lightHeight * 0.5f);

        var lampGo = new GameObject("Lamp");
        lampGo.transform.SetParent(pole.transform, false);
        lampGo.transform.localPosition = new Vector3(0f, lightHeight * 0.5f - 0.2f, 0f);

        var l = lampGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = lightColor;
        l.range = lightRange;
        l.intensity = lightIntensity;
        l.shadows = LightShadows.None;

        var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bulb.name = "Bulb";
        bulb.transform.SetParent(lampGo.transform, false);
        bulb.transform.localScale = Vector3.one * 0.4f;
        StripCollider(bulb);
        Paint(bulb, bulbMat);
    }

    // ==================================================================
    // Procedural textures

    static Texture2D NewTex(int size, string name) =>
        new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat };

    static void ApplyTexture(Material m, Texture2D tex, Vector2 scale)
    {
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Color"))     m.SetColor("_Color", Color.white);
        if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", scale); }
        if (m.HasProperty("_MainTex")) { m.SetTexture("_MainTex", tex); m.SetTextureScale("_MainTex", scale); }
    }

    Texture2D BuildAsphaltTexture()
    {
        const int size = 128;
        var px = new Color[size * size];
        for (int i = 0; i < px.Length; i++)
        {
            float v = 0.05f + Random.value * 0.04f;
            if (Random.value < 0.015f) v += 0.12f;
            px[i] = new Color(v, v, v * 1.08f, 1f);
        }
        var t = NewTex(size, "Asphalt"); t.SetPixels(px); t.Apply(); return t;
    }

    Texture2D BuildConcreteTexture()
    {
        const int size = 128;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float v = 0.15f + Random.value * 0.05f;
            if (x % 32 == 0 || y % 32 == 0) v *= 0.55f;
            px[y * size + x] = new Color(v, v, v * 1.02f, 1f);
        }
        var t = NewTex(size, "Concrete"); t.SetPixels(px); t.Apply(); return t;
    }

    Texture2D BuildGroundTexture()
    {
        const int size = 128;
        var px = new Color[size * size];
        for (int i = 0; i < px.Length; i++)
        {
            float v = 0.03f + Random.value * 0.03f;
            px[i] = new Color(v, v, v * 1.1f, 1f);
        }
        var t = NewTex(size, "GroundGrunge"); t.SetPixels(px); t.Apply(); return t;
    }

    Texture2D BuildMetalTexture()
    {
        const int size = 128;
        var px = new Color[size * size];
        var col = new float[size];
        for (int x = 0; x < size; x++) col[x] = 0.22f + Random.value * 0.12f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float v = col[x] + (Random.value - 0.5f) * 0.03f;
            if (y % 64 == 0) v *= 0.7f;
            px[y * size + x] = new Color(v, v, v * 1.05f, 1f);
        }
        var t = NewTex(size, "BrushedMetal"); t.SetPixels(px); t.Apply(); return t;
    }

    Texture2D BuildHazardTexture()
    {
        const int size = 64, stripe = 12;
        var px = new Color[size * size];
        var yellow = new Color(0.85f, 0.7f, 0.08f, 1f);
        var black  = new Color(0.05f, 0.05f, 0.05f, 1f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Color c = ((x + y) / stripe) % 2 == 0 ? yellow : black;
            float n = (Random.value - 0.5f) * 0.04f;
            px[y * size + x] = new Color(Mathf.Clamp01(c.r + n), Mathf.Clamp01(c.g + n), Mathf.Clamp01(c.b + n), 1f);
        }
        var t = NewTex(size, "Hazard"); t.SetPixels(px); t.Apply(); return t;
    }

    Texture2D BuildTrashTexture()
    {
        const int size = 64;
        var px = new Color[size * size];
        for (int i = 0; i < px.Length; i++)
        {
            float v = 0.06f + Random.value * 0.06f;
            px[i] = new Color(v, v * 0.97f, v * 0.9f, 1f);
        }
        var t = NewTex(size, "Trash"); t.SetPixels(px); t.Apply(); return t;
    }

    // ==================================================================
    // helpers

    static Transform Child(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    GameObject MakeBox(string name, Vector3 size, Material mat, Transform parent, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localScale = size;
        if (!keepCollider) StripCollider(go);
        Paint(go, mat);
        return go;
    }

    GameObject MakeTiledBox(string name, Vector3 size, Material src, Texture2D tex, float tile, Transform parent)
    {
        var go = MakeBox(name, size, src, parent, false);
        var m = new Material(src);
        ApplyTexture(m, tex, new Vector2(Mathf.Max(1f, size.x / tile), Mathf.Max(1f, size.z / tile)));
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        return go;
    }

    static Bounds GetBounds(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }

    static void AddRange(List<GameObject> list, GameObject[] arr)
    {
        if (arr != null) list.AddRange(arr);
    }

    static GameObject[] Concat(GameObject[] a, GameObject[] b)
    {
        var list = new List<GameObject>();
        AddRange(list, a); AddRange(list, b);
        return list.ToArray();
    }

    float Hash(int i, int j)
    {
        int h = (i * 73856093) ^ (j * 19349663) ^ (seed * 83492791);
        h &= 0x7fffffff;
        return (h % 1000) / 1000f;
    }

    static void Paint(GameObject go, Material mat)
    {
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = mat;
    }

    static void StripCollider(GameObject go)
    {
        var c = go.GetComponent<Collider>();
        if (c != null) DestroyThing(c);
    }

    static void DestroyThing(Object o)
    {
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }
}
