namespace TaskBarGame.Core;

/// <summary>Coins (the Market value of what you harvested), crops and Farming XP per hour.</summary>
public readonly record struct Rates(double CoinsPerHour, double CropsPerHour, double XpPerHour, double Seconds)
{
    /// <summary>Under a minute of farming is too little to say much.</summary>
    public bool IsMeasured => Seconds >= 60;
}

/// <summary>
/// Measures how fast your farm is going, over the last few minutes and since the game started,
/// so you can see whether an upgrade made a difference. Times are in seconds from any clock.
/// </summary>
public sealed class RateTracker
{
    /// <summary>Harvests are added up in buckets this many seconds long.</summary>
    private const double BucketSeconds = 10;

    private readonly double _window;
    private readonly Queue<Bucket> _buckets = new();
    private double? _start;
    private double _coins, _xp;
    private long _crops;

    /// <param name="windowSeconds">How far back "recent" looks (10 minutes by default).</param>
    public RateTracker(double windowSeconds = 600) => _window = windowSeconds;

    /// <summary>Starts timing (otherwise timing starts at the first harvest).</summary>
    public void Start(double now) => _start ??= now;

    /// <summary>Forgets everything and starts timing again (after the farmer goes somewhere else).</summary>
    public void Restart(double now)
    {
        _buckets.Clear();
        _coins = _xp = 0;
        _crops = 0;
        _start = now;
    }

    /// <summary>Records one harvest: what it's worth at the Market, how many crops, and the Farming XP.</summary>
    public void Add(double now, double coins, long crops, double xp)
    {
        _start ??= now;
        _coins += coins;
        _crops += crops;
        _xp += xp;

        double bucketStart = Math.Floor(now / BucketSeconds) * BucketSeconds;
        if (_buckets.Count == 0 || _buckets.Last().Start != bucketStart)
            _buckets.Enqueue(new Bucket(bucketStart));
        var bucket = _buckets.Last();
        bucket.Coins += coins;
        bucket.Crops += crops;
        bucket.Xp += xp;
        Trim(now);
    }

    /// <summary>The last few minutes (or since the start, if that's shorter).</summary>
    public Rates Recent(double now)
    {
        Trim(now);
        if (_start is not { } start) return default;
        double seconds = Math.Min(_window, now - start);
        double coins = 0, xp = 0;
        long crops = 0;
        foreach (var bucket in _buckets)
        {
            coins += bucket.Coins;
            crops += bucket.Crops;
            xp += bucket.Xp;
        }
        return PerHour(coins, crops, xp, seconds);
    }

    /// <summary>Since the game started.</summary>
    public Rates Session(double now) =>
        _start is { } start ? PerHour(_coins, _crops, _xp, now - start) : default;

    private static Rates PerHour(double coins, long crops, double xp, double seconds)
    {
        if (seconds <= 0) return new Rates(0, 0, 0, Math.Max(0, seconds));
        double scale = 3600 / seconds;
        return new Rates(coins * scale, crops * scale, xp * scale, seconds);
    }

    private void Trim(double now)
    {
        while (_buckets.Count > 0 && _buckets.Peek().Start + BucketSeconds <= now - _window)
            _buckets.Dequeue();
    }

    private sealed class Bucket(double start)
    {
        public double Start { get; } = start;
        public double Coins { get; set; }
        public long Crops { get; set; }
        public double Xp { get; set; }
    }
}
