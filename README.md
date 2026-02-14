# Music Player for Where Winds Meet (燕云十六声)

Convert any song into keyboard sequences and auto-play them in the Where Winds Meet music system.

## How It Works

The game maps 21 keyboard keys to 3 octaves of a diatonic (C major) scale:

```
High:  Q=C6  W=D6  E=E6  R=F6  T=G6  Y=A6  U=B6
Mid:   A=C5  S=D5  D=E5  F=F5  G=G5  H=A5  J=B5
Low:   Z=C4  X=D4  C=E4  V=F4  B=G4  N=A4  M=B4
```

This tool:
1. **Searches** for MIDI files online ([Midis101](https://midis101.com) for free direct download, [MidiShow](https://www.midishow.com) for Chinese songs)
2. **Converts** MIDI notes into the 21-key layout (pitch remapping, quantization, chord simplification)
3. **Auto-plays** the song by simulating keyboard input via Win32 `SendInput`

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows (for auto-play keyboard simulation; conversion works on any OS)
- **Run as Administrator** — required for sending keystrokes to games that run elevated

## Build

```bash
git clone https://github.com/TingluoHuang/music-player.git
cd music-player
dotnet build
dotnet test
```

## Workflow

```bash
# One command: search, download, and convert
dotnet run --project src/MusicPlayer -- convert "twinkle twinkle little star"

# Play — just use the song name, no need to remember file paths
dotnet run --project src/MusicPlayer -- play twinkle --delay 5

# Preview without sending keystrokes
dotnet run --project src/MusicPlayer -- play twinkle --dry-run
```

Or convert a local MIDI file:

```bash
dotnet run --project src/MusicPlayer -- convert mysong.mid
```

## Usage

### Search for MIDI Files

```bash
# Search by song name — downloads automatically from Midis101
dotnet run --project src/MusicPlayer -- search "twinkle twinkle little star"

# Chinese songs — tries Midis101 first, falls back to MidiShow (browser)
dotnet run --project src/MusicPlayer -- search "月亮代表我的心"
```

Results from [Midis101](https://midis101.com) are downloaded directly. Results from [MidiShow](https://www.midishow.com) open in your browser (login required to download).

### Convert a MIDI File

```bash
# Search online and convert in one step
dotnet run --project src/MusicPlayer -- convert "twinkle twinkle little star"

# Or convert a local MIDI file
dotnet run --project src/MusicPlayer -- convert mysong.mid
```

If the MIDI has multiple tracks, you'll be prompted to pick one.

### Play a Converted Song

```bash
# Play by song name — searches songs/ directory automatically
dotnet run --project src/MusicPlayer -- play twinkle --delay 5

# Or use the full path
dotnet run --project src/MusicPlayer -- play songs/mysong.json

# List all available songs
dotnet run --project src/MusicPlayer -- play

# Dry run — shows each note with timing and a progress bar
dotnet run --project src/MusicPlayer -- play twinkle --dry-run

# Play at half speed
dotnet run --project src/MusicPlayer -- play twinkle --speed 0.5
```

The `play` command supports smart song lookup:
- Exact name: `play twinkle` finds `songs/twinkle.json`
- Partial match: `play twinkle` also finds `songs/Twinkle-Twinkle-Little-Star.json`
- Multiple matches: prompts you to pick one

### View Key Mapping

```bash
dotnet run --project src/MusicPlayer -- mapping
```

### Test Keyboard Input

If the game doesn't respond to key presses, use the test command to debug:

```bash
# Test a single key press (default: Z)
dotnet run --project src/MusicPlayer -- test Z --delay 5

# Test a different key
dotnet run --project src/MusicPlayer -- test A --delay 5
```

Switch to the game window during the countdown. If the note doesn't play:
1. Make sure the game window is focused
2. **Run as Administrator** — most games run elevated, and Windows blocks simulated input from a non-admin process to an admin process (UIPI). Open Command Prompt or PowerShell as Administrator, then run the app from there
3. Check if anti-cheat is blocking simulated input

## All Options

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

## Song JSON Format

Converted songs are saved as JSON files in the `songs/` directory:

```json
{
  "title": "Twinkle Twinkle Little Star",
  "bpm": 120,
  "notes": [
    { "time": 0.0, "keys": ["Z"], "duration": 0.5 },
    { "time": 0.5, "keys": ["Z"], "duration": 0.5 },
    { "time": 1.0, "keys": ["B"], "duration": 0.5 }
  ]
}
```

You can manually edit these files to fix notes or adjust timing.

## Project Structure

```
music-player/
├── MusicPlayer.slnx
├── src/MusicPlayer/
│   ├── Program.cs               # CLI entry point
│   ├── NoteMapping.cs           # 21-key ↔ note mapping
│   ├── MidiSearcher.cs          # MIDI search & download (Midis101 + MidiShow)
│   ├── MidiConverter.cs         # MIDI → JSON conversion pipeline
│   ├── KeyboardPlayer.cs        # Auto-player (Win32 SendInput)
│   └── Models/
│       ├── Song.cs              # Song model
│       └── NoteEvent.cs         # Note event model
└── tests/MusicPlayer.Tests/
    ├── NoteMappingTests.cs
    └── MidiConverterTests.cs
```

## Publishing a Single Executable

```bash
dotnet publish src/MusicPlayer -c Release -r win-x64 --self-contained -o publish/

# Output: publish/MusicPlayer.exe (single file, no .NET runtime needed)
```

The published executable is in the `publish/` folder. Copy it anywhere or add it to your `PATH`, then use it directly:

```bash
musicplayer convert "twinkle twinkle little star"
musicplayer convert downloaded.mid
musicplayer play twinkle --delay 5
musicplayer mapping
```
