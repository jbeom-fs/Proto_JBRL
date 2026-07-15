using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders short-lived world-space panels for resolved InstantArea hit shapes.
/// Inputs are consumed into pooled meshes immediately; no target or cell list is retained.
/// </summary>
public sealed class SkillHitFlashRenderer : IDisposable
{
    public const int PoolCapacity = 8;
    public const int PreviewFillSortingOffset = 10;
    public const int FlashSortingOffset = PreviewFillSortingOffset + 1;

    private const float CellFillScale = 0.95f;
    private static readonly int s_ColorPropertyId = Shader.PropertyToID("_Color");

    private readonly Entry[] _entries = new Entry[PoolCapacity];
    private readonly GameObject _root;
    private readonly Material _sharedMaterial;
    private long _nextActivationSequence;
    private bool _disposed;

    public SkillHitFlashRenderer(int sortingLayerId, int sortingOrder)
    {
        _root = new GameObject("SkillHitFlashPool")
        {
            hideFlags = HideFlags.DontSave
        };

        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            _sharedMaterial = new Material(shader)
            {
                name = "SkillHitFlashMaterial",
                color = Color.white,
                hideFlags = HideFlags.DontSave
            };
        }

        for (int i = 0; i < _entries.Length; i++)
        {
            _entries[i] = new Entry(
                i,
                _root.transform,
                _sharedMaterial,
                sortingLayerId,
                sortingOrder);
        }
    }

    public void Flash(
        IReadOnlyList<Vector3> worldTargets,
        CustomShapeMatcher? customShape,
        float cellSize,
        Color color,
        float duration)
    {
        if (_disposed || duration <= 0f)
            return;

        int cellCount = customShape.HasValue
            ? customShape.Value.Cells?.Count ?? 0
            : worldTargets?.Count ?? 0;
        if (cellCount <= 0)
            return;

        Entry entry = RentEntry();
        if (customShape.HasValue)
            entry.BuildCustom(worldTargets, customShape.Value);
        else
            entry.BuildLegacy(worldTargets, cellSize);

        entry.Activate(color, duration, ++_nextActivationSequence);
    }

    public void Tick(float deltaTime)
    {
        if (_disposed)
            return;

        float elapsed = Mathf.Max(0f, deltaTime);
        for (int i = 0; i < _entries.Length; i++)
            _entries[i].Tick(elapsed);
    }

    public void Clear()
    {
        if (_disposed)
            return;

        for (int i = 0; i < _entries.Length; i++)
            _entries[i].Deactivate();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        for (int i = 0; i < _entries.Length; i++)
            _entries[i].Dispose();

        if (_sharedMaterial != null)
            UnityEngine.Object.Destroy(_sharedMaterial);
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
    }

    private Entry RentEntry()
    {
        for (int i = 0; i < _entries.Length; i++)
        {
            if (!_entries[i].IsActive)
                return _entries[i];
        }

        Entry oldest = _entries[0];
        for (int i = 1; i < _entries.Length; i++)
        {
            if (_entries[i].ActivationSequence < oldest.ActivationSequence)
                oldest = _entries[i];
        }

        return oldest;
    }

    private sealed class Entry : IDisposable
    {
        private readonly GameObject _gameObject;
        private readonly MeshRenderer _renderer;
        private readonly Mesh _mesh;
        private readonly MaterialPropertyBlock _propertyBlock = new();
        private Vector3[] _vertices;
        private Vector2[] _uvs;
        private int[] _triangles;
        private Color _color;
        private float _duration;
        private float _remaining;

        public Entry(
            int index,
            Transform parent,
            Material sharedMaterial,
            int sortingLayerId,
            int sortingOrder)
        {
            _gameObject = new GameObject($"SkillHitFlash_{index + 1}")
            {
                hideFlags = HideFlags.DontSave
            };
            _gameObject.transform.SetParent(parent, false);

            MeshFilter filter = _gameObject.AddComponent<MeshFilter>();
            _renderer = _gameObject.AddComponent<MeshRenderer>();
            _mesh = new Mesh
            {
                name = $"SkillHitFlashMesh_{index + 1}",
                hideFlags = HideFlags.DontSave
            };
            _mesh.MarkDynamic();
            filter.sharedMesh = _mesh;
            _renderer.sharedMaterial = sharedMaterial;
            _renderer.sortingLayerID = sortingLayerId;
            _renderer.sortingOrder = sortingOrder;
            _gameObject.SetActive(false);
        }

        public bool IsActive => _gameObject != null && _gameObject.activeSelf;
        public long ActivationSequence { get; private set; }

        public void BuildCustom(
            IReadOnlyList<Vector3> worldTargets,
            CustomShapeMatcher matcher)
        {
            IReadOnlyList<Vector2Int> cells = matcher.Cells;
            int cellCount = cells.Count;
            bool topologyChanged = EnsureBuffers(cellCount);
            float half = Mathf.Max(0.01f, matcher.CellSize) * CellFillScale * 0.5f;
            float radians = matcher.AngleDeg * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            Vector3 right = new Vector3(cosine * half, sine * half, 0f);
            Vector3 up = new Vector3(-sine * half, cosine * half, 0f);
            float worldZ = worldTargets != null && worldTargets.Count > 0
                ? worldTargets[0].z
                : 0f;

            for (int i = 0; i < cellCount; i++)
            {
                Vector2 center2D = matcher.GetCellWorldCenter(cells[i]);
                WriteQuad(i, new Vector3(center2D.x, center2D.y, worldZ), right, up);
            }

            UploadMesh(topologyChanged);
        }

        public void BuildLegacy(IReadOnlyList<Vector3> worldTargets, float cellSize)
        {
            int cellCount = worldTargets.Count;
            bool topologyChanged = EnsureBuffers(cellCount);
            float half = Mathf.Max(0.01f, cellSize) * CellFillScale * 0.5f;
            Vector3 right = new Vector3(half, 0f, 0f);
            Vector3 up = new Vector3(0f, half, 0f);

            for (int i = 0; i < cellCount; i++)
                WriteQuad(i, worldTargets[i], right, up);

            UploadMesh(topologyChanged);
        }

        public void Activate(Color color, float duration, long activationSequence)
        {
            _color = color;
            _duration = Mathf.Max(0.0001f, duration);
            _remaining = _duration;
            ActivationSequence = activationSequence;
            ApplyColor(1f);
            _gameObject.SetActive(true);
        }

        public void Tick(float deltaTime)
        {
            if (!IsActive)
                return;

            _remaining -= deltaTime;
            if (_remaining <= 0f)
            {
                Deactivate();
                return;
            }

            ApplyColor(_remaining / _duration);
        }

        public void Deactivate()
        {
            _remaining = 0f;
            if (_gameObject != null)
                _gameObject.SetActive(false);
        }

        public void Dispose()
        {
            if (_mesh != null)
                UnityEngine.Object.Destroy(_mesh);
        }

        private bool EnsureBuffers(int cellCount)
        {
            int vertexCount = cellCount * 4;
            int triangleCount = cellCount * 6;
            if (_vertices != null &&
                _vertices.Length == vertexCount &&
                _triangles.Length == triangleCount)
            {
                return false;
            }

            _vertices = new Vector3[vertexCount];
            _uvs = new Vector2[vertexCount];
            _triangles = new int[triangleCount];

            for (int i = 0; i < cellCount; i++)
            {
                int vertexIndex = i * 4;
                int triangleIndex = i * 6;

                _uvs[vertexIndex] = new Vector2(0f, 0f);
                _uvs[vertexIndex + 1] = new Vector2(1f, 0f);
                _uvs[vertexIndex + 2] = new Vector2(1f, 1f);
                _uvs[vertexIndex + 3] = new Vector2(0f, 1f);

                _triangles[triangleIndex] = vertexIndex;
                _triangles[triangleIndex + 1] = vertexIndex + 2;
                _triangles[triangleIndex + 2] = vertexIndex + 1;
                _triangles[triangleIndex + 3] = vertexIndex;
                _triangles[triangleIndex + 4] = vertexIndex + 3;
                _triangles[triangleIndex + 5] = vertexIndex + 2;
            }

            return true;
        }

        private void WriteQuad(int cellIndex, Vector3 center, Vector3 right, Vector3 up)
        {
            int vertexIndex = cellIndex * 4;
            _vertices[vertexIndex] = center - right - up;
            _vertices[vertexIndex + 1] = center + right - up;
            _vertices[vertexIndex + 2] = center + right + up;
            _vertices[vertexIndex + 3] = center - right + up;
        }

        private void UploadMesh(bool topologyChanged)
        {
            if (topologyChanged)
            {
                _mesh.Clear();
                _mesh.vertices = _vertices;
                _mesh.uv = _uvs;
                _mesh.triangles = _triangles;
            }
            else
            {
                _mesh.vertices = _vertices;
            }

            _mesh.RecalculateBounds();
        }

        private void ApplyColor(float alphaRatio)
        {
            Color faded = _color;
            faded.a *= Mathf.Clamp01(alphaRatio);
            _propertyBlock.SetColor(s_ColorPropertyId, faded);
            _renderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
