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

            // All keys should be valid game keys
            var validKeys = new HashSet<string>("QWERTYUASDFGHJZXCVBNM".Select(c => c.ToString()));
            foreach (var note in song.Notes)
            {
                foreach (var key in note.Keys)
                {
                    Assert.Contains(key, validKeys);
                }
            }
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public void Convert_ChordSimplification_MaxThreeKeys()
    {
        string testFile = CreateChordTestMidiFile();

        try
        {
            var converter = new MidiConverter(maxSimultaneousKeys: 3);
            var song = converter.Convert(testFile, quantizationMs: 0);

            foreach (var note in song.Notes)
            {
                Assert.True(note.Keys.Count <= 3,
                    $"Chord at t={note.Time} has {note.Keys.Count} keys (max 3)");
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
            Assert.True(tracks.Any(t => t.NoteCount > 0));
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

    /// <summary>
    /// Create a simple MIDI file with a C major scale (C4-B4) for testing.
    /// </summary>
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

    private static IEnumerable<Melanchall.DryWetMidi.Core.MidiEvent> CreateNoteEvents(int[] notes)
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
            { DeltaTime = 480 };  // quarter note at standard resolution
        }
    }
}
