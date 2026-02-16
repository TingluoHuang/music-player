using Melanchall.DryWetMidi.Core;
using MusicPlayer;
using MusicPlayer.Models;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0)
    {
        PrintUsage();
        return 1;
    }

    string command = args[0].ToLower();

    return command switch
    {
        "search" => await HandleSearchAsync(args[1..]),
        "convert" => await HandleConvertAsync(args[1..]),
        "play" => await HandlePlayAsync(args[1..]),
        "test" => HandleTest(args[1..]),
        "mapping" => HandleMapping(),
        "help" or "--help" or "-h" => PrintUsage(),
        _ => PrintUsage($"Unknown command: {command}")
    };
}

static async Task<int> HandleSearchAsync(string[] args)
{
    string? query = null;

    for (int i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--"))
            query = args[i];
    }

    if (string.IsNullOrEmpty(query))
    {
        Console.Error.WriteLine("Error: Provide a song name to search for.");
        Console.Error.WriteLine("  Usage: ./MusicPlayer.exe search \"song name\"");
        return 1;
    }

    string cacheDir = Path.Combine(Directory.GetCurrentDirectory(), "cache");
    var searcher = new MidiSearcher();
    var file = await searcher.SearchAndDownloadAsync(query, cacheDir);

    if (file != null)
    {
        Console.WriteLine($"\nTo convert: ./MusicPlayer.exe convert \"{file}\"");
    }

    return 0;
}

static async Task<int> HandleConvertAsync(string[] args)
{
    string? file = null;
    string? query = null;
    double quantize = 50;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--file":
                if (++i >= args.Length) { Console.Error.WriteLine("Error: --file requires a value."); return 1; }
                file = args[i];
                break;
            case "--quantize":
                if (++i >= args.Length) { Console.Error.WriteLine("Error: --quantize requires a value."); return 1; }
                if (!double.TryParse(args[i], out quantize)) { Console.Error.WriteLine($"Error: Invalid quantize value: {args[i]}"); return 1; }
                break;
            default:
                if (!args[i].StartsWith("--"))
                {
                    // If the positional arg is an existing file, use it directly
                    if (File.Exists(args[i]))
                        file = args[i];
                    else
                        query = args[i]; // Otherwise treat as song name to search
                }
                break;
        }
    }

    // If a song name was given, search and download first
    if (file == null && query != null)
    {
        string cacheDir = Path.Combine(Directory.GetCurrentDirectory(), "cache");
        var searcher = new MidiSearcher();
        file = await searcher.SearchAndDownloadAsync(query, cacheDir);

        if (file == null)
        {
            // SearchAndDownloadAsync already printed instructions (browser-only result)
            return 0;
        }
    }

    if (string.IsNullOrEmpty(file))
    {
        Console.Error.WriteLine("Error: Provide a MIDI file or song name.");
        Console.Error.WriteLine("  Usage: ./MusicPlayer.exe convert <file.mid>");
        Console.Error.WriteLine("  Usage: ./MusicPlayer.exe convert \"song name\"");
        Console.Error.WriteLine("  (or: dotnet run --project src/MusicPlayer -- convert ...)");
        return 1;
    }

    if (!File.Exists(file))
    {
        Console.Error.WriteLine($"Error: File not found: {file}");
        return 1;
    }

    return await ConvertFile(file, quantize);
}

