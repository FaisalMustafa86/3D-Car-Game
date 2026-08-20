using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Turns the scene into the night-time "vibe": dim cool moonlight, dark blue
/// fog fading into the distance, a dark sky, low ambient, and (in play mode) a
/// post-processing stack with bloom so the streetlights and car paint glow.
///
/// Attach to any GameObject (e.g. the World object). The environment settings
/// apply live in the editor via [ExecuteAlways] / the "Apply Night Atmosphere"
/// context menu; the post-processing volume is built at runtime so it doesn't
/// litter the scene. Tweak the fields and it re-applies.
/// </summary>
[ExecuteAlways]
public class NightAtmosphere : MonoBehaviour
{
    [Header("Moonlight (main directional light)")]
    [Tooltip("Leave empty to auto-find the scene's directional light.")]
    [SerializeField] Light sun;
    [SerializeField] Color moonColor = new Color(0.55f, 0.65f, 0.95f);
    [Tooltip("Keep this low — it's moonlight, not the sun.")]
    [SerializeField] float moonIntensity = 0.35f;
    [Tooltip("Direction the moonlight comes from (euler angles).")]
    [SerializeField] Vector3 moonAngles = new Vector3(55f, 150f, 0f);

    [Header("Ambient & Fog")]
    [SerializeField] Color ambientColor = new Color(0.05f, 0.06f, 0.11f);
    [SerializeField] Color fogColor = new Color(0.03f, 0.04f, 0.09f);
    [Tooltip("Higher = thicker haze / shorter view distance. Also hides the edge of the small world.")]
    [SerializeField] float fogDensity = 0.012f;

    [Header("Sky")]
    [SerializeField] Color skyTint = new Color(0.05f, 0.06f, 0.14f);
    [SerializeField] Color skyGround = new Color(0.02f, 0.02f, 0.03f);
    [Range(0f, 1f)]
    [SerializeField] float skyExposure = 0.35f;

    [Header("Post-processing (play mode)")]
    [SerializeField] bool enablePostProcessing = true;
    [Tooltip("Glow strength for bright things (streetlights, emissive bulbs, car highlights).")]
    [SerializeField] float bloomIntensity = 1.1f;
    [Tooltip("Brightness a pixel must exceed before it blooms. Lower = more things glow.")]
    [SerializeField] float bloomThreshold = 0.8f;
    [Range(0f, 1f)]
    [SerializeField] float vignette = 0.35f;
    [SerializeField] Color colorFilter = new Color(0.85f, 0.9f, 1f);

    Material skyboxMat;
    GameObject postVolumeGo;
    Bloom bloom;
    ColorAdjustments colorAdj;
    Vignette vig;
    Tonemapping tone;

    void OnEnable()
    {
        ApplyEnvironment();
        BuildPostProcessing();
    }

    void OnDisable()
    {
        TeardownPostProcessing();
    }

#if UNITY_EDITOR
    // Live-tune from the inspector: re-apply the look whenever a field changes.
    // Deferred so we don't touch the scene mid-validate.
    void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null || !isActiveAndEnabled) return;
            ApplyEnvironment();
            BuildPostProcessing();
        };
    }
#endif

    [ContextMenu("Apply Night Atmosphere")]
    public void ApplyEnvironment()
    {
        // --- Fog ---
        RenderSettings.fog        = true;
        RenderSettings.fogMode    = FogMode.ExponentialSquared;
        RenderSettings.fogColor   = fogColor;
        RenderSettings.fogDensity = fogDensity;

        // --- Ambient ---
        RenderSettings.ambientMode  = AmbientMode.Flat;
        RenderSettings.ambientLight = ambientColor;

        // --- Sky ---
        if (skyboxMat == null)
        {
            Shader sky = Shader.Find("Skybox/Procedural");
            if (sky != null) skyboxMat = new Material(sky) { name = "NightSky" };
        }
        if (skyboxMat != null)
        {
            if (skyboxMat.HasProperty("_SkyTint"))            skyboxMat.SetColor("_SkyTint", skyTint);
            if (skyboxMat.HasProperty("_GroundColor"))        skyboxMat.SetColor("_GroundColor", skyGround);
            if (skyboxMat.HasProperty("_Exposure"))           skyboxMat.SetFloat("_Exposure", skyExposure);
            if (skyboxMat.HasProperty("_AtmosphereThickness")) skyboxMat.SetFloat("_AtmosphereThickness", 0.4f);
            if (skyboxMat.HasProperty("_SunSize"))            skyboxMat.SetFloat("_SunSize", 0.02f);
            RenderSettings.skybox = skyboxMat;
        }

        // --- Moonlight ---
        if (sun == null) sun = FindDirectionalLight();
        if (sun != null)
        {
            sun.color              = moonColor;
            sun.intensity          = moonIntensity;
            sun.transform.rotation = Quaternion.Euler(moonAngles);
            RenderSettings.sun     = sun;
        }

        DynamicGI.UpdateEnvironment();
    }

    Light FindDirectionalLight()
    {
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) return l;
        return null;
    }

    // Builds the post-processing volume in BOTH edit and play mode so the night
    // look is visible in the Game view while tuning. The volume is a hidden,
    // non-saved object so it never clutters the hierarchy or dirties the scene.
    [ContextMenu("Rebuild Post Processing")]
    void BuildPostProcessing()
    {
        EnableCameraPost();

        if (!enablePostProcessing)
        {
            TeardownPostProcessing();
            return;
        }

        if (postVolumeGo == null)
        {
            postVolumeGo = new GameObject("NightPostVolume")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            postVolumeGo.transform.SetParent(transform, false);

            var volume = postVolumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            volume.sharedProfile = profile;

            bloom    = profile.Add<Bloom>();
            colorAdj = profile.Add<ColorAdjustments>();
            vig      = profile.Add<Vignette>();
            tone     = profile.Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.Neutral);
        }

        RefreshPostValues();
    }

    // Push the serialized fields onto the live volume — lets the inspector tune
    // the look without rebuilding the volume each time.
    void RefreshPostValues()
    {
        if (bloom != null)
        {
            bloom.intensity.Override(bloomIntensity);
            bloom.threshold.Override(bloomThreshold);
            bloom.scatter.Override(0.72f);
            bloom.tint.Override(new Color(0.8f, 0.85f, 1f));
        }
        if (colorAdj != null)
        {
            colorAdj.postExposure.Override(-0.15f);
            colorAdj.contrast.Override(12f);
            colorAdj.colorFilter.Override(colorFilter);
            colorAdj.saturation.Override(-6f);
        }
        if (vig != null)
        {
            vig.intensity.Override(vignette);
            vig.smoothness.Override(0.45f);
            vig.color.Override(new Color(0.02f, 0.02f, 0.05f));
        }
    }

    void EnableCameraPost()
    {
        // Post-processing must be enabled on the camera for the volume to show.
        Camera cam = Camera.main;
        if (cam != null)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
        }
    }

    void TeardownPostProcessing()
    {
        if (postVolumeGo == null) return;
        if (Application.isPlaying) Destroy(postVolumeGo);
        else                       DestroyImmediate(postVolumeGo);
        postVolumeGo = null;
        bloom = null; colorAdj = null; vig = null; tone = null;
    }
}
