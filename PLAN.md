# Music Player — Implementation Plan

## Understanding the Ask

You have the music-playing system in **Where Winds Meet** (燕云十六声), which maps 21 keyboard keys (3 rows × 7 columns) to musical notes on traditional Chinese instruments (guqin, flute, etc.):

```
Row 1 (High octave):   Q  W  E  R  T  Y  U
Row 2 (Mid octave):    A  S  D  F  G  H  J
Row 3 (Low octave):    Z  X  C  V  B  N  M
```

Each row = one octave of a **diatonic scale** (Do Re Mi Fa Sol La Ti), giving **3 full octaves** total.

You need a system that can:

1. **Convert any music** into a sequence of these 21 key presses.
2. **Auto-play** the converted music by sending real keyboard inputs to your game.

---

## Key Design Decisions

### 1. Note Mapping — 21 keys → 21 notes

With 21 keys, the most natural mapping is **1.75 chromatic octaves** (e.g. C4–G#5), or **3 diatonic octaves** if the game uses a major scale. We need to confirm which scale your game uses. The two most common layouts in music games are:

| Layout | Notes per octave | Range with 21 keys |
|---|---|---|
| **Chromatic** | 12 | 1.75 octaves |
| **Pentatonic (C major)** | 5 (C D E G A) | ~4 octaves |
| **Diatonic (C major)** | 7 (C D E F G A B) | 3 octaves |

**Decision:** Default to a **diatonic C-major** scale, which matches the Where Winds Meet instrument layout exactly. 21 keys = 3 diatonic octaves (C4–B6). Each row is one octave:

```
Row 1 (Q→U):  C6  D6  E6  F6  G6  A6  B6   (high)
Row 2 (A→J):  C5  D5  E5  F5  G5  A5  B5   (mid)
Row 3 (Z→M):  C4  D4  E4  F4  G4  A4  B4   (low)
```

The base octave will be configurable in case the game's tuning differs.

### 2. Input Format — Search-Based (with MIDI fallback)

**Primary flow:** User types a song name → app searches the internet for notes → auto-converts to playable format.

**Search sources (in priority order):**

| Source | What we get | Pros | Cons |
|---|---|---|---|
| **Game community sheets** | Pre-mapped key sequences for Where Winds Meet / similar games | Already in the right format, no conversion needed | Limited catalog, may not have every song |
| **Free MIDI databases** (BitMidi, FreeMIDI, etc.) | MIDI files with note data | Huge catalog, structured note data | Requires our MIDI→key conversion pipeline |
| **Numbered musical notation (简谱) sites** | Chinese-style notation (1=Do, 2=Re...) | Maps almost directly to our 7-note rows | Needs a simple parser, Chinese sites only |

**Why search rather than manual MIDI upload?**

- Removes friction — user just types a song name and plays.
- MIDI files are still supported as a **fallback** if the user already has one locally.
- Most users won't know where to find MIDI files; the app handles it.

**Decision:** Implement a multi-source search:
1. Search free MIDI databases via web scraping (BitMidi has a searchable catalog).
2. Optionally parse community note sheets if a structured source is found.
3. Still accept local `.mid` files as a fallback with `--file` flag.

**Technical approach:**
- Use `HttpClient` + `HtmlAgilityPack` for web scraping (no API keys needed).
- Download MIDI to a temp/cache directory, then run the existing conversion pipeline.
- Cache downloaded files so repeated plays don't re-download.

### 3. Conversion Strategy — Quantize & Fit to 21 Keys

Converting a full MIDI file (potentially 88 piano keys, multiple tracks) to 21 keys requires:

| Step | What it does | Why |
|---|---|---|
| **Track selection** | Pick the melody track (or let user choose) | Most songs have melody + chords + bass — we only want the main melody for a single-player key layout |
| **Pitch remapping** | Transpose all notes into our 21-key range | Notes outside the range get octave-shifted to the nearest valid octave |
| **Timing quantization** | Snap notes to a grid (e.g. 16th notes) | Games typically can't handle microsecond precision; quantizing makes playback cleaner |
| **Chord simplification** | Limit simultaneous notes to max **3** | Where Winds Meet supports up to 3 simultaneous keys; keeps it playable |
| **Output format** | Save as a simple JSON sequence | Easy to parse, human-readable, editable |

**Output format example:**

```json
{
  "title": "Twinkle Twinkle Little Star",
  "bpm": 120,
  "notes": [
    { "time": 0.0,   "keys": ["Z"],      "duration": 0.5 },
    { "time": 0.5,   "keys": ["Z"],      "duration": 0.5 },
    { "time": 1.0,   "keys": ["B"],      "duration": 0.5 },
    { "time": 1.5,   "keys": ["B"],      "duration": 0.5 },
    { "time": 2.0,   "keys": ["N"],      "duration": 0.5 },
    { "time": 2.0,   "keys": ["V"],      "duration": 0.5 }
  ]
}
```

### 4. Auto-Player — Simulate Keyboard Input

**How:** Use OS-level keyboard simulation to press/release keys at the right times.

**Decision:** Use the **Win32 `SendInput` API** via P/Invoke — this is the most reliable keyboard simulation method on Windows. It supports both key press and release events, which is essential since Where Winds Meet responds to note duration (how long a key is held). Being native Win32, it's less likely to be blocked by the game's input handling compared to third-party libraries.

The auto-player reads the JSON note sequence and sends timed keystrokes to the active game window.

### 5. Language & Stack — C# (.NET 8)

**Why C#?**

- **Native Windows platform** — Where Winds Meet is a Windows game; C# is the natural fit.
- **`DryWetMIDI`** — excellent MIDI parsing library with built-in quantization and track manipulation.
- **Win32 `SendInput`** — most reliable keyboard simulation on Windows via P/Invoke; less likely to be blocked by game input handling than third-party wrappers.
- **`HtmlAgilityPack`** — mature HTML parser for web scraping MIDI databases.
- **Single `.exe` distribution** — .NET 8 AOT or self-contained publish produces a single executable with zero dependencies for the end user.
- **Strong typing** — catches mapping/conversion bugs at compile time.

---

## Components / File Structure

```
music-player/
├── PLAN.md                          # This file
├── README.md                        # User-facing docs
├── MusicPlayer.sln                  # Solution file
├── src/
│   └── MusicPlayer/
│       ├── MusicPlayer.csproj       # Project file (.NET 8 console app)
│       ├── Program.cs               # Entry point & CLI
│       ├── NoteMapping.cs           # Note ↔ key mapping configuration
│       ├── MidiSearcher.cs          # Internet search for MIDI files
│       ├── MidiConverter.cs         # MIDI → JSON converter
│       ├── KeyboardPlayer.cs        # Auto-player (Win32 SendInput)
│       └── Models/
│           ├── Song.cs              # Song model (JSON serialization)
│           └── NoteEvent.cs         # Individual note event model
├── cache/                           # Cached downloaded MIDI files
├── songs/                           # Converted song JSON files
│   └── example.json
└── tests/
    └── MusicPlayer.Tests/
        ├── MusicPlayer.Tests.csproj
        ├── NoteMappingTests.cs
        ├── MidiConverterTests.cs
        └── MidiSearcherTests.cs
```

---

## Implementation Steps

| # | Task | Description |
|---|---|---|
| 1 | **Define key→note mapping** | Create `NoteMapping.cs` with configurable scale (diatonic/pentatonic/chromatic) and base octave |
| 2 | **MIDI search** | Implement `MidiSearcher.cs` to scrape free MIDI databases (BitMidi, etc.) by song name, download to cache |
| 3 | **MIDI parser** | Use `DryWetMIDI` to read MIDI, extract notes from selected track |
| 4 | **Pitch remapper** | Transpose out-of-range notes into the 21-key range via octave shifting |
| 5 | **Timing quantizer** | Snap note times to nearest grid division (DryWetMIDI has built-in quantizer) |
| 6 | **Chord simplifier** | Cap simultaneous notes at **3** (game limit) |
| 7 | **JSON exporter** | Write the converted sequence to JSON using `System.Text.Json` |
| 8 | **CLI for search+convert** | `musicplayer convert "song name"` (searches, downloads, converts) or `musicplayer convert --file song.mid` |
| 9 | **Auto-player** | Read JSON, simulate keystrokes with proper timing using Win32 `SendInput` P/Invoke |
| 10 | **CLI for playback** | `musicplayer play --input song.json --delay 3` (3 sec countdown to switch to game) |
| 11 | **Tests** | Unit tests with xUnit for mapping, conversion, search, and quantization |

---

## Open Questions (Need Your Input)

1. ~~**Which game is this for?**~~ → **Where Winds Meet** (燕云十六声) ✅
2. ~~**Does the game care about key release (note duration), or only key press?**~~ → **Yes, key release matters.** We need press+release simulation with proper hold duration. ✅
3. ~~**How many simultaneous keys does the game support?**~~ → **3 keys max.** Chord simplifier will cap at 3 simultaneous notes. ✅
4. ~~**What OS are you running the game on?**~~ → **Windows.** Will use `pynput` for keyboard simulation (well-supported on Windows). ✅
5. ~~**Do you want a GUI, or is a command-line tool sufficient?**~~ → **CLI-first.** A CLI is the right starting point: simpler to build, easy to script, and the typical workflow (convert → play) maps naturally to two commands. A GUI adds complexity without clear benefit at this stage — you'd just be clicking "Convert" and "Play" buttons that do the same thing. If we find the CLI awkward in practice (e.g., wanting to preview/edit note mappings visually), we can add a lightweight GUI later. ✅
6. ~~**Does the game's note mapping start at C, or a different root note?**~~ → **C.** Root note confirmed as C. ✅
