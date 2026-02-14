using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using MusicPlayer.Models;

namespace MusicPlayer;

/// <summary>
/// Converts MIDI files to the game's 21-key format.
/// Pipeline: Parse → Select Track → Remap Pitch → Quantize → Simplify Chords → Export
/// </summary>
public class MidiConverter
{
    private readonly NoteMapping _mapping;
    private readonly int _maxSimultaneousKeys;

    public MidiConverter(NoteMapping? mapping = null, int maxSimultaneousKeys = 3)
    {
        _mapping = mapping ?? new NoteMapping();
        _maxSimultaneousKeys = maxSimultaneousKeys;
    }

    /// <summary>
    /// Convert a MIDI file to a Song.
    /// Prompts the user to select a track if multiple tracks with notes exist.
    /// </summary>
    /// <param name="midiFilePath">Path to the .mid file.</param>
    /// <param name="quantizationMs">
    /// Snap notes to this grid in milliseconds. 0 = no quantization.
    /// Default 50ms ≈ 1/32 note at 150 BPM.
    /// </param>
    public Song Convert(string midiFilePath, double quantizationMs = 50)
    {
        var midiFile = MidiFile.Read(midiFilePath);
        string title = Path.GetFileNameWithoutExtension(midiFilePath);

        // Get tempo
        var tempoMap = midiFile.GetTempoMap();
        int bpm = GetBpm(tempoMap);

        // Prompt for track selection (or auto-select if only one track has notes)
        int trackIndex = PromptForTrack(midiFile);

        // Get notes from selected track
        var notes = GetNotesFromTrack(midiFile, trackIndex);

        if (notes.Count == 0)
        {
            Console.WriteLine("Warning: No notes found in MIDI file.");
            return new Song { Title = title, Bpm = bpm };
        }

        // Convert to our internal representation with timing in seconds
        var rawEvents = ConvertToTimedEvents(notes, tempoMap);

        // Remap pitches to our 21-key range
        var remappedEvents = RemapPitches(rawEvents);

        // Quantize timing
        if (quantizationMs > 0)
        {
            remappedEvents = QuantizeTiming(remappedEvents, quantizationMs / 1000.0);
        }

        // Merge simultaneous notes into chords and simplify
        var mergedEvents = MergeSimultaneousNotes(remappedEvents);

        // Simplify chords (max N simultaneous keys)
        var simplifiedEvents = SimplifyChords(mergedEvents);

        return new Song
        {
            Title = title,
            Bpm = bpm,
            Notes = simplifiedEvents
        };
    }

    /// <summary>
    /// List available tracks in a MIDI file with note counts.
    /// </summary>
    public static List<TrackInfo> ListTracks(string midiFilePath)
    {
        var midiFile = MidiFile.Read(midiFilePath);
        var result = new List<TrackInfo>();
        var chunks = midiFile.GetTrackChunks().ToList();

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var notes = chunk.GetNotes();
            var trackName = chunk.Events
                .OfType<SequenceTrackNameEvent>()
                .FirstOrDefault()?.Text ?? $"Track {i}";

            result.Add(new TrackInfo
            {
                Index = i,
                Name = trackName,
                NoteCount = notes.Count
            });
        }

