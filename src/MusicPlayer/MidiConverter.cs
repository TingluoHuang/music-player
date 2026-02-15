using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using MusicPlayer.Models;

namespace MusicPlayer;

/// <summary>
/// Converts MIDI files to the game's 36-key format.
/// Pipeline: Parse → Select Track → Remap Pitch → Quantize → Simplify Chords → Export
/// </summary>
public class MidiConverter
{
    private readonly NoteMapping _mapping;
    private readonly int _maxSimultaneousKeys;

    public MidiConverter(NoteMapping? mapping = null, int maxSimultaneousKeys = 2)
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

        // Auto-detect melody track using the same scoring as PromptForTrack
        if (chunks.Count == 1)
        {
            return chunks[0].GetNotes().ToList();
        }

        int bestTrack = 0;
        int bestScore = int.MinValue;

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            if (chunk.GetNotes().Count == 0) continue;

            var trackName = chunk.Events
                .OfType<SequenceTrackNameEvent>()
                .FirstOrDefault()?.Text ?? $"Track {i}";

            int score = ScoreTrackAsMelody(chunk, trackName);

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
    /// Score a track to estimate how likely it is the main melody.
    /// Higher score = more likely to be the melody track.
    /// </summary>
    private static int ScoreTrackAsMelody(Melanchall.DryWetMidi.Core.TrackChunk chunk, string trackName)
    {
        var notes = chunk.GetNotes().ToList();
        if (notes.Count == 0) return 0;

        int score = 0;

        // 1. Track name heuristic — melody/lead/vocal names are strong signals
        var lowerName = trackName.ToLowerInvariant();
        string[] melodyKeywords = { "melody", "lead", "vocal", "voice", "主旋律", "歌", "唱" };
        string[] penaltyKeywords = { "drum", "percussion", "bass", "鼓", "贝斯" };
        if (melodyKeywords.Any(k => lowerName.Contains(k)))
            score += 500;
        if (penaltyKeywords.Any(k => lowerName.Contains(k)))
            score -= 500;

        // 2. Skip percussion channel (channel 10 / index 9)
        var channels = notes.Select(n => (int)n.Channel).Distinct().ToList();
        if (channels.Count == 1 && channels[0] == 9)
            return -1000; // Percussion track

        // 3. Mid-range concentration — melody lives in C4–C6 (MIDI 60–95)
        int midRangeCount = notes.Count(n => n.NoteNumber >= 60 && n.NoteNumber <= 95);
        double midRangeRatio = (double)midRangeCount / notes.Count;
        score += (int)(midRangeRatio * 200);

        // 4. Monophony score — melody tracks are mostly single notes.
        //    Count how often multiple notes overlap in time.
        int monophonicNotes = 0;
        var sortedNotes = notes.OrderBy(n => n.Time).ToList();
        for (int i = 0; i < sortedNotes.Count; i++)
        {
            bool overlaps = false;
            if (i > 0 && sortedNotes[i].Time < sortedNotes[i - 1].Time + sortedNotes[i - 1].Length)
                overlaps = true;
            if (!overlaps)
                monophonicNotes++;
        }
        double monoRatio = (double)monophonicNotes / notes.Count;
        score += (int)(monoRatio * 150);

        // 5. Note count — too few notes (< 10) is likely not a melody.
        //    Moderate density is ideal; very dense tracks may be accompaniment.
        if (notes.Count < 10)
            score -= 200;
        else if (notes.Count >= 20 && notes.Count <= 500)
            score += 100;
        else if (notes.Count > 500)
            score += 50; // Still decent, but could be accompaniment

        // 6. Pitch variety — melodies typically use a moderate range of pitches
        int distinctPitches = notes.Select(n => (int)n.NoteNumber).Distinct().Count();
        if (distinctPitches >= 5 && distinctPitches <= 40)
            score += 80;

        return score;
    }

    /// <summary>
    /// Prompts the user to pick a track if multiple tracks contain notes.
    /// Shows a recommended track based on melody scoring.
    /// Returns track index (0-based).
    /// </summary>
    private int PromptForTrack(MidiFile midiFile)
    {
        var chunks = midiFile.GetTrackChunks().ToList();
        var tracksWithNotes = new List<(TrackInfo Info, int Score)>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            int noteCount = chunk.GetNotes().Count;
            if (noteCount == 0) continue;

            var trackName = chunk.Events
                .OfType<SequenceTrackNameEvent>()
                .FirstOrDefault()?.Text ?? $"Track {i}";

            int melodyScore = ScoreTrackAsMelody(chunk, trackName);

            tracksWithNotes.Add((new TrackInfo
            {
                Index = i,
                Name = trackName,
                NoteCount = noteCount
            }, melodyScore));
        }

