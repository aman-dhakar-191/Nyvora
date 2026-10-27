namespace Nyvora.Core.State;

/// <summary>
/// Explicit state machine for the pill. Not thread-safe: use it from the UI thread only.
///
/// Valid transitions:
///   Resting    -> Expanding   (RequestExpand)
///   Collapsing -> Expanding   (RequestExpand; interrupts the collapse)
///   Expanding  -> Collapsing  (RequestCollapse; interrupts the expansion)
///   Expanded   -> Collapsing  (RequestCollapse)
///   Expanding  -> Expanded    (CompleteAnimation)
///   Collapsing -> Resting     (CompleteAnimation)
///
/// Every animated transition gets a new <see cref="Generation"/>. An animation reports
/// completion with the generation it was started for, so a finish callback from an
/// interrupted animation cannot move the machine.
/// </summary>
public sealed class PillStateMachine
{
    public PillState State { get; private set; } = PillState.Resting;

    public int Generation { get; private set; }

    public event Action<PillState, PillState>? StateChanged;

    public bool RequestExpand() =>
        State is PillState.Resting or PillState.Collapsing && Enter(PillState.Expanding, newGeneration: true);

    public bool RequestCollapse() =>
        State is PillState.Expanded or PillState.Expanding && Enter(PillState.Collapsing, newGeneration: true);

    public bool CompleteAnimation(int generation)
    {
        if (generation != Generation)
        {
            return false;
        }

        return State switch
        {
            PillState.Expanding => Enter(PillState.Expanded, newGeneration: false),
            PillState.Collapsing => Enter(PillState.Resting, newGeneration: false),
            _ => false,
        };
    }

    private bool Enter(PillState next, bool newGeneration)
    {
        var previous = State;
        State = next;
        if (newGeneration)
        {
            Generation++;
        }

        StateChanged?.Invoke(previous, next);
        return true;
    }
}
