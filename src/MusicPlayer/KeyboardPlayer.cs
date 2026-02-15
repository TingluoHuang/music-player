using System.Runtime.InteropServices;
using MusicPlayer.Models;

namespace MusicPlayer;

/// <summary>
/// Auto-plays a Song by simulating keyboard input using Win32 SendInput.
/// Designed for Windows — on other platforms, falls back to console output.
/// </summary>
public class KeyboardPlayer
{
    private readonly bool _isWindows;
    private readonly bool _dryRun;

    // Map keyboard character to virtual key code
    private static readonly Dictionary<char, ushort> VirtualKeyCodes = new()
    {
        ['Q'] = 0x51, ['W'] = 0x57, ['E'] = 0x45, ['R'] = 0x52,
        ['T'] = 0x54, ['Y'] = 0x59, ['U'] = 0x55,
        ['A'] = 0x41, ['S'] = 0x53, ['D'] = 0x44, ['F'] = 0x46,
        ['G'] = 0x47, ['H'] = 0x48, ['J'] = 0x4A,
        ['Z'] = 0x5A, ['X'] = 0x58, ['C'] = 0x43, ['V'] = 0x56,
        ['B'] = 0x42, ['N'] = 0x4E, ['M'] = 0x4D,
    };

    // Modifier virtual key codes
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;