        // 0 or 1 tracks with notes — no need to prompt
        if (tracksWithNotes.Count <= 1)
            return tracksWithNotes.FirstOrDefault().Info?.Index ?? 0;

        // Sort by score descending — best melody candidate first
        var ranked = tracksWithNotes.OrderByDescending(t => t.Score).ToList();
        int recommendedIdx = 0; // Index into ranked list

        // Show tracks with recommendation
        Console.WriteLine($"\nFound {ranked.Count} tracks with notes:");
        for (int i = 0; i < ranked.Count; i++)
        {
            string marker = i == recommendedIdx ? " ★ recommended" : "";
            Console.WriteLine($"  [{i + 1}] {ranked[i].Info.Name} — {ranked[i].Info.NoteCount} notes{marker}");
        }

        Console.Write($"\nPick a track (1-{ranked.Count}) [1]: ");
        string? input = Console.ReadLine()?.Trim();

        int selectedIndex = 0;
        if (!string.IsNullOrEmpty(input) && int.TryParse(input, out int choice)
            && choice >= 1 && choice <= ranked.Count)
        {
            selectedIndex = choice - 1;
        }

        var selected = ranked[selectedIndex].Info;
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
        if (events.Count == 0)
            return new List<RawNoteEvent>();

        // Calculate optimal octave shift to center notes around the Mid row.
        // Mid row center ≈ MIDI 77.5 (midpoint of C5=72 .. B5=83).
        // We shift all notes by whole octaves (multiples of 12) so the median
        // pitch lands closest to this center, producing a more natural spread.
        int midRangeCenter = 78; // ~F5, center of our 36-key range
        var midiNotes = events.Select(e => e.MidiNote).OrderBy(n => n).ToList();
        int medianPitch = midiNotes[midiNotes.Count / 2];

        // Find the octave shift (in semitones) that puts the median closest to center
        int bestShift = 0;
        int bestDistance = Math.Abs(medianPitch - midRangeCenter);
        for (int shift = -60; shift <= 60; shift += 12)
        {
            int distance = Math.Abs((medianPitch + shift) - midRangeCenter);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestShift = shift;
            }
        }

        if (bestShift != 0)
        {
            Console.WriteLine($"Shifting notes by {bestShift / 12:+#;-#;0} octave(s) to center around Mid row.");
        }

        return events.Select(e =>
        {
            int shifted = e.MidiNote + bestShift;
            int remapped = _mapping.FindNearestNote(shifted);
            string? key = _mapping.GetKey(remapped);

            if (key == null)
            {
                // Should not happen after FindNearestNote, but safety fallback
                return null;
            }

            return new RawNoteEvent
            {
                MidiNote = remapped,
                Key = key,
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
                .Select(g => g.Key)
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

            // Keep the highest note (melody) and lowest note (bass) for
            // the best musical spread, then fill remaining slots from the top.
            var sortedKeys = e.Keys
                .Select(k => new { Key = k, Midi = _mapping.GetMidiNote(k) ?? 0 })
                .OrderByDescending(k => k.Midi)
                .ToList();

            var selected = new List<string>();

            // Always keep the highest (melody)
            selected.Add(sortedKeys.First().Key);

            // If we have room, keep the lowest (bass) for spread
            if (_maxSimultaneousKeys >= 2 && sortedKeys.Count >= 2
                && sortedKeys.Last().Key != sortedKeys.First().Key)
            {
                selected.Add(sortedKeys.Last().Key);
            }

            // Fill remaining slots from the top (after the first)
            foreach (var k in sortedKeys.Skip(1))
            {
                if (selected.Count >= _maxSimultaneousKeys) break;
                if (!selected.Contains(k.Key))
                    selected.Add(k.Key);
            }

            return new Models.NoteEvent
            {
                Time = e.Time,
                Keys = selected,
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
        public string Key { get; set; } = string.Empty;
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