static async Task<int> ConvertFile(string file, double quantize)
{
    // Convert with error handling for invalid MIDI files
    Song song;
    try
    {
        var converter = new MidiConverter();
        song = converter.Convert(file, quantizationMs: quantize);
    }
    catch (NotEnoughBytesException)
    {
        Console.Error.WriteLine($"Error: '{file}' is not a valid MIDI file (truncated or corrupt).");
        Console.Error.WriteLine($"  File size: {new FileInfo(file).Length:N0} bytes");
        Console.Error.WriteLine($"  The source file may be damaged. Try a different search result.");
        return 1;
    }
    catch (InvalidChunkSizeException)
    {
        Console.Error.WriteLine($"Error: '{file}' is not a valid MIDI file (invalid chunk size).");
        Console.Error.WriteLine($"  File size: {new FileInfo(file).Length:N0} bytes");
        Console.Error.WriteLine($"  The source file may be damaged. Try a different search result.");
        return 1;
    }
    catch (Exception ex) when (ex.Message.Contains("MIDI") || ex.Message.Contains("midi"))
    {
        Console.Error.WriteLine($"Error: Failed to read MIDI file: {ex.Message}");
        return 1;
    }

    // Save to songs/<safe-title>.json
    string songsDir = Path.Combine(Directory.GetCurrentDirectory(), "songs");
    Directory.CreateDirectory(songsDir);
    string safeTitle = MidiSearcher.SanitizeFileName(song.Title);
    string output = Path.Combine(songsDir, $"{safeTitle}.json");

    await song.SaveAsync(output);
    Console.WriteLine($"\nConverted: {song.Title}");
    Console.WriteLine($"  Notes: {song.Notes.Count}");
    Console.WriteLine($"  Duration: {song.Duration:F1}s");
    Console.WriteLine($"  BPM: {song.Bpm} (effective playback speed)");
    Console.WriteLine($"  Saved to: {output}");
    Console.WriteLine($"\nTo play:     ./MusicPlayer.exe play \"{output}\"");
    Console.WriteLine($"To preview:  ./MusicPlayer.exe play \"{output}\" --dry-run");
    return 0;
}

static async Task<int> HandlePlayAsync(string[] args)
{
    string? input = null;
    int delay = 3;
    double speed = 1.0;
    bool dryRun = false;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--delay":
                if (++i >= args.Length) { Console.Error.WriteLine("Error: --delay requires a value."); return 1; }
                if (!int.TryParse(args[i], out delay)) { Console.Error.WriteLine($"Error: Invalid delay value: {args[i]}"); return 1; }
                break;
            case "--speed":
                if (++i >= args.Length) { Console.Error.WriteLine("Error: --speed requires a value."); return 1; }
                if (!double.TryParse(args[i], out speed)) { Console.Error.WriteLine($"Error: Invalid speed value: {args[i]}"); return 1; }
                break;
            case "--dry-run":
                dryRun = true;
                break;
            default:
                if (!args[i].StartsWith("--"))
                    input = args[i];
                break;
        }
    }

    if (string.IsNullOrEmpty(input))
    {
        // No input — list available songs if any exist
        string songsDir = Path.Combine(Directory.GetCurrentDirectory(), "songs");
        if (Directory.Exists(songsDir))
        {
            var songFiles = Directory.GetFiles(songsDir, "*.json");
            if (songFiles.Length > 0)
            {
                Console.Error.WriteLine("Available songs:");
                foreach (var f in songFiles)
                    Console.Error.WriteLine($"  {Path.GetFileNameWithoutExtension(f)}");
                Console.Error.WriteLine($"\nUsage: ./MusicPlayer.exe play <name>");
                return 1;
            }
        }
        Console.Error.WriteLine("Error: Provide a song JSON file path.");
        Console.Error.WriteLine("  Usage: ./MusicPlayer.exe play song.json");
        return 1;
    }

    // If input doesn't exist as a file, try finding it in songs/ directory
    if (!File.Exists(input))
    {
        string songsDir = Path.Combine(Directory.GetCurrentDirectory(), "songs");
        string? found = null;

        if (Directory.Exists(songsDir))
        {
            // Try exact match first: songs/<input>.json
            string candidate = Path.Combine(songsDir, $"{input}.json");
            if (File.Exists(candidate))
            {
                found = candidate;
            }
            else
            {
                // Try case-insensitive partial match
                var matches = Directory.GetFiles(songsDir, "*.json")
                    .Where(f => Path.GetFileNameWithoutExtension(f)
                        .Contains(input, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matches.Count == 1)
                {
                    found = matches[0];
                }
                else if (matches.Count > 1)
                {
                    Console.WriteLine("Multiple matches found:");
                    for (int j = 0; j < matches.Count; j++)
                        Console.WriteLine($"  [{j + 1}] {Path.GetFileNameWithoutExtension(matches[j])}");

                    Console.Write($"\nPick a number (1-{matches.Count}) [1]: ");
                    string? pick = Console.ReadLine()?.Trim();
                    int pickIndex = 0;
                    if (!string.IsNullOrEmpty(pick) && int.TryParse(pick, out int c) && c >= 1 && c <= matches.Count)
                        pickIndex = c - 1;
                    found = matches[pickIndex];
                }
            }
        }

        if (found != null)
        {
            input = found;
        }
        else
        {
            Console.Error.WriteLine($"Error: File not found: {input}");
            Console.Error.WriteLine("  Tip: Convert a song first with: ./MusicPlayer.exe convert \"song name\"");
            return 1;
        }
    }

    var song = await Song.LoadAsync(input);
    var player = new KeyboardPlayer(dryRun);

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        Console.WriteLine("\nStopped.");
    };

    try
    {
        await player.PlayAsync(song, delay, speed, cts.Token);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Playback cancelled.");
    }

    return 0;
}

