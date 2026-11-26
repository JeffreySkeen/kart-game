using System.Collections.Generic;
using UnityEngine;

public enum TrackSegmentType
{
    Straight,
    RightTurn,
    LeftTurn
}

/// <summary>
/// Builds one continuous road mesh from a list of preset segments.
/// The track starts at this object's origin, heading along +Z, and is built in local space.
/// The top surface of the road sits at local Y = 0.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class TrackGenerator : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] List<TrackSegmentType> segments = DefaultSegments();
    [Tooltip("Tick when the layout should end exactly where it started. Joins the last ring to the first.")]
    [SerializeField] bool closeLoop = true;

    [Header("Segment presets")]
    [SerializeField] float roadWidth = 16f;       // Straight.glb was 12; widened for easier driving
    [SerializeField] float straightLength = 16f;  // matches Straight.glb
    [SerializeField] float turnRadius = 32f;      // radius of the road's center line (the figure 8 needs 32)
    [SerializeField] float turnAngle = 90f;       // degrees per turn segment
    [SerializeField] float edgeDepth = 2f;        // how far the side walls drop below the surface

    [Header("Mesh detail")]
    [SerializeField] float straightStep = 4f;     // meters between cross-sections on straights
    [SerializeField] float turnStepDegrees = 5f;  // degrees between cross-sections on turns

    [Header("Rendering")]
    [SerializeField] Material roadMaterial;

    [Header("Street lights")]
    [SerializeField] GameObject streetLightPrefab;
    [SerializeField] float lightSpacing = 16f;        // meters along the center line
    [Tooltip("Distance from the road edge. Positive = outside the road, negative = onto it.")]
    [SerializeField] float lightEdgeOffset = 0.5f;
    [Tooltip("On: a light on each side at every spot. Off: lights alternate left and right.")]
    [SerializeField] bool lightsBothSides = true;

    [Header("Start / finish line")]
    [SerializeField] bool buildFinishLine = true;
    [Tooltip("Meters along the center line. Put it on a straight piece.")]
    [SerializeField] float finishLineDistance = 8f;
    [SerializeField] float lineSquareSize = 1f;       // size of one checker square; the line is two squares deep
    [SerializeField] float lineHeight = 0.02f;        // lift above the road to avoid flicker

    const string LightsName = "Track Lights";
    const string FinishLineName = "Finish Line";

    Mesh lineMesh;
    Material lineWhite, lineBlack;

    struct Ring
    {
        public Vector3 pos;
        public Vector3 right;
        public float dist;
    }

    Mesh mesh;

    // Figure 8 (needs Turn Radius 32 and Straight Length 16). One clockwise loop of three right turns and one
    // counter-clockwise loop of three left turns. The two loops meet at a flat crossing, 16 m before the
    // start point. Each list entry below is one segment: S = straight, R = right turn, L = left turn.
    static List<TrackSegmentType> DefaultSegments()
    {
        const TrackSegmentType S = TrackSegmentType.Straight;
        const TrackSegmentType R = TrackSegmentType.RightTurn;
        const TrackSegmentType L = TrackSegmentType.LeftTurn;

        return new List<TrackSegmentType>
        {
            S, S,                   // start straight (finish line here)
            R, S, R, S, R,          // clockwise loop
            S, S, S, S, S, S,       // back through the crossing and on into the second loop
            L, S, L, S, L,          // counter-clockwise loop
            S, S, S, S,             // up to the crossing again and back to the start
        };
    }

    // The earlier layout: a rounded rectangle, 3 + 2 + 3 + 2 straights joined by four right turns (radius 24).
    static List<TrackSegmentType> RoundedRectangleSegments()
    {
        const TrackSegmentType S = TrackSegmentType.Straight;
        const TrackSegmentType R = TrackSegmentType.RightTurn;

        return new List<TrackSegmentType>
        {
            S, S, S, R,
            S, S, R,
            S, S, S, R,
            S, S, R,
        };
    }

    [ContextMenu("Load Figure 8 Layout")]
    void LoadFigure8Layout()
    {
        segments = DefaultSegments();
        turnRadius = 32f;
        straightLength = 16f;
        turnAngle = 90f;
        MarkChanged();
        Rebuild();
    }

    [ContextMenu("Load Rounded Rectangle Layout")]
    void LoadRoundedRectangleLayout()
    {
        segments = RoundedRectangleSegments();
        turnRadius = 24f;
        straightLength = 16f;
        turnAngle = 90f;
        MarkChanged();
        Rebuild();
    }

    void MarkChanged()
    {
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    void Reset()
    {
        segments = DefaultSegments();
#if UNITY_EDITOR
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit != null && roadMaterial == null) roadMaterial = new Material(lit) { color = new Color(0.25f, 0.25f, 0.28f) };
#endif
        Rebuild();
    }

    void OnEnable() => ScheduleRebuild();

