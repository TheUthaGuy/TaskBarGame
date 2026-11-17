namespace TaskBarGame.Core;

public enum FarmerAction { Idle, Walking }

/// <summary>
/// The farmer who works the field row.
///
/// When crops are grown the farmer farms the row the way you would by hand: they walk along it
/// swinging their hoe (always the right hoe for the planted crop), break every grown crop they
/// pass, and turn around at the end of the row to sweep back the other way. When nothing is
/// grown they rest and wander a little.
///
/// Position is measured in crops: 0 is the middle of the first crop in the row, 1 the second, and so on.
/// </summary>
public sealed class FarmerAgent
{
    /// <summary>Speed while strolling around with nothing to do, in crops per second.</summary>
    public const double WanderSpeed = 1.2;

    /// <summary>How long the hoe swing shows after breaking a crop, in seconds.</summary>
    public const double SwingDuration = 0.18;

    private readonly Random _rng;
    private double _idleFor = 1;
    private double? _wanderTo;

    public FarmerAgent(Random? rng = null) => _rng = rng ?? new Random();

    public double Position { get; private set; }

    /// <summary>1 = facing right, -1 = facing left.</summary>
    public int Facing { get; private set; } = 1;

    public FarmerAction Action { get; private set; } = FarmerAction.Idle;

    /// <summary>Seconds spent in the current action, for animations.</summary>
    public double ActionTime { get; private set; }

    /// <summary>True while sweeping the row and breaking crops.</summary>
    public bool IsFarming { get; private set; }

    /// <summary>Seconds since the farmer last broke a crop.</summary>
    public double SinceSwing { get; private set; } = double.MaxValue;

    /// <summary>True for a moment after each crop breaks, to show the hoe swing.</summary>
    public bool IsSwinging => SinceSwing < SwingDuration;

    /// <summary>Raised when the farmer breaks a crop by themselves.</summary>
    public event Action<BreakResult>? AutoBroke;

    public void Tick(double dt, GameEngine engine)
    {
        if (dt <= 0 || !double.IsFinite(dt)) return;
        dt = Math.Min(dt, 0.5);
        ActionTime += dt;
        if (SinceSwing < double.MaxValue) SinceSwing += dt;

        int length = engine.FieldLength;
        Position = Math.Clamp(Position, 0, Math.Max(0, length - 1));

        bool anyGrown = engine.State.Settings.FarmerAutoHarvest && engine.GrownCount > 0;
        if (!anyGrown)
        {
            if (IsFarming)
            {
                // Row's done: catch your breath before wandering off.
                IsFarming = false;
                _wanderTo = null;
                _idleFor = 1 + _rng.NextDouble() * 2;
                SetAction(FarmerAction.Idle);
            }
            Wander(dt, length);
            return;
        }

        if (!IsFarming)
        {
            // Start a new sweep heading toward the closest grown crop.
            IsFarming = true;
            _wanderTo = null;
            int nearest = FindNearestGrown(engine);
            if (nearest > Position + 0.001) Facing = 1;
            else if (nearest < Position - 0.001) Facing = -1;
            SetAction(FarmerAction.Walking);
        }

        Sweep(dt, engine, length);
    }

    private void Sweep(double dt, GameEngine engine, int length)
    {
        double from = Position;
        double to = from;

        if (length > 1)
        {
            to = from + Facing * engine.FarmerSpeed * dt;
            if (to >= length - 1)
            {
                to = length - 1;
                Facing = -1; // end of the row: turn around
            }
            else if (to <= 0)
            {
                to = 0;
                Facing = 1;
            }
        }
        Position = to;

        // Break every grown crop whose middle we walked over this tick.
        int first = (int)Math.Ceiling(Math.Min(from, to) - 1e-9);
        int last = (int)Math.Floor(Math.Max(from, to) + 1e-9);
        for (int i = Math.Max(0, first); i <= last && i < length; i++)
        {
            var result = engine.Break(i, handBreak: false);
            if (result is null) continue;
            SinceSwing = 0;
            AutoBroke?.Invoke(result);
        }
    }

    private int FindNearestGrown(GameEngine engine)
    {
        int best = 0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < engine.FieldLength; i++)
        {
            if (!engine.IsGrown(i)) continue;
            double distance = Math.Abs(i - Position);
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }
        return best;
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
