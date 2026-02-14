using HtmlAgilityPack;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MusicPlayer;

/// <summary>
/// Searches multiple MIDI sources and downloads files.
/// Primary: Midis101 (free direct download)
/// Fallback: MidiShow (Chinese catalog, browser-only download)
/// </summary>
public class MidiSearcher
{
    private readonly HttpClient _httpClient;

    public MidiSearcher()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    /// <summary>
    /// Search all sources for MIDI files matching the query.
    /// Midis101 results (free download) are listed first.
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(string query)
    {
        var all = new List<SearchResult>();

        // Search Midis101 first (supports direct download)
        try
        {
            var midis101 = await SearchMidis101Async(query);
            all.AddRange(midis101);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Midis101 search failed: {ex.Message}");
        }

        // Then search MidiShow (larger Chinese catalog, browser-only)
        try
        {
            var midiShow = await SearchMidiShowAsync(query);
            all.AddRange(midiShow);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MidiShow search failed: {ex.Message}");
        }

        return all;
    }

    /// <summary>
    /// Search, let user pick a result, download MIDI (or open browser for MidiShow),
    /// and return the path to the downloaded .mid file (or null if browser-only).
    /// </summary>
    public async Task<string?> SearchAndDownloadAsync(string query, string cacheDir)
    {
        Console.WriteLine($"Searching for '{query}'...");
        var results = await SearchAsync(query);

        if (results.Count == 0)
        {
            Console.WriteLine("No MIDI files found.");
            Console.WriteLine("Try searching on https://www.midishow.com or https://midis101.com directly.");
            return null;
        }

        int displayCount = Math.Min(results.Count, 15);
        Console.WriteLine($"\nFound {results.Count} result(s):\n");
        for (int i = 0; i < displayCount; i++)
        {
            var r = results[i];
            string tag = r.CanDownload ? "  " : "🌐";
            Console.WriteLine($"  [{i + 1}] {tag} {r.Title}{(r.Source != null ? $"  ({r.Source})" : "")}");
        }

        if (results.Any(r => !r.CanDownload))
        {
            Console.WriteLine("\n  🌐 = opens in browser (requires manual download)");
        }

        int selectedIndex = 0;

        if (displayCount > 1)
        {
            Console.Write($"\nPick a number (1-{displayCount}) [1]: ");
            string? input = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(input) && int.TryParse(input, out int choice)
                && choice >= 1 && choice <= displayCount)
            {
                selectedIndex = choice - 1;
            }
        }

        var selected = results[selectedIndex];

