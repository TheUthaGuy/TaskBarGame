using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskBarGame.Core;

/// <summary>
/// Loads and saves the game as JSON in %APPDATA%\TaskBarGame\save.json.
/// The previous save is kept as save.json.bak in case something goes wrong.
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
            "TaskBarGame",
            "save.json");
    }

    public string SavePath { get; }
    public string BackupPath => SavePath + ".bak";

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
