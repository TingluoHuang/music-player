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

        // Schedule all key presses and releases
        var events = BuildTimeline(song, speedMultiplier);

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

            // Show progress on key press
            if (evt.IsPress && totalDuration > 0)
            {
                double progress = (evt.Time / totalDuration) * 100;
                Console.Write($"\r  {ProgressBar(progress)}  {evt.Time:F1}s / {totalDuration:F1}s");
            }

            // Execute the event
            if (evt.IsPress)
                PressKey(evt.Key);
            else
                ReleaseKey(evt.Key);
        }

        Console.WriteLine(); // Clear progress line
    }

    private List<TimelineEvent> BuildTimeline(Song song, double speedMultiplier)
    {
        var timeline = new List<TimelineEvent>();

        foreach (var note in song.Notes)
        {
            double time = note.Time / speedMultiplier;
            double duration = note.Duration / speedMultiplier;

            foreach (string keyStr in note.Keys)
            {
                if (keyStr.Length == 0) continue;
                char key = char.ToUpper(keyStr[0]);

                timeline.Add(new TimelineEvent
                {
                    Time = time,
                    Key = key,
                    IsPress = true
                });

                timeline.Add(new TimelineEvent
                {
                    Time = time + duration,
                    Key = key,
                    IsPress = false
                });
            }
        }

        return timeline.OrderBy(e => e.Time).ThenBy(e => e.IsPress ? 0 : 1).ToList();
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

    private static string ProgressBar(double percent)
    {
        const int width = 20;
        int filled = (int)(percent / 100 * width);
        filled = Math.Clamp(filled, 0, width);
        return $"[{new string('█', filled)}{new string('░', width - filled)}] {percent:F0}%";
    }

    #region Win32 P/Invoke

    private static void SendKeyEvent(ushort virtualKeyCode, bool isKeyUp)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKeyCode,
                    wScan = 0,
                    dwFlags = isKeyUp ? KEYEVENTF_KEYUP : 0u,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
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

    #endregion

    private class TimelineEvent
    {
        public double Time { get; set; }
        public char Key { get; set; }
        public bool IsPress { get; set; }
    }
}
