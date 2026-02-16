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
    [InlineData("Shift+Z", 61)]  // C#4 (primary: Shift)
    [InlineData("Ctrl+C", 63)]   // Eb4 (primary: Ctrl)
    [InlineData("Shift+V", 66)]  // F#4 (primary: Shift)
    [InlineData("Shift+B", 68)]  // G#4 (primary: Shift)
    [InlineData("Ctrl+M", 70)]   // Bb4 (primary: Ctrl)
    [InlineData("Shift+A", 73)]  // C#5 (primary: Shift)
    [InlineData("Ctrl+D", 75)]   // Eb5 (primary: Ctrl)
    [InlineData("Shift+F", 78)]  // F#5 (primary: Shift)
    [InlineData("Shift+G", 80)]  // G#5 (primary: Shift)
    [InlineData("Ctrl+J", 82)]   // Bb5 (primary: Ctrl)
    [InlineData("Shift+Q", 85)]  // C#6 (primary: Shift)
    [InlineData("Ctrl+E", 87)]   // Eb6 (primary: Ctrl)
    [InlineData("Shift+R", 90)]  // F#6 (primary: Shift)
    [InlineData("Shift+T", 92)]  // G#6 (primary: Shift)
    [InlineData("Ctrl+U", 94)]   // Bb6 (primary: Ctrl)
    public void KeyToMidiMapping_ChromaticPrimary(string key, int expectedMidi)
    {
        int? midi = _mapping.GetMidiNote(key);
        Assert.NotNull(midi);
        Assert.Equal(expectedMidi, midi.Value);
    }

    [Theory]
    [InlineData("Ctrl+X", 61)]   // Db4 = C#4 (alias)
    [InlineData("Shift+X", 63)]  // D#4 = Eb4 (alias)
    [InlineData("Ctrl+B", 66)]   // Gb4 = F#4 (alias)
    [InlineData("Ctrl+N", 68)]   // Ab4 = G#4 (alias)
    [InlineData("Shift+N", 70)]  // A#4 = Bb4 (alias)
    [InlineData("Ctrl+S", 73)]   // Db5 = C#5 (alias)
    [InlineData("Shift+S", 75)]  // D#5 = Eb5 (alias)
    [InlineData("Ctrl+G", 78)]   // Gb5 = F#5 (alias)
    [InlineData("Ctrl+H", 80)]   // Ab5 = G#5 (alias)
    [InlineData("Shift+H", 82)]  // A#5 = Bb5 (alias)
    [InlineData("Ctrl+W", 85)]   // Db6 = C#6 (alias)
    [InlineData("Shift+W", 87)]  // D#6 = Eb6 (alias)
    [InlineData("Ctrl+T", 90)]   // Gb6 = F#6 (alias)
    [InlineData("Ctrl+Y", 92)]   // Ab6 = G#6 (alias)
    [InlineData("Shift+Y", 94)]  // A#6 = Bb6 (alias)
    public void KeyToMidiMapping_EnharmonicAliases(string key, int expectedMidi)
    {
        int? midi = _mapping.GetMidiNote(key);
        Assert.NotNull(midi);
        Assert.Equal(expectedMidi, midi.Value);
    }

    [Theory]
    [InlineData(60, "Z")]          // C4
    [InlineData(72, "A")]          // C5
    [InlineData(84, "Q")]          // C6
    [InlineData(61, "Shift+Z")]    // C#4 (Shift)
    [InlineData(63, "Ctrl+C")]     // Eb4 (Ctrl)
    [InlineData(66, "Shift+V")]    // F#4 (Shift)
    [InlineData(68, "Shift+B")]    // G#4 (Shift)
    [InlineData(70, "Ctrl+M")]     // Bb4 (Ctrl)
    [InlineData(73, "Shift+A")]    // C#5 (Shift)
    [InlineData(75, "Ctrl+D")]     // Eb5 (Ctrl)
    [InlineData(78, "Shift+F")]    // F#5 (Shift)
    [InlineData(80, "Shift+G")]    // G#5 (Shift)
    [InlineData(82, "Ctrl+J")]     // Bb5 (Ctrl)
    [InlineData(85, "Shift+Q")]    // C#6 (Shift)
    [InlineData(87, "Ctrl+E")]     // Eb6 (Ctrl)
    [InlineData(90, "Shift+R")]    // F#6 (Shift)
    [InlineData(92, "Shift+T")]    // G#6 (Shift)
    [InlineData(94, "Ctrl+U")]     // Bb6 (Ctrl)
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
    public void GetMappingDisplay_ContainsChromaticNotes()
    {
        string display = _mapping.GetMappingDisplay();
        // Should show chromatic note names with Ctrl prefix symbol (^)
        Assert.Contains("C#", display);
        Assert.Contains("F#", display);
    }
}
