using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class GridArrayGenerator : MonoBehaviour
{
    public GameObject prefab;

    [Min(1)] public int countX = 5;
    [Min(1)] public int countY = 3;

    public float stepX = 1.0f;
    public float stepY = 1.0f;

    public bool alignRotationToThis = true;
    public bool parentInstances = true;
    public bool centerOnGrid = false;

    public bool autoRebuildInEditor = true;
    public bool clearBeforeBuild = true;

    public void GenerateOrRebuild()
    {
        if (!prefab) return;

        if (clearBeforeBuild) ClearChildren();

        var baseRot = alignRotationToThis ? transform.rotation : Quaternion.identity;
        var basePos = transform.position;

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
    void OnValidate()
    {
        if (!autoRebuildInEditor) return;
        if (!prefab) return;

        countX = Mathf.Max(1, countX);
        countY = Mathf.Max(1, countY);

        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GenerateOrRebuild();
        }
    }
#endif
}

#if UNITY_EDITOR
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
