using UnityEngine;

/// <summary>
/// Flips the enemy’s visual so it always faces the player on X.
/// Assumes default art faces RIGHT. Works with EnemyBase.animTarget.
/// </summary>
[RequireComponent(typeof(EnemyBase))]
public class EnemyFacePlayer2D : MonoBehaviour
{
    [Header("What to flip")]
    [Tooltip("If empty, uses EnemyBase.animTarget; else use a child visual root.")]
    public Transform visualRoot;

    [Header("Behavior")]
    public bool useSpriteFlipInstead = false;  // if your art prefers SpriteRenderer.flipX
    public bool onlyWhenPlayerExists = true;   // skip if player not found
    public float deadzoneX = 0.01f;            // don’t flip if nearly aligned

    EnemyBase _base;
    Transform _player;
    Vector3 _baseScale;
    SpriteRenderer _sr; // optional, if you use flipX

    void Awake()
    {
        _base = GetComponent<EnemyBase>();
        if (!visualRoot) visualRoot = _base.animTarget ? _base.animTarget : transform;

        var pgo = GameObject.FindGameObjectWithTag(string.IsNullOrEmpty(_base.playerTag) ? "Player" : _base.playerTag);
        _player = pgo ? pgo.transform : null;

        _baseScale = visualRoot.localScale;

        if (useSpriteFlipInstead)
            _sr = visualRoot.GetComponentInChildren<SpriteRenderer>();
    }

    void LateUpdate()
    {
        if (onlyWhenPlayerExists && !_player) return;

        // Pick a target X to compare against our own X
        float dx = (_player ? _player.position.x : Camera.main.transform.position.x) - transform.position.x;
        if (Mathf.Abs(dx) < deadzoneX) return; // avoid jitter

        // Default faces RIGHT. If player is to the LEFT, face left (negative X).
        bool faceLeft = dx < 0f;

        if (useSpriteFlipInstead && _sr)
        {
            // For art that faces right by default, flipX = true means face LEFT.
            _sr.flipX = faceLeft;
        }
        else
        {
            // Preserve current magnitude (animations may scale), just set the sign.
            var s = visualRoot.localScale;
            float magX = Mathf.Abs(s.x) > 0.0001f ? Mathf.Abs(s.x) : Mathf.Abs(_baseScale.x);
            s.x = faceLeft ? -magX : magX;
            visualRoot.localScale = s;
        }
    }
}
