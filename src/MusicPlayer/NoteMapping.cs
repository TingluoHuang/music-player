namespace MusicPlayer;

/// <summary>
/// Maps MIDI note numbers to keyboard keys for Where Winds Meet.
/// 
/// Layout (3 rows × 7 natural keys + 5 chromatic keys = 36 keys = 3 chromatic octaves):
///   Row 1 (High):  Q  W  E  R  T  Y  U   → C6 D6 E6 F6 G6 A6 B6
///   Row 2 (Mid):   A  S  D  F  G  H  J   → C5 D5 E5 F5 G5 A5 B5
///   Row 3 (Low):   Z  X  C  V  B  N  M   → C4 D4 E4 F4 G4 A4 B4
///
///   Shift + key → sharp (#): raises the note by 1 semitone
///     C#: Shift+Z / Shift+A / Shift+Q
///     F#: Shift+V / Shift+F / Shift+R
///     G#: Shift+B / Shift+G / Shift+T
///   Ctrl  + key → flat  (b): lowers the note by 1 semitone
///     Eb: Ctrl+C / Ctrl+D / Ctrl+E
///     Bb: Ctrl+M / Ctrl+J / Ctrl+U
/// </summary>
public class NoteMapping
{
    // C-major diatonic scale intervals (semitones from root)
    private static readonly int[] DiatonicIntervals = { 0, 2, 4, 5, 7, 9, 11 };

    // Chromatic note definitions: (semitone, modifier, keyIndex)
    //   modifier = true  → Shift on keyIndex (sharp: raises the note)
    //   modifier = false → Ctrl  on keyIndex (flat:  lowers the note)
    //
    // semitone 1  (C#) → Shift on index 0 (C key)   — #1
    // semitone 3  (Eb) → Ctrl  on index 2 (E key)   — b3
    // semitone 6  (F#) → Shift on index 3 (F key)   — #4
    // semitone 8  (G#) → Shift on index 4 (G key)   — #5
    // semitone 10 (Bb) → Ctrl  on index 6 (B key)   — b7
    private static readonly (int Semitone, bool IsShift, int KeyIndex)[] ChromaticNotes =
    {
        (1,  true,  0),  // C#  = Shift + C key
        (3,  false, 2),  // Eb  = Ctrl  + E key
        (6,  true,  3),  // F#  = Shift + F key
        (8,  true,  4),  // G#  = Shift + G key
        (10, false, 6),  // Bb  = Ctrl  + B key
    };

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
    /// All 36 valid MIDI note numbers in ascending order (12 per octave × 3 octaves).
    /// </summary>
    public IReadOnlyList<int> ValidMidiNotes { get; }

    /// <summary>
    /// Map from MIDI note number → keyboard key string (e.g. "Q", "Shift+A", "Ctrl+D").
    /// </summary>
    private readonly Dictionary<int, string> _midiToKey = new();

    /// <summary>
    /// Map from keyboard key string → MIDI note number.
    /// </summary>
    private readonly Dictionary<string, int> _keyToMidi = new(StringComparer.OrdinalIgnoreCase);

