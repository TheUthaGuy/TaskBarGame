namespace TaskBarGame.Core;

public enum AnglerPhase
{
    /// <summary>Swinging the rod: the bobber flies out over the water.</summary>
    Casting,

    /// <summary>The bobber floats until something bites.</summary>
    Waiting,

    /// <summary>A "!" over the bobber: something's biting.</summary>
    Biting,

    /// <summary>Reeling in: the catch flies back to the pontoon.</summary>
    Reeling,

    /// <summary>A short breather before the next cast.</summary>
    Resting,
}

/// <summary>
/// The farmer fishing from the pontoon, all by themselves: cast, wait for a bite, reel in, rest,
/// and again. Positions are in the same units as the field (crops), so the lake lines up with
/// where the field was: the farmer stands on the pontoon at <see cref="StandAt"/>.
/// </summary>
public sealed class AnglerAgent
{
    public const double CastDuration = 0.6;
    public const double BiteDuration = 0.9;
    public const double ReelDuration = 0.7;
    public const double RestDuration = 0.5;

    /// <summary>Where the farmer stands on the pontoon, in crops.</summary>
    public const double StandAt = 0.5;

    /// <summary>The closest the bobber lands to the farmer, in crops.</summary>
    public const double NearestCast = 3;

    private readonly Random _rng;
    private double _waitFor;

    public AnglerAgent(Random? rng = null) => _rng = rng ?? new Random();

    public AnglerPhase Phase { get; private set; } = AnglerPhase.Resting;

    /// <summary>Seconds spent in the current phase, for animations.</summary>
    public double PhaseTime { get; private set; }

    /// <summary>Where the bobber is (or is flying to), in crops.</summary>
    public double BobberAt { get; private set; } = NearestCast + 2;

    /// <summary>What was caught last (shown flying back to the pontoon while reeling).</summary>
    public CatchResult? LastCatch { get; private set; }

    /// <summary>0..1 through the current phase (Waiting counts up to the bite).</summary>
    public double Progress => Phase switch
    {
        AnglerPhase.Casting => Math.Clamp(PhaseTime / CastDuration, 0, 1),
        AnglerPhase.Waiting => _waitFor > 0 ? Math.Clamp(PhaseTime / _waitFor, 0, 1) : 1,
        AnglerPhase.Biting => Math.Clamp(PhaseTime / BiteDuration, 0, 1),
        AnglerPhase.Reeling => Math.Clamp(PhaseTime / ReelDuration, 0, 1),
        _ => Math.Clamp(PhaseTime / RestDuration, 0, 1),
    };

    /// <summary>True while the line is out (from the end of the cast until the catch leaves the water).</summary>
    public bool LineOut => Phase is AnglerPhase.Waiting or AnglerPhase.Biting;

    /// <summary>Raised when the farmer reels something in.</summary>
    public event Action<CatchResult>? Caught;

    /// <summary>Starts over with a fresh cast (after travelling to a lake).</summary>
    public void Reset()
    {
        Phase = AnglerPhase.Resting;
        PhaseTime = 0;
        LastCatch = null;
    }

    public void Tick(double dt, GameEngine engine)
    {
        if (dt <= 0 || !double.IsFinite(dt)) return;
        if (!engine.IsFishing)
        {
            Reset();
            return;
        }
        PhaseTime += Math.Min(dt, 0.5);

        switch (Phase)
        {
            case AnglerPhase.Resting when PhaseTime >= RestDuration:
                double far = Math.Max(NearestCast + 1, engine.FieldLength - 1.5);
                BobberAt = NearestCast + _rng.NextDouble() * (far - NearestCast);
                Enter(AnglerPhase.Casting);
                break;

            case AnglerPhase.Casting when PhaseTime >= CastDuration:
                // Somewhere between 60% and 140% of the average wait, so bites don't come like clockwork.
                _waitFor = engine.AverageWaitSeconds * (0.6 + _rng.NextDouble() * 0.8);
                Enter(AnglerPhase.Waiting);
                break;

            case AnglerPhase.Waiting when PhaseTime >= _waitFor:
                Enter(AnglerPhase.Biting);
                break;

            case AnglerPhase.Biting when PhaseTime >= BiteDuration:
                LastCatch = engine.Catch();
                Enter(AnglerPhase.Reeling);
                if (LastCatch is not null) Caught?.Invoke(LastCatch);
                break;

            case AnglerPhase.Reeling when PhaseTime >= ReelDuration:
                Enter(AnglerPhase.Resting);
                break;
        }
    }

    private void Enter(AnglerPhase phase)
    {
        Phase = phase;
        PhaseTime = 0;
    }
}
