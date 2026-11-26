using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Records the kart's pose every few physics steps once the race starts. Each completed lap is stored
/// as a ghost. At each finish-line crossing, the lap just completed becomes a new ghost that appears
/// after a short delay and replays that lap. Earlier ghosts are not restarted.
/// Put this on the Player (the object with the Rigidbody).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class GhostRecorder : MonoBehaviour
{
    struct Sample
    {
        public float t;        // seconds since the lap started
        public Vector3 pos;
        public Quaternion rot;
    }

    // One recorded lap and the object that replays it
    class GhostRun
    {
        public List<Sample> samples;
        public GameObject obj;
        public float time;
        public int index;
        public bool playing;
    }

    [Header("Recording")]
    [Tooltip("Record once every this many physics steps (3 steps at 50 Hz = 0.06 s).")]
    [SerializeField, Min(1)] int fixedStepsPerSample = 3;
    [Tooltip("How many laps are kept as ghosts. When the limit is reached the oldest is removed.")]
    [SerializeField, Min(1)] int maxGhosts = 5;
    [Tooltip("Seconds between crossing the line and the new ghost appearing.")]
    [SerializeField, Min(0f)] float spawnDelay = 0.5f;

    [Header("Ghost look")]
    [Tooltip("Model for the ghost, for example Assets/Models/Karts/Prefab/NewKart_test.prefab. Falls back to a box.")]
    [SerializeField] GameObject ghostPrefab;
    [Tooltip("Optional. The kart model under Player. The ghost copies its local position, rotation and scale.")]
    [SerializeField] Transform visualReference;
    [Tooltip("Optional. A material using the Kart/Ghost shader. Needed in builds.")]
    [SerializeField] Material ghostMaterial;

    Rigidbody body;

    // recording
    readonly List<Sample> recording = new List<Sample>();
    bool isRecording;
    float recordStartTime;
    int stepCounter;

    // playback: one entry per recorded lap, oldest first
    readonly List<GhostRun> ghosts = new List<GhostRun>();
    Material runtimeGhostMaterial;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    void OnEnable()
    {
        FinishLine.RaceStarted += OnRaceStarted;
        FinishLine.LapCompleted += OnLapCompleted;
    }

    void OnDisable()
    {
        FinishLine.RaceStarted -= OnRaceStarted;
        FinishLine.LapCompleted -= OnLapCompleted;
    }

    void OnDestroy()
    {
        foreach (var g in ghosts)
            if (g.obj) Destroy(g.obj);
        ghosts.Clear();
        if (runtimeGhostMaterial) Destroy(runtimeGhostMaterial);
    }

    void OnRaceStarted()
    {
        BeginRecording();
    }

    void OnLapCompleted(int lap)
    {
        if (!isRecording) return;

        AddSample(); // the final sample sits on the finish line
        GhostRun run = AddGhost(new List<Sample>(recording));
        if (run != null) StartGhost(run);
        BeginRecording();
    }

    void BeginRecording()
    {
        recording.Clear();
        isRecording = true;
        recordStartTime = Time.fixedTime;
        stepCounter = 0;
        AddSample();
    }

    void FixedUpdate()
    {
        if (!isRecording) return;
        if (++stepCounter % fixedStepsPerSample == 0) AddSample();
    }

    void AddSample()
    {
        float t = Time.fixedTime - recordStartTime;
        if (recording.Count > 0 && t - recording[recording.Count - 1].t < 1e-4f) return;
        recording.Add(new Sample { t = t, pos = body.position, rot = body.rotation });
    }

    // ---- ghosts ----

    GhostRun AddGhost(List<Sample> samples)
    {
        if (samples.Count < 2) return null;

        // Drop the oldest ghost(s) once the limit is reached
        while (ghosts.Count >= maxGhosts)
        {
            if (ghosts[0].obj) Destroy(ghosts[0].obj);
            ghosts.RemoveAt(0);
        }

        var run = new GhostRun { samples = samples, obj = CreateGhostObject() };
        ghosts.Add(run);
        return run;
    }

    GameObject CreateGhostObject()
    {
        var root = new GameObject("Ghost");

        GameObject model;
        if (ghostPrefab != null)
        {
            model = Instantiate(ghostPrefab, root.transform);
        }
        else
        {
            model = GameObject.CreatePrimitive(PrimitiveType.Cube);
            model.transform.SetParent(root.transform, false);
            model.transform.localScale = new Vector3(1.2f, 0.6f, 2f);
            model.transform.localPosition = new Vector3(0f, 0.4f, 0f);
        }

        if (visualReference != null)
        {
            model.transform.localPosition = visualReference.localPosition;
            model.transform.localRotation = visualReference.localRotation;
            model.transform.localScale = visualReference.localScale;
        }

        // The ghost is visual only
        foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (var rb in model.GetComponentsInChildren<Rigidbody>()) Destroy(rb);

        Material mat = GetGhostMaterial();
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (mat == null) continue;
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }

        root.SetActive(false);
        return root;
    }

    Material GetGhostMaterial()
    {
        if (ghostMaterial != null) return ghostMaterial;
        if (runtimeGhostMaterial == null)
        {
            var shader = Shader.Find("Kart/Ghost");
            if (shader == null)
            {
                Debug.LogWarning("GhostRecorder: Kart/Ghost shader not found. Assign a Ghost Material.", this);
                return null;
            }
            runtimeGhostMaterial = new Material(shader);
        }
        return runtimeGhostMaterial;
    }

    // Only the newest ghost is started. It stays hidden for spawnDelay seconds, then appears at the line.
    // Older ghosts are untouched and keep looping their own laps.
    void StartGhost(GhostRun g)
    {
        g.time = -spawnDelay;
        g.index = 0;
        g.playing = true;
        g.obj.transform.SetPositionAndRotation(g.samples[0].pos, g.samples[0].rot);
        g.obj.SetActive(false);
    }

    void Update()
    {
        foreach (var g in ghosts)
        {
            if (!g.playing) continue;

            g.time += Time.deltaTime;
            if (g.time < 0f) continue; // still waiting out the spawn delay
            if (!g.obj.activeSelf) g.obj.SetActive(true);

            Sample last = g.samples[g.samples.Count - 1];

            if (g.time >= last.t)
            {
                // Lap finished: loop straight back to the start. Each ghost runs on its own clock,
                // so it keeps driving its lap over and over without being reset by the player.
                g.time = Mathf.Repeat(g.time, last.t);
                g.index = 0;
            }

            while (g.index < g.samples.Count - 2 && g.samples[g.index + 1].t <= g.time)
                g.index++;

            Sample a = g.samples[g.index];
            Sample b = g.samples[g.index + 1];
            float u = Mathf.InverseLerp(a.t, b.t, g.time);
            g.obj.transform.SetPositionAndRotation(
                Vector3.Lerp(a.pos, b.pos, u),
                Quaternion.Slerp(a.rot, b.rot, u));
        }
    }
}
