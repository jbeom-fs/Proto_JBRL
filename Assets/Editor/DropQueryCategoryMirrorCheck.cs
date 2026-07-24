using System;
using UnityEditor;
using UnityEngine;

internal static class DropQueryCategoryMirrorCheck
{
    [InitializeOnLoadMethod]
    private static void Validate()
    {
        foreach (ItemType value in Enum.GetValues(typeof(ItemType)))
        {
            if (!Enum.IsDefined(typeof(DropQueryCategory), (int)value))
                Debug.LogError("[DropQueryCategory] Missing ItemType mirror: " + value + " (" + (int)value + ").");
        }
    }
}