#if UNITY_EDITOR
    void OnValidate() => ScheduleRebuild();
#endif

    void ScheduleRebuild()
    {
#if UNITY_EDITOR
        // In the editor, wait a tick so we never create or destroy objects while Unity is validating or loading.
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += () => { if (this != null) Rebuild(); };
            return;
        }
#endif
        Rebuild();
    }

    [ContextMenu("Rebuild Track")]
    public void Rebuild()
    {
        var filter = GetComponent<MeshFilter>();
        var rend = GetComponent<MeshRenderer>();
        var col = GetComponent<MeshCollider>();

        if (mesh == null)
        {
            mesh = new Mesh { name = "Generated Track", hideFlags = HideFlags.DontSave };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }
        mesh.Clear();

        List<Ring> rings = BuildRings();
        if (rings.Count >= 2)
            FillMesh(rings);

        filter.sharedMesh = mesh;
        if (roadMaterial != null) rend.sharedMaterial = roadMaterial;
        col.sharedMesh = null; // force the collider to rebuild
        col.sharedMesh = mesh;

        RebuildLights(rings);
        RebuildFinishLine(rings);
    }

    static void DestroyObject(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    Material MakeLineMaterial(Color color)
    {
        // Reuse the road's shader so the line gets the same lighting (and wet look) as the road.
        Shader sh = roadMaterial != null ? roadMaterial.shader : null;
#if UNITY_EDITOR
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
#endif
        if (sh == null) return null;
        var m = new Material(sh) { hideFlags = HideFlags.DontSave };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        return m;
    }

    // A checkered strip across the road with a trigger box the kart drives through.
    void RebuildFinishLine(List<Ring> rings)
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name != FinishLineName) continue;
            child.name = FinishLineName + " (old)";
            DestroyObject(child.gameObject);
        }

        if (!buildFinishLine || rings.Count < 2) return;

        // Find the point on the center line at the requested distance
        float d = Mathf.Clamp(finishLineDistance, 0f, rings[rings.Count - 1].dist);
        int idx = 0;
        while (idx < rings.Count - 2 && rings[idx + 1].dist < d) idx++;
        Ring a = rings[idx];
        Ring b = rings[idx + 1];
        float u = Mathf.Clamp01((d - a.dist) / Mathf.Max(1e-4f, b.dist - a.dist));
        Vector3 center = Vector3.Lerp(a.pos, b.pos, u);
        Vector3 right = Vector3.Slerp(a.right, b.right, u).normalized;
        Vector3 forward = Vector3.Cross(right, Vector3.up);

        var go = new GameObject(FinishLineName) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(transform, false);
        go.transform.localPosition = center;
        go.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up);

        // Checkered mesh in the line's own space: x across the road, z along it
        int cols = Mathf.Max(2, Mathf.RoundToInt(roadWidth / Mathf.Max(0.1f, lineSquareSize)));
        const int rows = 2;
        float cellW = roadWidth / cols;
        float cellD = Mathf.Max(0.1f, lineSquareSize);
        float x0 = -roadWidth * 0.5f;
        float z0 = -rows * cellD * 0.5f;

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var whiteTris = new List<int>();
        var blackTris = new List<int>();

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                float xa = x0 + c * cellW, xb = xa + cellW;
                float za = z0 + r * cellD, zb = za + cellD;
                int v = verts.Count;
                verts.Add(new Vector3(xa, lineHeight, za)); // 0: near left
                verts.Add(new Vector3(xa, lineHeight, zb)); // 1: far left
                verts.Add(new Vector3(xb, lineHeight, za)); // 2: near right
                verts.Add(new Vector3(xb, lineHeight, zb)); // 3: far right
                for (int k = 0; k < 4; k++) normals.Add(Vector3.up);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1));
                uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, 1));

                var tris = ((r + c) % 2 == 0) ? whiteTris : blackTris;
                tris.Add(v + 0); tris.Add(v + 1); tris.Add(v + 2);
                tris.Add(v + 2); tris.Add(v + 1); tris.Add(v + 3);
            }
        }

        if (lineMesh == null) lineMesh = new Mesh { name = "Finish Line", hideFlags = HideFlags.DontSave };
        lineMesh.Clear();
        lineMesh.SetVertices(verts);
        lineMesh.SetNormals(normals);
        lineMesh.SetUVs(0, uvs);
        lineMesh.subMeshCount = 2;
        lineMesh.SetTriangles(whiteTris, 0);
        lineMesh.SetTriangles(blackTris, 1);
        lineMesh.RecalculateBounds();

        DestroyObject(lineWhite);
        DestroyObject(lineBlack);
        lineWhite = MakeLineMaterial(new Color(0.92f, 0.92f, 0.92f));
        lineBlack = MakeLineMaterial(new Color(0.04f, 0.04f, 0.04f));

        go.AddComponent<MeshFilter>().sharedMesh = lineMesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = new[] { lineWhite, lineBlack };

        // Trigger volume: full road width, tall enough for the kart, one meter deep
        var trigger = go.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, 2f, 0f);
        trigger.size = new Vector3(roadWidth, 4f, 1f);

        go.AddComponent<FinishLine>();
    }

    // Places the street light prefab along both sides of the center line. Lights are regenerated
    // on every rebuild and are not saved in the scene.
    void RebuildLights(List<Ring> rings)
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name != LightsName) continue;
            child.name = LightsName + " (old)";
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }

        if (streetLightPrefab == null || rings.Count < 2 || lightSpacing < 1f) return;

        var container = new GameObject(LightsName) { hideFlags = HideFlags.DontSave };
        container.transform.SetParent(transform, false);

        Vector3 prefabOffset = streetLightPrefab.transform.localPosition; // e.g. the light's height
        float total = rings[rings.Count - 1].dist;
        float sideDistance = roadWidth * 0.5f + lightEdgeOffset;

        int ringIndex = 0;
        int placed = 0;
        for (float d = lightSpacing * 0.5f; d < total; d += lightSpacing)
        {
            while (ringIndex < rings.Count - 2 && rings[ringIndex + 1].dist < d) ringIndex++;
            Ring a = rings[ringIndex];
            Ring b = rings[ringIndex + 1];
            float span = Mathf.Max(1e-4f, b.dist - a.dist);
            float u = Mathf.Clamp01((d - a.dist) / span);

            Vector3 center = Vector3.Lerp(a.pos, b.pos, u);
            Vector3 right = Vector3.Slerp(a.right, b.right, u).normalized;

            float[] sides = lightsBothSides ? new[] { +1f, -1f } : new[] { placed % 2 == 0 ? +1f : -1f };
            foreach (float side in sides)
            {
                // Skip spots that would put a light on another part of the track (the figure 8 crossing)
                Vector3 spot = center + right * (side * sideDistance);
                if (IsOnAnotherRoad(rings, d, spot, total)) continue;
                PlaceLight(container.transform, center, right, side, sideDistance, prefabOffset);
            }
            placed++;
        }
    }

    // True when a point lies on a part of the track that is far away along the path, such as at a crossing.
    bool IsOnAnotherRoad(List<Ring> rings, float d, Vector3 p, float total)
    {
        float limit = roadWidth * 0.5f + 3f;
        float limitSq = limit * limit;
        foreach (Ring r in rings)
        {
            float along = Mathf.Abs(r.dist - d);
            if (Mathf.Min(along, total - along) < 40f) continue; // own stretch of road
            float dx = r.pos.x - p.x;
            float dz = r.pos.z - p.z;
            if (dx * dx + dz * dz < limitSq) return true;
        }
        return false;
    }

    // side: +1 = right edge, -1 = left edge. The light faces the road.
    void PlaceLight(Transform parent, Vector3 center, Vector3 right, float side,
                    float sideDistance, Vector3 prefabOffset)
    {
        Vector3 inward = -right * side;
        Quaternion rot = Quaternion.LookRotation(inward, Vector3.up);
        Vector3 basePos = center + right * (side * sideDistance);

        GameObject go;
#if UNITY_EDITOR
        go = UnityEditor.PrefabUtility.InstantiatePrefab(streetLightPrefab, parent) as GameObject;
#else
        go = Instantiate(streetLightPrefab, parent);
#endif
        go.hideFlags = HideFlags.DontSave;
        go.transform.localPosition = basePos + rot * prefabOffset;
        go.transform.localRotation = rot;
    }

    // Unit vectors for a heading angle (radians, clockwise seen from above, 0 = +Z)
    static Vector3 Forward(float theta) => new Vector3(Mathf.Sin(theta), 0f, Mathf.Cos(theta));
    static Vector3 Right(float theta) => new Vector3(Mathf.Cos(theta), 0f, -Mathf.Sin(theta));

    static Ring MakeRing(Vector3 pos, float theta, float dist) =>
        new Ring { pos = pos, right = Right(theta), dist = dist };

    // Walks the segment list and records a cross-section (ring) every few meters/degrees.
    List<Ring> BuildRings()
    {
        var rings = new List<Ring>();
        Vector3 pos = Vector3.zero;
        float theta = 0f;
        float dist = 0f;
        rings.Add(MakeRing(pos, theta, dist));

        foreach (var seg in segments)
        {
            if (seg == TrackSegmentType.Straight)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(straightLength / Mathf.Max(0.1f, straightStep)));
                Vector3 f = Forward(theta);
                for (int j = 1; j <= n; j++)
                {
                    float s = straightLength * j / n;
                    rings.Add(MakeRing(pos + f * s, theta, dist + s));
                }
                pos += f * straightLength;
                dist += straightLength;
            }
            else
            {
                float k = seg == TrackSegmentType.RightTurn ? 1f : -1f; // +1 turns right, -1 turns left
                float total = turnAngle * Mathf.Deg2Rad;
                int n = Mathf.Max(1, Mathf.CeilToInt(turnAngle / Mathf.Max(0.5f, turnStepDegrees)));
                Vector3 center = pos + k * Right(theta) * turnRadius;

                for (int j = 1; j <= n; j++)
                {
                    float phi = total * j / n;
                    float th = theta + k * phi;
                    Vector3 p = center - k * Right(th) * turnRadius;
                    rings.Add(MakeRing(p, th, dist + turnRadius * phi));
                }
                theta += k * total;
                pos = rings[rings.Count - 1].pos;
                dist += turnRadius * total;
            }
        }

        if (closeLoop && rings.Count > 2)
        {
            Ring first = rings[0];
            Ring last = rings[rings.Count - 1];
            float gap = Vector3.Distance(first.pos, last.pos);
            if (gap > 0.1f)
            {
                Debug.LogWarning($"TrackGenerator: layout does not close (end is {gap:F2} m from the start). " +
                                 "Opposite straights must have equal total length and the turns must add up to 360°.", this);
            }
            else
            {
                // Remove the tiny float drift so the last ring sits exactly on the first.
                last.pos = first.pos;
                last.right = first.right;
                rings[rings.Count - 1] = last;
            }
        }

        return rings;
    }

    // Per ring: 0 topLeft, 1 topRight, 2 leftWall top, 3 leftWall bottom, 4 rightWall top, 5 rightWall bottom.
    // Walls use their own vertices so their edges stay sharp.
    void FillMesh(List<Ring> rings)
    {
        const int PerRing = 6;
        float half = roadWidth * 0.5f;
        int count = rings.Count;

        var verts = new Vector3[count * PerRing];
        var normals = new Vector3[count * PerRing];
        var uvs = new Vector2[count * PerRing];
        var tris = new int[(count - 1) * 18];

        for (int i = 0; i < count; i++)
        {
            Ring r = rings[i];
            Vector3 left = r.pos - r.right * half;
            Vector3 right = r.pos + r.right * half;
            Vector3 down = Vector3.down * edgeDepth;
            float v = r.dist / roadWidth; // one texture repeat per road-width of length
            int b = i * PerRing;

            verts[b + 0] = left;          normals[b + 0] = Vector3.up;  uvs[b + 0] = new Vector2(0f, v);
            verts[b + 1] = right;         normals[b + 1] = Vector3.up;  uvs[b + 1] = new Vector2(1f, v);
            verts[b + 2] = left;          normals[b + 2] = -r.right;    uvs[b + 2] = new Vector2(0f, v);
            verts[b + 3] = left + down;   normals[b + 3] = -r.right;    uvs[b + 3] = new Vector2(0.1f, v);
            verts[b + 4] = right;         normals[b + 4] = r.right;     uvs[b + 4] = new Vector2(1f, v);
            verts[b + 5] = right + down;  normals[b + 5] = r.right;     uvs[b + 5] = new Vector2(0.9f, v);
        }

        int t = 0;
        for (int i = 0; i < count - 1; i++)
        {
            int a = i * PerRing;
            int n = (i + 1) * PerRing;

            // top surface
            tris[t++] = a + 0; tris[t++] = n + 0; tris[t++] = a + 1;
            tris[t++] = a + 1; tris[t++] = n + 0; tris[t++] = n + 1;
            // right wall
            tris[t++] = a + 5; tris[t++] = a + 4; tris[t++] = n + 4;
            tris[t++] = a + 5; tris[t++] = n + 4; tris[t++] = n + 5;
            // left wall
            tris[t++] = a + 3; tris[t++] = n + 2; tris[t++] = a + 2;
            tris[t++] = a + 3; tris[t++] = n + 3; tris[t++] = n + 2;
        }

        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
    }

    void OnDestroy()
    {
        DestroyObject(mesh);
        DestroyObject(lineMesh);
        DestroyObject(lineWhite);
        DestroyObject(lineBlack);
    }
}
