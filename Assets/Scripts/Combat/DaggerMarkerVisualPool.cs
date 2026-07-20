using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DaggerMarkerVisualPool : MonoBehaviour
{
    private const string RuntimePoolName = "DaggerMarkerVisualPool";
    private const string DefaultMarkerResourcePath = "Dagger/Test_Marker";

    [SerializeField] private GameObject markerPrefab;
    [SerializeField] private GameObject burstPrefab;
    [SerializeField] private Sprite markerSprite;
    [SerializeField] private Vector3 markerOffset = Vector3.zero;
    [SerializeField, Min(0.01f)] private float burstLifetime = 0.5f;
    [SerializeField] private string markerSortingLayerName = "Actor";
    [SerializeField] private int markerSortingOrder = 50;

    private readonly Dictionary<EnemyController, GameObject> _activeMarkers = new();
    private readonly List<GameObject> _markerPool = new();
    private readonly List<GameObject> _burstPool = new();
    private readonly List<BurstState> _activeBursts = new();
    private readonly List<EnemyController> _scratchInvalidEnemies = new();

    public static DaggerMarkerVisualPool Active { get; private set; }

    private struct BurstState
    {
        public GameObject Instance;
        public float Remaining;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Active != null)
            return;

        GameObject poolObject = new GameObject(RuntimePoolName, typeof(DaggerMarkerVisualPool));
        DontDestroyOnLoad(poolObject);
    }

    private void Awake()
    {
        if (Active != null && Active != this)
        {
            enabled = false;
            return;
        }

        Active = this;
        ResolveMarkerSprite();
    }

    private void OnDestroy()
    {
        if (Active == this)
            Active = null;
    }

    private void LateUpdate()
    {
        TickMarkers();
        TickBursts(Time.deltaTime);
    }

    public void Show(EnemyController enemy)
    {
        if (enemy == null || !enemy.IsAlive)
            return;

        if (!_activeMarkers.TryGetValue(enemy, out GameObject marker) || marker == null)
        {
            marker = GetMarker();
            if (marker == null)
                return;

            _activeMarkers[enemy] = marker;
        }

        marker.transform.position = enemy.MarkerAnchorWorld + markerOffset;
        marker.SetActive(true);
    }

    public void Hide(EnemyController enemy)
    {
        if (ReferenceEquals(enemy, null))
            return;

        if (!_activeMarkers.TryGetValue(enemy, out GameObject marker))
            return;

        _activeMarkers.Remove(enemy);
        if (marker != null)
            marker.SetActive(false);
    }

    public void Burst(EnemyController enemy)
    {
        if (enemy == null || burstPrefab == null)
            return;

        GameObject burst = Get(burstPrefab, _burstPool);
        burst.transform.position = enemy.MarkerAnchorWorld;
        burst.SetActive(true);
        _activeBursts.Add(new BurstState
        {
            Instance = burst,
            Remaining = Mathf.Max(0.01f, burstLifetime)
        });
    }

    private void TickMarkers()
    {
        if (_activeMarkers.Count == 0)
            return;

        _scratchInvalidEnemies.Clear();
        foreach (KeyValuePair<EnemyController, GameObject> pair in _activeMarkers)
        {
            EnemyController enemy = pair.Key;
            GameObject marker = pair.Value;
            if (enemy == null || !enemy.IsAlive || marker == null)
            {
                _scratchInvalidEnemies.Add(enemy);
                continue;
            }

            marker.transform.position = enemy.MarkerAnchorWorld + markerOffset;
        }

        for (int i = 0; i < _scratchInvalidEnemies.Count; i++)
            Hide(_scratchInvalidEnemies[i]);
        _scratchInvalidEnemies.Clear();
    }

    private void TickBursts(float deltaTime)
    {
        for (int i = _activeBursts.Count - 1; i >= 0; i--)
        {
            BurstState state = _activeBursts[i];
            state.Remaining -= deltaTime;
            if (state.Remaining <= 0f)
            {
                if (state.Instance != null)
                    state.Instance.SetActive(false);
                _activeBursts.RemoveAt(i);
                continue;
            }

            _activeBursts[i] = state;
        }
    }

    private GameObject GetMarker()
    {
        if (markerPrefab != null)
            return Get(markerPrefab, _markerPool);

        ResolveMarkerSprite();
        if (markerSprite == null)
            return null;

        return GetGeneratedMarker();
    }

    private GameObject GetGeneratedMarker()
    {
        for (int i = 0; i < _markerPool.Count; i++)
        {
            GameObject instance = _markerPool[i];
            if (instance != null && !instance.activeSelf)
                return instance;
        }

        GameObject marker = new GameObject("DaggerMarker", typeof(SpriteRenderer));
        marker.transform.SetParent(transform, false);
        SpriteRenderer spriteRenderer = marker.GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = markerSprite;
        spriteRenderer.sortingLayerName = markerSortingLayerName;
        spriteRenderer.sortingOrder = markerSortingOrder;
        marker.SetActive(false);
        _markerPool.Add(marker);
        return marker;
    }

    private void ResolveMarkerSprite()
    {
        if (markerSprite != null)
            return;

        markerSprite = Resources.Load<Sprite>(DefaultMarkerResourcePath);
    }

    private GameObject Get(GameObject prefab, List<GameObject> pool)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            GameObject instance = pool[i];
            if (instance != null && !instance.activeSelf)
                return instance;
        }

        GameObject created = Instantiate(prefab, transform);
        created.SetActive(false);
        pool.Add(created);
        return created;
    }
}
