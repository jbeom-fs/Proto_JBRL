using UnityEngine;

public class PlayerAnimationController : MonoBehaviour
{
    [SerializeField] private PlayerInputReader inputReader;
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerCombatController combat;
    [SerializeField] private float inputDeadZone = 0.01f;

    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int LastMoveXHash = Animator.StringToHash("LastMoveX");
    private static readonly int LastMoveYHash = Animator.StringToHash("LastMoveY");
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

    private Vector2 _lastMoveDirection = Vector2.down;

    private void Awake()
    {
        if (inputReader == null)
            inputReader = GetComponent<PlayerInputReader>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (combat == null)
            combat = GetComponent<PlayerCombatController>();
    }

    private void OnEnable()
    {
        ApplyIdleParameters();
    }

    private void Update()
    {
        if (inputReader == null || animator == null) return;

        if (combat != null && combat.IsDead)
        {
            ApplyIdleParameters();
            return;
        }

        Vector2 input = inputReader.MoveInput;
        if (input.sqrMagnitude <= inputDeadZone * inputDeadZone)
        {
            animator.SetBool(IsMovingHash, false);
            animator.SetFloat(MoveXHash, 0f);
            animator.SetFloat(MoveYHash, 0f);
            return;
        }

        Vector2 direction = ResolveCardinalDirection(input);
        _lastMoveDirection = direction;

        animator.SetBool(IsMovingHash, true);
        animator.SetFloat(MoveXHash, direction.x);
        animator.SetFloat(MoveYHash, direction.y);
        animator.SetFloat(LastMoveXHash, _lastMoveDirection.x);
        animator.SetFloat(LastMoveYHash, _lastMoveDirection.y);
    }

    private void ApplyIdleParameters()
    {
        if (animator == null) return;

        animator.SetBool(IsMovingHash, false);
        animator.SetFloat(MoveXHash, 0f);
        animator.SetFloat(MoveYHash, 0f);
        animator.SetFloat(LastMoveXHash, _lastMoveDirection.x);
        animator.SetFloat(LastMoveYHash, _lastMoveDirection.y);
    }

    private static Vector2 ResolveCardinalDirection(Vector2 input)
    {
        if (Mathf.Abs(input.x) >= Mathf.Abs(input.y))
            return new Vector2(Mathf.Sign(input.x), 0f);

        return new Vector2(0f, Mathf.Sign(input.y));
    }
}
