using System.Text.Json.Serialization;

namespace MusicPlayer.Models;

/// <summary>
/// A single note event — one or more keys pressed at a specific time.
/// </summary>
public class NoteEvent
{
    /// <summary>
    /// Time in seconds from the start of the song.
    /// </summary>
    [JsonPropertyName("time")]
    public double Time { get; set; }

    /// <summary>
    /// Keyboard keys to press (e.g., ["Z"], ["A", "D"] for a chord).
    /// </summary>
    [JsonPropertyName("keys")]
    public List<string> Keys { get; set; } = new();

    /// <summary>
    /// Duration in seconds to hold the key(s).
    /// </summary>
    [JsonPropertyName("duration")]
    public double Duration { get; set; }
}
