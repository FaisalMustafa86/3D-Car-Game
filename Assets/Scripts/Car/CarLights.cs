using UnityEngine;

/// <summary>
/// Per-car headlights + tail/brake/reverse lights. Drop this on any car that has a
/// <see cref="CarController"/> (on the same object or a parent) and it wires itself up
/// — no scene-wide manager, so any number of cars each drive their own lights.
///
/// Runs in the editor too (<see cref="ExecuteAlways"/>) so you can see and place the
/// lights without pressing Play. Everything it spawns lives under a hidden, non-saved
/// rig object, so it never clutters the hierarchy or gets baked into the scene/prefab.
///
/// Design for MANY cars:
///  • Lamp positions AUTO-FIT to the car's actual mesh bounds, so a whole fleet of
///    differently-sized cars all get lights on the right corners with no hand-tuning.
///  • Headlights are real Spot lights (they light the road) but default to NO shadows,
///    which is the expensive part. On the PC tier (Forward+ / clustered) dozens are
///    cheap; on the Mobile tier (Forward) keep the count modest or disable headlights.
///  • Tail/brake/reverse lamps are cheap EMISSIVE quads (glow with bloom), not lights.
///    They share ONE material across every car, tinted per-lamp via MaterialPropertyBlock.
/// </summary>
[ExecuteAlways]
public class CarLights : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("The car this belongs to. Auto-found on this object or a parent if left empty.")]
    [SerializeField] CarController car;
    [Tooltip("Master switch — turn all lights off (e.g. for a daytime car).")]
    [SerializeField] bool lightsOn = true;

    [Header("Placement")]
    [Tooltip("Place the lamps automatically from the car's mesh bounds (recommended). " +
             "Turn off to use the manual offsets below.")]
    [SerializeField] bool autoFitToBody = true;
    [Tooltip("How far out toward the front/back corners the lamps sit (0..1 of half-length/width).")]
    [Range(0.5f, 1f)]
    [SerializeField] float lampSpread = 0.8f;
    [Tooltip("Nudge lamps this far OUT past the bodywork so they aren't hidden inside the mesh (m).")]
    [SerializeField] float lampPush = 0.08f;

    [Header("Headlights (real spot lights)")]
    [SerializeField] bool enableHeadlights = true;
    [Tooltip("Manual local positions (used only when Auto-Fit is off).")]
    [SerializeField] Vector3 leftHeadlightPos  = new Vector3(-0.6f, 0.6f, 1.9f);
    [SerializeField] Vector3 rightHeadlightPos = new Vector3( 0.6f, 0.6f, 1.9f);
    [SerializeField] Color headlightColor = new Color(1f, 0.96f, 0.85f);
    [Tooltip("Beam brightness. Night streets need a lot — this is a URP spot light.")]
    [SerializeField] float headlightIntensity = 10f;
    [Tooltip("How far the beam reaches (m). Low values = it only lights things you're right on top of.")]
    [SerializeField] float headlightRange = 50f;
    [Range(10f, 120f)]
    [SerializeField] float headlightAngle = 55f;
    [Tooltip("How many degrees the beams tilt down toward the road.")]
    [SerializeField] float headlightPitch = 9f;
    [Tooltip("Spot shadows are the costly part with many cars — leave off unless it's a hero car.")]
    [SerializeField] bool headlightShadows = false;
    [Tooltip("Emissive glow discs at the headlight lenses so the source itself is visible.")]
    [SerializeField] float headlightGlow = 4f;

    [Header("Tail / Brake / Reverse (emissive)")]
    [Tooltip("Manual local positions (used only when Auto-Fit is off).")]
    [SerializeField] Vector3 leftTailPos  = new Vector3(-0.6f, 0.6f, -1.9f);
    [SerializeField] Vector3 rightTailPos = new Vector3( 0.6f, 0.6f, -1.9f);
    [Tooltip("Dim red always-on running lights (when lights are on).")]
    [SerializeField] Color runningColor = new Color(1f, 0.05f, 0.05f);
    [SerializeField] float runningIntensity = 1.2f;
    [Tooltip("Bright red under braking / handbrake.")]
    [SerializeField] Color brakeColor = new Color(1f, 0.02f, 0.02f);
    [SerializeField] float brakeIntensity = 6f;
    [Tooltip("White reverse lights while backing up.")]
    [SerializeField] Color reverseColor = new Color(1f, 1f, 0.95f);
    [SerializeField] float reverseIntensity = 4f;
    [Tooltip("Diameter of each lamp glow disc (metres).")]
    [SerializeField] float lampSize = 0.28f;
    [Tooltip("How fast brake lights snap on/off. Higher = snappier.")]
    [SerializeField] float brakeResponse = 14f;

    // --- runtime ---
    const string RigName = "CarLights_Rig (generated)";
    Transform rig;                   // hidden, counter-scaled parent for everything we spawn
    Light headLeft, headRight;
    Renderer headGlowL, headGlowR;   // front lens glow
    Renderer tailL, tailR;           // rear lamps
    MaterialPropertyBlock mpb;
    float brakeBlend;                // 0..1 smoothed brake amount
    bool  lastReversing;             // cached to skip redundant work when idle

    // One emissive material shared by every lamp on every car (tinted via MPB).
    static Material sharedLampMat;
    static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    static readonly int BaseColor     = Shader.PropertyToID("_BaseColor");

    void OnEnable()  { Build(); }
    void OnDisable() { Teardown(); }