        if (selected.CanDownload && selected.DownloadUrl != null)
        {
            Console.WriteLine($"\nDownloading: {selected.Title}...");
            Directory.CreateDirectory(cacheDir);

            string safeTitle = SanitizeFileName(selected.Title);
            string filePath = Path.Combine(cacheDir, $"{safeTitle}.mid");

            var midiBytes = await _httpClient.GetByteArrayAsync(selected.DownloadUrl);

            // Verify it starts with MThd (MIDI header)
            if (midiBytes.Length < 4 || midiBytes[0] != 'M' || midiBytes[1] != 'T'
                || midiBytes[2] != 'h' || midiBytes[3] != 'd')
            {
                Console.Error.WriteLine("Error: Downloaded file is not a valid MIDI file.");
                return null;
            }

            await File.WriteAllBytesAsync(filePath, midiBytes);
            Console.WriteLine($"  Saved to: {filePath}");
            return filePath;
        }
        else
        {
            // MidiShow — can't download directly, open in browser
            Console.WriteLine($"\nOpening in browser: {selected.Title}");
            Console.WriteLine($"  URL: {selected.PageUrl}");
            Console.WriteLine($"\nDownload the MIDI file from the page, then convert it with:");
            Console.WriteLine($"  musicplayer convert <downloaded.mid>");
            Console.WriteLine($"  (or: dotnet run --project src/MusicPlayer -- convert <downloaded.mid>)");

            try { OpenInBrowser(selected.PageUrl); }
            catch { /* URL is already printed */ }

            return null;
        }
    }

    /// <summary>
    /// Replace anything that isn't a letter, digit, dot, or hyphen with a hyphen,
    /// then collapse runs of hyphens and trim them from the edges.
    /// </summary>
    internal static string SanitizeFileName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '.' || c == '-')
                sb.Append(c);
            else
                sb.Append('-');
        }
        // Collapse multiple hyphens and trim
        string result = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"-{2,}", "-").Trim('-');
        return string.IsNullOrEmpty(result) ? "midi" : result;
    }

    private static void OpenInBrowser(string url)
    {
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        else if (OperatingSystem.IsMacOS())
            Process.Start("open", url);
        else
            Process.Start("xdg-open", url);
    }

    // ──────────────────────────────────────────────
    // Midis101 — free MIDI downloads, no login needed
    // ──────────────────────────────────────────────

    private async Task<List<SearchResult>> SearchMidis101Async(string query)
    {
        var results = new List<SearchResult>();

        // Midis101 uses path-based search: /search/{query}
        string encodedQuery = Uri.EscapeDataString(query.Replace(' ', '+'));
        string searchUrl = $"https://midis101.com/search/{encodedQuery}";

        var html = await _httpClient.GetStringAsync(searchUrl);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Results are in a table, each row has <a href="/free-midi/{id}-{slug}">Title</a>
        var links = doc.DocumentNode.SelectNodes("//a[starts-with(@href, '/free-midi/')]");
        if (links == null) return results;

        foreach (var link in links.Take(10))
        {
            string href = link.GetAttributeValue("href", "");
            if (string.IsNullOrEmpty(href)) continue;

            // Skip pagination or non-result links
            if (!Regex.IsMatch(href, @"^/free-midi/\d+-")) continue;

            string title = HtmlEntity.DeEntitize(link.InnerText?.Trim() ?? "");
            if (string.IsNullOrEmpty(title) || title.Length < 2) continue;

            // Download URL follows the pattern: /download/{id}-{slug}
            string slug = href.Replace("/free-midi/", "");
            string downloadUrl = $"https://midis101.com/download/{slug}";
            string pageUrl = $"https://midis101.com{href}";

            results.Add(new SearchResult
            {
                Title = title,
                PageUrl = pageUrl,
                DownloadUrl = downloadUrl,
                Source = "Midis101",
                CanDownload = true
            });
        }

        return results;
    }

    // ──────────────────────────────────────────────
    // MidiShow — large Chinese MIDI catalog (login required to download)
    // ──────────────────────────────────────────────

    private async Task<List<SearchResult>> SearchMidiShowAsync(string query)
    {
        var results = new List<SearchResult>();

        string encodedQuery = Uri.EscapeDataString(query);
        string searchUrl = $"https://www.midishow.com/search/result?q={encodedQuery}";

        var html = await _httpClient.GetStringAsync(searchUrl);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var items = doc.DocumentNode.SelectNodes("//div[@id='search-result']//div[@data-key]");
        if (items == null) return results;

        foreach (var item in items.Take(10))
        {
            var link = item.SelectSingleNode(".//a[contains(@href, '/midi/') and contains(@href, '.html')]");
            if (link == null) continue;

            string href = link.GetAttributeValue("href", "");
            if (string.IsNullOrEmpty(href)) continue;

            var titleNode = item.SelectSingleNode(".//h3");
            string title = titleNode != null
                ? HtmlEntity.DeEntitize(titleNode.InnerText?.Trim() ?? "")
                : "";
            title = Regex.Replace(title, @"<[^>]+>", "").Trim();

            if (string.IsNullOrEmpty(title) || title.Length < 2) continue;

            string pageUrl = href.StartsWith("http")
                ? href
                : $"https://www.midishow.com{href}";

            results.Add(new SearchResult
            {
                Title = title,
                PageUrl = pageUrl,
                DownloadUrl = null,
                Source = "MidiShow",
                CanDownload = false
            });
        }

        return results
            .GroupBy(r => r.PageUrl)
            .Select(g => g.First())
            .Take(10)
            .ToList();
    }

    public class SearchResult
    {
        public string Title { get; set; } = string.Empty;
        public string PageUrl { get; set; } = string.Empty;
        public string? DownloadUrl { get; set; }
        public string? Source { get; set; }
        public bool CanDownload { get; set; }
    }
}
