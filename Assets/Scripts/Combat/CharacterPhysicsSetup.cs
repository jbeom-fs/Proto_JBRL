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
            rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.sharedMaterial = GetNoFrictionMaterial();
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        CircleCollider2D circle = go.GetComponent<CircleCollider2D>();
        if (circle == null)
            circle = go.AddComponent<CircleCollider2D>();
        circle.isTrigger = false;
        circle.radius = 0.32f;
        circle.offset = Vector2.zero;
        circle.sharedMaterial = GetNoFrictionMaterial();

        foreach (BoxCollider2D box in go.GetComponents<BoxCollider2D>())
            box.enabled = false;

        int layer = LayerMask.NameToLayer(layerName);
        if (layer >= 0)
            go.layer = layer;

        return (rb, circle);
    }
}
