namespace MusicPlayer.Tests;

public class NoteMappingTests
{
    private readonly NoteMapping _mapping = new(baseOctave: 4);

    [Fact]
    public void Has36ValidNotes()
    {
        // 12 chromatic notes × 3 octaves = 36
        Assert.Equal(36, _mapping.ValidMidiNotes.Count);
    }

    [Fact]
    public void LowestNoteIsC4()
    {
        // C4 = MIDI 60
        Assert.Equal(60, _mapping.ValidMidiNotes[0]);
    }

    [Fact]
    public void HighestNoteIsB6()
    {
        // B6 = MIDI 95
        Assert.Equal(95, _mapping.ValidMidiNotes[^1]);
    }

    [Theory]
    [InlineData("Z", 60)]  // C4
    [InlineData("X", 62)]  // D4
    [InlineData("C", 64)]  // E4
    [InlineData("V", 65)]  // F4
    [InlineData("B", 67)]  // G4
    [InlineData("N", 69)]  // A4
    [InlineData("M", 71)]  // B4
    [InlineData("A", 72)]  // C5
    [InlineData("S", 74)]  // D5
    [InlineData("D", 76)]  // E5
    [InlineData("F", 77)]  // F5
    [InlineData("G", 79)]  // G5
    [InlineData("H", 81)]  // A5
    [InlineData("J", 83)]  // B5
    [InlineData("Q", 84)]  // C6
    [InlineData("W", 86)]  // D6
    [InlineData("E", 88)]  // E6
    [InlineData("R", 89)]  // F6
    [InlineData("T", 91)]  // G6
    [InlineData("Y", 93)]  // A6
    [InlineData("U", 95)]  // B6
    public void KeyToMidiMapping_NaturalNotes(string key, int expectedMidi)
    {
        int? midi = _mapping.GetMidiNote(key);
        Assert.NotNull(midi);
        Assert.Equal(expectedMidi, midi.Value);
    }

    [Theory]
    [InlineData("Shift+Z", 61)]  // C#4
    [InlineData("Shift+X", 63)]  // D#4
    [InlineData("Shift+V", 66)]  // F#4
    [InlineData("Shift+B", 68)]  // G#4
    [InlineData("Shift+N", 70)]  // A#4
    [InlineData("Shift+A", 73)]  // C#5
    [InlineData("Shift+S", 75)]  // D#5
    [InlineData("Shift+F", 78)]  // F#5
    [InlineData("Shift+G", 80)]  // G#5
    [InlineData("Shift+H", 82)]  // A#5
    [InlineData("Shift+Q", 85)]  // C#6
    [InlineData("Shift+W", 87)]  // D#6
    [InlineData("Shift+R", 90)]  // F#6
    [InlineData("Shift+T", 92)]  // G#6
    [InlineData("Shift+Y", 94)]  // A#6
    public void KeyToMidiMapping_SharpNotes(string key, int expectedMidi)
    {
        int? midi = _mapping.GetMidiNote(key);
        Assert.NotNull(midi);
        Assert.Equal(expectedMidi, midi.Value);
    }

    [Theory]
    [InlineData("Ctrl+X", 61)]   // Db4 = C#4
    [InlineData("Ctrl+C", 63)]   // Eb4 = D#4
    [InlineData("Ctrl+B", 66)]   // Gb4 = F#4
    [InlineData("Ctrl+N", 68)]   // Ab4 = G#4
    [InlineData("Ctrl+M", 70)]   // Bb4 = A#4
    [InlineData("Ctrl+E", 87)]   // Eb6 = D#6
    public void KeyToMidiMapping_FlatAlternatives(string key, int expectedMidi)
    {
        int? midi = _mapping.GetMidiNote(key);
        Assert.NotNull(midi);
        Assert.Equal(expectedMidi, midi.Value);
    }

    [Theory]
    [InlineData(60, "Z")]          // C4
    [InlineData(72, "A")]          // C5
    [InlineData(84, "Q")]          // C6
    [InlineData(61, "Shift+Z")]    // C#4
    [InlineData(73, "Shift+A")]    // C#5
    [InlineData(85, "Shift+Q")]    // C#6
    [InlineData(78, "Shift+F")]    // F#5
    public void MidiToKeyMapping(int midi, string expectedKey)
    {
        string? key = _mapping.GetKey(midi);
        Assert.NotNull(key);
        Assert.Equal(expectedKey, key);
    }

    [Fact]
    public void GetKey_ReturnsNullForNoteOutsideRange()
    {
        // C3 = 48, below our 3-octave range
        Assert.Null(_mapping.GetKey(48));
        // C7 = 96, above our range
        Assert.Null(_mapping.GetKey(96));
    }

    [Theory]
    [InlineData(48, 60)]   // C3 (below range) → C4 (octave shift)
    [InlineData(96, 84)]   // C7 (above range) → C6 (closest valid C)
    [InlineData(60, 60)]   // C4 → C4 (already valid)
    [InlineData(61, 61)]   // C#4 → C#4 (now valid as chromatic note)
    [InlineData(63, 63)]   // D#4 → D#4 (now valid)
    [InlineData(66, 66)]   // F#4 → F#4 (now valid)
    public void FindNearestNote_SnapsCorrectly(int input, int expected)
    {
        int result = _mapping.FindNearestNote(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsExactMatch_TrueForAllChromaticNotes()
    {
        // All 36 notes in range 60-95 should be exact matches
        for (int midi = 60; midi <= 95; midi++)
        {
            Assert.True(_mapping.IsExactMatch(midi), $"MIDI {midi} should be an exact match");
        }
    }

    [Fact]
    public void IsExactMatch_FalseOutsideRange()
    {
        Assert.False(_mapping.IsExactMatch(59));
        Assert.False(_mapping.IsExactMatch(96));
    }

    [Theory]
    [InlineData(60, "C4")]
    [InlineData(61, "C#4")]
    [InlineData(69, "A4")]
    [InlineData(72, "C5")]
    public void MidiNoteToName(int midi, string expected)
    {
        Assert.Equal(expected, NoteMapping.MidiNoteToName(midi));
    }

    [Fact]
    public void GetMappingDisplay_ContainsAllNaturalKeys()
    {
        string display = _mapping.GetMappingDisplay();
        foreach (char key in "QWERTYUASDFGHJZXCVBNM")
        {
            Assert.Contains(key.ToString(), display);
        }
    }

    [Fact]
    public void GetMappingDisplay_ContainsSharpNotes()
    {
        string display = _mapping.GetMappingDisplay();
        // Should show sharp note names
        Assert.Contains("C#", display);
        Assert.Contains("F#", display);
    }
}
