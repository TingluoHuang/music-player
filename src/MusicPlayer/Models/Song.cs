using System.Text.Json;
using System.Text.Json.Serialization;

namespace MusicPlayer.Models;

/// <summary>
/// A converted song ready for playback.
/// </summary>
public class Song
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("bpm")]
    public int Bpm { get; set; }

    [JsonPropertyName("notes")]
    public List<NoteEvent> Notes { get; set; } = new();

    /// <summary>
    /// Total duration of the song in seconds.
    /// </summary>
    [JsonPropertyName("duration")]
    public double Duration => Notes.Count > 0
        ? Notes.Max(n => n.Time + n.Duration)
        : 0;

    /// <summary>
    /// Save the song to a JSON file.
    /// </summary>
    public async Task SaveAsync(string filePath)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        };

        string json = JsonSerializer.Serialize(this, options);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Load a song from a JSON file.
    /// </summary>
    public static async Task<Song> LoadAsync(string filePath)
    {
        string json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<Song>(json)
               ?? throw new InvalidOperationException($"Failed to deserialize song from {filePath}");
    }
}