static int HandleMapping()
{
    var mapping = new NoteMapping();
    Console.WriteLine(mapping.GetMappingDisplay());
    return 0;
}

static int HandleTest(string[] args)
{
    int delay = 3;
    string keyStr = "Z"; // Default to Z (C4, lowest note)

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--delay":
                if (++i < args.Length) int.TryParse(args[i], out delay);
                break;
            default:
                if (!args[i].StartsWith("--") && args[i].Length > 0)
                    keyStr = args[i];
                break;
        }
    }

    Console.WriteLine("=== Keyboard Input Test ===");
    Console.WriteLine($"Key to send: {keyStr}");
    Console.WriteLine("Switch to the game window now!");
    Console.WriteLine();

    for (int i = delay; i > 0; i--)
    {
        Console.Write($"\rSending in {i}...");
        Thread.Sleep(1000);
    }
    Console.WriteLine("\rSending!         ");

    var player = new KeyboardPlayer();
    player.TestKey(keyStr);

    Console.WriteLine();
    Console.WriteLine("If the game didn't receive the key:");
    Console.WriteLine("  1. Make sure the game window is focused");
    Console.WriteLine("  2. Try running as Administrator");
    Console.WriteLine("  3. Check if the game's anti-cheat blocks simulated input");

    return 0;
}

static int PrintUsage(string? error = null)
{
    if (error != null)
        Console.Error.WriteLine($"Error: {error}\n");

    Console.WriteLine("""
        Music Player for Where Winds Meet (燕云十六声)
        ================================================

        Commands:
          convert <song or file>  Search, download & convert (or convert a local .mid)
          play <name or file>     Play a converted song via keyboard simulation
          search <song>           Search & download MIDI files
          test [key]              Test a single key press (debug input issues)
          mapping                 Show the key-to-note mapping

        Convert options:
          --quantize <ms>         Quantization grid in ms (0 = off, default: 50)

        Play options:
          --delay <seconds>       Countdown before playing (default: 3)
          --speed <multiplier>    Playback speed (0.5 = slow, 2.0 = fast, default: 1.0)
          --dry-run               Show notes with timing info without sending keystrokes

        Test options:
          --delay <seconds>       Countdown before sending (default: 3)

        Examples:
          ./MusicPlayer.exe convert "twinkle twinkle little star"  (search → download → convert)
          ./MusicPlayer.exe convert downloaded.mid                  (convert local file)
          ./MusicPlayer.exe play twinkle --dry-run                  (preview)
          ./MusicPlayer.exe play twinkle --delay 5                  (play in game)
          ./MusicPlayer.exe test Z --delay 5                        (test single key)
        """);

    return error != null ? 1 : 0;
}
