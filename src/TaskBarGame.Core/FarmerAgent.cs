namespace TaskBarGame.Core;

public enum FarmerAction { Idle, Walking, Teleporting }

/// <summary>
/// The farmer who works the field row.
///
/// When crops are grown the farmer farms the row the way you would by hand: they walk along it
/// from left to right swinging their hoe (always the right hoe for the planted crop) and break
/// every grown crop they pass. At the end of the row they step on a teleport pad that sends them
/// back to the pad at the start, and off they go again. A lap is always finished; if nothing has
/// grown back by the time they reach the start, they rest and wander a little.
///
/// Position is measured in crops: 0 is the middle of the first crop in the row, 1 the second, and
/// so on. The start pad is at -1 and the end pad at FieldLength.
/// </summary>
public sealed class FarmerAgent
{
    /// <summary>Speed while strolling around with nothing to do, in crops per second.</summary>
    public const double WanderSpeed = 1.2;

    /// <summary>How long the hoe swing shows after breaking a crop, in seconds.</summary>
    public const double SwingDuration = 0.18;

    /// <summary>Seconds a teleport takes: fading out on the end pad, then in on the start pad.</summary>
    public const double TeleportDuration = 0.6;

    /// <summary>Where the start pad is, in crops.</summary>
    public const double StartPad = -1;

    private readonly Random _rng;
    private double _idleFor = 1;
    private double? _wanderTo;
    private bool _arrived;

    public FarmerAgent(Random? rng = null) => _rng = rng ?? new Random();

    public double Position { get; private set; }

    /// <summary>1 = facing right, -1 = facing left.</summary>
    public int Facing { get; private set; } = 1;

    public FarmerAction Action { get; private set; } = FarmerAction.Idle;

    /// <summary>Seconds spent in the current action, for animations.</summary>
    public double ActionTime { get; private set; }

    /// <summary>True while working the row (walking it or teleporting back to the start).</summary>
    public bool IsFarming { get; private set; }

    /// <summary>Seconds since the farmer last broke a crop.</summary>
    public double SinceSwing { get; private set; } = double.MaxValue;

    /// <summary>True for a moment after each crop breaks, to show the hoe swing.</summary>
    public bool IsSwinging => SinceSwing < SwingDuration;

    public bool IsTeleporting => Action == FarmerAction.Teleporting;

    /// <summary>0..1 through the teleport: fading out on the end pad until 0.5, then fading in at the start.</summary>
    public double TeleportProgress => IsTeleporting ? Math.Clamp(ActionTime / TeleportDuration, 0, 1) : 0;

    /// <summary>Raised when the farmer breaks a crop by themselves.</summary>
    public event Action<BreakResult>? AutoBroke;

    /// <summary>Raised when the farmer steps on the end pad and starts teleporting.</summary>
    public event Action? Teleported;

    public void Tick(double dt, GameEngine engine)
    {
        if (dt <= 0 || !double.IsFinite(dt)) return;
        dt = Math.Min(dt, 0.5);
        ActionTime += dt;
        if (SinceSwing < double.MaxValue) SinceSwing += dt;

        int length = engine.FieldLength;
        bool autoFarm = engine.State.Settings.FarmerAutoHarvest;
        bool anyGrown = autoFarm && engine.GrownCount > 0;
        bool backAtStart = false;

        if (IsTeleporting)
        {
            // Halfway through, the farmer disappears from the end pad and appears on the start pad.
            if (!_arrived && ActionTime >= TeleportDuration / 2)
            {
                Position = StartPad;
                _arrived = true;
            }
            if (ActionTime < TeleportDuration) return;
            SetAction(FarmerAction.Walking);
            backAtStart = true;
        }

        Position = Math.Clamp(Position, StartPad, length);

        // Stop when told to, or when a lap is over and nothing has grown back.
        if (IsFarming && (!autoFarm || (backAtStart && !anyGrown)))
        {
            IsFarming = false;
            _wanderTo = null;
            _idleFor = 1 + _rng.NextDouble() * 2;
            SetAction(FarmerAction.Idle);
        }

        if (!IsFarming)
        {
            if (!anyGrown)
            {
                Wander(dt, length);
                return;
            }
            IsFarming = true;
            _wanderTo = null;
            SetAction(FarmerAction.Walking);
        }
        Facing = 1;
        Sweep(dt, engine, length);
    }

    private void Sweep(double dt, GameEngine engine, int length)
    {
        double from = Position;
        double to = Math.Min(length, from + engine.FarmerSpeed * dt);
        Position = to;

        // Break every grown crop whose middle we walked over this tick.
        int first = Math.Max(0, (int)Math.Ceiling(from - 1e-9));
        int last = Math.Min(length - 1, (int)Math.Floor(to + 1e-9));
        for (int i = first; i <= last; i++)
        {
            var result = engine.Break(i, handBreak: false);
            if (result is null) continue;
            SinceSwing = 0;
            AutoBroke?.Invoke(result);
        }

        if (to >= length)
        {
            // On the end pad: teleport back to the start.
            _arrived = false;
            SetAction(FarmerAction.Teleporting);
            Teleported?.Invoke();
        }
    }

    private void Wander(double dt, int length)
    {
        if (_wanderTo is double target)
        {
            double delta = target - Position;
            double step = WanderSpeed * dt;
            if (Math.Abs(delta) <= step)
            {
                Position = target;
                _wanderTo = null;
                _idleFor = 2 + _rng.NextDouble() * 5;
                SetAction(FarmerAction.Idle);
            }
            else
            {
                Facing = delta > 0 ? 1 : -1;
                Position += Math.Sign(delta) * step;
                if (Action != FarmerAction.Walking) SetAction(FarmerAction.Walking);
            }
            return;
        }

        if (Action != FarmerAction.Idle) SetAction(FarmerAction.Idle);
        _idleFor -= dt;
        if (_idleFor <= 0 && length > 1)
            _wanderTo = _rng.NextDouble() * (length - 1);
    }

    private void SetAction(FarmerAction action)
    {
        Action = action;
        ActionTime = 0;
    }
}
