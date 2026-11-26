using UnityEngine;

/// <summary>
/// Paints tire tracks into a world-space RenderTexture that the WetGround shader reads.
/// Only the XZ position of each track point is used, so the points can sit anywhere
/// above the tire's center.
/// </summary>
public class TireTracks : MonoBehaviour
{
    static readonly int TrackMapId = Shader.PropertyToID("_TrackMap");
    static readonly int TrackMapRectId = Shader.PropertyToID("_TrackMapRect");
    static readonly int SegABId = Shader.PropertyToID("_SegAB");
    static readonly int SegParamsId = Shader.PropertyToID("_SegParams");
    static readonly int SegCountId = Shader.PropertyToID("_SegCount");
    static readonly int FadeId = Shader.PropertyToID("_Fade");

    [Header("References")]
    [SerializeField] PlayerController player;
    [SerializeField] Rigidbody body;
    [Tooltip("Up to 4 points, one per tire that leaves a mark (e.g. the two rear tires).")]
    [SerializeField] Transform[] trackPoints;
    [SerializeField] Shader stampShader;

    [Header("Map (must cover the ground you drive on)")]
    [SerializeField] Vector2 mapCenter = Vector2.zero; // world X, Z
    [SerializeField] float mapSize = 200f;             // meters, square
    [SerializeField] int resolution = 2048;

    [Header("Look")]
    [SerializeField] float trackWidth = 0.35f;         // meters
    [SerializeField, Range(0f, 1f)] float strength = 1f;
    [Tooltip("Seconds for a fresh track to disappear. Keep under about 25 s.")]
    [SerializeField] float fadeSeconds = 10f;
    [SerializeField] float minSpeed = 0.5f;

    Material stampMat;
    RenderTexture current, other;
    Vector3[] previous;
    bool[] hasPrevious;
    readonly Vector4[] segAB = new Vector4[4];
    readonly Vector4[] segParams = new Vector4[4];

    void Awake()
    {
        if (!player) player = GetComponentInParent<PlayerController>();
        if (!body) body = GetComponentInParent<Rigidbody>();
        if (!stampShader) stampShader = Shader.Find("Hidden/Kart/TireStamp");

        stampMat = new Material(stampShader);
        current = CreateMap();
        other = CreateMap();

        int n = Mathf.Min(trackPoints.Length, 4);
        previous = new Vector3[n];
        hasPrevious = new bool[n];
    }

    RenderTexture CreateMap()
    {
        var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RHalf)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        rt.Create();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, Color.black);
        RenderTexture.active = prev;
        return rt;
    }

    void LateUpdate()
    {
        float speed = body ? body.linearVelocity.magnitude : 0f;
        bool drawing = (!player || player.OnGround) && speed > minSpeed;

        float radiusUv = trackWidth * 0.5f / mapSize;
        int count = 0;

        for (int i = 0; i < previous.Length; i++)
        {
            if (!trackPoints[i]) continue;
            Vector3 pos = trackPoints[i].position;

            if (drawing && hasPrevious[i])
            {
                Vector2 a = WorldToUv(previous[i]);
                Vector2 b = WorldToUv(pos);
                segAB[count] = new Vector4(a.x, a.y, b.x, b.y);
                segParams[count] = new Vector4(radiusUv, strength, 0f, 0f);
                count++;
            }

            previous[i] = pos;
            hasPrevious[i] = drawing; // restart the line after a jump so tracks don't bridge the gap
        }

        stampMat.SetVectorArray(SegABId, segAB);
        stampMat.SetVectorArray(SegParamsId, segParams);
        stampMat.SetFloat(SegCountId, count);
        stampMat.SetFloat(FadeId, Time.deltaTime / Mathf.Max(0.01f, fadeSeconds));

        Graphics.Blit(current, other, stampMat);
        (current, other) = (other, current);

        Shader.SetGlobalTexture(TrackMapId, current);
        Shader.SetGlobalVector(TrackMapRectId, new Vector4(mapCenter.x, mapCenter.y, mapSize, 0f));
    }

    Vector2 WorldToUv(Vector3 world)
    {
        return new Vector2(
            (world.x - mapCenter.x) / mapSize + 0.5f,
            (world.z - mapCenter.y) / mapSize + 0.5f);
    }

    void OnDisable()
    {
        // Size 0 tells the ground shader to ignore the map.
        Shader.SetGlobalVector(TrackMapRectId, Vector4.zero);
    }

    void OnDestroy()
    {
        if (current) current.Release();
        if (other) other.Release();
        if (stampMat) Destroy(stampMat);
    }
}
