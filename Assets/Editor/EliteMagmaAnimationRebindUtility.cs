using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public static class EliteMagmaAnimationRebindUtility
{
    private const string SpritePath = "Assets/Sprite/Enemy/Elite/Elite_Magma_01_px.png";
    private const string AnimationDir = "Assets/Animation/Enemy/Elite/Elite_Magma_01";
    private const string PrefabPath = "Assets/Perfabs/Enemy/Elite_Magma_01.prefab";
    private const int Columns = 6;
    private const int Rows = 6;
    private const int ExpectedCellSize = 256;
    private const int ExpectedTextureSize = ExpectedCellSize * Columns;
    private const float FrameRate = 12f;
    private const string SessionKey = "EliteMagmaAnimationRebindUtility.Rebound.px.v1";

    [InitializeOnLoadMethod]
    private static void RebindOnEditorLoad()
    {
        if (SessionState.GetBool(SessionKey, false))
            return;

        SessionState.SetBool(SessionKey, true);
        EditorApplication.delayCall += RebindSafely;
    }

    public static void Rebind()
    {
        ConfigureImporter();
        AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceUpdate);

        Sprite[] sprites = LoadOrderedSprites();
        RebuildClip("Elite_Magma_01_Idle", sprites, 0, true);
        RebuildClip("Elite_Magma_01_Walk", sprites, 1, true);
        RebuildClip("Elite_Magma_01_Dash", sprites, 2, false);
        RebuildClip("Elite_Magma_01_Jump", sprites, 3, false);
        RebuildClip("Elite_Magma_01_Projectile", sprites, 4, false);
        RebuildClip("Elite_Magma_01_Death", sprites, 5, false);
        UpdatePrefabDefaultSprite(sprites[0]);

    }

    private static void RebindSafely()
    {
        try
        {
            Rebind();
        }
        catch (Exception ex)
        {
            Debug.LogError("[EliteMagmaAnimationRebindUtility] Failed to rebind Elite_Magma_01 animation sprites.\n" + ex);
        }
    }

    private static void ConfigureImporter()
    {
        TextureImporter importer = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException("Missing importer: " + SpritePath);

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.spritePixelsPerUnit = 100f;
        importer.spritePivot = new Vector2(0.5f, 0f);

        importer.SaveAndReimport();
        WriteSpriteRects(importer);
        importer.SaveAndReimport();
    }

    private static void WriteSpriteRects(TextureImporter importer)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SpritePath);
        if (texture == null)
            throw new InvalidOperationException("Missing texture: " + SpritePath);

        int cellWidth = texture.width / Columns;
        int cellHeight = texture.height / Rows;
        if (texture.width != texture.height || texture.width % Columns != 0 || texture.height % Rows != 0)
            throw new InvalidOperationException($"Invalid Elite_Magma_01 texture size: {texture.width}x{texture.height}.");
        if (cellWidth <= 0 || cellHeight <= 0)
            throw new InvalidOperationException($"Invalid Elite_Magma_01 cell size: {cellWidth}x{cellHeight}.");

        if (texture.width != ExpectedTextureSize || texture.height != ExpectedTextureSize)
        {
            Debug.LogWarning(
                $"[EliteMagmaAnimationRebindUtility] {SpritePath} is {texture.width}x{texture.height}, not expected " +
                $"{ExpectedTextureSize}x{ExpectedTextureSize}. Using {cellWidth}x{cellHeight} grid cells from the current file.");
        }

        SpriteDataProviderFactories factories = new SpriteDataProviderFactories();
        factories.Init();
        ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();

        SpriteRect[] rects = new SpriteRect[Rows * Columns];
        for (int row = 0; row < Rows; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                int index = row * Columns + column;
                rects[index] = new SpriteRect
                {
                    name = "Elite_Magma_01_px_" + index,
                    spriteID = GUID.Generate(),
                    rect = new Rect(
                        column * cellWidth,
                        texture.height - ((row + 1) * cellHeight),
                        cellWidth,
                        cellHeight),
                    alignment = SpriteAlignment.BottomCenter,
                    pivot = new Vector2(0.5f, 0f)
                };
            }
        }

        provider.SetSpriteRects(rects);
        provider.Apply();
    }

    private static Sprite[] LoadOrderedSprites()
    {
        UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(SpritePath);
        List<Sprite> sprites = new List<Sprite>(Rows * Columns);

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] is Sprite sprite && TryGetSpriteIndex(sprite.name, out int index) && index >= 0 && index < Rows * Columns)
                sprites.Add(sprite);
        }

        sprites.Sort(CompareSpriteByIndex);
        if (sprites.Count != Rows * Columns)
            throw new InvalidOperationException($"Expected {Rows * Columns} Elite_Magma_01 sprites, found {sprites.Count}.");

        return sprites.ToArray();
    }

    private static int CompareSpriteByIndex(Sprite a, Sprite b)
    {
        TryGetSpriteIndex(a.name, out int ai);
        TryGetSpriteIndex(b.name, out int bi);
        return ai.CompareTo(bi);
    }

    private static bool TryGetSpriteIndex(string spriteName, out int index)
    {
        index = -1;
        Match match = Regex.Match(spriteName, @"_(\d+)$");
        return match.Success &&
               int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
    }

    private static void RebuildClip(string clipName, Sprite[] sprites, int row, bool loop)
    {
        string path = AnimationDir + "/" + clipName + ".anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
            throw new InvalidOperationException("Missing animation clip: " + path);

        EditorCurveBinding binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = string.Empty,
            propertyName = "m_Sprite"
        };

        ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[Columns];
        for (int i = 0; i < Columns; i++)
        {
            keys[i] = new ObjectReferenceKeyframe
            {
                time = i / FrameRate,
                value = sprites[row * Columns + i]
            };
        }

        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        clip.frameRate = FrameRate;
        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssetIfDirty(clip);
    }

    private static void UpdatePrefabDefaultSprite(Sprite sprite)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException("Missing prefab: " + PrefabPath);

        SpriteRenderer renderer = prefab.GetComponentInChildren<SpriteRenderer>(true);
        if (renderer == null)
            throw new InvalidOperationException("Elite_Magma_01 prefab has no SpriteRenderer.");

        renderer.sprite = sprite;
        EditorUtility.SetDirty(renderer);
        PrefabUtility.SavePrefabAsset(prefab);
    }
}
