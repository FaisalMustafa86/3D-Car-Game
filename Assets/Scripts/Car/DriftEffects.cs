using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Skid marks + tyre smoke while the car slides. Reads each wheel's real slip
/// from its WheelCollider, so it reacts naturally to drifting, hard braking and
/// wheelspin. Everything (trails, particles, materials, a soft smoke texture)
/// is generated at runtime — just drop this on the car GameObject. If the wheel
/// list is left empty it auto-finds the WheelColliders in the children.
/// </summary>
public class DriftEffects : MonoBehaviour
{
    [Tooltip("Wheels to mark. Leave empty to auto-use every WheelCollider under this object.")]
    [SerializeField] WheelCollider[] wheels;

    [Header("Trigger")]
    [Tooltip("Combined tyre slip needed before marks/smoke start. Lower = triggers more easily.")]
    [SerializeField] float slipThreshold = 0.35f;
    [Tooltip("Slip at which the effect is at full strength (thickest smoke).")]
    [SerializeField] float slipFull = 0.9f;
    [Tooltip("Ignore effects below this speed (km/h) so a parked car doesn't smoke.")]
    [SerializeField] float minSpeedKmh = 4f;

    [Header("Skid Marks")]
    [SerializeField] bool skidMarks = true;
    [SerializeField] float markWidth = 0.28f;
    [Tooltip("Seconds a skid mark stays before fading out.")]
    [SerializeField] float markLifetime = 9f;
    [SerializeField] Color markColor = new Color(0.02f, 0.02f, 0.02f, 0.75f);

    [Header("Tyre Smoke")]
    [SerializeField] bool smoke = true;
    [SerializeField] Color smokeColor = new Color(0.78f, 0.8f, 0.85f, 0.45f);
    [Tooltip("Max particles per second per wheel at full slip.")]
    [SerializeField] float smokeRate = 55f;

    class WheelFx
    {
        public WheelCollider wheel;
        public Transform anchor;
        public TrailRenderer trail;
        public ParticleSystem ps;
        public ParticleSystem.EmissionModule emission;
    }

    readonly List<WheelFx> fx = new();
    Rigidbody rb;
    Material skidMat;
    Material smokeMat;
    Texture2D softDot;

    void Start()
    {
        rb = GetComponentInParent<Rigidbody>();

        if (wheels == null || wheels.Length == 0)
            wheels = GetComponentsInChildren<WheelCollider>();

        BuildAssets();

        foreach (var w in wheels)
            if (w != null) fx.Add(CreateWheelFx(w));
    }

    void BuildAssets()
    {
        softDot = MakeSoftDot(64);

        // Sprites/Default is unlit, transparent and honours vertex colour — works
        // for both trails and particles under URP.
        Shader sprite = Shader.Find("Sprites/Default");
        skidMat = new Material(sprite) { name = "SkidMat" };

        smokeMat = new Material(sprite) { name = "SmokeMat" };
        smokeMat.mainTexture = softDot;
    }

    WheelFx CreateWheelFx(WheelCollider w)
    {
        var anchorGo = new GameObject($"Fx_{w.name}");
        anchorGo.transform.SetParent(transform, false);
        var fxItem = new WheelFx { wheel = w, anchor = anchorGo.transform };

        if (skidMarks)
        {
            var trail = anchorGo.AddComponent<TrailRenderer>();
            trail.material          = skidMat;
            trail.time              = markLifetime;
            trail.startWidth        = markWidth;
            trail.endWidth          = markWidth;
            trail.minVertexDistance = 0.08f;
            trail.numCapVertices    = 2;
            trail.textureMode       = LineTextureMode.Stretch;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows    = false;
            trail.startColor        = markColor;
            trail.endColor          = new Color(markColor.r, markColor.g, markColor.b, 0f);
            trail.emitting          = false;
            fxItem.trail = trail;
        }

        if (smoke)
        {
            var smokeGo = new GameObject("Smoke");
            smokeGo.transform.SetParent(anchorGo.transform, false);
            var ps = smokeGo.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.startLifetime    = 1.1f;
            main.startSpeed       = 0.6f;
            main.startSize        = 1.1f;
            main.startColor       = smokeColor;
            main.gravityModifier  = -0.04f;               // drifts gently upward
            main.simulationSpace  = ParticleSystemSimulationSpace.World;
            main.maxParticles     = 400;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius    = 0.12f;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size    = new ParticleSystem.MinMaxCurve(1f, GrowCurve());

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color   = FadeGradient(smokeColor);

            var renderer = smokeGo.GetComponent<ParticleSystemRenderer>();
            renderer.material            = smokeMat;
            renderer.shadowCastingMode   = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows      = false;

            fxItem.ps       = ps;
            fxItem.emission = emission;
        }

        return fxItem;
    }

    void Update()
    {
        float speedKmh = rb != null ? rb.linearVelocity.magnitude * 3.6f : 0f;

        foreach (var f in fx)
        {
            bool grounded = f.wheel.GetGroundHit(out WheelHit hit);
            float slip = 0f;

            if (grounded && speedKmh > minSpeedKmh)
                slip = Mathf.Abs(hit.sidewaysSlip) + Mathf.Abs(hit.forwardSlip) * 0.5f;

            bool active = grounded && slip > slipThreshold;

            // Sit the effect anchor on the tyre's contact patch.
            if (grounded)
                f.anchor.position = hit.point + Vector3.up * 0.02f;

            if (f.trail != null)
                f.trail.emitting = active && skidMarks;

            if (f.ps != null)
            {
                float intensity = Mathf.InverseLerp(slipThreshold, slipFull, slip);
                f.emission.rateOverTime = active ? smokeRate * intensity : 0f;
            }
        }
    }

    // ---- procedural asset helpers ----

    static AnimationCurve GrowCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0.35f),
            new Keyframe(1f, 1f));
    }

    static Gradient FadeGradient(Color c)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    static Texture2D MakeSoftDot(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float r = size * 0.5f;
        var center = new Vector2(r, r);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center) / r;
                float a = Mathf.Clamp01(1f - d);
                a *= a;                                   // soft falloff
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return tex;
    }
}
