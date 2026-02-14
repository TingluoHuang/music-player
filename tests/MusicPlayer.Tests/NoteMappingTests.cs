namespace MusicPlayer.Tests;

public class NoteMappingTests
{
    private readonly NoteMapping _mapping = new(baseOctave: 4);

    [Fact]
    public void Has21ValidNotes()
    {
        Assert.Equal(21, _mapping.ValidMidiNotes.Count);
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
    [InlineData('Z', 60)]  // C4
    [InlineData('X', 62)]  // D4
    [InlineData('C', 64)]  // E4
    [InlineData('V', 65)]  // F4
    [InlineData('B', 67)]  // G4
    [InlineData('N', 69)]  // A4
    [InlineData('M', 71)]  // B4
    [InlineData('A', 72)]  // C5
    [InlineData('S', 74)]  // D5
    [InlineData('D', 76)]  // E5
    [InlineData('F', 77)]  // F5
    [InlineData('G', 79)]  // G5
    [InlineData('H', 81)]  // A5
    [InlineData('J', 83)]  // B5
    [InlineData('Q', 84)]  // C6
    [InlineData('W', 86)]  // D6
    [InlineData('E', 88)]  // E6
    [InlineData('R', 89)]  // F6
    [InlineData('T', 91)]  // G6
    [InlineData('Y', 93)]  // A6
    [InlineData('U', 95)]  // B6
    public void KeyToMidiMapping(char key, int expectedMidi)
    {
        int? midi = _mapping.GetMidiNote(key);
        Assert.NotNull(midi);
        Assert.Equal(expectedMidi, midi.Value);
    }

    [Theory]
    [InlineData(60, 'Z')]  // C4
    [InlineData(72, 'A')]  // C5
    [InlineData(84, 'Q')]  // C6
    public void MidiToKeyMapping(int midi, char expectedKey)
    {
        char? key = _mapping.GetKey(midi);
        Assert.NotNull(key);
        Assert.Equal(expectedKey, key.Value);
    }

    [Fact]
    public void GetKey_ReturnsNullForInvalidNote()
    {
        // C#4 = 61, not in diatonic scale
        Assert.Null(_mapping.GetKey(61));
    }

    [Theory]
    [InlineData(61, 60)]   // C#4 → C4 (nearest diatonic)
    [InlineData(63, 62)]   // D#4 → D4 (nearest diatonic, lower wins on tie)
    [InlineData(48, 60)]   // C3 (below range) → C4 (octave shift)
    [InlineData(96, 84)]   // C7 (above range) → C6 (closest valid C)
    [InlineData(60, 60)]   // C4 → C4 (already valid)
    public void FindNearestNote_SnapsCorrectly(int input, int expected)
    {
        int result = _mapping.FindNearestNote(input);
        Assert.Equal(expected, result);
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
    public void GetMappingDisplay_ContainsAllKeys()
    {
        string display = _mapping.GetMappingDisplay();
        foreach (char key in "QWERTYUASDFGHJZXCVBNM")
        {
            Assert.Contains(key.ToString(), display);
        }
    }
}
