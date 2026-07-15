using System;

public sealed class MultiHitSkillRunner
{
    private readonly Action<SkillExecutionContext, HitStep> _executeStep;
    private readonly Func<SkillExecutionContext, bool> _canContinue;

    private SkillExecutionContext _context;
    private int _stepIndex;
    private float _timeUntilNextStep;

    public MultiHitSkillRunner(
        Action<SkillExecutionContext, HitStep> executeStep,
        Func<SkillExecutionContext, bool> canContinue)
    {
        _executeStep = executeStep ?? throw new ArgumentNullException(nameof(executeStep));
        _canContinue = canContinue;
    }

    public bool IsActive { get; private set; }
    public SkillData ActiveSkill => IsActive ? _context?.Skill : null;

    public void Start(SkillExecutionContext context)
    {
        Cancel();
        if (context == null || context.Skill == null)
            return;
        if (context.Skill.hitSteps == null || context.Skill.hitSteps.Count == 0)
            return;

        _context = context;
        _stepIndex = 0;
        _timeUntilNextStep = GetDelay(context.Skill.hitSteps[0]);
        IsActive = true;

        Tick(0f);
    }

    public void Tick(float deltaTime)
    {
        if (!IsActive)
            return;
        if (!CanContinue())
        {
            Cancel();
            return;
        }

        _timeUntilNextStep -= Math.Max(0f, deltaTime);
        while (IsActive && _timeUntilNextStep <= 0f)
        {
            if (!CanContinue())
            {
                Cancel();
                return;
            }

            if (!TryGetStep(_stepIndex, out HitStep step))
            {
                Cancel();
                return;
            }

            _executeStep(_context, step);
            if (!IsActive)
                return;

            _stepIndex++;
            if (_context.Skill.hitSteps == null || _stepIndex >= _context.Skill.hitSteps.Count)
            {
                Cancel();
                return;
            }

            _timeUntilNextStep += GetDelay(_context.Skill.hitSteps[_stepIndex]);
        }
    }

    public void Cancel()
    {
        IsActive = false;
        _context = null;
        _stepIndex = 0;
        _timeUntilNextStep = 0f;
    }

    private bool TryGetStep(int index, out HitStep step)
    {
        step = null;
        if (_context == null || _context.Skill == null || _context.Skill.hitSteps == null)
            return false;
        if ((uint)index >= (uint)_context.Skill.hitSteps.Count)
            return false;

        step = _context.Skill.hitSteps[index];
        return step != null;
    }

    private bool CanContinue()
    {
        return _canContinue == null || _canContinue(_context);
    }

    private static float GetDelay(HitStep step)
    {
        return step != null ? Math.Max(0f, step.delay) : 0f;
    }
}
