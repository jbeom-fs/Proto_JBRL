using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EnemyData))]
[CanEditMultipleObjects]
public sealed class EnemyDataEditor : Editor
{
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
    private SerializedProperty _attackCooldown;
    private SerializedProperty _attackWindup;
    private SerializedProperty _attackRecovery;
    private SerializedProperty _contactDamageRadius;
    private SerializedProperty _contactDamageSkin;
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
    private SerializedProperty _knockbackResistance;
    private SerializedProperty _rangedMovementType;
    private SerializedProperty _preferredRange;
    private SerializedProperty _kiteRetreatRange;
    private SerializedProperty _randomMoveIntervalMin;
    private SerializedProperty _randomMoveIntervalMax;
    private SerializedProperty _randomMoveRadius;

    private bool _reservedFoldout;

    private void OnEnable()
    {
        _enemyName = serializedObject.FindProperty(nameof(EnemyData.enemyName));
        _maxHp = serializedObject.FindProperty(nameof(EnemyData.maxHp));
        _attack = serializedObject.FindProperty(nameof(EnemyData.attack));
        _defense = serializedObject.FindProperty(nameof(EnemyData.defense));
        _expReward = serializedObject.FindProperty(nameof(EnemyData.expReward));
        _deathDelay = serializedObject.FindProperty(nameof(EnemyData.deathDelay));
        _spawnCost = serializedObject.FindProperty(nameof(EnemyData.spawnCost));
        _allowedRegions = serializedObject.FindProperty(nameof(EnemyData.allowedRegions));
        _behaviorType = serializedObject.FindProperty(nameof(EnemyData.behaviorType));
        _detectRange = serializedObject.FindProperty(nameof(EnemyData.detectRange));
        _attackRange = serializedObject.FindProperty(nameof(EnemyData.attackRange));
        _moveSpeed = serializedObject.FindProperty(nameof(EnemyData.moveSpeed));
        _isStationary = serializedObject.FindProperty(nameof(EnemyData.isStationary));
        _immuneToKnockback = serializedObject.FindProperty(nameof(EnemyData.immuneToKnockback));
        _attackCooldown = serializedObject.FindProperty(nameof(EnemyData.attackCooldown));
        _attackWindup = serializedObject.FindProperty(nameof(EnemyData.attackWindup));
        _attackRecovery = serializedObject.FindProperty(nameof(EnemyData.attackRecovery));
        _contactDamageRadius = serializedObject.FindProperty(nameof(EnemyData.contactDamageRadius));
        _contactDamageSkin = serializedObject.FindProperty(nameof(EnemyData.contactDamageSkin));
        _projectilePrefab = serializedObject.FindProperty(nameof(EnemyData.projectilePrefab));
        _projectileDamage = serializedObject.FindProperty(nameof(EnemyData.projectileDamage));
        _projectileSpeed = serializedObject.FindProperty(nameof(EnemyData.projectileSpeed));
        _projectileLifetime = serializedObject.FindProperty(nameof(EnemyData.projectileLifetime));
        _firePattern = serializedObject.FindProperty(nameof(EnemyData.firePattern));
        _projectileCount = serializedObject.FindProperty(nameof(EnemyData.projectileCount));
        _spreadAngle = serializedObject.FindProperty(nameof(EnemyData.spreadAngle));
        _burstInterval = serializedObject.FindProperty(nameof(EnemyData.burstInterval));
        _projectileWallHitMode = serializedObject.FindProperty(nameof(EnemyData.projectileWallHitMode));
        _projectileMaxBounceCount = serializedObject.FindProperty(nameof(EnemyData.projectileMaxBounceCount));
        _knockbackResistance = serializedObject.FindProperty(nameof(EnemyData.knockbackResistance));
        _rangedMovementType = serializedObject.FindProperty(nameof(EnemyData.rangedMovementType));
        _preferredRange = serializedObject.FindProperty(nameof(EnemyData.preferredRange));
        _kiteRetreatRange = serializedObject.FindProperty(nameof(EnemyData.kiteRetreatRange));
        _randomMoveIntervalMin = serializedObject.FindProperty(nameof(EnemyData.randomMoveIntervalMin));
        _randomMoveIntervalMax = serializedObject.FindProperty(nameof(EnemyData.randomMoveIntervalMax));
        _randomMoveRadius = serializedObject.FindProperty(nameof(EnemyData.randomMoveRadius));
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUI.BeginChangeCheck();

        DrawBasicSection();
        DrawCommonCombatStatusSection();
        DrawStationaryGuidance();

        if (_behaviorType != null && _behaviorType.hasMultipleDifferentValues)
        {
            EditorGUILayout.HelpBox(
                "Multiple EnemyData assets have different behavior types. Behavior-specific sections are hidden until the selection has one behavior type.",
                MessageType.Info);
        }
        else
        {
            switch (GetBehaviorType())
            {
                case EnemyBehaviorType.Contact:
                    DrawContactSection();
                    break;

                case EnemyBehaviorType.Ranged:
                    DrawRangedSection();
                    DrawRangedMovementSection();
                    break;

                default:
                    EditorGUILayout.HelpBox(
                        "This behavior type has no dedicated inspector section yet. Serialized values remain available in Reserved / Future.",
                        MessageType.Info);
                    break;
            }
        }

        DrawReservedSection();

        if (EditorGUI.EndChangeCheck())
            serializedObject.ApplyModifiedProperties();
    }

