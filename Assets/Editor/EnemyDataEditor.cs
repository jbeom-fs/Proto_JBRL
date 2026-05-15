using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EnemyData))]
[CanEditMultipleObjects]
public sealed class EnemyDataEditor : Editor
{
    private readonly HashSet<string> _handledProperties = new HashSet<string>();

    private SerializedProperty _script;
    private SerializedProperty _enemyName;
    private SerializedProperty _maxHp;
    private SerializedProperty _attack;
    private SerializedProperty _defense;
    private SerializedProperty _expReward;
    private SerializedProperty _deathDelay;
    private SerializedProperty _spawnCost;
    private SerializedProperty _allowedRegions;
    private SerializedProperty _behaviorType;
    private SerializedProperty _detectRange;
    private SerializedProperty _attackRange;
    private SerializedProperty _moveSpeed;
    private SerializedProperty _isStationary;
    private SerializedProperty _immuneToKnockback;
    private SerializedProperty _blocksMovement;
    private SerializedProperty _attackCooldown;
    private SerializedProperty _attackWindup;
    private SerializedProperty _attackRecovery;
    private SerializedProperty _contactDamageRadius;
    private SerializedProperty _contactDamageSkin;
    private SerializedProperty _specialAttackType;
    private SerializedProperty _specialAttackRange;
    private SerializedProperty _specialAttackCooldown;
    private SerializedProperty _specialAttackWindup;
    private SerializedProperty _specialAttackRecovery;
    private SerializedProperty _rushSpeed;
    private SerializedProperty _rushDuration;
    private SerializedProperty _rushDamage;
    private SerializedProperty _rushHitRadius;
    private SerializedProperty _rushImpact;
    private SerializedProperty _jumpDuration;
    private SerializedProperty _jumpDamage;
    private SerializedProperty _jumpImpactRadius;
    private SerializedProperty _jumpMaxDistance;
    private SerializedProperty _jumpStayInRoom;
    private SerializedProperty _jumpImpact;
    private SerializedProperty _projectilePrefab;
    private SerializedProperty _projectileDamage;
    private SerializedProperty _projectileSpeed;
    private SerializedProperty _projectileLifetime;
    private SerializedProperty _firePattern;
    private SerializedProperty _projectileCount;
    private SerializedProperty _spreadAngle;
    private SerializedProperty _burstInterval;
    private SerializedProperty _projectileWallHitMode;
    private SerializedProperty _projectileMaxBounceCount;
    private SerializedProperty _projectileImpact;
    private SerializedProperty _knockbackResistance;
    private SerializedProperty _rangedMovementType;
    private SerializedProperty _preferredRange;
    private SerializedProperty _kiteRetreatRange;
    private SerializedProperty _randomMoveIntervalMin;
    private SerializedProperty _randomMoveIntervalMax;
    private SerializedProperty _randomMoveRadius;

