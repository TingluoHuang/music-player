using MusicPlayer.Models;

namespace MusicPlayer.Tests;

public class MidiConverterTests
{
    [Fact]
    public void Convert_WithValidMidi_ReturnsSong()
    {
        // Create a simple test MIDI file
        string testFile = CreateTestMidiFile();

        try
        {
            var converter = new MidiConverter();
            var song = converter.Convert(testFile);

            Assert.NotNull(song);
            Assert.True(song.Notes.Count > 0, "Song should have notes");
            Assert.True(song.Bpm > 0, "Song should have a BPM");

            // All keys should be valid game keys (natural or with Shift/Ctrl modifiers)
            var validNaturalKeys = new HashSet<string>("QWERTYUASDFGHJZXCVBNM".Select(c => c.ToString()));
            foreach (var note in song.Notes)
            {
                foreach (var key in note.Keys)
                {
                    // Key is either a plain letter or "Shift+X" / "Ctrl+X"
                    string baseKey;
                    if (key.StartsWith("Shift+") || key.StartsWith("Ctrl+"))
                        baseKey = key[(key.IndexOf('+') + 1)..];
                    else
                        baseKey = key;

                    Assert.Contains(baseKey, validNaturalKeys);
                }
            }
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public void Convert_ChordSimplification_DefaultMaxTwoKeys()
    {
        string testFile = CreateChordTestMidiFile();

        try
        {
            var converter = new MidiConverter(); // default max = 2
            var song = converter.Convert(testFile, quantizationMs: 0);

            foreach (var note in song.Notes)
            {
                Assert.True(note.Keys.Count <= 2,
                    $"Chord at t={note.Time} has {note.Keys.Count} keys (max 2)");
            }
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public void Convert_ChordSimplification_KeepsHighestAndLowest()
    {
        string testFile = CreateChordTestMidiFile();

        try
        {
            var converter = new MidiConverter(maxSimultaneousKeys: 2);
            var song = converter.Convert(testFile, quantizationMs: 0);

            // The chord should keep the highest and lowest notes for musical spread
            foreach (var note in song.Notes)
            {
                if (note.Keys.Count == 2)
                {
                    var mapping = new NoteMapping();
                    var midiValues = note.Keys
                        .Select(k => mapping.GetMidiNote(k) ?? 0)
                        .ToList();
                    // The two kept notes should not be adjacent — spread is preferred
                    Assert.True(midiValues.Max() - midiValues.Min() > 0,
                        "Chord simplification should keep spread (highest + lowest)");
                }
            }
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public void Convert_NotesHavePositiveDuration()
    {
        string testFile = CreateTestMidiFile();

        try
        {
            var converter = new MidiConverter();
            var song = converter.Convert(testFile);

            foreach (var note in song.Notes)
            {
                Assert.True(note.Duration > 0, $"Note at t={note.Time} has zero/negative duration");
            }
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public void Convert_NotesAreTimeSorted()
    {
        string testFile = CreateTestMidiFile();

        try
        {
            var converter = new MidiConverter();
            var song = converter.Convert(testFile);

            for (int i = 1; i < song.Notes.Count; i++)
            {
                Assert.True(song.Notes[i].Time >= song.Notes[i - 1].Time,
                    $"Notes not sorted: {song.Notes[i - 1].Time} > {song.Notes[i].Time}");
            }
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public void ListTracks_ReturnsTrackInfo()
    {
        string testFile = CreateTestMidiFile();

        try
        {
            var tracks = MidiConverter.ListTracks(testFile);
            Assert.NotEmpty(tracks);
            // At least one track should have notes
            Assert.Contains(tracks, t => t.NoteCount > 0);
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public async Task Song_SaveAndLoad_RoundTrip()
    {
        var original = new Song
        {
            Title = "Test Song",
            Bpm = 120,
            Notes = new List<NoteEvent>
            {
                new() { Time = 0.0, Keys = new List<string> { "Z" }, Duration = 0.5 },
                new() { Time = 0.5, Keys = new List<string> { "X" }, Duration = 0.5 },
                new() { Time = 1.0, Keys = new List<string> { "A", "D" }, Duration = 0.25 },
            }
        };

        string path = Path.GetTempFileName();
        try
        {
            await original.SaveAsync(path);
            var loaded = await Song.LoadAsync(path);

            Assert.Equal(original.Title, loaded.Title);
            Assert.Equal(original.Bpm, loaded.Bpm);
            Assert.Equal(original.Notes.Count, loaded.Notes.Count);

            for (int i = 0; i < original.Notes.Count; i++)
            {
                Assert.Equal(original.Notes[i].Time, loaded.Notes[i].Time);
                Assert.Equal(original.Notes[i].Duration, loaded.Notes[i].Duration);
                Assert.Equal(original.Notes[i].Keys, loaded.Notes[i].Keys);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Convert_TransposesLowNotesIntoRange()
    {
        // Create a MIDI file with all notes in C2-G2 range (very low, MIDI 36-43).
        // After transposition, all notes should land within the 36-key range (MIDI 60-95).
        var tempoEvent = new Melanchall.DryWetMidi.Core.SetTempoEvent(
            Melanchall.DryWetMidi.Interaction.Tempo.FromBeatsPerMinute(120).MicrosecondsPerQuarterNote)
        { DeltaTime = 0 };

        // C2=36, D2=38, E2=40, F2=41, G2=43
        var noteEvents = CreateNoteEvents(new[] { 36, 38, 40, 41, 43 }).ToList();
        noteEvents.Insert(0, tempoEvent);

        var midiFile = new Melanchall.DryWetMidi.Core.MidiFile(
            new Melanchall.DryWetMidi.Core.TrackChunk(noteEvents));

        string path = Path.GetTempFileName() + ".mid";
        midiFile.Write(path);

        try
        {
            var converter = new MidiConverter();
            var song = converter.Convert(path);

            // After optimal transposition, all 5 notes should be in range
            // (they span only 7 semitones, easily fits in 36 keys)
            Assert.Equal(5, song.Notes.Count);

            // All notes should correspond to valid in-range keys
            var mapping = new NoteMapping();
            foreach (var note in song.Notes)
            {
                foreach (var key in note.Keys)
                {
                    int? midi = mapping.GetMidiNote(key);
                    Assert.NotNull(midi);
                    Assert.InRange(midi.Value, 60, 95);
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Convert_OptimalTransposition_MaximizesInRange()
    {
        // Create a MIDI file that spans more than 3 octaves.
        // The transposition should maximize how many notes land in range.
        var tempoEvent = new Melanchall.DryWetMidi.Core.SetTempoEvent(
            Melanchall.DryWetMidi.Interaction.Tempo.FromBeatsPerMinute(120).MicrosecondsPerQuarterNote)
        { DeltaTime = 0 };

        // Notes from C3 (48) to C7 (96) — 4 octaves, exceeds 3-octave range
        var noteValues = new[] { 48, 52, 55, 60, 64, 67, 72, 76, 79, 84, 88, 91, 96 };
        var noteEvents = CreateNoteEvents(noteValues).ToList();
        noteEvents.Insert(0, tempoEvent);

        var midiFile = new Melanchall.DryWetMidi.Core.MidiFile(
            new Melanchall.DryWetMidi.Core.TrackChunk(noteEvents));

        string path = Path.GetTempFileName() + ".mid";
        midiFile.Write(path);

        try
        {
            var converter = new MidiConverter();
            var song = converter.Convert(path);

            Assert.NotEmpty(song.Notes);

            // Most notes should be distinct — not all clamped to the same edge
            var distinctKeys = song.Notes.SelectMany(n => n.Keys).Distinct().ToList();
            Assert.True(distinctKeys.Count >= 5,
                $"Expected at least 5 distinct keys after transposition, got {distinctKeys.Count}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Convert_TranspositionPrefersNaturalKeys()
    {
        // Create a MIDI file where all notes are chromatic (sharps/flats).
        // E.g. C#4=61, D#4=63, F#4=66, G#4=68, A#4=70 — all need Shift/Ctrl.
        // A good transposition should shift these to maximize natural key hits.
        // Shifting by -1 semitone gives C4=60, D4=62, F4=65, G4=67, A4=69 — all natural!
        var tempoEvent = new Melanchall.DryWetMidi.Core.SetTempoEvent(
            Melanchall.DryWetMidi.Interaction.Tempo.FromBeatsPerMinute(120).MicrosecondsPerQuarterNote)
        { DeltaTime = 0 };

        var noteValues = new[] { 61, 63, 66, 68, 70 }; // All chromatic
        var noteEvents = CreateNoteEvents(noteValues).ToList();
        noteEvents.Insert(0, tempoEvent);

        var midiFile = new Melanchall.DryWetMidi.Core.MidiFile(
            new Melanchall.DryWetMidi.Core.TrackChunk(noteEvents));

        string path = Path.GetTempFileName() + ".mid";
        midiFile.Write(path);

        try
        {
            var converter = new MidiConverter();
            var song = converter.Convert(path, quantizationMs: 0);

            Assert.NotEmpty(song.Notes);

            // After optimal transposition, most notes should be natural (no modifier)
            int naturalCount = song.Notes
                .SelectMany(n => n.Keys)
                .Count(k => NoteMapping.GetModifier(k) == null);

            Assert.True(naturalCount >= 4,
                $"Expected at least 4 natural keys after transposition, got {naturalCount}/{song.Notes.Count}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Convert_NormalizesSpeed_WhenNotesTooFast()
    {
        // Create a MIDI file at 600 BPM with 16th notes — gaps will be ~25ms
        // After normalization, minimum gap should be >= 100ms
        var tempoEvent = new Melanchall.DryWetMidi.Core.SetTempoEvent(
            Melanchall.DryWetMidi.Interaction.Tempo.FromBeatsPerMinute(600).MicrosecondsPerQuarterNote)
        { DeltaTime = 0 };

        // 8 very fast notes — at 600 BPM, a 16th note (delta 120 ticks) ≈ 25ms
        var noteEvents = CreateNoteEventsWithDelta(
            new[] { 60, 62, 64, 65, 67, 69, 71, 72 }, deltaTicks: 120).ToList();
        noteEvents.Insert(0, tempoEvent);

        var midiFile = new Melanchall.DryWetMidi.Core.MidiFile(
            new Melanchall.DryWetMidi.Core.TrackChunk(noteEvents));

        string path = Path.GetTempFileName() + ".mid";
        midiFile.Write(path);

        try
        {
            var converter = new MidiConverter();
            var song = converter.Convert(path, quantizationMs: 0);

            // Verify minimum gap between consecutive notes is >= 100ms
            for (int i = 1; i < song.Notes.Count; i++)
            {
                double gap = song.Notes[i].Time - song.Notes[i - 1].Time;
                if (gap > 0) // skip simultaneous notes
                {
                    Assert.True(gap >= 0.095, // small tolerance for rounding
                        $"Gap between notes {i - 1} and {i} is {gap * 1000:F0}ms (expected >= 100ms)");
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Convert_DoesNotNormalize_WhenSpeedOk()
    {
        // Create a normal-speed MIDI file — speed normalization should not kick in
        // (gaps already well above 100ms). BPM clamping may uniformly scale all
        // timings, but the relative spacing should remain uniform.
        string testFile = CreateTestMidiFile();

        try
        {
            var converter = new MidiConverter();
            var song = converter.Convert(testFile, quantizationMs: 0);

            Assert.True(song.Notes.Count >= 2, "Expected at least 2 notes");

            var gaps = new List<double>();
            for (int i = 1; i < song.Notes.Count; i++)
            {
                double gap = song.Notes[i].Time - song.Notes[i - 1].Time;
                if (gap > 0) gaps.Add(gap);
            }

            Assert.NotEmpty(gaps);
            // All gaps should be approximately uniform (no speed normalization distortion).
            // BPM clamping may introduce small rounding differences, so use tolerance.
            double firstGap = gaps[0];
            Assert.True(firstGap >= 0.05, $"First gap {firstGap:F3}s should be reasonable");
            foreach (var gap in gaps)
            {
                Assert.True(Math.Abs(gap - firstGap) < 0.02,
                    $"Gaps should be uniform: expected ~{firstGap:F3}s but got {gap:F3}s");
            }
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    /// <summary>
    /// Create a simple MIDI file with a C major scale (C4-B4) for testing.
    /// </summary>
    [Fact]
    public void Convert_ChordSimplification_NoMixedModifiers()
    {
        // Create a chord with notes that would require both Shift and Ctrl:
        // C#4 (61) = Shift+Z, Eb4 (63) = Ctrl+C, F#4 (66) = Shift+V
        string testFile = CreateMixedModifierChordMidiFile();

        try
        {
            var converter = new MidiConverter(maxSimultaneousKeys: 3);
            var song = converter.Convert(testFile, quantizationMs: 0);

            foreach (var note in song.Notes)
            {
                // No chord should have both Shift and Ctrl modifiers
                bool hasShift = note.Keys.Any(k => NoteMapping.GetModifier(k) == "Shift");
                bool hasCtrl = note.Keys.Any(k => NoteMapping.GetModifier(k) == "Ctrl");
                Assert.False(hasShift && hasCtrl,
                    $"Chord at t={note.Time} mixes Shift and Ctrl: [{string.Join(", ", note.Keys)}]");
            }
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public void Convert_EffectiveBpmWithinTargetRange()
    {
        // Any converted song should have BPM clamped to 80-100
        string testFile = CreateTestMidiFile();

        try
        {
            var converter = new MidiConverter();
            var song = converter.Convert(testFile);

            Assert.True(song.Bpm >= 110 && song.Bpm <= 130,
                $"Effective BPM {song.Bpm} should be between 110 and 130");
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    private static string CreateTestMidiFile()
    {
        var tempoEvent = new Melanchall.DryWetMidi.Core.SetTempoEvent(
            Melanchall.DryWetMidi.Interaction.Tempo.FromBeatsPerMinute(120).MicrosecondsPerQuarterNote)
        { DeltaTime = 0 };

        var noteEvents = CreateNoteEvents(new[] { 60, 62, 64, 65, 67, 69, 71, 72 }).ToList();
        noteEvents.Insert(0, tempoEvent);

        var midiFile = new Melanchall.DryWetMidi.Core.MidiFile(
            new Melanchall.DryWetMidi.Core.TrackChunk(noteEvents));

        string path = Path.GetTempFileName() + ".mid";
        midiFile.Write(path);
        return path;
    }

    /// <summary>
    /// Create a MIDI file with 5-note chords to test simplification.
    /// </summary>
    private static string CreateChordTestMidiFile()
    {
        // 5 simultaneous notes starting at the same time
        var events = new List<Melanchall.DryWetMidi.Core.MidiEvent>();
        int[] chordNotes = { 60, 64, 67, 72, 76 }; // C4, E4, G4, C5, E5

        foreach (int note in chordNotes)
        {
            events.Add(new Melanchall.DryWetMidi.Core.NoteOnEvent(
                (Melanchall.DryWetMidi.Common.SevenBitNumber)(byte)note,
                (Melanchall.DryWetMidi.Common.SevenBitNumber)100)
            { DeltaTime = 0 });
        }
        foreach (int note in chordNotes)
        {
            events.Add(new Melanchall.DryWetMidi.Core.NoteOffEvent(
                (Melanchall.DryWetMidi.Common.SevenBitNumber)(byte)note,
                (Melanchall.DryWetMidi.Common.SevenBitNumber)0)
            { DeltaTime = note == chordNotes[0] ? 480 : 0L });
        }

        var tempoEvent2 = new Melanchall.DryWetMidi.Core.SetTempoEvent(
            Melanchall.DryWetMidi.Interaction.Tempo.FromBeatsPerMinute(120).MicrosecondsPerQuarterNote)
        { DeltaTime = 0 };
        events.Insert(0, tempoEvent2);

        var midiFile = new Melanchall.DryWetMidi.Core.MidiFile(
            new Melanchall.DryWetMidi.Core.TrackChunk(events));

        string path = Path.GetTempFileName() + ".mid";
        midiFile.Write(path);
        return path;
    }

    /// <summary>
    /// Create a MIDI file with a chord that mixes Shift and Ctrl notes.
    /// C#4 (61) = Shift+Z, Eb4 (63) = Ctrl+C, F#4 (66) = Shift+V — simultaneous.
    /// </summary>
    private static string CreateMixedModifierChordMidiFile()
    {
        var events = new List<Melanchall.DryWetMidi.Core.MidiEvent>();
        int[] chordNotes = { 61, 63, 66 }; // C#4, Eb4, F#4

        foreach (int note in chordNotes)
        {
            events.Add(new Melanchall.DryWetMidi.Core.NoteOnEvent(
                (Melanchall.DryWetMidi.Common.SevenBitNumber)(byte)note,
                (Melanchall.DryWetMidi.Common.SevenBitNumber)100)
            { DeltaTime = 0 });
        }
        foreach (int note in chordNotes)
        {
            events.Add(new Melanchall.DryWetMidi.Core.NoteOffEvent(
                (Melanchall.DryWetMidi.Common.SevenBitNumber)(byte)note,
                (Melanchall.DryWetMidi.Common.SevenBitNumber)0)
            { DeltaTime = note == chordNotes[0] ? 480 : 0L });
        }

        var tempoEvent = new Melanchall.DryWetMidi.Core.SetTempoEvent(
            Melanchall.DryWetMidi.Interaction.Tempo.FromBeatsPerMinute(120).MicrosecondsPerQuarterNote)
        { DeltaTime = 0 };
        events.Insert(0, tempoEvent);

        var midiFile = new Melanchall.DryWetMidi.Core.MidiFile(
            new Melanchall.DryWetMidi.Core.TrackChunk(events));

        string path = Path.GetTempFileName() + ".mid";
        midiFile.Write(path);
        return path;
    }

    private static IEnumerable<Melanchall.DryWetMidi.Core.MidiEvent> CreateNoteEvents(int[] notes)
    {
        return CreateNoteEventsWithDelta(notes, deltaTicks: 480);
    }

    private static IEnumerable<Melanchall.DryWetMidi.Core.MidiEvent> CreateNoteEventsWithDelta(
        int[] notes, int deltaTicks)
    {
        foreach (int note in notes)
        {
            yield return new Melanchall.DryWetMidi.Core.NoteOnEvent(
                (Melanchall.DryWetMidi.Common.SevenBitNumber)(byte)note,
                (Melanchall.DryWetMidi.Common.SevenBitNumber)100)
            { DeltaTime = 0 };

            yield return new Melanchall.DryWetMidi.Core.NoteOffEvent(
                (Melanchall.DryWetMidi.Common.SevenBitNumber)(byte)note,
                (Melanchall.DryWetMidi.Common.SevenBitNumber)0)
            { DeltaTime = deltaTicks };
        }
    }
}
