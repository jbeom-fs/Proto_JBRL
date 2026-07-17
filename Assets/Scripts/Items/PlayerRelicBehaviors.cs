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
        _runtime = new RelicBehaviorRuntime(Heal);
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
        _runtime.HandleKill();
    }

    private void HandleSkillUsed(SkillData skill)
    {
        _runtime.HandleSkillUsed(skill);
    }

    private void Heal(int amount)
    {
        if (_combat == null || _combat.IsDead)
            return;

        _combat.RestoreHp(amount);
    }
}