        return result;
    }

    private int GetBpm(TempoMap tempoMap)
    {
        var tempo = tempoMap.GetTempoAtTime(new MidiTimeSpan(0));
        return (int)Math.Round(60_000_000.0 / tempo.MicrosecondsPerQuarterNote);
    }

    private List<Melanchall.DryWetMidi.Interaction.Note> GetNotesFromTrack(
        MidiFile midiFile, int trackIndex)
    {
        var chunks = midiFile.GetTrackChunks().ToList();

        if (chunks.Count == 0)
            return new List<Melanchall.DryWetMidi.Interaction.Note>();

        if (trackIndex >= 0 && trackIndex < chunks.Count)
        {
            return chunks[trackIndex].GetNotes().ToList();
        }

        // Auto-detect: try melody from all notes, or pick the track with most notes
        // in the mid-range (C4–C6)
        if (chunks.Count == 1)
        {
            return chunks[0].GetNotes().ToList();
        }

        // Score each track: prefer tracks with notes in C4–C6 range
        int bestTrack = 0;
        int bestScore = 0;

        for (int i = 0; i < chunks.Count; i++)
        {
            var notes = chunks[i].GetNotes().ToList();
            if (notes.Count == 0) continue;

            int midRangeCount = notes.Count(n =>
                n.NoteNumber >= 60 && n.NoteNumber <= 84); // C4–C6

            // Score = mid-range notes (prefer melody range)
            int score = midRangeCount * 2 + notes.Count;

            if (score > bestScore)
            {
                bestScore = score;
                bestTrack = i;
            }
        }

        var selected = chunks[bestTrack].GetNotes().ToList();
        Console.WriteLine($"Auto-selected track {bestTrack} with {selected.Count} notes.");
        return selected;
    }

    /// <summary>
    /// Prompts the user to pick a track if multiple tracks contain notes.
    /// Returns track index (0-based).
    /// </summary>
    private int PromptForTrack(MidiFile midiFile)
    {
        var chunks = midiFile.GetTrackChunks().ToList();
        var tracksWithNotes = new List<TrackInfo>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            int noteCount = chunk.GetNotes().Count;
            if (noteCount == 0) continue;

            var trackName = chunk.Events
                .OfType<SequenceTrackNameEvent>()
                .FirstOrDefault()?.Text ?? $"Track {i}";

            tracksWithNotes.Add(new TrackInfo
            {
                Index = i,
                Name = trackName,
                NoteCount = noteCount
            });
        }

        // 0 or 1 tracks with notes — no need to prompt
        if (tracksWithNotes.Count <= 1)
            return tracksWithNotes.FirstOrDefault()?.Index ?? 0;

        // Show tracks and prompt for selection
        Console.WriteLine($"\nFound {tracksWithNotes.Count} tracks with notes:");
        for (int i = 0; i < tracksWithNotes.Count; i++)
        {
            Console.WriteLine($"  [{i + 1}] {tracksWithNotes[i].Name} — {tracksWithNotes[i].NoteCount} notes");
        }

        Console.Write($"\nPick a track (1-{tracksWithNotes.Count}) [1]: ");
        string? input = Console.ReadLine()?.Trim();

        int selectedIndex = 0;
        if (!string.IsNullOrEmpty(input) && int.TryParse(input, out int choice)
            && choice >= 1 && choice <= tracksWithNotes.Count)
        {
            selectedIndex = choice - 1;
        }

        var selected = tracksWithNotes[selectedIndex];
        Console.WriteLine($"Using track: {selected.Name} ({selected.NoteCount} notes)");
        return selected.Index;
    }

    private List<RawNoteEvent> ConvertToTimedEvents(
        List<Melanchall.DryWetMidi.Interaction.Note> notes, TempoMap tempoMap)
    {
        var events = new List<RawNoteEvent>();

        foreach (var note in notes)
        {
            var timeSpan = note.TimeAs<MetricTimeSpan>(tempoMap);
            var lengthSpan = note.LengthAs<MetricTimeSpan>(tempoMap);

            double timeSec = timeSpan.TotalMicroseconds / 1_000_000.0;
            double durationSec = lengthSpan.TotalMicroseconds / 1_000_000.0;

            // Minimum duration of 50ms to ensure the key press is registered
            durationSec = Math.Max(durationSec, 0.05);

            events.Add(new RawNoteEvent
            {
                MidiNote = note.NoteNumber,
                Time = timeSec,
                Duration = durationSec
            });
        }

        return events.OrderBy(e => e.Time).ToList();
    }

    private List<RawNoteEvent> RemapPitches(List<RawNoteEvent> events)
    {
        return events.Select(e =>
        {
            int remapped = _mapping.FindNearestNote(e.MidiNote);
            char? key = _mapping.GetKey(remapped);

            if (key == null)
            {
                // Should not happen after FindNearestNote, but safety fallback
                return null;
            }

            return new RawNoteEvent
            {
                MidiNote = remapped,
                Key = key.Value,
                Time = e.Time,
                Duration = e.Duration
            };
        }).Where(e => e != null).Select(e => e!).ToList();
    }

    private List<RawNoteEvent> QuantizeTiming(List<RawNoteEvent> events, double gridSec)
    {
        return events.Select(e => new RawNoteEvent
        {
            MidiNote = e.MidiNote,
            Key = e.Key,
            Time = Math.Round(e.Time / gridSec) * gridSec,
            Duration = Math.Max(Math.Round(e.Duration / gridSec) * gridSec, gridSec)
        }).ToList();
    }

    private List<Models.NoteEvent> MergeSimultaneousNotes(List<RawNoteEvent> events)
    {
        // Group notes that start at the same time (within 10ms tolerance)
        var merged = new List<Models.NoteEvent>();
        var sorted = events.OrderBy(e => e.Time).ToList();

        int i = 0;
        while (i < sorted.Count)
        {
            double currentTime = sorted[i].Time;
            var group = new List<RawNoteEvent> { sorted[i] };
            i++;

            // Collect all notes within 10ms of each other
            while (i < sorted.Count && Math.Abs(sorted[i].Time - currentTime) < 0.01)
            {
                group.Add(sorted[i]);
                i++;
            }

            // Deduplicate keys in the same group
            var uniqueKeys = group
                .Select(g => g.Key.ToString())
                .Distinct()
                .ToList();

            double maxDuration = group.Max(g => g.Duration);

            merged.Add(new Models.NoteEvent
            {
                Time = Math.Round(currentTime, 3),
                Keys = uniqueKeys,
                Duration = Math.Round(maxDuration, 3)
            });
        }

        return merged;
    }

    private List<Models.NoteEvent> SimplifyChords(List<Models.NoteEvent> events)
    {
        return events.Select(e =>
        {
            if (e.Keys.Count <= _maxSimultaneousKeys)
                return e;

            // Keep only the top N notes (highest pitch = most audible in melody)
            var sortedKeys = e.Keys
                .Select(k => new { Key = k, Midi = _mapping.GetMidiNote(k[0]) ?? 0 })
                .OrderByDescending(k => k.Midi)
                .Take(_maxSimultaneousKeys)
                .Select(k => k.Key)
                .ToList();

            return new Models.NoteEvent
            {
                Time = e.Time,
                Keys = sortedKeys,
                Duration = e.Duration
            };
        }).ToList();
    }

    /// <summary>
    /// Internal representation during conversion.
    /// </summary>
    private class RawNoteEvent
    {
        public int MidiNote { get; set; }
        public char Key { get; set; }
        public double Time { get; set; }
        public double Duration { get; set; }
    }

    public class TrackInfo
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public int NoteCount { get; set; }
    }
}
