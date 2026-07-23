using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerBehaviors : MonoBehaviour
{
    [SerializeField] private CombatEventChannel combatChannel;

    private PlayerInventory _inventory;
    private PlayerCombatController _combat;
    private EngravingLoadout _engravingLoadout;
    private BehaviorRuntime _runtime;
    private readonly PassiveEngravingData[] _equippedPassives =
        new PassiveEngravingData[EngravingLoadout.PassiveSlotCount];

    public IReadOnlyList<AilmentApplication> AttackAilments =>
        _runtime != null ? _runtime.AttackAilments : Array.Empty<AilmentApplication>();

    private void Awake()
    {
        _inventory = GetComponent<PlayerInventory>();
        _combat = GetComponent<PlayerCombatController>();
        _engravingLoadout = GetComponent<EngravingLoadout>();
        _runtime = new BehaviorRuntime(Heal, ExecuteProc, GrantShield);
    }

    private void OnEnable()
    {
        if (combatChannel != null)
        {
            combatChannel.OnEnemyKilled += HandleEnemyKilled;
            combatChannel.OnSkillUsed += HandleSkillUsed;
            combatChannel.OnSkillCanceled += HandleSkillCanceled;
            combatChannel.OnLoadoutChanged += HandleLoadoutChanged;
        }

        if (_inventory != null)
            _inventory.OnInventoryChanged += HandleInventoryChanged;
        if (_engravingLoadout != null)
            _engravingLoadout.OnPassiveChanged += HandlePassiveChanged;

        RescanBehaviors();
    }

    private void OnDisable()
    {
        if (combatChannel != null)
        {
            combatChannel.OnEnemyKilled -= HandleEnemyKilled;
            combatChannel.OnSkillUsed -= HandleSkillUsed;
            combatChannel.OnSkillCanceled -= HandleSkillCanceled;
            combatChannel.OnLoadoutChanged -= HandleLoadoutChanged;
        }

        if (_inventory != null)
            _inventory.OnInventoryChanged -= HandleInventoryChanged;
        if (_engravingLoadout != null)
            _engravingLoadout.OnPassiveChanged -= HandlePassiveChanged;
    }

    private void HandleInventoryChanged()
    {
        RescanBehaviors();
    }

    private void HandlePassiveChanged()
    {
        RescanBehaviors();
    }

    private void HandleLoadoutChanged()
    {
        RescanBehaviors();
    }

    private void RescanBehaviors()
    {
        if (_runtime == null)
            return;

        PlayerFormId form = _combat != null ? _combat.CurrentFormId : PlayerFormId.Normal;
        for (int i = 0; i < _equippedPassives.Length; i++)
        {
            _equippedPassives[i] = _engravingLoadout != null
                ? _engravingLoadout.GetPassiveSlot(form, i)
                : null;
        }

        _runtime.Rescan(_inventory != null ? _inventory.Items : null, _equippedPassives);
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

    private void HandleSkillCanceled(SkillData canceled, SkillData canceling)
    {
        if (EnemyPoolManager.Instance == null || !EnemyPoolManager.Instance.HasActiveEnemies)
            return;

        _runtime.HandleCancel(transform.position, ResolveAimDirection());
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

    private void GrantShield(int amount, float duration)
    {
        if (_combat == null || _combat.IsDead)
            return;

        _combat.GrantShield(amount, duration);
    }
}
