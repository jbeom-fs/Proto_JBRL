using UnityEngine;

public sealed class ArenaHealthBarPanel : MonoBehaviour
{
    public static ArenaHealthBarPanel Active { get; private set; }

    [SerializeField] private ArenaHealthBarRowUI[] bossRows;
    [SerializeField] private ArenaHealthBarRowUI[] eliteRows;

    private bool _warnedBossCapacity;
    private bool _warnedEliteCapacity;

    private void Awake()
    {
        if (Active != null && !ReferenceEquals(Active, this))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning(
                $"[ArenaHealthBarPanel] Replacing existing Active instance ({Active.name}) with {name}.",
                this);
#endif
        }

        Active = this;
        DetachAll();
        PrewarmRows(bossRows);
        PrewarmRows(eliteRows);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Active, this))
            Active = null;
    }

    public void Attach(EnemyController enemy, bool isBoss)
    {
        if (enemy == null || FindBoundRow(enemy) != null)
            return;

        ArenaHealthBarRowUI[] rows = isBoss ? bossRows : eliteRows;
        if (rows != null)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                ArenaHealthBarRowUI row = rows[i];
                if (row == null || !row.IsAvailable)
                    continue;

                row.Bind(enemy);
                return;
            }
        }

        WarnCapacityOnce(isBoss);
    }

    public void Detach(EnemyController enemy)
    {
        if (enemy == null)
            return;

        ArenaHealthBarRowUI row = FindBoundRow(enemy);
        row?.Release();
    }

    public void DetachAll()
    {
        ReleaseRows(bossRows);
        ReleaseRows(eliteRows);
    }

    private ArenaHealthBarRowUI FindBoundRow(EnemyController enemy)
    {
        ArenaHealthBarRowUI row = FindBoundRow(bossRows, enemy);
        return row != null ? row : FindBoundRow(eliteRows, enemy);
    }

    private static ArenaHealthBarRowUI FindBoundRow(
        ArenaHealthBarRowUI[] rows,
        EnemyController enemy)
    {
        if (rows == null)
            return null;

        for (int i = 0; i < rows.Length; i++)
        {
            ArenaHealthBarRowUI row = rows[i];
            if (row != null && row.BoundEnemy == enemy)
                return row;
        }

        return null;
    }

    private static void ReleaseRows(ArenaHealthBarRowUI[] rows)
    {
        if (rows == null)
            return;

        for (int i = 0; i < rows.Length; i++)
            rows[i]?.Release();
    }

    private static void PrewarmRows(ArenaHealthBarRowUI[] rows)
    {
        if (rows == null)
            return;

        for (int i = 0; i < rows.Length; i++)
            rows[i]?.AilmentStrip?.Prewarm();
    }

    private void WarnCapacityOnce(bool isBoss)
    {
        if (isBoss)
        {
            if (_warnedBossCapacity)
                return;

            _warnedBossCapacity = true;
            Debug.LogWarning("[ArenaHealthBarPanel] No available boss health bar row.", this);
            return;
        }

        if (_warnedEliteCapacity)
            return;

        _warnedEliteCapacity = true;
        Debug.LogWarning("[ArenaHealthBarPanel] No available elite health bar row.", this);
    }
}