    /// <summary>
    /// Parse a key string like "Q", "Shift+Q", or "Ctrl+E" into
    /// (modifier VK or 0, base key char).
    /// </summary>
    private static (ushort Modifier, char Key) ParseKeyString(string keyStr)
    {
        if (keyStr.StartsWith("Shift+", StringComparison.OrdinalIgnoreCase) && keyStr.Length > 6)
            return (VK_SHIFT, char.ToUpper(keyStr[6]));
        if (keyStr.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase) && keyStr.Length > 5)
            return (VK_CONTROL, char.ToUpper(keyStr[5]));
        return (0, char.ToUpper(keyStr[0]));
    }

    /// <summary>
    /// Check whether a key string (possibly with modifier) is a valid game key.
    /// </summary>
    private static bool IsValidKeyString(string keyStr)
    {
        var (_, key) = ParseKeyString(keyStr);
        return VirtualKeyCodes.ContainsKey(key);
    }

    /// <summary>
    /// Create a keyboard player.
    /// </summary>
    /// <param name="dryRun">If true, only prints actions without sending keystrokes.</param>
    public KeyboardPlayer(bool dryRun = false)
    {
        _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        _dryRun = dryRun;

        if (!_isWindows && !dryRun)
        {
            Console.WriteLine("Warning: Keyboard simulation requires Windows. Running in dry-run mode.");
            _dryRun = true;
        }
    }

    /// <summary>
    /// Play a song with a countdown before starting.
    /// </summary>
    /// <param name="song">The song to play.</param>
    /// <param name="delaySeconds">Countdown before playing (to switch to game window).</param>
    /// <param name="speedMultiplier">Playback speed (1.0 = normal, 0.5 = half speed, 2.0 = double speed).</param>
    /// <param name="cancellationToken">Token to cancel playback.</param>
    public async Task PlayAsync(Song song, int delaySeconds = 3,
        double speedMultiplier = 1.0, CancellationToken cancellationToken = default)
    {
        if (song.Notes.Count == 0)
        {
            Console.WriteLine("No notes to play.");
            return;
        }

        Console.WriteLine($"Playing: {song.Title} ({song.Notes.Count} notes, {song.Duration:F1}s)");
        Console.WriteLine($"Speed: {speedMultiplier:F1}x | BPM: {song.Bpm}");

        if (_dryRun)
            Console.WriteLine("[DRY RUN — no keystrokes will be sent]");

        // Countdown
        for (int i = delaySeconds; i > 0; i--)
        {
            Console.Write($"\rStarting in {i}...");
            await Task.Delay(1000, cancellationToken);
        }
        Console.WriteLine("\rPlaying!         ");

        if (_dryRun)
        {
            // Dry-run: print each note with timing info
            await PlayDryRunAsync(song, speedMultiplier, cancellationToken);
        }
        else
        {
            // Real playback with high-resolution timing
            await PlayRealAsync(song, speedMultiplier, cancellationToken);
        }

        Console.WriteLine("\nDone!");
    }

    private async Task PlayDryRunAsync(Song song, double speedMultiplier,
        CancellationToken cancellationToken)
    {
        double totalDuration = song.Duration / speedMultiplier;

        foreach (var note in song.Notes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double time = note.Time / speedMultiplier;
            double duration = note.Duration / speedMultiplier;
            string keys = string.Join("+", note.Keys);
            double progress = totalDuration > 0 ? (time / totalDuration) * 100 : 0;

            Console.WriteLine($"  [{time,7:F2}s] {keys,-8} ({duration * 1000:F0}ms)  {ProgressBar(progress)}");
        }
    }

    private async Task PlayRealAsync(Song song, double speedMultiplier,
        CancellationToken cancellationToken)
    {
        double totalDuration = song.Duration / speedMultiplier;

        // Use a high-resolution timer for accuracy
        var startTime = System.Diagnostics.Stopwatch.GetTimestamp();
        var tickFrequency = (double)System.Diagnostics.Stopwatch.Frequency;

        // Build note tap events (press + immediate release, no hold duration)
        // Game only cares about the key tap, not how long it's held
        var events = BuildTapTimeline(song, speedMultiplier);

        foreach (var evt in events)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Wait until it's time for this event
            double targetTime = evt.Time;
            while (true)
            {
                double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - startTime) / tickFrequency;
                if (elapsed >= targetTime)
                    break;

                double remaining = targetTime - elapsed;
                if (remaining > 0.015) // 15ms threshold
                    await Task.Delay((int)(remaining * 500), cancellationToken); // Sleep ~half the remaining
                else
                    Thread.SpinWait(100); // Busy-wait for precision
            }

            // Show progress
            if (totalDuration > 0)
            {
                double progress = (evt.Time / totalDuration) * 100;
                string keys = string.Join(" ", evt.Keys);
                Console.Write($"\r  {ProgressBar(progress)}  {evt.Time:F1}s / {totalDuration:F1}s  {keys,-12}");
            }

            // Tap each key: press modifiers + press key + brief hold + release
            foreach (string key in evt.Keys)
                PressKeyString(key);

            Thread.Sleep(30); // Brief hold for the game to register

            foreach (string key in evt.Keys)
                ReleaseKeyString(key);
        }

        Console.WriteLine(); // Clear progress line
    }

    private List<TapEvent> BuildTapTimeline(Song song, double speedMultiplier)
    {
        var taps = new List<TapEvent>();

        foreach (var note in song.Notes)
        {
            double time = note.Time / speedMultiplier;
            var keys = note.Keys
                .Where(k => k.Length > 0)
                .Where(IsValidKeyString)
                .ToList();

            if (keys.Count > 0)
            {
                taps.Add(new TapEvent { Time = time, Keys = keys });
            }
        }

        return taps.OrderBy(e => e.Time).ToList();
    }

    private void PressKey(char key)
    {
        if (_dryRun)
            return; // Dry-run output is handled in PlayDryRunAsync

        if (_isWindows && VirtualKeyCodes.TryGetValue(key, out ushort vk))
        {
            SendKeyEvent(vk, isKeyUp: false);
        }
    }

    private void ReleaseKey(char key)
    {
        if (_dryRun)
            return;

        if (_isWindows && VirtualKeyCodes.TryGetValue(key, out ushort vk))
        {
            SendKeyEvent(vk, isKeyUp: true);
        }
    }

    /// <summary>
    /// Press a key string that may include a modifier (e.g. "Shift+Q", "Ctrl+E", or "Q").
    /// Presses the modifier first, then the base key.
    /// </summary>
    private void PressKeyString(string keyStr)
    {
        if (_dryRun)
            return;

        var (modifier, key) = ParseKeyString(keyStr);
        if (modifier != 0)
            SendKeyEvent(modifier, isKeyUp: false);
        PressKey(key);
    }

    /// <summary>
    /// Release a key string that may include a modifier.
    /// Releases the base key first, then the modifier.
    /// </summary>
    private void ReleaseKeyString(string keyStr)
    {
        if (_dryRun)
            return;

        var (modifier, key) = ParseKeyString(keyStr);
        ReleaseKey(key);
        if (modifier != 0)
            SendKeyEvent(modifier, isKeyUp: true);
    }

    private static string ProgressBar(double percent)
    {
        const int width = 20;
        int filled = (int)(percent / 100 * width);
        filled = Math.Clamp(filled, 0, width);
        return $"[{new string('█', filled)}{new string('░', width - filled)}] {percent:F0}%";
    }

    /// <summary>
    /// Send a test key press to verify the game receives input.
    /// Press and immediately release the specified key.
    /// </summary>
    public void TestKey(char key)
    {
        if (!_isWindows)
        {
            Console.WriteLine("Keyboard simulation requires Windows.");
            return;
        }

        key = char.ToUpper(key);
        if (!VirtualKeyCodes.TryGetValue(key, out ushort vk))
        {
            Console.WriteLine($"Unknown key: {key}");
            return;
        }

        Console.WriteLine($"Sending key '{key}' (VK=0x{vk:X2}, Scan=0x{MapVirtualKey(vk, MAPVK_VK_TO_VSC):X2})...");
        PressKey(key);
        Thread.Sleep(50); // Brief hold
        ReleaseKey(key);
        Console.WriteLine("Key sent. Did the game receive it?");
    }

    #region Win32 P/Invoke

    private static void SendKeyEvent(ushort virtualKeyCode, bool isKeyUp)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        // Get the hardware scan code — games using DirectInput/Raw Input need this
        ushort scanCode = (ushort)MapVirtualKey(virtualKeyCode, MAPVK_VK_TO_VSC);

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKeyCode,
                    wScan = scanCode,
                    dwFlags = KEYEVENTF_SCANCODE | (isKeyUp ? KEYEVENTF_KEYUP : 0u),
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        uint result = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (result == 0)
        {
            int error = Marshal.GetLastWin32Error();
            Console.Error.WriteLine($"SendInput failed (error {error}). Try running as Administrator.");
        }
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint MAPVK_VK_TO_VSC = 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    // Union must include all members so sizeof(INPUT) matches the Win32 definition.
    // MOUSEINPUT is the largest member — without it, cbSize is too small and
    // SendInput returns ERROR_INVALID_PARAMETER (87).
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    #endregion

    private class TapEvent
    {
        public double Time { get; set; }
        public List<string> Keys { get; set; } = new();
    }
}
