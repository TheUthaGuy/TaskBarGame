namespace TaskBarGame.Core;

public enum FarmerAction { Idle, Walking }

/// <summary>
/// The little farmer who works the garden strip.
///
/// When crops are ripe the farmer farms the way you would by hand: they walk along the row
/// swinging their hoe, break every ripe crop they pass, and turn around at the end of the row
/// to sweep back the other way. When nothing is ripe they rest and wander a little.
///
/// Position is measured in plots: 0 is the middle of the first plot, 1 the second, and so on.
/// </summary>
public sealed class FarmerAgent
{
    /// <summary>Speed while sweeping the row, in plots per second.</summary>
    public const double SweepSpeed = 2.2;

    /// <summary>Speed while strolling around with nothing to do.</summary>
    public const double WanderSpeed = 1.2;

    /// <summary>How long the hoe swing lasts after breaking a crop, in seconds.</summary>
    public const double SwingDuration = 0.22;

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

    /// <summary>True while sweeping the row and breaking crops (the farmer carries their hoe).</summary>
    public bool IsFarming { get; private set; }

    /// <summary>Seconds since the farmer last broke a crop.</summary>
    public double SinceSwing { get; private set; } = double.MaxValue;

    /// <summary>True for a moment after each crop breaks, to show the hoe swing.</summary>
    public bool IsSwinging => SinceSwing < SwingDuration;

    /// <summary>Raised when the farmer breaks a crop by themselves.</summary>
    public event Action<HarvestResult>? AutoHarvested;

    public void Tick(double dt, GameEngine engine)
    {
        if (dt <= 0 || !double.IsFinite(dt)) return;
        dt = Math.Min(dt, 0.5);
        ActionTime += dt;
        if (SinceSwing < double.MaxValue) SinceSwing += dt;

        int plotCount = engine.Plots.Count;
        Position = Math.Clamp(Position, 0, Math.Max(0, plotCount - 1));

        bool anyRipe = engine.State.Settings.FarmerAutoHarvest && engine.Plots.Any(GameEngine.IsRipe);
        if (!anyRipe)
        {
            if (IsFarming)
            {
                // Row's done: catch your breath before wandering off.
                IsFarming = false;
                _wanderTo = null;
                _idleFor = 1 + _rng.NextDouble() * 2;
                SetAction(FarmerAction.Idle);
            }
            Wander(dt, plotCount);
            return;
        }

        if (!IsFarming)
        {
            // Start a new sweep heading toward the closest ripe crop.
            IsFarming = true;
            _wanderTo = null;
            int nearest = FindNearestRipe(engine);
            if (nearest > Position + 0.001) Facing = 1;
            else if (nearest < Position - 0.001) Facing = -1;
            SetAction(FarmerAction.Walking);
        }

        Sweep(dt, engine, plotCount);
    }

    private void Sweep(double dt, GameEngine engine, int plotCount)
    {
        double from = Position;
        double to = from;

        if (plotCount > 1)
        {
            to = from + Facing * SweepSpeed * dt;
            if (to >= plotCount - 1)
            {
                to = plotCount - 1;
                Facing = -1; // end of the row: turn around
            }
            else if (to <= 0)
            {
                to = 0;
                Facing = 1;
            }
        }
        Position = to;

        // Break every ripe crop whose middle we walked over this tick.
        int first = (int)Math.Ceiling(Math.Min(from, to) - 1e-9);
        int last = (int)Math.Floor(Math.Max(from, to) + 1e-9);
        for (int i = first; i <= last && i < plotCount; i++)
        {
            if (i < 0 || !GameEngine.IsRipe(engine.Plots[i])) continue;
            var result = engine.Harvest(i, handHarvest: false);
            if (result is null) continue;
            SinceSwing = 0;
            AutoHarvested?.Invoke(result);
        }
    }

    private int FindNearestRipe(GameEngine engine)
    {
        int best = 0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < engine.Plots.Count; i++)
        {
            if (!GameEngine.IsRipe(engine.Plots[i])) continue;
            double distance = Math.Abs(i - Position);
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }
        return best;
    }

    private void Wander(double dt, int plotCount)
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
        if (_idleFor <= 0 && plotCount > 1)
            _wanderTo = _rng.NextDouble() * (plotCount - 1);
    }

    private void SetAction(FarmerAction action)
    {
        Action = action;
        ActionTime = 0;
    }
}