    private void OnEnable()
    {
        _script = serializedObject.FindProperty("m_Script");
        _enemyName = FindHandled(nameof(EnemyData.enemyName));
        _maxHp = FindHandled(nameof(EnemyData.maxHp));
        _attack = FindHandled(nameof(EnemyData.attack));
        _defense = FindHandled(nameof(EnemyData.defense));
        _expReward = FindHandled(nameof(EnemyData.expReward));
        _deathDelay = FindHandled(nameof(EnemyData.deathDelay));
        _spawnCost = FindHandled(nameof(EnemyData.spawnCost));
        _allowedRegions = FindHandled(nameof(EnemyData.allowedRegions));
        _behaviorType = FindHandled(nameof(EnemyData.behaviorType));
        _detectRange = FindHandled(nameof(EnemyData.detectRange));
        _attackRange = FindHandled(nameof(EnemyData.attackRange));
        _moveSpeed = FindHandled(nameof(EnemyData.moveSpeed));
        _isStationary = FindHandled(nameof(EnemyData.isStationary));
        _immuneToKnockback = FindHandled(nameof(EnemyData.immuneToKnockback));
        _blocksMovement = FindHandled(nameof(EnemyData.blocksMovement));
        _attackCooldown = FindHandled(nameof(EnemyData.attackCooldown));
        _attackWindup = FindHandled(nameof(EnemyData.attackWindup));
        _attackRecovery = FindHandled(nameof(EnemyData.attackRecovery));
        _contactDamageRadius = FindHandled(nameof(EnemyData.contactDamageRadius));
        _contactDamageSkin = FindHandled(nameof(EnemyData.contactDamageSkin));
        _specialAttackType = FindHandled(nameof(EnemyData.specialAttackType));
        _specialAttackRange = FindHandled(nameof(EnemyData.specialAttackRange));
        _specialAttackCooldown = FindHandled(nameof(EnemyData.specialAttackCooldown));
        _specialAttackWindup = FindHandled(nameof(EnemyData.specialAttackWindup));
        _specialAttackRecovery = FindHandled(nameof(EnemyData.specialAttackRecovery));
        _rushSpeed = FindHandled(nameof(EnemyData.rushSpeed));
        _rushDuration = FindHandled(nameof(EnemyData.rushDuration));
        _rushDamage = FindHandled(nameof(EnemyData.rushDamage));
        _rushHitRadius = FindHandled(nameof(EnemyData.rushHitRadius));
        _rushImpact = FindHandled(nameof(EnemyData.rushImpact));
        _jumpDuration = FindHandled(nameof(EnemyData.jumpDuration));
        _jumpDamage = FindHandled(nameof(EnemyData.jumpDamage));
        _jumpImpactRadius = FindHandled(nameof(EnemyData.jumpImpactRadius));
        _jumpMaxDistance = FindHandled(nameof(EnemyData.jumpMaxDistance));
        _jumpStayInRoom = FindHandled(nameof(EnemyData.jumpStayInRoom));
        _jumpImpact = FindHandled(nameof(EnemyData.jumpImpact));
        _projectilePrefab = FindHandled(nameof(EnemyData.projectilePrefab));
        _projectileDamage = FindHandled(nameof(EnemyData.projectileDamage));
        _projectileSpeed = FindHandled(nameof(EnemyData.projectileSpeed));
        _projectileLifetime = FindHandled(nameof(EnemyData.projectileLifetime));
        _firePattern = FindHandled(nameof(EnemyData.firePattern));
        _projectileCount = FindHandled(nameof(EnemyData.projectileCount));
        _spreadAngle = FindHandled(nameof(EnemyData.spreadAngle));
        _burstInterval = FindHandled(nameof(EnemyData.burstInterval));
        _projectileWallHitMode = FindHandled(nameof(EnemyData.projectileWallHitMode));
        _projectileMaxBounceCount = FindHandled(nameof(EnemyData.projectileMaxBounceCount));
        _projectileImpact = FindHandled(nameof(EnemyData.projectileImpact));
        _knockbackResistance = FindHandled(nameof(EnemyData.knockbackResistance));
        _rangedMovementType = FindHandled(nameof(EnemyData.rangedMovementType));
        _preferredRange = FindHandled(nameof(EnemyData.preferredRange));
        _kiteRetreatRange = FindHandled(nameof(EnemyData.kiteRetreatRange));
        _randomMoveIntervalMin = FindHandled(nameof(EnemyData.randomMoveIntervalMin));
        _randomMoveIntervalMax = FindHandled(nameof(EnemyData.randomMoveIntervalMax));
        _randomMoveRadius = FindHandled(nameof(EnemyData.randomMoveRadius));
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawScriptField();
        DrawBasicSection();

        if (_behaviorType != null && _behaviorType.hasMultipleDifferentValues)
        {
            EditorGUILayout.HelpBox(
                "Multiple EnemyData assets have different behavior types. Behavior-specific sections are hidden until the selection has one behavior type.",
                MessageType.Info);
        }
        else if (GetBehaviorType() == EnemyBehaviorType.Contact)
        {
            DrawContactSection();
        }
        else
        {
            DrawRangedTimingSection();
            DrawRangedMovementSection();
            DrawRangedProjectileSection();
        }

        DrawSeparationCollisionSection();
        DrawRewardMiscSection();
        DrawUnhandledSection();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawScriptField()
    {
        if (_script == null)
            return;

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(_script);
    }

    private void DrawBasicSection()
    {
        DrawSectionHeader("Basic");
        DrawProperty(_enemyName);
        DrawProperty(_maxHp);
        DrawProperty(_attack);
        DrawProperty(_defense);
        DrawProperty(_moveSpeed);
        DrawProperty(_detectRange);
        DrawProperty(_behaviorType);
        DrawProperty(_isStationary);
        DrawProperty(_immuneToKnockback);
        DrawProperty(_blocksMovement);

        if (IsBoolEnabled(_blocksMovement))
        {
            EditorGUILayout.HelpBox(
                "Blocks Movement only affects Player walking and dash movement. Stationary controls AI movement, and Immune To Knockback controls combat impact movement.",
                MessageType.Info);
        }
    }

    private void DrawContactSection()
    {
        DrawSectionHeader("Contact");
        EditorGUILayout.HelpBox(
            "Contact enemies use proximity or collider contact damage. Ranged windup and projectile settings are not used.",
            MessageType.Info);
        DrawProperty(_contactDamageRadius);
        DrawProperty(_contactDamageSkin);
        DrawContactSpecialSection();
    }

    private void DrawContactSpecialSection()
    {
        DrawSectionHeader("Contact - Special Attack");
        DrawProperty(_specialAttackType);

        if (_specialAttackType != null && _specialAttackType.hasMultipleDifferentValues)
        {
            EditorGUILayout.HelpBox(
                "Multiple EnemyData assets have different special attack types. Special-specific fields are hidden until the selection has one type.",
                MessageType.Info);
            return;
        }

        EnemySpecialAttackType specialType = GetSpecialAttackType();
        if (specialType == EnemySpecialAttackType.None)
        {
            EditorGUILayout.HelpBox("None preserves existing Contact behavior.", MessageType.Info);
            return;
        }

        DrawProperty(_specialAttackRange);
        DrawProperty(_specialAttackWindup);
        DrawProperty(_specialAttackRecovery);
        DrawProperty(_specialAttackCooldown);

        switch (specialType)
        {
            case EnemySpecialAttackType.Rush:
                DrawProperty(_rushSpeed);
                DrawProperty(_rushDuration);
                DrawProperty(_rushDamage);
                DrawProperty(_rushHitRadius);
                DrawProperty(_rushImpact, "Rush Impact");
                break;

            case EnemySpecialAttackType.Jump:
                DrawProperty(_jumpDuration);
                DrawProperty(_jumpDamage);
                DrawProperty(_jumpImpactRadius);
                DrawProperty(_jumpMaxDistance);
                DrawProperty(_jumpStayInRoom);
                DrawProperty(_jumpImpact, "Jump Landing Impact");
                break;
        }
    }

    private void DrawRangedTimingSection()
    {
        DrawSectionHeader("Ranged - Timing");
        DrawProperty(_attackRange);
        DrawProperty(_attackWindup);
        DrawProperty(_attackRecovery);
        DrawProperty(_attackCooldown);
    }

    private void DrawRangedMovementSection()
    {
        DrawSectionHeader("Ranged - Movement");
        DrawProperty(_rangedMovementType);

        if (IsBoolEnabled(_isStationary))
        {
            EditorGUILayout.HelpBox(
                "Stationary is enabled, so ranged movement settings are effectively unused.",
                MessageType.Info);
        }

        if (_rangedMovementType != null && _rangedMovementType.hasMultipleDifferentValues)
        {
            EditorGUILayout.HelpBox(
                "Multiple EnemyData assets have different ranged movement types. Movement-specific fields are hidden until the selection has one movement type.",
                MessageType.Info);
            return;
        }

        switch (GetRangedMovementType())
        {
            case RangedMovementType.Chase:
                EditorGUILayout.HelpBox("Chase uses the default follow behavior and has no extra movement fields.", MessageType.Info);
                break;

            case RangedMovementType.Random:
                DrawProperty(_randomMoveIntervalMin);
                DrawProperty(_randomMoveIntervalMax);
                DrawProperty(_randomMoveRadius);
                break;

            case RangedMovementType.Kiting:
                DrawProperty(_preferredRange);
                DrawProperty(_kiteRetreatRange);
                break;
        }
    }

    private void DrawRangedProjectileSection()
    {
        DrawSectionHeader("Ranged - Projectile");
        DrawProperty(_projectilePrefab);
        DrawProperty(_projectileDamage);
        DrawProperty(_projectileSpeed);
        DrawProperty(_projectileLifetime);
        DrawProperty(_firePattern);

        if (_firePattern != null && _firePattern.hasMultipleDifferentValues)
        {
            EditorGUILayout.HelpBox(
                "Multiple EnemyData assets have different fire patterns. Pattern-specific projectile fields are hidden until the selection has one fire pattern.",
                MessageType.Info);
        }
        else
        {
            DrawFirePatternFields();
        }

        DrawProperty(_projectileWallHitMode);
        if (IsProjectileWallHitMode(ProjectileWallHitMode.Bounce))
            DrawProperty(_projectileMaxBounceCount);
        DrawProperty(_projectileImpact, "Projectile Impact");
    }

    private void DrawFirePatternFields()
    {
        switch (GetFirePattern())
        {
            case ProjectileFirePattern.Single:
                EditorGUILayout.HelpBox("Single fires one projectile. Count, spread, and burst interval are not used.", MessageType.Info);
                break;

            case ProjectileFirePattern.Burst:
                DrawProperty(_projectileCount);
                DrawProperty(_burstInterval);
                break;

            case ProjectileFirePattern.Spread:
                DrawProperty(_projectileCount);
                DrawProperty(_spreadAngle);
                break;

            case ProjectileFirePattern.Circle:
                DrawProperty(_projectileCount);
                break;
        }
    }

    private void DrawSeparationCollisionSection()
    {
        DrawSectionHeader("Separation / Collision");
        DrawProperty(_knockbackResistance);
    }

    private void DrawRewardMiscSection()
    {
        DrawSectionHeader("Reward / Misc");
        DrawProperty(_expReward);
        DrawProperty(_deathDelay);
        DrawProperty(_spawnCost);
        DrawProperty(_allowedRegions);
    }

    private void DrawUnhandledSection()
    {
        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;
        bool drewHeader = false;

        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (iterator.propertyPath == "m_Script" || _handledProperties.Contains(iterator.propertyPath))
                continue;

            if (!drewHeader)
            {
                DrawSectionHeader("Unhandled");
                EditorGUILayout.HelpBox(
                    "These serialized fields are not explicitly assigned to an EnemyDataEditor section yet.",
                    MessageType.Warning);
                drewHeader = true;
            }

            EditorGUILayout.PropertyField(iterator, true);
        }
    }

