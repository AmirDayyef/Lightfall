using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Blender-style Array in one script:
/// 1) First arrays along X (row)
/// 2) Then arrays those rows along Y (grid)
/// Includes inspector buttons: Generate/Rebuild, Clear.
/// Works in Edit Mode and Play Mode.
/// </summary>
[ExecuteAlways]
public class GridArrayGenerator : MonoBehaviour
{
    [Header("Source")]
    public GameObject prefab;

    [Header("Counts")]
    [Min(1)] public int countX = 5;     // items per row (X)
    [Min(1)] public int countY = 3;     // rows (Y)

    [Header("Spacing (world units)")]
    public float stepX = 1.0f;          // spacing along X
    public float stepY = 1.0f;          // spacing along Y

    [Header("Placement")]
    public bool alignRotationToThis = true; // copy this transform's rotation
    public bool parentInstances = true;     // parent clones to this transform
    public bool centerOnGrid = false;       // center the grid around the generator

    [Header("Rebuild Options")]
    public bool autoRebuildInEditor = true; // auto regenerate on inspector changes (Editor)
    public bool clearBeforeBuild = true;    // delete children before generating

    // Context-menu shortcuts
    [ContextMenu("Generate / Rebuild")]
    public void GenerateOrRebuild()
    {
        if (!prefab) return;

        if (clearBeforeBuild) ClearChildren();

        var baseRot = alignRotationToThis ? transform.rotation : Quaternion.identity;
        var basePos = transform.position;

        // Optional center offset so the whole grid is centered on this transform
        Vector3 centerOffset = Vector3.zero;
        if (centerOnGrid)
        {
            float totalX = (countX - 1) * stepX;
            float totalY = (countY - 1) * stepY;
            centerOffset = new Vector3(-totalX * 0.5f, -totalY * 0.5f, 0f);
        }

        for (int y = 0; y < countY; y++)
        {
            float rowY = y * stepY;

            for (int x = 0; x < countX; x++)
            {
                float colX = x * stepX;

                var pos = basePos + centerOffset + new Vector3(colX, rowY, 0f);
                GameObject clone;

#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    // Keep prefab linkage in editor
                    clone = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    clone.transform.SetPositionAndRotation(pos, baseRot);
                }
                else
                {
                    clone = Instantiate(prefab, pos, baseRot);
                }
#else
                clone = Instantiate(prefab, pos, baseRot);
#endif
                if (parentInstances) clone.transform.SetParent(transform, true);
                clone.name = $"{prefab.name}_x{x:00}_y{y:00}";
            }
        }
    }

    [ContextMenu("Clear Children")]
    public void ClearChildren()
    {
        var toDestroy = new System.Collections.Generic.List<GameObject>();
        foreach (Transform child in transform) toDestroy.Add(child.gameObject);

        foreach (var go in toDestroy)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(go);
            else Destroy(go);
#else
            Destroy(go);
#endif
        }
    }

#if UNITY_EDITOR
    // Auto rebuild on inspector changes (Editor only)
    void OnValidate()
    {
        if (!autoRebuildInEditor) return;
        if (!prefab) return;

        countX = Mathf.Max(1, countX);
        countY = Mathf.Max(1, countY);

        // Avoid spam when entering playmode
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GenerateOrRebuild();
        }
    }
#endif
}

#if UNITY_EDITOR
// In-file custom inspector with buttons
[CustomEditor(typeof(GridArrayGenerator))]
public class GridArrayGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var gen = (GridArrayGenerator)target;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Array Controls", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate / Rebuild", GUILayout.Height(28)))
            {
                Undo.RegisterFullObjectHierarchyUndo(gen.gameObject, "GridArray Generate");
                gen.GenerateOrRebuild();
            }

            if (GUILayout.Button("Clear Children", GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog("Clear Children?",
                    "Delete all child objects under this generator?", "Clear", "Cancel"))
                {
                    Undo.RegisterFullObjectHierarchyUndo(gen.gameObject, "GridArray Clear");
                    gen.ClearChildren();
                }
            }
        }
    }
}
#endif