    private void DrawBasicSection()
    {
        DrawSectionHeader("Basic");
        DrawProperty(_enemyName);
        DrawProperty(_maxHp);
        DrawProperty(_expReward);
        DrawProperty(_deathDelay);
        DrawProperty(_spawnCost);
        DrawProperty(_allowedRegions);
        DrawProperty(_moveSpeed);
        DrawProperty(_detectRange);
        DrawProperty(_behaviorType);
    }

    private void DrawCommonCombatStatusSection()
    {
        DrawSectionHeader("Common Combat / Status");
        DrawProperty(_attack);
        DrawProperty(_defense);
        DrawProperty(_knockbackResistance);
        DrawProperty(_isStationary);
        DrawProperty(_immuneToKnockback);
    }

    private void DrawContactSection()
    {
        DrawSectionHeader("Contact");
        EditorGUILayout.HelpBox(
            "Contact enemies do not enter AttackState; they damage the player by distance or collider contact.",
            MessageType.Info);
        DrawProperty(_contactDamageRadius);
        DrawProperty(_contactDamageSkin);
    }

    private void DrawRangedSection()
    {
        DrawSectionHeader("Ranged");
        DrawProperty(_attackRange);
        DrawProperty(_attackWindup);
        DrawProperty(_attackRecovery);
        DrawProperty(_attackCooldown);
        DrawProperty(_projectilePrefab);
        DrawProperty(_projectileDamage);
        DrawProperty(_projectileSpeed);
        DrawProperty(_projectileLifetime);
        DrawProperty(_firePattern);
        DrawProperty(_projectileWallHitMode);

        if (IsProjectileWallHitMode(ProjectileWallHitMode.Bounce))
            DrawProperty(_projectileMaxBounceCount);

        DrawFirePatternProperties();
    }

    private void DrawFirePatternProperties()
    {
        if (_firePattern != null && _firePattern.hasMultipleDifferentValues)
        {
            EditorGUILayout.HelpBox(
                "Multiple EnemyData assets have different fire patterns. Pattern-specific fields are hidden until the selection has one fire pattern.",
                MessageType.Info);
            return;
        }

        switch (GetFirePattern())
        {
            case ProjectileFirePattern.Single:
                EditorGUILayout.HelpBox("Single fires one projectile. Count, spread, and burst interval are not used.", MessageType.Info);
                break;

            case ProjectileFirePattern.Spread:
                DrawProperty(_projectileCount);
                DrawProperty(_spreadAngle);
                break;

            case ProjectileFirePattern.Burst:
                DrawProperty(_projectileCount);
                DrawProperty(_burstInterval);
                break;

            case ProjectileFirePattern.Circle:
                DrawProperty(_projectileCount);
                break;

            default:
                EditorGUILayout.HelpBox("This fire pattern has no dedicated inspector fields yet.", MessageType.Info);
                break;
        }
    }