#if UNITY_EDITOR
    // Live re-place/re-tune when you tweak fields in the inspector. Deferred so we
    // don't rebuild GameObjects in the middle of Unity's OnValidate.
    void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null || !isActiveAndEnabled) return;
            Build();
        };
    }
#endif

    [ContextMenu("Rebuild Lights")]
    void Build()
    {
        Teardown();

        if (car == null) car = GetComponentInParent<CarController>();
        if (mpb == null) mpb = new MaterialPropertyBlock();

        // A counter-scaled rig so lamps stay real-metre sized/placed even if this
        // component ends up on a scaled object. Hidden + never saved.
        var rigGo = new GameObject(RigName) { hideFlags = HideFlags.HideAndDontSave };
        rig = rigGo.transform;
        rig.SetParent(transform, false);
        Vector3 ls = transform.lossyScale;
        rig.localScale = new Vector3(Inv(ls.x), Inv(ls.y), Inv(ls.z));

        // Work out where the four corners are.
        Vector3 lHead, rHead, lTail, rTail;
        ResolvePositions(out lHead, out rHead, out lTail, out rTail);

        if (enableHeadlights)
        {
            headLeft  = MakeHeadlight("Headlight_L", lHead, out headGlowL);
            headRight = MakeHeadlight("Headlight_R", rHead, out headGlowR);
        }

        tailL = MakeLamp("Taillight_L", lTail);
        tailR = MakeLamp("Taillight_R", rTail);

        Apply();   // set initial state so nothing flickers on frame 1
    }

    static float Inv(float v) => Mathf.Abs(v) < 1e-5f ? 1f : 1f / v;

    // Decide the four lamp positions — either fitted to the car's mesh bounds or
    // taken straight from the manual offsets.
    void ResolvePositions(out Vector3 lHead, out Vector3 rHead, out Vector3 lTail, out Vector3 rTail)
    {
        if (autoFitToBody && TryGetBodyBounds(out Bounds b))
        {
            float halfW = b.extents.x, halfL = b.extents.z;
            float x = halfW * lampSpread;
            float headY = b.center.y - b.extents.y * 0.15f;   // headlights sit low
            float tailY = b.center.y + b.extents.y * 0.05f;   // tail lights a touch higher
            float frontZ = b.center.z + halfL + lampPush;
            float backZ  = b.center.z - halfL - lampPush;

            lHead = new Vector3(b.center.x - x, headY, frontZ);
            rHead = new Vector3(b.center.x + x, headY, frontZ);
            lTail = new Vector3(b.center.x - x, tailY, backZ);
            rTail = new Vector3(b.center.x + x, tailY, backZ);
        }
        else
        {
            lHead = leftHeadlightPos;  rHead = rightHeadlightPos;
            lTail = leftTailPos;       rTail = rightTailPos;
        }
    }

    // Combined bounds of the car's mesh, expressed in the car's local space. The car
    // sits axis-aligned at build time, so the world AABB maps cleanly to local.
    bool TryGetBodyBounds(out Bounds local)
    {
        local = new Bounds();
        Transform root = car != null ? car.transform : transform;
        var rends = root.GetComponentsInChildren<Renderer>();
        bool has = false;
        Bounds world = new Bounds();
        foreach (var r in rends)
        {
            // Skip anything we generated (lamps live under the hidden rig).
            if (rig != null && r.transform.IsChildOf(rig)) continue;
            if (r is ParticleSystemRenderer) continue;
            if (!has) { world = r.bounds; has = true; }
            else       world.Encapsulate(r.bounds);
        }
        if (!has) return false;

        Vector3 c  = root.InverseTransformPoint(world.center);
        Vector3 s  = root.lossyScale;
        Vector3 e  = new Vector3(world.extents.x / Mathf.Max(1e-5f, Mathf.Abs(s.x)),
                                 world.extents.y / Mathf.Max(1e-5f, Mathf.Abs(s.y)),
                                 world.extents.z / Mathf.Max(1e-5f, Mathf.Abs(s.z)));
        local = new Bounds(c, e * 2f);
        return true;
    }

    Light MakeHeadlight(string name, Vector3 localPos, out Renderer glow)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        go.transform.SetParent(rig, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(headlightPitch, 0f, 0f);

        var l = go.AddComponent<Light>();
        l.type       = LightType.Spot;
        l.color      = headlightColor;
        l.intensity  = headlightIntensity;
        l.range      = headlightRange;
        l.spotAngle  = headlightAngle;
        l.innerSpotAngle = headlightAngle * 0.6f;   // soft edge, bright core
        l.shadows    = headlightShadows ? LightShadows.Soft : LightShadows.None;
        l.renderMode = LightRenderMode.ForcePixel;  // keep the beam crisp on the road

        // A small emissive disc at the lens so the headlight source glows too.
        glow = MakeLamp(name + "_Glow", localPos, faceForward: true);
        return l;
    }

    // Builds a flat emissive quad lamp facing the car's rear (or front) at a local pos.
    Renderer MakeLamp(string name, Vector3 localPos, bool faceForward = false)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        go.hideFlags = HideFlags.HideAndDontSave;
        // Quads ship with a collider we never want on a light.
        var col = go.GetComponent<Collider>();
        if (col != null) DestroyImmediate(col);

        go.transform.SetParent(rig, false);
        go.transform.localPosition = localPos;
        // Quad normal is +Z; face it out the back (default) or the front.
        go.transform.localRotation = Quaternion.Euler(0f, faceForward ? 0f : 180f, 0f);
        go.transform.localScale    = Vector3.one * lampSize;

        var r = go.GetComponent<Renderer>();
        r.sharedMaterial    = LampMaterial();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows    = false;
        return r;
    }

    static Material LampMaterial()
    {
        if (sharedLampMat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            sharedLampMat = new Material(shader) { name = "CarLamp_Shared" };
            sharedLampMat.SetColor(BaseColor, Color.black);
            sharedLampMat.EnableKeyword("_EMISSION");
            // Runtime-only glow — don't let the lightmapper try to bake it.
            sharedLampMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }
        return sharedLampMat;
    }

    void Update()
    {
        // Smooth the brake state so the lamps fade rather than pop.
        bool braking = lightsOn && car != null && (car.IsBraking || car.Handbrake);
        float newBlend = Mathf.MoveTowards(brakeBlend, braking ? 1f : 0f, Time.deltaTime * brakeResponse);
        bool reversing = lightsOn && car != null && car.IsReversing;

        // Only touch the renderers when something actually changed — an idle,
        // cruising car costs nothing, which keeps a full grid of cars cheap.
        if (newBlend != brakeBlend || reversing != lastReversing)
        {
            brakeBlend    = newBlend;
            lastReversing = reversing;
            Apply();
        }
    }

    void Apply()
    {
        bool on = lightsOn;

        if (headLeft  != null) headLeft.enabled  = on && enableHeadlights;
        if (headRight != null) headRight.enabled = on && enableHeadlights;

        Color frontGlow = on && enableHeadlights ? headlightColor * headlightGlow : Color.black;
        Tint(headGlowL, frontGlow);
        Tint(headGlowR, frontGlow);

        bool reversing = on && car != null && car.IsReversing;

        // Priority: reverse (white) > brake (bright red) > running (dim red) > off.
        Color rear;
        if (!on)               rear = Color.black;
        else if (reversing)    rear = reverseColor * reverseIntensity;
        else                   rear = Color.Lerp(runningColor * runningIntensity,
                                                  brakeColor   * brakeIntensity, brakeBlend);
        Tint(tailL, rear);
        Tint(tailR, rear);
    }

    void Tint(Renderer r, Color emission)
    {
        if (r == null) return;
        r.GetPropertyBlock(mpb);
        mpb.SetColor(EmissionColor, emission);
        mpb.SetColor(BaseColor, emission);   // so the disc reads lit even without bloom
        r.SetPropertyBlock(mpb);
    }

    void Teardown()
    {
        // Kill our current rig, plus any orphan left by a domain reload / recompile.
        if (rig != null) DestroySafe(rig.gameObject);
        rig = null;
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i);
            if (c != null && c.name == RigName) DestroySafe(c.gameObject);
        }
        headLeft = headRight = null;
        headGlowL = headGlowR = tailL = tailR = null;
    }

    static void DestroySafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else                       DestroyImmediate(o);
    }

    // Let other systems (a day/night cycle, UI) flip the lights.
    public void SetLightsOn(bool value)
    {
        lightsOn = value;
        Apply();
    }
}
