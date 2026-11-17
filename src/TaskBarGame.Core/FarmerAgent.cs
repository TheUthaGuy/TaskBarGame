namespace TaskBarGame.Core;

public enum FarmerAction { Idle, Walking, Harvesting }

/// <summary>
/// The little farmer who walks along the garden strip and harvests ripe crops.
/// Position is measured in plots: 0 is the middle of the first plot, 1 the second, and so on.
/// </summary>
public sealed class FarmerAgent
{
    /// <summary>Walking speed in plots per second.</summary>
    public const double WalkSpeed = 1.6;

    /// <summary>Seconds the harvest animation takes.</summary>
    public const double HarvestDuration = 0.45;

    private readonly Random _rng;
    private double _idleFor;
    private double? _wanderTo;

    public FarmerAgent(Random? rng = null)
    {
        _rng = rng ?? new Random();
        _idleFor = 1;
    }

    public double Position { get; private set; }

    /// <summary>1 = facing right, -1 = facing left.</summary>
    public int Facing { get; private set; } = 1;

    public FarmerAction Action { get; private set; } = FarmerAction.Idle;

    /// <summary>Seconds spent in the current action, for animations.</summary>
    public double ActionTime { get; private set; }

    /// <summary>The plot the farmer is heading to or harvesting, if any.</summary>
    public int? TargetPlot { get; private set; }

    /// <summary>Raised when the farmer harvests a plot by themselves.</summary>
    public event Action<HarvestResult>? AutoHarvested;

    public void Tick(double dt, GameEngine engine)
    {
        if (dt <= 0 || !double.IsFinite(dt)) return;
        dt = Math.Min(dt, 0.5);
        ActionTime += dt;

        int plotCount = engine.Plots.Count;
        Position = Math.Clamp(Position, 0, Math.Max(0, plotCount - 1));

        if (!engine.State.Settings.FarmerAutoHarvest)
        {
            TargetPlot = null;
            if (Action == FarmerAction.Harvesting) SetAction(FarmerAction.Idle);
            Wander(dt, plotCount);
            return;
        }

        if (Action == FarmerAction.Harvesting)
        {
            if (ActionTime < HarvestDuration) return;
            if (TargetPlot is int plot)
            {
                var result = engine.Harvest(plot, handHarvest: false);
                if (result is not null) AutoHarvested?.Invoke(result);
            }
            TargetPlot = null;
            SetAction(FarmerAction.Idle);
            _idleFor = 1 + _rng.NextDouble() * 2; // admire the harvest before wandering off
            _wanderTo = null;
        }

        // Drop the target if someone else (you!) harvested it first.
        if (TargetPlot is int t && (t >= plotCount || !GameEngine.IsRipe(engine.Plots[t])))
            TargetPlot = null;

        TargetPlot ??= FindNearestRipe(engine);

        if (TargetPlot is int target)
        {
            _wanderTo = null;
            if (MoveTowards(target, dt))
                SetAction(FarmerAction.Harvesting);
            return;
        }

        Wander(dt, plotCount);
    }

    private int? FindNearestRipe(GameEngine engine)
    {
        int? best = null;
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
        if (_wanderTo is double to)
        {
            if (MoveTowards(to, dt))
            {
                _wanderTo = null;
                _idleFor = 2 + _rng.NextDouble() * 5;
                SetAction(FarmerAction.Idle);
            }
            return;
        }

        if (Action != FarmerAction.Idle) SetAction(FarmerAction.Idle);
        _idleFor -= dt;
        if (_idleFor <= 0 && plotCount > 1)
            _wanderTo = _rng.NextDouble() * (plotCount - 1);
    }

    /// <summary>Moves toward <paramref name="x"/>. Returns true once arrived.</summary>
    private bool MoveTowards(double x, double dt)
    {
        double delta = x - Position;
        if (Math.Abs(delta) <= 0.02)
        {
            Position = x;
            return true;
        }

        Facing = delta > 0 ? 1 : -1;
        if (Action != FarmerAction.Walking) SetAction(FarmerAction.Walking);

        double step = WalkSpeed * dt;
        if (step >= Math.Abs(delta))
        {
            Position = x;
            return true;
        }
        Position += Math.Sign(delta) * step;
        return false;
    }

    private void SetAction(FarmerAction action)
    {
        Action = action;
        ActionTime = 0;
    }
}