    private void DrawRangedMovementSection()
    {
        DrawSectionHeader("Ranged Movement");
        DrawProperty(_rangedMovementType);

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
                EditorGUILayout.HelpBox("Chase directly follows the player.", MessageType.Info);
                break;

            case RangedMovementType.Kiting:
                EditorGUILayout.HelpBox("Kiting keeps distance from the player.", MessageType.Info);
                DrawProperty(_preferredRange);
                DrawProperty(_kiteRetreatRange);
                break;

            case RangedMovementType.Random:
                EditorGUILayout.HelpBox("Random movement picks nearby destinations.", MessageType.Info);
                DrawProperty(_randomMoveIntervalMin);
                DrawProperty(_randomMoveIntervalMax);
                DrawProperty(_randomMoveRadius);
                break;

            default:
                EditorGUILayout.HelpBox("This ranged movement type has no dedicated inspector fields yet.", MessageType.Info);
                break;
        }
    }

    private void DrawStationaryGuidance()
    {
        if (!IsBoolEnabled(_isStationary))
            return;

        EditorGUILayout.HelpBox(
            "Stationary enemies do not move; movement is blocked even if moveSpeed or rangedMovementType is set.",
            MessageType.Info);

        if (!IsBoolEnabled(_immuneToKnockback))
        {
            EditorGUILayout.HelpBox(
                "This stationary enemy can still be moved by knockback because immuneToKnockback is off.",
                MessageType.Warning);
        }
    }

    private void DrawReservedSection()
    {
        EditorGUILayout.Space(6f);
        _reservedFoldout = EditorGUILayout.Foldout(_reservedFoldout, "Reserved / Future", true);
        if (!_reservedFoldout)
            return;

        EditorGUI.indentLevel++;

        if (_behaviorType != null && _behaviorType.hasMultipleDifferentValues)
        {
            EditorGUILayout.HelpBox(
                "Select one behavior type to see which serialized fields are inactive for that behavior.",
                MessageType.Info);
            EditorGUI.indentLevel--;
            return;
        }

        switch (GetBehaviorType())
        {
            case EnemyBehaviorType.Contact:
                DrawContactReservedFields();
                break;

            case EnemyBehaviorType.Ranged:
                DrawRangedReservedFields();
                break;

            default:
                DrawAllReservedFields();
                break;
        }

        EditorGUI.indentLevel--;
    }

    private void DrawContactReservedFields()
    {
        EditorGUILayout.HelpBox(
            "These ranged fields stay serialized on the asset but are not used while behaviorType is Contact.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(true))
        {
            DrawProperty(_attackRange);
            DrawProperty(_attackWindup);
            DrawProperty(_attackRecovery);
            DrawProperty(_attackCooldown);
            DrawProperty(_projectilePrefab);
            DrawProperty(_projectileDamage);
            DrawProperty(_projectileSpeed);
            DrawProperty(_projectileLifetime);
            DrawProperty(_firePattern);
            DrawProperty(_projectileCount);
            DrawProperty(_spreadAngle);
            DrawProperty(_burstInterval);
            DrawProperty(_projectileWallHitMode);
            DrawProperty(_projectileMaxBounceCount);
            DrawProperty(_rangedMovementType);
            DrawProperty(_preferredRange);
            DrawProperty(_kiteRetreatRange);
            DrawProperty(_randomMoveIntervalMin);
            DrawProperty(_randomMoveIntervalMax);
            DrawProperty(_randomMoveRadius);
        }
    }

    private void DrawRangedReservedFields()
    {
        EditorGUILayout.HelpBox(
            "These serialized fields are inactive for the current ranged configuration.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(true))
        {
            DrawProperty(_contactDamageRadius);
            DrawProperty(_contactDamageSkin);

            if (!IsProjectileWallHitMode(ProjectileWallHitMode.Bounce))
                DrawProperty(_projectileMaxBounceCount);

            DrawInactiveFirePatternFields();
            DrawInactiveRangedMovementFields();
        }
    }

    private void DrawAllReservedFields()
    {
        EditorGUILayout.HelpBox(
            "These fields remain serialized while a dedicated editor section is added for this behavior type.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(true))
        {
            DrawProperty(_attackRange);
            DrawProperty(_attackWindup);
            DrawProperty(_attackRecovery);
            DrawProperty(_attackCooldown);
            DrawProperty(_contactDamageRadius);
            DrawProperty(_contactDamageSkin);
            DrawProperty(_projectilePrefab);
            DrawProperty(_projectileDamage);
            DrawProperty(_projectileSpeed);
            DrawProperty(_projectileLifetime);
            DrawProperty(_firePattern);
            DrawProperty(_projectileCount);
            DrawProperty(_spreadAngle);
            DrawProperty(_burstInterval);
            DrawProperty(_projectileWallHitMode);
            DrawProperty(_projectileMaxBounceCount);
            DrawProperty(_rangedMovementType);
            DrawProperty(_preferredRange);
            DrawProperty(_kiteRetreatRange);
            DrawProperty(_randomMoveIntervalMin);
            DrawProperty(_randomMoveIntervalMax);
            DrawProperty(_randomMoveRadius);
        }
    }

    private void DrawInactiveFirePatternFields()
    {
        if (_firePattern == null || _firePattern.hasMultipleDifferentValues)
            return;

        switch (GetFirePattern())
        {
            case ProjectileFirePattern.Single:
                DrawProperty(_projectileCount);
                DrawProperty(_spreadAngle);
                DrawProperty(_burstInterval);
                break;

            case ProjectileFirePattern.Spread:
                DrawProperty(_burstInterval);
                break;

            case ProjectileFirePattern.Burst:
                DrawProperty(_spreadAngle);
                break;

            case ProjectileFirePattern.Circle:
                DrawProperty(_spreadAngle);
                DrawProperty(_burstInterval);
                break;

            default:
                break;
        }
    }

    private void DrawInactiveRangedMovementFields()
    {
        if (_rangedMovementType == null || _rangedMovementType.hasMultipleDifferentValues)
            return;

        switch (GetRangedMovementType())
        {
            case RangedMovementType.Chase:
                DrawProperty(_preferredRange);
                DrawProperty(_kiteRetreatRange);
                DrawProperty(_randomMoveIntervalMin);
                DrawProperty(_randomMoveIntervalMax);
                DrawProperty(_randomMoveRadius);
                break;

            case RangedMovementType.Kiting:
                DrawProperty(_randomMoveIntervalMin);
                DrawProperty(_randomMoveIntervalMax);
                DrawProperty(_randomMoveRadius);
                break;

            case RangedMovementType.Random:
                DrawProperty(_preferredRange);
                DrawProperty(_kiteRetreatRange);
                break;

            default:
                break;
        }
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
}
