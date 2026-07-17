using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerRelicBehaviors : MonoBehaviour
{
    [SerializeField] private CombatEventChannel combatChannel;

    private PlayerInventory _inventory;
    private PlayerCombatController _combat;
    private RelicBehaviorRuntime _runtime;

    public IReadOnlyList<AilmentApplication> AttackAilments =>
        _runtime != null ? _runtime.AttackAilments : Array.Empty<AilmentApplication>();

    private void Awake()
    {
        _inventory = GetComponent<PlayerInventory>();
        _combat = GetComponent<PlayerCombatController>();
        _runtime = new RelicBehaviorRuntime(Heal, ExecuteProc);
    }

    private void OnEnable()
    {
        if (combatChannel != null)
        {
            combatChannel.OnEnemyKilled += HandleEnemyKilled;
            combatChannel.OnSkillUsed += HandleSkillUsed;
        }

        if (_inventory != null)
            _inventory.OnInventoryChanged += HandleInventoryChanged;

        _runtime?.Rescan(_inventory != null ? _inventory.Items : null);
    }

    private void OnDisable()
    {
        if (combatChannel != null)
        {
            combatChannel.OnEnemyKilled -= HandleEnemyKilled;
            combatChannel.OnSkillUsed -= HandleSkillUsed;
        }

        if (_inventory != null)
            _inventory.OnInventoryChanged -= HandleInventoryChanged;
    }

    private void HandleInventoryChanged()
    {
        _runtime.Rescan(_inventory.Items);
    }

    private void HandleEnemyKilled(EnemyController enemy)
    {
        Vector3 playerPosition = transform.position;
        Vector3 enemyPosition = enemy != null ? enemy.transform.position : playerPosition;
        _runtime.HandleKill(enemyPosition, playerPosition);
    }

    private void HandleSkillUsed(SkillData skill)
    {
        _runtime.HandleSkillUsed(skill, transform.position, ResolveAimDirection());
    }

    private void LateUpdate()
    {
        _runtime?.FlushKillProcs(transform.position, ResolveAimDirection());
    }

    private void ExecuteProc(SkillData skill, Vector3 origin, Vector2 direction)
    {
        _combat?.ExecuteSkillProc(skill, origin, direction);
    }

    private Vector2 ResolveAimDirection()
    {
        return _combat != null ? _combat.CurrentAimDirection : Vector2.down;
    }

    private void Heal(int amount)
    {
        if (_combat == null || _combat.IsDead)
            return;

        _combat.RestoreHp(amount);
    }
}
