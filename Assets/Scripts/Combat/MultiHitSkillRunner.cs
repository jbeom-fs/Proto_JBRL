using System;
using System.Collections.Generic;

public sealed class MultiHitSkillRunner
{
    public const int MaxConcurrentProcSequences = 16;

    private sealed class ProcSequence
    {
        public SkillExecutionContext Context;
        public int StepIndex;
        public float TimeUntilNextStep;
    }

    private readonly Action<SkillExecutionContext, HitStep> _executeStep;
    private readonly Func<SkillExecutionContext, bool> _canContinue;
    private readonly List<ProcSequence> _procSequences = new(MaxConcurrentProcSequences);
    private readonly Stack<ProcSequence> _procSequencePool = new(MaxConcurrentProcSequences);

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
        TickPlayerSequence(deltaTime);
        TickProcSequences(deltaTime);
    }

    public bool TryStartProcSequence(SkillExecutionContext context)
    {
        if (context == null || context.Skill == null ||
            context.Skill.hitSteps == null || context.Skill.hitSteps.Count == 0)
            return false;
        if (_procSequences.Count >= MaxConcurrentProcSequences)
            return false;

        ProcSequence sequence = _procSequencePool.Count > 0
            ? _procSequencePool.Pop()
            : new ProcSequence();
        sequence.Context = context;
        sequence.StepIndex = 0;
        sequence.TimeUntilNextStep = GetDelay(context.Skill.hitSteps[0]);
        _procSequences.Add(sequence);
        TickProcSequence(_procSequences.Count - 1, 0f);
        return true;
    }

    public void ClearAllProcSequences()
    {
        for (int i = _procSequences.Count - 1; i >= 0; i--)
            ReleaseProcSequence(i);
    }

    private void TickPlayerSequence(float deltaTime)
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

    private void TickProcSequences(float deltaTime)
    {
        for (int i = _procSequences.Count - 1; i >= 0; i--)
            TickProcSequence(i, deltaTime);
    }

    private void TickProcSequence(int index, float deltaTime)
    {
        ProcSequence sequence = _procSequences[index];
        if (!CanContinueProc(sequence.Context))
        {
            ReleaseProcSequence(index);
            return;
        }

        sequence.TimeUntilNextStep -= Math.Max(0f, deltaTime);
        while (sequence.TimeUntilNextStep <= 0f)
        {
            if (!CanContinueProc(sequence.Context) ||
                !TryGetProcStep(sequence, out HitStep step))
            {
                ReleaseProcSequence(index);
                return;
            }

            _executeStep(sequence.Context, step);
            sequence.StepIndex++;
            if (sequence.Context.Skill.hitSteps == null ||
                sequence.StepIndex >= sequence.Context.Skill.hitSteps.Count)
            {
                ReleaseProcSequence(index);
                return;
            }

            sequence.TimeUntilNextStep += GetDelay(sequence.Context.Skill.hitSteps[sequence.StepIndex]);
        }
    }

    private void ReleaseProcSequence(int index)
    {
        ProcSequence sequence = _procSequences[index];
        int lastIndex = _procSequences.Count - 1;
        _procSequences[index] = _procSequences[lastIndex];
        _procSequences.RemoveAt(lastIndex);
        sequence.Context = null;
        sequence.StepIndex = 0;
        sequence.TimeUntilNextStep = 0f;
        _procSequencePool.Push(sequence);
    }

    private static bool TryGetProcStep(ProcSequence sequence, out HitStep step)
    {
        step = null;
        if (sequence.Context == null || sequence.Context.Skill == null ||
            sequence.Context.Skill.hitSteps == null)
            return false;
        if ((uint)sequence.StepIndex >= (uint)sequence.Context.Skill.hitSteps.Count)
            return false;

        step = sequence.Context.Skill.hitSteps[sequence.StepIndex];
        return step != null;
    }

    private static bool CanContinueProc(SkillExecutionContext context)
    {
        return context != null && context.Skill != null &&
               (context.CasterCombat == null || !context.CasterCombat.IsDead);
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
