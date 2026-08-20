using UnityEngine;

/// <summary>
/// Procedural car sound: a looping engine hum whose pitch/volume track speed and
/// throttle, plus a tyre screech that rises with how hard the car is sliding.
/// Both clips are synthesized at runtime, so there are no audio files to import —
/// just drop this on the car GameObject (it finds the CarController automatically).
/// Designed to sit gently UNDER the relaxing background music.
/// </summary>
[RequireComponent(typeof(CarController))]
public class CarAudio : MonoBehaviour
{
    [Header("Sound Clips (optional — drop real sounds here)")]
    [Tooltip("Looping engine sound. If set, it replaces the built-in synth tone.")]
    [SerializeField] AudioClip engineClip;
    [Tooltip("Looping tyre-screech/skid sound. If set, it replaces the built-in synth noise.")]
    [SerializeField] AudioClip screechClip;
    [Tooltip("Master switch. Turn off to silence car audio entirely (e.g. until real sounds are added).")]
    [SerializeField] bool audioEnabled = true;

    [Header("Engine")]
    [Range(0f, 1f)]
    [SerializeField] float engineVolume = 0.35f;
    [Tooltip("Engine pitch at a standstill (idle).")]
    [SerializeField] float idlePitch = 0.7f;
    [Tooltip("Engine pitch at top speed.")]
    [SerializeField] float maxPitch = 2.2f;
    [Tooltip("How quickly the engine note follows speed changes.")]
    [SerializeField] float pitchSmoothing = 4f;

    [Header("Tyre Screech")]
    [Range(0f, 1f)]
    [SerializeField] float screechVolume = 0.5f;
    [Tooltip("How quickly the screech fades in/out with slip.")]
    [SerializeField] float screechSmoothing = 10f;

    [Header("Reference")]
    [Tooltip("Top speed used to normalise engine pitch. Match the car's top speed.")]
    [SerializeField] float referenceTopSpeedKmh = 80f;

    CarController car;
    AudioSource engineSource;
    AudioSource screechSource;

    float smoothedPitch;
    float smoothedScreech;

    const int SampleRate = 44100;

    void Awake()
    {
        car = GetComponent<CarController>();

        if (!audioEnabled)
        {
            enabled = false;   // stop Update; stays silent until re-enabled
            return;
        }

        // Use a provided clip if there is one, otherwise fall back to the synth.
        engineSource  = CreateSource("EngineAudio", engineClip  != null ? engineClip  : BuildEngineClip(), engineVolume);
        screechSource = CreateSource("ScreechAudio", screechClip != null ? screechClip : BuildNoiseClip(), 0f);

        smoothedPitch = idlePitch;
    }

    AudioSource CreateSource(string name, AudioClip clip, float volume)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.clip          = clip;
        src.loop          = true;
        src.playOnAwake   = false;
        src.volume        = volume;
        src.spatialBlend  = 0f;      // 2D — it's the player's own car
        src.Play();
        return src;
    }

    void Update()
    {
        float speedT = Mathf.Clamp01(car.SpeedKmh / Mathf.Max(1f, referenceTopSpeedKmh));

        // Engine: pitch mostly from speed, with a small throttle "rev" bump so
        // pressing the gas is audible even before the car gets moving.
        float throttleBump = Mathf.Abs(car.Throttle) * 0.15f;
        float targetPitch  = Mathf.Lerp(idlePitch, maxPitch, speedT) + throttleBump;
        smoothedPitch      = Mathf.Lerp(smoothedPitch, targetPitch, Time.deltaTime * pitchSmoothing);
        engineSource.pitch = smoothedPitch;
        // A touch louder under load / at speed.
        engineSource.volume = engineVolume * (0.7f + 0.3f * speedT + throttleBump);

        // Tyre screech tracks the car's sideways slide.
        float targetScreech = car.SlipAmount;
        smoothedScreech     = Mathf.Lerp(smoothedScreech, targetScreech, Time.deltaTime * screechSmoothing);
        screechSource.volume = smoothedScreech * screechVolume;
        // Slightly higher pitch the harder the slide, for a more urgent screech.
        screechSource.pitch  = 0.9f + smoothedScreech * 0.5f;
    }

    // --- procedural clips ---

    // A short looping engine tone: a low fundamental plus a couple of harmonics
    // for a bit of grit, so pitch-shifting it reads as an engine rather than a beep.
    AudioClip BuildEngineClip()
    {
        float freq   = 55f;               // low rumble; pitch multiplier does the rest
        int length   = SampleRate;        // 1 second, loops seamlessly at integer Hz
        var data     = new float[length];

        for (int i = 0; i < length; i++)
        {
            float t = (float)i / SampleRate;
            float v = 0f;
            v += Mathf.Sin(2f * Mathf.PI * freq * t)         * 0.6f;
            v += Mathf.Sin(2f * Mathf.PI * freq * 2f * t)    * 0.25f;
            v += Mathf.Sin(2f * Mathf.PI * freq * 3f * t)    * 0.12f;
            // Mild waveshaping adds engine-like buzz.
            v = Mathf.Clamp(v * 1.3f, -1f, 1f);
            data[i] = v * 0.5f;
        }

        var clip = AudioClip.Create("EngineTone", length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Filtered white noise for the tyre screech.
    AudioClip BuildNoiseClip()
    {
        int length = SampleRate;          // 1 second loop
        var data   = new float[length];
        var rng    = new System.Random(1234);
        float prev = 0f;

        for (int i = 0; i < length; i++)
        {
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            // Simple low-pass so it's a hiss/screech, not harsh static.
            prev = Mathf.Lerp(prev, white, 0.35f);
            data[i] = prev * 0.6f;
        }

        var clip = AudioClip.Create("ScreechNoise", length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