    public NoteMapping(int baseOctave = 4)
    {
        BaseOctave = baseOctave;
        var validNotes = new List<int>();

        for (int row = 0; row < 3; row++)
        {
            int octave = baseOctave + row;
            int octaveBase = (octave + 1) * 12; // MIDI: C4 = 60 = (4+1)*12

            // Map the 7 diatonic (natural) notes
            for (int note = 0; note < 7; note++)
            {
                int midiNote = octaveBase + DiatonicIntervals[note];
                string key = KeyboardRows[row][note].ToString();

                _midiToKey[midiNote] = key;
                _keyToMidi[key] = midiNote;
                validNotes.Add(midiNote);
            }

            // Map the 5 chromatic notes.
            // The game uses Shift for sharps (#1, #4, #5) and Ctrl for flats (b3, b7).
            foreach (var (semitone, isShift, keyIndex) in ChromaticNotes)
            {
                int midiNote = octaveBase + semitone;
                char baseKey = KeyboardRows[row][keyIndex];
                string modifier = isShift ? "Shift" : "Ctrl";
                string primaryKey = $"{modifier}+{baseKey}";

                _midiToKey[midiNote] = primaryKey;
                _keyToMidi[primaryKey] = midiNote;
                validNotes.Add(midiNote);

                // Register the enharmonic alias (Shift↔Ctrl on the neighboring key)
                if (isShift)
                {
                    // Shift+X = sharp → also register Ctrl on the next key up as alias
                    int flatKeyIndex = keyIndex + 1;
                    if (flatKeyIndex < 7)
                    {
                        char flatBase = KeyboardRows[row][flatKeyIndex];
                        string flatKey = $"Ctrl+{flatBase}";
                        if (!_keyToMidi.ContainsKey(flatKey))
                            _keyToMidi[flatKey] = midiNote;
                    }
                }
                else
                {
                    // Ctrl+X = flat → also register Shift on the previous key as alias
                    int sharpKeyIndex = keyIndex - 1;
                    if (sharpKeyIndex >= 0)
                    {
                        char sharpBase = KeyboardRows[row][sharpKeyIndex];
                        string sharpKey = $"Shift+{sharpBase}";
                        if (!_keyToMidi.ContainsKey(sharpKey))
                            _keyToMidi[sharpKey] = midiNote;
                    }
                }
            }
        }

        validNotes.Sort();
        ValidMidiNotes = validNotes.AsReadOnly();
    }

    /// <summary>
    /// Get the keyboard key string for a MIDI note number.
    /// Returns a plain key like "Q" for natural notes, "Shift+A" for sharps, or "Ctrl+D" for flats.
    /// Returns null if the note is not in our 36-key range.
    /// </summary>
    public string? GetKey(int midiNote)
    {
        return _midiToKey.TryGetValue(midiNote, out var key) ? key : null;
    }

    /// <summary>
    /// Get the MIDI note number for a keyboard key string.
    /// Accepts plain keys ("Q"), sharps ("Shift+A"), or flats ("Ctrl+D").
    /// </summary>
    public int? GetMidiNote(string key)
    {
        return _keyToMidi.TryGetValue(key, out var note) ? note : null;
    }

    /// <summary>
    /// Check if the given MIDI note falls within our 36-key range
    /// (i.e. can be played without any snapping).
    /// </summary>
    public bool IsExactMatch(int midiNote) => _midiToKey.ContainsKey(midiNote);

    /// <summary>
    /// Find the nearest valid MIDI note for any given MIDI pitch.
    /// With 36-key (full chromatic) support, this only needs to handle
    /// notes outside the 3-octave range by octave-shifting, then clamping.
    /// </summary>
    public int FindNearestNote(int midiNote)
    {
        // If it's already a valid note, return as-is
        if (_midiToKey.ContainsKey(midiNote))
            return midiNote;

        // Try to find the same pitch class in a valid octave (closest octave first)
        int noteInOctave = midiNote % 12;

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

        // Fallback: snap to nearest valid note by absolute distance
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
    /// Get a display string showing the full mapping (natural + chromatic).
    /// </summary>
    public string GetMappingDisplay()
    {
        var lines = new List<string>
        {
            "Key → Note Mapping (36 keys):",
            ""
        };

        string[] rowLabels = { "Low", "Mid", "High" };
        for (int row = 2; row >= 0; row--)
        {
            // Natural notes
            var natural = new List<string>();
            for (int note = 0; note < 7; note++)
            {
                char key = KeyboardRows[row][note];
                string keyStr = key.ToString();
                int midi = _keyToMidi[keyStr];
                string noteName = MidiNoteToName(midi);
                natural.Add($"{key}={noteName}");
            }
            lines.Add($"  {rowLabels[row],-4}: {string.Join("  ", natural)}");

            // Chromatic notes (Shift for sharps, Ctrl for flats)
            var chromatics = new List<string>();
            foreach (var (semitone, isShift, keyIndex) in ChromaticNotes)
            {
                int octave = BaseOctave + row;
                int midiNote = (octave + 1) * 12 + semitone;
                char baseKey = KeyboardRows[row][keyIndex];
                string noteName = MidiNoteToName(midiNote);
                string symbol = isShift ? "⇧" : "^";
                chromatics.Add($"{symbol}{baseKey}={noteName}");
            }
            lines.Add($"        {string.Join("  ", chromatics)}");
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
