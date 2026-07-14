using UnityEngine;

public static class AimDirectionUtility
{
    private const float AxisDeadZone = 0.001f;

    /// <summary>
    /// Converts the authored-space +Y=forward convention to a Z-axis rotation angle in degrees.
    /// </summary>
    public static float ToAuthoredFacingAngle(Vector2 facing)
    {
        return Vector2.SignedAngle(Vector2.up, facing);
    }

    public static bool TryGetEightWayRaw(Vector2 input, out Vector2Int rawDirection)
    {
        rawDirection = new Vector2Int(
            QuantizeAxis(input.x),
            QuantizeAxis(input.y));

        return rawDirection != Vector2Int.zero;
    }

    public static Vector2Int ResolveEightWayRaw(Vector2 input, Vector2Int fallback)
    {
        return TryGetEightWayRaw(input, out Vector2Int rawDirection)
            ? rawDirection
            : ResolveFallbackRaw(fallback);
    }

    public static Vector2 ToNormalizedDirection(Vector2Int rawDirection)
    {
        rawDirection = ResolveFallbackRaw(rawDirection);
        return new Vector2(rawDirection.x, rawDirection.y).normalized;
    }

    public static Vector2Int ToCardinalDirection(Vector2Int rawDirection)
    {
        rawDirection = ResolveFallbackRaw(rawDirection);

        if (Mathf.Abs(rawDirection.x) >= Mathf.Abs(rawDirection.y))
            return new Vector2Int(rawDirection.x > 0 ? 1 : -1, 0);

        return new Vector2Int(0, rawDirection.y > 0 ? 1 : -1);
    }

    private static Vector2Int ResolveFallbackRaw(Vector2Int rawDirection)
    {
        return rawDirection != Vector2Int.zero ? rawDirection : Vector2Int.down;
    }

    private static int QuantizeAxis(float value)
    {
        if (Mathf.Abs(value) <= AxisDeadZone)
            return 0;

        return value > 0f ? 1 : -1;
    }
}
