using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally builds a small night-time city block to drive around: a dark
/// ground plane, a grid of roads, buildings on the blocks between roads, and
/// warm streetlights along the roadsides.
///
/// Attach to an empty GameObject, then use the inspector's context menu
/// ("Generate World" / "Clear World") — the geometry is created as real child
/// objects so it persists in the scene. Tweak the fields and regenerate.
/// </summary>
public class WorldGenerator : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Number of city blocks along each axis. Roads run between them.")]
    [SerializeField] int blocksX = 3;
    [SerializeField] int blocksZ = 3;
    [Tooltip("Size (metres) of one square block, not counting the road.")]
    [SerializeField] float blockSize = 26f;
    [Tooltip("Width (metres) of the roads between blocks.")]
    [SerializeField] float roadWidth = 9f;
    [Tooltip("Width (metres) of the sidewalk strip around each block.")]
    [SerializeField] float sidewalkWidth = 1.6f;

    [Header("Buildings")]
    [SerializeField] float minBuildingHeight = 6f;
    [SerializeField] float maxBuildingHeight = 24f;
    [Tooltip("Gap kept between a building footprint and the block edge.")]
    [SerializeField] float buildingInset = 2.5f;
    [Range(0f, 1f)]
    [Tooltip("Chance a lit window emissive tint is used instead of a dark one.")]
    [SerializeField] float litBuildingChance = 0.6f;

    [Header("Streetlights")]
    [SerializeField] bool spawnStreetlights = true;
    [SerializeField] float lightSpacing = 14f;
    [SerializeField] float lightHeight = 5.5f;
    [SerializeField] float lightRange = 14f;
    [SerializeField] float lightIntensity = 3.2f;
    [SerializeField] Color lightColor = new Color(1f, 0.82f, 0.55f);

    [Header("General")]
    [SerializeField] int seed = 12345;

    const string GeneratedRootName = "GeneratedWorld";

    // ---- Cached materials (created once per generation) ----
    Material groundMat;
    Material roadMat;
    Material sidewalkMat;
    Material poleMat;
    Material litBuildingMat;
    Material darkBuildingMat;
    Material bulbMat;

    void Reset()
    {
        // Handy defaults when first added.
        transform.position = Vector3.zero;
    }

    [ContextMenu("Generate World")]
    public void Generate()
    {
        Clear();
        Random.State prevState = Random.state;
        Random.InitState(seed);

        BuildMaterials();

        Transform root = new GameObject(GeneratedRootName).transform;
        root.SetParent(transform, false);

        float spanX = blocksX * blockSize + (blocksX + 1) * roadWidth;
        float spanZ = blocksZ * blockSize + (blocksZ + 1) * roadWidth;

        CreateGround(root, spanX, spanZ);
        CreateRoads(root, spanX, spanZ);
        CreateBlocks(root);

        Random.state = prevState;
        Debug.Log($"[WorldGenerator] Generated {blocksX}x{blocksZ} blocks ({spanX:F0}x{spanZ:F0} m).");
    }

    [ContextMenu("Clear World")]
    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child != null && child.name == GeneratedRootName)
                DestroyChild(child.gameObject);
        }
    }

    // ------------------------------------------------------------------

    void BuildMaterials()
    {
        groundMat = MakeMat("World_Ground", new Color(0.05f, 0.05f, 0.07f), 0.1f, 0.0f);
        roadMat = MakeMat("World_Road", new Color(0.09f, 0.09f, 0.11f), 0.35f, 0.0f);
        sidewalkMat = MakeMat("World_Sidewalk", new Color(0.22f, 0.22f, 0.25f), 0.15f, 0.0f);
        poleMat = MakeMat("World_Pole", new Color(0.12f, 0.12f, 0.14f), 0.5f, 0.6f);
        litBuildingMat = MakeMat("World_BuildingLit", new Color(0.16f, 0.17f, 0.22f), 0.2f, 0.1f);
        darkBuildingMat = MakeMat("World_BuildingDark", new Color(0.08f, 0.08f, 0.1f), 0.15f, 0.1f);

        bulbMat = MakeMat("World_Bulb", lightColor, 0f, 0f);
        if (bulbMat.HasProperty("_EmissionColor"))
        {
            bulbMat.EnableKeyword("_EMISSION");
            bulbMat.SetColor("_EmissionColor", lightColor * 2.5f);
            bulbMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
    }

    static Shader UrpLit => Shader.Find("Universal Render Pipeline/Lit");

    Material MakeMat(string name, Color color, float smoothness, float metallic)
    {
        Shader s = UrpLit;
        Material m = new Material(s != null ? s : Shader.Find("Standard"));
        m.name = name;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        return m;
    }

    void CreateGround(Transform root, float spanX, float spanZ)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(root, false);
        // Unity plane is 10x10 units at scale 1; pad a little beyond the roads.
        float pad = 1.4f;
        ground.transform.localScale = new Vector3(spanX * pad / 10f, 1f, spanZ * pad / 10f);
        ground.transform.localPosition = new Vector3(0f, -0.02f, 0f);
        Paint(ground, groundMat);
    }

    void CreateRoads(Transform root, float spanX, float spanZ)
    {
        // Roads run the full span along each grid line. We lay one long strip
        // per road line in each direction; overlaps at intersections are fine.
        float step = blockSize + roadWidth;
        float startX = -spanX * 0.5f + roadWidth * 0.5f;
        float startZ = -spanZ * 0.5f + roadWidth * 0.5f;

        for (int i = 0; i <= blocksX; i++)
        {
            float x = startX + i * step;
            GameObject r = MakeBox($"Road_V{i}", new Vector3(roadWidth, 0.1f, spanZ), roadMat);
            r.transform.SetParent(root, false);
            r.transform.localPosition = new Vector3(x, 0.03f, 0f);
        }
        for (int j = 0; j <= blocksZ; j++)
        {
            float z = startZ + j * step;
            GameObject r = MakeBox($"Road_H{j}", new Vector3(spanX, 0.1f, roadWidth), roadMat);
            r.transform.SetParent(root, false);
            r.transform.localPosition = new Vector3(0f, 0.03f, z);
        }
    }

    void CreateBlocks(Transform root)
    {
        float step = blockSize + roadWidth;
        float originX = -(blocksX - 1) * step * 0.5f;
        float originZ = -(blocksZ - 1) * step * 0.5f;

        for (int bx = 0; bx < blocksX; bx++)
        {
            for (int bz = 0; bz < blocksZ; bz++)
            {
                Vector3 center = new Vector3(originX + bx * step, 0f, originZ + bz * step);
                CreateBlock(root, center, bx, bz);
            }
        }

        if (spawnStreetlights)
            CreateStreetlights(root, step);
    }

    void CreateBlock(Transform root, Vector3 center, int bx, int bz)
    {
        // Raised sidewalk pad.
        GameObject pad = MakeBox($"Sidewalk_{bx}_{bz}",
            new Vector3(blockSize, 0.25f, blockSize), sidewalkMat);
        pad.transform.SetParent(root, false);
        pad.transform.localPosition = center + new Vector3(0f, 0.12f, 0f);

        // One or a few buildings per block.
        float usable = blockSize - buildingInset * 2f - sidewalkWidth * 2f;
        int split = Random.value > 0.5f ? 2 : 1;
        float cell = usable / split;

        for (int ix = 0; ix < split; ix++)
        {
            for (int iz = 0; iz < split; iz++)
            {
                if (split > 1 && Random.value < 0.2f) continue; // occasional gap
                float footprint = cell * Random.Range(0.6f, 0.9f);
                float h = Random.Range(minBuildingHeight, maxBuildingHeight);
                Vector3 offset = new Vector3(
                    (ix - (split - 1) * 0.5f) * cell,
                    0f,
                    (iz - (split - 1) * 0.5f) * cell);

                Material mat = Random.value < litBuildingChance ? litBuildingMat : darkBuildingMat;
                GameObject b = MakeBox($"Building_{bx}_{bz}_{ix}{iz}",
                    new Vector3(footprint, h, footprint), mat);
                b.transform.SetParent(root, false);
                b.transform.localPosition = center + offset + new Vector3(0f, h * 0.5f + 0.25f, 0f);
            }
        }
    }

    void CreateStreetlights(Transform root, float step)
    {
        Transform lightsParent = new GameObject("Streetlights").transform;
        lightsParent.SetParent(root, false);

        float spanX = blocksX * blockSize + (blocksX + 1) * roadWidth;
        float spanZ = blocksZ * blockSize + (blocksZ + 1) * roadWidth;
        float startX = -spanX * 0.5f + roadWidth * 0.5f;
        float startZ = -spanZ * 0.5f + roadWidth * 0.5f;

        for (int i = 0; i <= blocksX; i++)
        {
            float x = startX + i * step;
            for (float z = -spanZ * 0.5f + lightSpacing * 0.5f; z < spanZ * 0.5f; z += lightSpacing)
                SpawnLight(lightsParent, new Vector3(x, 0f, z));
        }
        for (int j = 0; j <= blocksZ; j++)
        {
            float z = startZ + j * step;
            for (float x = -spanX * 0.5f + lightSpacing * 0.5f; x < spanX * 0.5f; x += lightSpacing)
                SpawnLight(lightsParent, new Vector3(x, 0f, z));
        }
    }

    void SpawnLight(Transform parent, Vector3 basePos)
    {
        GameObject pole = MakeBox("LightPole", new Vector3(0.18f, lightHeight, 0.18f), poleMat);
        pole.transform.SetParent(parent, false);
        pole.transform.localPosition = basePos + new Vector3(0f, lightHeight * 0.5f, 0f);

        GameObject lampGo = new GameObject("Lamp");
        lampGo.transform.SetParent(pole.transform, false);
        lampGo.transform.localPosition = new Vector3(0f, lightHeight * 0.5f - 0.2f, 0f);

        Light l = lampGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = lightColor;
        l.range = lightRange;
        l.intensity = lightIntensity;
        l.shadows = LightShadows.None;

        // Small glowing bulb so the light source is visible at night.
        GameObject bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bulb.name = "Bulb";
        bulb.transform.SetParent(lampGo.transform, false);
        bulb.transform.localScale = Vector3.one * 0.4f;
        StripCollider(bulb);
        Paint(bulb, bulbMat);
    }

    // ------------------------------------------------------------------
    // helpers

    GameObject MakeBox(string name, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.localScale = size;
        Paint(go, mat);
        return go;
    }

    static void Paint(GameObject go, Material mat)
    {
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = mat;
    }

    static void StripCollider(GameObject go)
    {
        var c = go.GetComponent<Collider>();
        if (c != null) DestroyChild(c);
    }

    static void DestroyChild(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
