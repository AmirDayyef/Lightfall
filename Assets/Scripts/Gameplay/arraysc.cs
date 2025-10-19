using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Experimental.SceneManagement;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
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

#if UNITY_EDITOR
    bool _pendingRebuild;
#endif

    public void GenerateOrRebuild()
    {
        if (!prefab) return;

#if UNITY_EDITOR
        if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabAsset(gameObject))
            return;
#endif

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
                    Undo.RegisterCreatedObjectUndo(clone, "GridArray Create");
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
#if UNITY_EDITOR
        if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabAsset(gameObject))
            return;
#endif

        var toDestroy = new System.Collections.Generic.List<GameObject>();
        foreach (Transform child in transform) toDestroy.Add(child.gameObject);

        foreach (var go in toDestroy)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.DestroyObjectImmediate(go);
            }
            else
            {
                Destroy(go);
            }
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

        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (BuildPipeline.isBuildingPlayer) return;
        if (PrefabUtility.IsPartOfPrefabAsset(gameObject)) return;

        if (_pendingRebuild) return;
        _pendingRebuild = true;

        EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            _pendingRebuild = false;

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.IsPartOfPrefabContents(gameObject)) { }

            if (BuildPipeline.isBuildingPlayer) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Undo.RegisterFullObjectHierarchyUndo(gameObject, "GridArray Auto Rebuild");
            GenerateOrRebuild();

            if (!Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
        };
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
                EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
            }

            if (GUILayout.Button("Clear Children", GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog("Clear Children?",
                    "Delete all child objects under this generator?", "Clear", "Cancel"))
                {
                    Undo.RegisterFullObjectHierarchyUndo(gen.gameObject, "GridArray Clear");
                    gen.ClearChildren();
                    EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
                }
            }
        }
    }
}
#endif
