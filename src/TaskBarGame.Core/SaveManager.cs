using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskBarGame.Core;

/// <summary>A copy of the save kept in the backups folder.</summary>
/// <param name="Summary">"Farming 18 · Garden 8 · 48,250 coins".</param>
public sealed record SaveBackup(string Path, DateTime Time, string Summary);

/// <summary>
/// Loads and saves the game as JSON in %APPDATA%\TaskBarGame Harvest Edition\save.json
/// (separate from the original game's save, so both can be played).
/// The previous save is kept as save.json.bak in case something goes wrong, and the last
/// <see cref="BackupsKept"/> backups (one when the game starts, then every couple of hours) are
/// kept in the backups folder.
/// </summary>
public sealed class SaveManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SaveManager(string? path = null)
    {
        SavePath = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            FolderName,
            "save.json");
    }

    /// <summary>The folder in %APPDATA% that holds the save file and error log.</summary>
    public const string FolderName = "TaskBarGame Harvest Edition";

    public string SavePath { get; }
    public string BackupPath => SavePath + ".bak";

    /// <summary>How many backups the backups folder keeps (the oldest go first).</summary>
    public const int BackupsKept = 10;

    /// <summary>While you play, a backup is made this often.</summary>
    public static readonly TimeSpan BackupInterval = TimeSpan.FromHours(2);

    public string BackupFolder => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(SavePath))!, "backups");

    /// <summary>Loads the save (or the backup if the save is broken), else starts a new game.</summary>
    public GameState LoadOrCreate()
    {
        foreach (var path in new[] { SavePath, BackupPath })
        {
            var state = TryLoad(path);
            if (state is not null) return state;
        }

        if (File.Exists(SavePath))
        {
            // Keep the broken file around so progress can be recovered by hand.
            try { File.Copy(SavePath, SavePath + $".broken-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true); }
            catch (IOException) { }
        }
        return GameState.CreateNew();
    }

    public void Save(GameState state)
    {
        state.LastSavedUtc = DateTime.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);

        string temp = SavePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));

        if (File.Exists(SavePath))
            File.Replace(temp, SavePath, BackupPath, ignoreMetadataErrors: true);
        else
            File.Move(temp, SavePath);
    }

    /// <summary>
    /// Writes a copy of the game to the backups folder and removes the oldest ones beyond
    /// <see cref="BackupsKept"/>. Returns the backup's path.
    /// </summary>
    /// <param name="label">Added to the file name, e.g. "before-restore".</param>
    public string CreateBackup(GameState state, string? label = null, DateTime? now = null)
    {
        Directory.CreateDirectory(BackupFolder);
        var time = now ?? DateTime.Now;
        string name = $"save-{time:yyyy-MM-dd_HH-mm-ss}" + (string.IsNullOrEmpty(label) ? "" : "-" + label) + ".json";
        string path = Path.Combine(BackupFolder, name);
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
        File.SetLastWriteTime(path, time);

        foreach (var old in ListBackups().Skip(BackupsKept))
        {
            try { File.Delete(old.Path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return path;
    }

    /// <summary>The backups, newest first.</summary>
    public IReadOnlyList<SaveBackup> ListBackups()
    {
        if (!Directory.Exists(BackupFolder)) return Array.Empty<SaveBackup>();
        return new DirectoryInfo(BackupFolder).GetFiles("save-*.json")
            .OrderByDescending(f => f.LastWriteTime)
            .ThenByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new SaveBackup(f.FullName, f.LastWriteTime, Summarize(TryLoad(f.FullName))))
            .ToList();
    }

    /// <summary>Saves a copy of the game somewhere you choose (Export).</summary>
    public void Export(GameState state, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));

    /// <summary>
    /// Reads a save file (a backup or an exported save). Null if it isn't a Harvest Edition save
    /// (any other JSON file, or a save from the original edition, which works differently).
    /// </summary>
    public static GameState? Read(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(nameof(GameState.Version), out var version)
                || !version.TryGetInt32(out int number) || number != new GameState().Version
                || !doc.RootElement.TryGetProperty(nameof(GameState.Field), out _))
                return null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
        return TryLoad(path);
    }

    /// <summary>"Farming 18 · Garden 8 · 48,250 coins", or a warning for a file that can't be read.</summary>
    public static string Summarize(GameState? state) =>
        state is null
            ? "Can't be read"
            : $"Farming {FarmingSkill.LevelForXp(state.FarmingXp)} · Garden {GardenProgression.LevelForXp(state.GardenXp)} · {state.Coins:N0} coins";

    public void Delete()
    {
        foreach (var path in new[] { SavePath, BackupPath })
            if (File.Exists(path)) File.Delete(path);
    }

    private static GameState? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var state = JsonSerializer.Deserialize<GameState>(File.ReadAllText(path), JsonOptions);
            state?.Normalize();
            return state;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
