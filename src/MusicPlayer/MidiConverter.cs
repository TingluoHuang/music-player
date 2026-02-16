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

        // Remap pitches to our 36-key range
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

        // Normalize speed so the fastest note gap is playable
        var normalizedEvents = NormalizeSpeed(simplifiedEvents);

        return new Song
        {
            Title = title,
            Bpm = bpm,
            Notes = normalizedEvents
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

        // Our 36-key range: MIDI 60 (C4) to 95 (B6)
        const int rangeMin = 60;
        const int rangeMax = 95;

        var midiNotes = events.Select(e => e.MidiNote).ToList();

        // Try all 12 semitone transpositions combined with octave shifts.
        // For each candidate, score by: (1) how many notes land in range,
        // (2) total clamping distance for out-of-range notes (less is better).
        int bestShift = 0;
        int bestInRange = -1;
        int bestClampDist = int.MaxValue;

        for (int semitone = 0; semitone < 12; semitone++)
        {
            // For this semitone offset, find the best octave shift
            for (int octaveShift = -60; octaveShift <= 60; octaveShift += 12)
            {
                int totalShift = semitone + octaveShift;
                int inRange = 0;
                int clampDist = 0;

                foreach (int note in midiNotes)
                {
                    int shifted = note + totalShift;
                    if (shifted >= rangeMin && shifted <= rangeMax)
                    {
                        inRange++;
                    }
                    else
                    {
                        // Distance to nearest edge
                        clampDist += shifted < rangeMin
                            ? rangeMin - shifted
                            : shifted - rangeMax;
                    }
                }

                if (inRange > bestInRange ||
                    (inRange == bestInRange && clampDist < bestClampDist))
                {
                    bestInRange = inRange;
                    bestClampDist = clampDist;
                    bestShift = totalShift;
                }
            }
        }

        // Report transposition to the user
        if (bestShift != 0)
        {
            int octaves = bestShift / 12;
            int semitones = bestShift % 12;
            string desc = "";
            if (octaves != 0)
                desc += $"{octaves:+#;-#;0} octave(s)";
            if (semitones != 0)
            {
                if (desc.Length > 0) desc += " and ";
                desc += $"{semitones:+#;-#;0} semitone(s)";
            }
            int outOfRange = midiNotes.Count - bestInRange;
            Console.WriteLine($"Transposing {desc} — {bestInRange}/{midiNotes.Count} notes in range" +
                (outOfRange > 0 ? $" ({outOfRange} clamped)" : "") + ".");
        }
        else
        {
            int outOfRange = midiNotes.Count - bestInRange;
            if (outOfRange > 0)
                Console.WriteLine($"No transposition needed — {bestInRange}/{midiNotes.Count} notes in range ({outOfRange} clamped).");
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

    /// <summary>
    /// Minimum gap in seconds between consecutive notes.
    /// The game needs ~30ms key hold + modifier overhead; 100ms gives comfortable margin.
    /// </summary>
    private const double MinNoteGapSec = 0.100;

    /// <summary>
    /// If the smallest gap between consecutive notes is below MinNoteGapSec,
    /// uniformly stretch all timing so that gap becomes exactly MinNoteGapSec.
    /// This preserves rhythm proportionally while ensuring every note is playable.
    /// </summary>
    private static List<Models.NoteEvent> NormalizeSpeed(List<Models.NoteEvent> events)
    {
        if (events.Count < 2)
            return events;

        // Find the minimum time gap between consecutive notes
        double minGap = double.MaxValue;
        for (int i = 1; i < events.Count; i++)
        {
            double gap = events[i].Time - events[i - 1].Time;
            if (gap > 0 && gap < minGap)
                minGap = gap;
        }

        if (minGap >= MinNoteGapSec || minGap <= 0)
            return events; // Already playable or only simultaneous notes

        double scaleFactor = MinNoteGapSec / minGap;
        double effectiveSpeed = 1.0 / scaleFactor;
        Console.WriteLine($"Normalizing speed: {effectiveSpeed:F2}x (min gap was {minGap * 1000:F0}ms → {MinNoteGapSec * 1000:F0}ms)");

        return events.Select(e => new Models.NoteEvent
        {
            Time = Math.Round(e.Time * scaleFactor, 3),
            Keys = e.Keys,
            Duration = Math.Round(e.Duration * scaleFactor, 3)
        }).ToList();
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
            var keys = e.Keys;

            // First, resolve mixed modifier conflicts.
            // The game can't handle Shift+key and Ctrl+key pressed simultaneously.
            keys = ResolveMixedModifiers(keys);

            if (keys.Count <= _maxSimultaneousKeys)
            {
                return new Models.NoteEvent
                {
                    Time = e.Time,
                    Keys = keys,
                    Duration = e.Duration
                };
            }

            // Keep the highest note (melody) and lowest note (bass) for
            // the best musical spread, then fill remaining slots from the top.
            var sortedKeys = keys
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
    /// Resolve mixed Shift/Ctrl modifiers in a chord.
    /// The game cannot handle Shift+key and Ctrl+key pressed at the same time.
    /// When a chord mixes both, keep the modifier group with more notes and
    /// drop the conflicting chromatic notes (keep any plain/natural notes).
    /// </summary>
    private List<string> ResolveMixedModifiers(List<string> keys)
    {
        if (keys.Count <= 1)
            return keys;

        var plainKeys = new List<string>();
        var shiftKeys = new List<string>();
        var ctrlKeys = new List<string>();

        foreach (var k in keys)
        {
            var mod = NoteMapping.GetModifier(k);
            if (mod == "Shift")
                shiftKeys.Add(k);
            else if (mod == "Ctrl")
                ctrlKeys.Add(k);
            else
                plainKeys.Add(k);
        }

        // No conflict if only one modifier type (or none) is present
        if (shiftKeys.Count == 0 || ctrlKeys.Count == 0)
            return keys;

        // Conflict: keep the modifier group with more notes, drop the other
        var kept = shiftKeys.Count >= ctrlKeys.Count ? shiftKeys : ctrlKeys;
        var result = new List<string>(plainKeys);
        result.AddRange(kept);
        return result;
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