    private SerializedProperty FindHandled(string propertyName)
    {
        _handledProperties.Add(propertyName);
        return serializedObject.FindProperty(propertyName);
    }

    private EnemyBehaviorType GetBehaviorType()
    {
        if (_behaviorType == null || _behaviorType.hasMultipleDifferentValues)
            return EnemyBehaviorType.Contact;

        return (EnemyBehaviorType)_behaviorType.enumValueIndex;
    }

    private ProjectileFirePattern GetFirePattern()
    {
        if (_firePattern == null || _firePattern.hasMultipleDifferentValues)
            return ProjectileFirePattern.Single;

        return (ProjectileFirePattern)_firePattern.enumValueIndex;
    }

    private RangedMovementType GetRangedMovementType()
    {
        if (_rangedMovementType == null || _rangedMovementType.hasMultipleDifferentValues)
            return RangedMovementType.Chase;

        return (RangedMovementType)_rangedMovementType.enumValueIndex;
    }

    private EnemySpecialAttackType GetSpecialAttackType()
    {
        if (_specialAttackType == null || _specialAttackType.hasMultipleDifferentValues)
            return EnemySpecialAttackType.None;

        return (EnemySpecialAttackType)_specialAttackType.enumValueIndex;
    }

    private bool IsProjectileWallHitMode(ProjectileWallHitMode mode)
    {
        return _projectileWallHitMode != null &&
               !_projectileWallHitMode.hasMultipleDifferentValues &&
               _projectileWallHitMode.enumValueIndex == (int)mode;
    }

    private static bool IsBoolEnabled(SerializedProperty property)
    {
        return property != null && !property.hasMultipleDifferentValues && property.boolValue;
    }

    private static void DrawSectionHeader(string title)
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    private static void DrawProperty(SerializedProperty property)
    {
        if (property == null)
            return;

        EditorGUILayout.PropertyField(property, true);
    }

    private static void DrawProperty(SerializedProperty property, string label)
    {
        if (property == null)
            return;

        EditorGUILayout.PropertyField(property, new GUIContent(label), true);
    }
}
