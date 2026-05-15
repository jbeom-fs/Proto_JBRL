using UnityEngine;

public static class CharacterPhysicsSetup
{
    private static PhysicsMaterial2D s_NoFrictionMaterial;

    public static PhysicsMaterial2D GetNoFrictionMaterial()
    {
        if (s_NoFrictionMaterial != null) return s_NoFrictionMaterial;

        s_NoFrictionMaterial = new PhysicsMaterial2D("NoFriction")
        {
            friction = 0f,
            bounciness = 0f
        };
        return s_NoFrictionMaterial;
    }

    public static (Rigidbody2D rb, CircleCollider2D circle) Configure(GameObject go, string layerName)
    {
        Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
        if (rb == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning($"[CharacterPhysicsSetup] {go.name}: Rigidbody2D가 없습니다. 자동 생성하지 않습니다.", go);
#endif
        }
        else
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.sharedMaterial = GetNoFrictionMaterial();
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        CircleCollider2D circle = go.GetComponent<CircleCollider2D>();
        if (circle == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning($"[CharacterPhysicsSetup] {go.name}: CircleCollider2D가 없습니다. 자동 생성하지 않습니다.", go);
#endif
        }
        else
        {
            circle.isTrigger = false;
            circle.sharedMaterial = GetNoFrictionMaterial();
        }

        int layer = LayerMask.NameToLayer(layerName);
        if (layer >= 0)
            go.layer = layer;

        return (rb, circle);
    }
}
