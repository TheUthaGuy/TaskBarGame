using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using TaskBarGame.Core;

namespace TaskBarGame.App;

/// <summary>
/// Owns the running game: the engine, the farmer, the sprites, the save file and the game loop.
/// Both windows share one GameHost.
/// </summary>
public sealed class GameHost : IDisposable
{
    /// <summary>Seconds between autosaves.</summary>
    private const double AutosaveSeconds = 30;

    /// <summary>A gap longer than this (PC asleep, app frozen) counts as offline time.</summary>
    private static readonly TimeSpan SleepGap = TimeSpan.FromSeconds(20);

    /// <summary>Milliseconds between game updates: normal, on battery (with Battery saver on), and while the strip is hidden.</summary>
    private const int NormalFrameMs = 33, BatteryFrameMs = 66, HiddenFrameMs = 250;

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private double _lastFrame;
    private DateTime _lastWallClock;
    private double _sinceSave;

    public GameHost() : this(new SaveManager())
    {
    }

    /// <param name="state">A game to play instead of the save file (used for screenshots).</param>
    public GameHost(SaveManager saves, GameState? state = null)
    {
        Saves = saves;
        Engine = new GameEngine(state ?? Saves.LoadOrCreate());
        Farmer = new FarmerAgent();
        Sprites = new SpriteCache(SpriteSheet.LoadDefault());

        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(NormalFrameMs) };
        _timer.Tick += OnTick;
        Format.ShortNumbers = Settings.ShortNumbers;
    }

    public GameEngine Engine { get; }
    public FarmerAgent Farmer { get; }
    public SaveManager Saves { get; }
    public SpriteCache Sprites { get; }
    public GameSettings Settings => Engine.State.Settings;

    /// <summary>True while the laptop is running on battery and Battery saver is on.</summary>
    public bool SavingBattery { get; private set; }

    /// <summary>Raised about 30 times a second (less when saving battery), after the game has been updated. Argument = seconds passed.</summary>
    public event Action<double>? Frame;

    /// <summary>Raised after catching up on time the PC spent asleep.</summary>
    public event Action<OfflineReport>? CaughtUp;

    /// <summary>Raised when strip mode, size, position or another setting changes.</summary>
    public event Action? SettingsChanged;

    public OfflineReport CatchUpOfflineTime() =>
        Engine.ApplyOfflineProgress(DateTime.UtcNow - Engine.State.LastSavedUtc);

    public void Start()
    {
        _lastWallClock = DateTime.UtcNow;
        _clock.Start();
        _timer.Start();
    }

    /// <summary>Call after changing anything in <see cref="Settings"/>.</summary>
    public void ApplySettings()
    {
        Format.ShortNumbers = Settings.ShortNumbers;
        UpdateFrameRate(_stripHidden);
        Save();
        SettingsChanged?.Invoke();
    }

    private bool _stripHidden;

    /// <summary>
    /// Battery saver: the game updates less often while nothing is on screen (a fullscreen app is in
    /// front) and, if that setting is on, while the laptop is unplugged. Crops and the farmer keep
    /// going at the same speed either way; only the animation gets choppier.
    /// </summary>
    public void UpdateFrameRate(bool stripHidden)
    {
        _stripHidden = stripHidden;
        bool onBattery;
        try { onBattery = SystemParameters.PowerLineStatus == PowerLineStatus.Offline; }
        catch (Exception) { onBattery = false; }
        SavingBattery = Settings.BatterySaver && onBattery;

        int ms = stripHidden ? HiddenFrameMs : SavingBattery ? BatteryFrameMs : NormalFrameMs;
        if ((int)_timer.Interval.TotalMilliseconds != ms) _timer.Interval = TimeSpan.FromMilliseconds(ms);
    }

    public void Save()
    {
        _sinceSave = 0;
        try
        {
            Saves.Save(Engine.State);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _timer.Stop();
        _clock.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = now - _lastFrame;
        _lastFrame = now;

        var wallNow = DateTime.UtcNow;
        var wallGap = wallNow - _lastWallClock;
        _lastWallClock = wallNow;

        if (wallGap > SleepGap)
        {
            var report = Engine.ApplyOfflineProgress(wallGap);
            CaughtUp?.Invoke(report);
            dt = 0;
        }

        dt = Math.Clamp(dt, 0, Math.Max(0.25, 2 * _timer.Interval.TotalSeconds));
        Engine.Tick(dt);
        Farmer.Tick(dt, Engine);

        _sinceSave += dt;
        if (_sinceSave >= AutosaveSeconds) Save();

        Frame?.Invoke(dt);
    }
}
