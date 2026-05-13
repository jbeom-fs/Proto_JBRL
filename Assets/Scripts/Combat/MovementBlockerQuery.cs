using System.Collections.Generic;
using UnityEngine;

public static class MovementBlockerQuery
{
    private const int BlockerBufferSize = 128;

    private static readonly Collider2D[] s_BlockerBuffer = new Collider2D[BlockerBufferSize];
    private static readonly Dictionary<Collider2D, EnemyController> s_EnemyCache = new();

    public static bool IsPlayerMovementBlocked(Vector3 worldPosition, float radius)
    {
        float queryRadius = Mathf.Max(0.01f, radius);
        int count = Physics2D.OverlapCircle(
            worldPosition,
            queryRadius,
            CombatLayers.EnemyFilter,
            s_BlockerBuffer);

        for (int i = 0; i < count; i++)
        {
            Collider2D collider = s_BlockerBuffer[i];
            if (collider == null)
                continue;

            EnemyController enemy = ResolveEnemy(collider);
            if (enemy == null || !enemy.IsAlive)
                continue;

            EnemyData data = enemy.data;
            if (data != null && data.blocksMovement)
                return true;
        }

        return false;
    }

    private static EnemyController ResolveEnemy(Collider2D collider)
    {
        if (s_EnemyCache.TryGetValue(collider, out EnemyController cached))
        {
            if (cached != null)
                return cached;

            s_EnemyCache.Remove(collider);
        }

        EnemyController enemy = null;
        Rigidbody2D attachedRigidbody = collider.attachedRigidbody;
        if (attachedRigidbody != null)
            attachedRigidbody.TryGetComponent(out enemy);

        if (enemy == null)
            collider.TryGetComponent(out enemy);

        s_EnemyCache[collider] = enemy;
        return enemy;
    }
}
