# Music Player for Where Winds Meet (燕云十六声)

Convert any song into keyboard sequences and auto-play them in the Where Winds Meet music system.

## Quick Start

1. Download `MusicPlayer.exe` from [Releases](https://github.com/TingluoHuang/music-player/releases) (or [build from source](#building-from-source))
2. **Right-click → Run as Administrator** (required for the game to receive keystrokes)
3. In the game, [switch to the 36-key layout](#game-setup) (required for sharps/flats)
4. Convert and play:

```bash
./MusicPlayer.exe convert "twinkle twinkle little star"
./MusicPlayer.exe play twinkle --delay 5
```

## How It Works

The game maps 36 keyboard keys to 3 full chromatic octaves using modifier keys:

```
Natural notes (21 keys):
  High:  Q=C6  W=D6  E=E6  R=F6  T=G6  Y=A6  U=B6
  Mid:   A=C5  S=D5  D=E5  F=F5  G=G5  H=A5  J=B5
  Low:   Z=C4  X=D4  C=E4  V=F4  B=G4  N=A4  M=B4

Sharps — Shift + key (15 keys):
  High:  ⇧Q=C#6  ⇧W=D#6  ⇧R=F#6  ⇧T=G#6  ⇧Y=A#6
  Mid:   ⇧A=C#5  ⇧S=D#5  ⇧F=F#5  ⇧G=G#5  ⇧H=A#5
  Low:   ⇧Z=C#4  ⇧X=D#4  ⇧V=F#4  ⇧B=G#4  ⇧N=A#4

Flats — Ctrl + key (alternative for the same sharps):
  e.g.  Ctrl+X=Db4  Ctrl+C=Eb4  Ctrl+E=Eb6  etc.
```

This tool:
1. **Searches** for MIDI files online ([FreeMidi](https://freemidi.org), [MidiWorld](https://www.midiworld.com), [Midis101](https://midis101.com), [Ichigo's](https://ichigos.com), [VGMusic](https://www.vgmusic.com), [MidiShow](https://www.midishow.com))
2. **Validates** downloads automatically — only shows files that are confirmed valid
3. **Converts** MIDI notes into the 36-key chromatic layout (pitch remapping, quantization, chord simplification) — sharps and flats are preserved instead of being snapped to the nearest natural note
4. **Auto-plays** the song by simulating keyboard input via Win32 `SendInput`, pressing Shift/Ctrl modifiers as needed

## Game Setup

Before playing, you must switch to the **36-key keyboard** in the game:

1. Open the music system and enter **Free Play** mode
2. Press **F1** to cycle the keyboard layout until you see the **36-key** version (with sharp/flat keys visible)
3. The 36-key layout adds Shift (sharp) and Ctrl (flat) modifiers — this is required for the tool to play chromatic notes correctly

> **Important:** The game defaults to a 21-key (diatonic only) layout. If you stay on 21 keys, any sharp/flat notes in the song will not play correctly.

## Usage

### Convert a Song

```bash
# Search online, download, and convert in one step
./MusicPlayer.exe convert "twinkle twinkle little star"

# Or convert a local MIDI file
./MusicPlayer.exe convert mysong.mid
```

If the MIDI has multiple tracks, you'll be prompted to pick one.

### Play a Song

```bash
# Play by song name — searches songs/ directory automatically
./MusicPlayer.exe play twinkle --delay 5

# List all available songs
./MusicPlayer.exe play

# Preview without sending keystrokes
./MusicPlayer.exe play twinkle --dry-run

# Play at half speed
./MusicPlayer.exe play twinkle --speed 0.5
```

The `play` command supports smart song lookup:
- Exact name: `play twinkle` finds `songs/twinkle.json`
- Partial match: `play twinkle` also finds `songs/Twinkle-Twinkle-Little-Star.json`
- Multiple matches: prompts you to pick one

### Search for MIDI Files

```bash
# Search by song name
./MusicPlayer.exe search "twinkle twinkle little star"

# Anime & game songs — Ichigo's and VGMusic specialize in ACG content
./MusicPlayer.exe search "naruto"
./MusicPlayer.exe search "zelda"

# Chinese songs — MidiShow has the largest Chinese catalog
./MusicPlayer.exe search "月亮代表我的心"
```

Results from [FreeMidi](https://freemidi.org), [MidiWorld](https://www.midiworld.com), [Midis101](https://midis101.com), [Ichigo's](https://ichigos.com) (anime/game piano), and [VGMusic](https://www.vgmusic.com) (videogame music) are downloaded and validated automatically. Results from [MidiShow](https://www.midishow.com) open in your browser (login required to download).

### View Key Mapping

```bash
./MusicPlayer.exe mapping
```

### All Options

```
Commands:
  convert <song or file>  Search, download & convert (or convert a local .mid)
  play <name or file>     Play a converted song via keyboard simulation
  search <song>           Search & download MIDI files
  test [key]              Test a single key press (debug input issues)
  mapping                 Show key-to-note mapping

Convert options:
  --quantize <ms>         Quantization grid in ms (0 = off, default: 50)

Play options:
  --delay <seconds>       Countdown before playing (default: 3)
  --speed <multiplier>    Playback speed (0.5 = slow, 2.0 = fast, default: 1.0)
  --dry-run               Show notes with timing info without sending keystrokes

Test options:
  --delay <seconds>       Countdown before sending (default: 3)
```

## Troubleshooting

**Game doesn't receive key presses:**

1. Make sure the game window is focused before the countdown ends
2. **Run as Administrator** — most games run elevated, and Windows blocks simulated input from a non-admin process (UIPI). Right-click the exe or open your terminal as Administrator
3. Use `./MusicPlayer.exe test Z --delay 5` to test a single key press
4. If the test key works but songs sound wrong, try `--dry-run` to preview

**Corrupt MIDI files:** The tool pre-downloads and validates all MIDI files before showing results. If you still see issues with locally provided files, try a different source.

## Song JSON Format

Converted songs are saved as JSON in `songs/`:

```json
{
  "title": "Twinkle Twinkle Little Star",
  "bpm": 120,
  "notes": [
    { "time": 0.0, "keys": ["Z"], "duration": 0.5 },
    { "time": 0.5, "keys": ["Z"], "duration": 0.5 },
    { "time": 1.0, "keys": ["B"], "duration": 0.5 },
    { "time": 1.5, "keys": ["Shift+V"], "duration": 0.5 }
  ]
}
```

Key format: plain letter for natural notes (`"Z"`, `"A"`, `"Q"`), `"Shift+X"` for sharps, `"Ctrl+X"` for flats. You can manually edit these files to fix notes or adjust timing.

---

## Development

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows (for auto-play keyboard simulation; conversion works on any OS)

### Building from Source

```bash
git clone https://github.com/TingluoHuang/music-player.git
cd music-player
dotnet build
dotnet test
```

Run from source:

```bash
dotnet run --project src/MusicPlayer -- convert "twinkle twinkle little star"
dotnet run --project src/MusicPlayer -- play twinkle --delay 5
```

### Publishing a Single Executable

```bash
dotnet publish src/MusicPlayer -c Release -r win-x64 --self-contained -o publish/
```

Output: `publish/MusicPlayer.exe` — single file, no .NET runtime needed. Copy it anywhere or add it to your `PATH`.

### Project Structure

```
music-player/
├── MusicPlayer.slnx
├── src/MusicPlayer/
│   ├── Program.cs               # CLI entry point
│   ├── NoteMapping.cs           # 36-key ↔ note mapping (natural + sharp/flat)
│   ├── MidiSearcher.cs          # MIDI search & download (FreeMidi + MidiWorld + Midis101 + Ichigos + VGMusic + MidiShow)
│   ├── MidiConverter.cs         # MIDI → JSON conversion pipeline
│   ├── KeyboardPlayer.cs        # Auto-player (Win32 SendInput)
│   └── Models/
│       ├── Song.cs              # Song model
│       └── NoteEvent.cs         # Note event model
└── tests/MusicPlayer.Tests/
    ├── NoteMappingTests.cs
    └── MidiConverterTests.cs
```
