// LightCloakWalker.cs
// Procedural "light cloaked swordsman" — 1:1 vibe of the reference (glow outline, hood, face, eyes, cape, sword).
// No textures, no Animator. Shapes are meshes (discs, capsules, quads, triangles) with Unlit/Color.
// Works in Built-in/URP. Guaranteed visible (auto Main Camera, centers at start).

using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class LightCloakWalker : MonoBehaviour
{
    // -------- Public tuning --------
    [Header("Movement")]
    public bool driveWithInput = true;
    public float moveSpeed = 3.5f;

    [Header("Walk Cycle")]
    public float stepFrequency = 2.3f;
    public float bobHeight = 0.08f;
    public float legSwing = 32f;          // deg
    public float armSwing = 20f;          // deg
    public float capeSway = 12f;          // deg
    public float footLift = 0.06f;

    [Header("Look / Scale")]
    public float scale = 1.0f;            // global character scale
    public Color lightCore = new(1.00f, 0.97f, 0.80f, 1f);
    public Color bodyDark = new(0.02f, 0.02f, 0.02f, 1f);
    public Color outerGlow = new(1.00f, 0.92f, 0.55f, 0.30f);
    public float outlineThickness = 1.14f; // glow expansion

    [Header("Guarantee Visibility")]
    public bool snapToCameraOnStart = true;

    // -------- Internals --------
    Rigidbody2D rb;
    Transform visRoot, hood, face, eyeL, eyeR, cloak, armSword, armBack, legFront, legBack, cape, sword, swordTip, bigGlow;
    Material matLight, matDark, matGlow;
    float phase, lastDir = 1f;
    Vector2 desiredV;

    void Awake()
    {
        EnsureMainCamera();

        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        // Materials (Unlit/Color fallback chain)
        Shader sh = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        matLight = new Material(sh); SetColor(matLight, lightCore);
        matDark = new Material(sh); SetColor(matDark, bodyDark);
        matGlow = new Material(sh); SetColor(matGlow, outerGlow);

        // Visual root (so physics stays clean)
        visRoot = new GameObject("Vis").transform;
        visRoot.SetParent(transform, false);
        visRoot.localScale = Vector3.one * scale;

        // --- Build silhouette parts (z ordering by local Z) ---
        // Giant soft halo behind everything
        bigGlow = Disc("BigGlow", 1.8f, matGlow, -0.20f, 42);
        bigGlow.SetParent(visRoot, false);

        // Cloak body (capsule-ish blob)
        cloak = Capsule("Cloak", 0.95f, 0.85f, matDark, -0.05f, 28);
        cloak.SetParent(visRoot, false);
        cloak.localPosition = new Vector3(0f, -0.02f, 0f);

        // Hood (disc) + glow outline
        hood = Disc("Hood", 0.55f, matDark, -0.06f, 36); hood.SetParent(visRoot, false);
        hood.localPosition = new Vector3(0.02f, 0.55f, 0f);
        var hoodGlow = Disc("HoodGlow", 0.55f * outlineThickness, matGlow, -0.07f, 36); hoodGlow.SetParent(visRoot, false); hoodGlow.localPosition = hood.localPosition;

        // Face (light oval) + eyes (dark discs)
        face = Disc("Face", 0.46f, matLight, -0.08f, 28); face.SetParent(visRoot, false);
        face.localPosition = hood.localPosition + new Vector3(-0.02f, -0.01f, 0f);
        eyeL = Disc("EyeL", 0.07f, matDark, -0.09f, 20); eyeL.SetParent(face, false); eyeL.localPosition = new Vector3(-0.13f, 0.02f, 0f);
        eyeR = Disc("EyeR", 0.07f, matDark, -0.09f, 20); eyeR.SetParent(face, false); eyeR.localPosition = new Vector3(+0.08f, 0.02f, 0f);

        // Cape (behind cloak)
        cape = Quad("Cape", 0.95f, 1.00f, matDark, -0.10f); cape.SetParent(visRoot, false);
        cape.localPosition = new Vector3(-0.06f, 0.10f, 0f);
        var capeGlow = Quad("CapeGlow", 0.95f * outlineThickness, 1.00f * outlineThickness, matGlow, -0.11f);
        capeGlow.SetParent(visRoot, false); capeGlow.localPosition = cape.localPosition;

        // Legs (rounded capsules)
        legFront = Capsule("LegFront", 0.26f, 0.52f, matDark, +0.01f, 20); legFront.SetParent(visRoot, false);
        legBack = Capsule("LegBack", 0.26f, 0.52f, matDark, +0.00f, 20); legBack.SetParent(visRoot, false);
        legFront.localPosition = new Vector3(+0.18f, -0.65f, 0f);
        legBack.localPosition = new Vector3(-0.16f, -0.66f, 0f);

        // Arms
        armBack = Capsule("ArmBack", 0.20f, 0.46f, matDark, -0.02f, 20); armBack.SetParent(visRoot, false);
        armBack.localPosition = new Vector3(-0.30f, -0.05f, 0f);
        armSword = Capsule("ArmSword", 0.22f, 0.48f, matDark, -0.02f, 20); armSword.SetParent(visRoot, false);
        armSword.localPosition = new Vector3(+0.34f, -0.06f, 0f);

        // Sword (light)
        sword = Quad("Sword", 0.10f, 0.55f, matLight, -0.03f); sword.SetParent(armSword, false);
        sword.localPosition = new Vector3(+0.10f, +0.30f, 0f);
        sword.localRotation = Quaternion.Euler(0, 0, 10f);
        swordTip = Triangle("SwordTip", 0.18f, 0.18f, matLight, -0.035f); swordTip.SetParent(sword, false);
        swordTip.localPosition = new Vector3(0f, +0.37f, 0f);

        // Outer “character glow” (a slightly bigger clone)
        var outline = Capsule("Outline", 0.98f * outlineThickness, 0.90f * outlineThickness, matGlow, -0.12f, 28);
        outline.SetParent(visRoot, false);
        outline.localPosition = cloak.localPosition + new Vector3(0f, 0.02f, 0f);
    }

    void Start()
    {
        if (snapToCameraOnStart && Camera.main)
        {
            var p = Camera.main.transform.position; p.z = 0f;
            transform.position = p;
        }
    }

    void Update()
    {
        if (driveWithInput)
        {
            float x = Input.GetAxisRaw("Horizontal");
            desiredV = new Vector2(x * moveSpeed, 0f);
        }

        float speed = Mathf.Abs(rb.linearVelocity.x);
        float hz = Mathf.Lerp(0f, stepFrequency, Mathf.Clamp01(speed / Mathf.Max(0.01f, moveSpeed)));
        phase += hz * Mathf.PI * 2f * Time.deltaTime;

        // Facing
        float dir = rb.linearVelocity.x != 0 ? Mathf.Sign(rb.linearVelocity.x) : lastDir;
        lastDir = dir;
        visRoot.localScale = new Vector3(scale * (dir >= 0 ? 1f : -1f), scale, 1f);

        // Root bob
        float bob = Mathf.Sin(phase) * bobHeight * scale;
        visRoot.localPosition = new Vector3(0, bob, 0);

        // Legs swing (front/back out-of-phase) + little vertical lift on the stepping leg
        float a = Mathf.Sin(phase) * legSwing;
        float b = Mathf.Sin(phase + Mathf.PI) * legSwing;
        legFront.localRotation = Quaternion.Euler(0, 0, a);
        legBack.localRotation = Quaternion.Euler(0, 0, b);
        float liftF = Mathf.Max(0, Mathf.Cos(phase)) * footLift * scale;
        float liftB = Mathf.Max(0, Mathf.Cos(phase + Mathf.PI)) * footLift * scale;
        legFront.localPosition = new Vector3(+0.18f, -0.65f + liftF, 0f);
        legBack.localPosition = new Vector3(-0.16f, -0.66f + liftB, 0f);

        // Arms (sword arm counter-sways)
        float armA = Mathf.Sin(phase + Mathf.PI) * armSwing;
        float armB = Mathf.Sin(phase) * (armSwing * 0.8f);
        armSword.localRotation = Quaternion.Euler(0, 0, armA);
        armBack.localRotation = Quaternion.Euler(0, 0, armB);

        // Cape sway with slight lag
        float capeA = Mathf.Sin(phase - 0.5f) * capeSway * (0.6f + 0.4f * Mathf.Clamp01(speed));
        cape.localRotation = Quaternion.Euler(0, 0, -capeA);
    }

    void FixedUpdate()
    {
        var v = rb.linearVelocity;
        var a = (desiredV - v) * 8f;
        rb.linearVelocity = v + a * Time.fixedDeltaTime;
    }

    // --------- Shape builders ---------
    Transform Disc(string name, float radius, Material mat, float z, int seg = 24)
    {
        var go = new GameObject(name);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        mf.sharedMesh = MakeDiscMesh(radius, seg);
        go.transform.localPosition = new Vector3(0, 0, z);
        return go.transform;
    }

    Transform Quad(string name, float w, float h, Material mat, float z)
    {
        var go = new GameObject(name);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        mf.sharedMesh = MakeQuadMesh(w, h);
        go.transform.localPosition = new Vector3(0, 0, z);
        return go.transform;
    }

    Transform Capsule(string name, float w, float h, Material mat, float z, int seg = 20)
    {
        // Center quad + top/bot semicircle discs
        var root = new GameObject(name).transform;
        var body = Quad(name + "_Mid", w, h - w, mat, z); body.SetParent(root, false);
        var top = Disc(name + "_Top", w * 0.5f, mat, z, seg); top.SetParent(root, false); top.localPosition = new Vector3(0, (h - w) / 2f, 0);
        var bot = Disc(name + "_Bot", w * 0.5f, mat, z, seg); bot.SetParent(root, false); bot.localPosition = new Vector3(0, -(h - w) / 2f, 0);
        return root;
    }

    Transform Triangle(string name, float baseW, float height, Material mat, float z)
    {
        var go = new GameObject(name);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        mf.sharedMesh = MakeTriMesh(baseW, height);
        go.transform.localPosition = new Vector3(0, 0, z);
        return go.transform;
    }

    // --------- Mesh generators ---------
    static Mesh MakeQuadMesh(float w, float h)
    {
        var m = new Mesh();
        m.vertices = new Vector3[] {
            new(-w/2,-h/2,0), new(w/2,-h/2,0), new(-w/2,h/2,0), new(w/2,h/2,0)
        };
        m.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateBounds(); m.RecalculateNormals();
        return m;
    }
    static Mesh MakeTriMesh(float baseW, float height)
    {
        var m = new Mesh();
        m.vertices = new Vector3[] { new(-baseW / 2, 0, 0), new(baseW / 2, 0, 0), new(0, height, 0) };
        m.triangles = new int[] { 0, 2, 1 };
        m.RecalculateBounds(); m.RecalculateNormals();
        return m;
    }
    static Mesh MakeDiscMesh(float radius, int segments)
    {
        segments = Mathf.Max(12, segments);
        var verts = new Vector3[segments + 1];
        var tris = new int[segments * 3];
        verts[0] = Vector3.zero;
        for (int i = 0; i < segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0);
            tris[i * 3 + 0] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = i == segments - 1 ? 1 : i + 2;
        }
        var m = new Mesh();
        m.vertices = verts; m.triangles = tris;
        m.RecalculateBounds(); m.RecalculateNormals();
        return m;
    }

    // --------- Utils ---------
    static void SetColor(Material m, Color c)
    {
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
    }
    static void EnsureMainCamera()
    {
        if (Camera.main) return;
        var go = new GameObject("Main Camera");
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 5;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.02f, 0.02f, 0.02f, 1f);
        go.tag = "MainCamera"; go.transform.position = new Vector3(0, 0, -10);
    }
}
