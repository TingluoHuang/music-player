namespace MusicPlayer;

/// <summary>
/// Maps MIDI note numbers to keyboard keys for Where Winds Meet.
/// 
/// Layout (3 rows × 7 keys = 21 keys = 3 diatonic octaves):
///   Row 1 (High):  Q  W  E  R  T  Y  U   → C6 D6 E6 F6 G6 A6 B6
///   Row 2 (Mid):   A  S  D  F  G  H  J   → C5 D5 E5 F5 G5 A5 B5
///   Row 3 (Low):   Z  X  C  V  B  N  M   → C4 D4 E4 F4 G4 A4 B4
/// </summary>
public class NoteMapping
{
    // C-major diatonic scale intervals (semitones from root)
    private static readonly int[] DiatonicIntervals = { 0, 2, 4, 5, 7, 9, 11 };

    // Keyboard layout: row 3 (low), row 2 (mid), row 1 (high)
    private static readonly char[][] KeyboardRows =
    {
        new[] { 'Z', 'X', 'C', 'V', 'B', 'N', 'M' }, // Low octave
        new[] { 'A', 'S', 'D', 'F', 'G', 'H', 'J' }, // Mid octave
        new[] { 'Q', 'W', 'E', 'R', 'T', 'Y', 'U' }, // High octave
    };

    /// <summary>
    /// The base octave (MIDI octave for the lowest row). Default = 4 → C4.
    /// </summary>
    public int BaseOctave { get; }

    /// <summary>
    /// All 21 valid MIDI note numbers in ascending order.
    /// </summary>
    public IReadOnlyList<int> ValidMidiNotes { get; }

    /// <summary>
    /// Map from MIDI note number → keyboard key character.
    /// </summary>
    private readonly Dictionary<int, char> _midiToKey = new();

    /// <summary>
    /// Map from keyboard key character → MIDI note number.
    /// </summary>
    private readonly Dictionary<char, int> _keyToMidi = new();

    public NoteMapping(int baseOctave = 4)
    {
        BaseOctave = baseOctave;
        var validNotes = new List<int>();

        for (int row = 0; row < 3; row++)
        {
            int octave = baseOctave + row;
            int octaveBase = (octave + 1) * 12; // MIDI: C4 = 60 = (4+1)*12

            for (int note = 0; note < 7; note++)
            {
                int midiNote = octaveBase + DiatonicIntervals[note];
                char key = KeyboardRows[row][note];

                _midiToKey[midiNote] = key;
                _keyToMidi[key] = midiNote;
                validNotes.Add(midiNote);
            }
        }

        ValidMidiNotes = validNotes.AsReadOnly();
    }

    /// <summary>
    /// Get the keyboard key for a MIDI note number.
    /// Returns null if the note is not in our 21-key range.
    /// </summary>
    public char? GetKey(int midiNote)
    {
        return _midiToKey.TryGetValue(midiNote, out var key) ? key : null;
    }

    /// <summary>
    /// Get the MIDI note number for a keyboard key.
    /// </summary>
    public int? GetMidiNote(char key)
    {
        char upper = char.ToUpper(key);
        return _keyToMidi.TryGetValue(upper, out var note) ? note : null;
    }

    /// <summary>
    /// Find the nearest valid MIDI note for any given MIDI pitch.
    /// Uses octave shifting first (keep same scale degree), 
    /// then snaps to nearest diatonic note.
    /// </summary>
    public int FindNearestNote(int midiNote)
    {
        // First, try to find the same scale degree in a valid octave
        int noteInOctave = midiNote % 12;
        int lowestValid = ValidMidiNotes[0];
        int highestValid = ValidMidiNotes[^1];

        // Check if this pitch class exists in the diatonic scale
        if (DiatonicIntervals.Contains(noteInOctave))
        {
            // Find the octave-shifted version within our range
            for (int row = 0; row < 3; row++)
            {
                int octave = BaseOctave + row;
                int candidate = (octave + 1) * 12 + noteInOctave;
                if (_midiToKey.ContainsKey(candidate))
                {
                    // Prefer the octave closest to the original
                    // For now, just pick the first valid one, then refine
                }
            }

            // Find the closest octave
            int bestCandidate = -1;
            int bestDistance = int.MaxValue;
            for (int row = 0; row < 3; row++)
            {
                int octave = BaseOctave + row;
                int candidate = (octave + 1) * 12 + noteInOctave;
                if (_midiToKey.ContainsKey(candidate))
                {
                    int distance = Math.Abs(candidate - midiNote);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestCandidate = candidate;
                    }
                }
            }

            if (bestCandidate >= 0)
                return bestCandidate;
        }

        // Note is not in diatonic scale (e.g., a sharp/flat) — snap to nearest valid note
        int nearest = ValidMidiNotes[0];
        int minDist = Math.Abs(midiNote - nearest);

        foreach (int valid in ValidMidiNotes)
        {
            int dist = Math.Abs(midiNote - valid);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = valid;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Get a display string showing the full mapping.
    /// </summary>
    public string GetMappingDisplay()
    {
        var lines = new List<string>
        {
            "Key → Note Mapping:",
            ""
        };

        string[] rowLabels = { "Low", "Mid", "High" };
        for (int row = 2; row >= 0; row--)
        {
            var parts = new List<string>();
            for (int note = 0; note < 7; note++)
            {
                char key = KeyboardRows[row][note];
                int midi = _keyToMidi[key];
                string noteName = MidiNoteToName(midi);
                parts.Add($"{key}={noteName}");
            }
            lines.Add($"  {rowLabels[row],-4}: {string.Join("  ", parts)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Convert a MIDI note number to a human-readable name (e.g., 60 → "C4").
    /// </summary>
    public static string MidiNoteToName(int midiNote)
    {
        string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        int octave = (midiNote / 12) - 1;
        int noteIndex = midiNote % 12;
        return $"{noteNames[noteIndex]}{octave}";
    }
}
