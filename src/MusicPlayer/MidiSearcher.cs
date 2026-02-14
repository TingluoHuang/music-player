using HtmlAgilityPack;
using Melanchall.DryWetMidi.Core;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MusicPlayer;

/// <summary>
/// Searches multiple MIDI sources and downloads files.
/// Primary: FreeMidi, MidiWorld (free direct download)
/// Secondary: Midis101 (free direct download)
/// Fallback: MidiShow (Chinese catalog, browser-only download)
/// </summary>
public class MidiSearcher
{
    private readonly HttpClient _httpClient;

    public MidiSearcher()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        };
        _httpClient = new HttpClient(handler);
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

        // Search all direct-download sources in parallel
        var tasks = new (string Name, Func<string, Task<List<SearchResult>>> Search)[]
        {
            ("FreeMidi", SearchFreeMidiAsync),
            ("MidiWorld", SearchMidiWorldAsync),
            ("Midis101", SearchMidis101Async),
        };

        var searchTasks = tasks.Select(async t =>
        {
            try { return await t.Search(query); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{t.Name} search failed: {ex.Message}");
                return new List<SearchResult>();
            }
        }).ToArray();

        var results = await Task.WhenAll(searchTasks);
        foreach (var r in results)
            all.AddRange(r);

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
    /// Search, pre-download and validate MIDI files, let user pick a verified result,
    /// and return the path to the downloaded .mid file (or null if browser-only).
    /// Only shows results with valid MIDI data. Keeps searching until 10 good candidates are found.
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

        Directory.CreateDirectory(cacheDir);

        // Separate downloadable vs browser-only results
        var downloadable = results.Where(r => r.CanDownload && r.DownloadUrl != null).ToList();
        var browserOnly = results.Where(r => !r.CanDownload).ToList();

        // Pre-download and validate all downloadable results
        Console.Write("Validating MIDI files");
        var verified = new List<VerifiedResult>();
        const int targetCount = 10;
        const int batchSize = 5;

        for (int i = 0; i < downloadable.Count && verified.Count < targetCount; i += batchSize)
        {
            var batch = downloadable.Skip(i).Take(batchSize).ToList();
            var tasks = batch.Select(async r =>
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, r.DownloadUrl);
                    if (r.PageUrl != null)
                        request.Headers.Referrer = new Uri(r.PageUrl);
                    var response = await _httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    var bytes = await response.Content.ReadAsByteArrayAsync();

                    // Verify MThd header
                    if (bytes.Length < 14 || bytes[0] != 'M' || bytes[1] != 'T'
                        || bytes[2] != 'h' || bytes[3] != 'd')
                        return null;

                    // Try parsing to catch truncated/corrupt files
                    string tempPath = Path.Combine(cacheDir, $"_validate_{Guid.NewGuid():N}.mid");
                    try
                    {
                        await File.WriteAllBytesAsync(tempPath, bytes);
                        Melanchall.DryWetMidi.Core.MidiFile.Read(tempPath);
                    }
                    catch
                    {
                        return null; // Corrupt MIDI
                    }
                    finally
                    {
                        try { File.Delete(tempPath); } catch { }
                    }

                    return new VerifiedResult { Result = r, MidiBytes = bytes };
                }
                catch
                {
                    return null; // Download failed
                }
            }).ToArray();

            var batchResults = await Task.WhenAll(tasks);
            foreach (var v in batchResults)
            {
                if (v != null && verified.Count < targetCount)
                    verified.Add(v);
            }
            Console.Write(".");
        }
        Console.WriteLine($" {verified.Count} valid file(s) found.");

        // Build final display list: verified downloads + browser-only
        var displayList = new List<(string Title, string? Source, VerifiedResult? Verified, SearchResult? BrowserResult)>();
        foreach (var v in verified)
            displayList.Add((v.Result.Title, v.Result.Source, v, null));
        foreach (var b in browserOnly.Take(5))
            displayList.Add((b.Title, b.Source, null, b));

        if (displayList.Count == 0)
        {
            Console.WriteLine("No valid MIDI files found. Try a different search term.");
            return null;
        }

        int displayCount = displayList.Count;
        Console.WriteLine($"\n{displayCount} result(s):\n");
        for (int i = 0; i < displayCount; i++)
        {
            var (title, source, v, _) = displayList[i];
            string sizeInfo = v != null ? $"  [{v.MidiBytes.Length / 1024.0:F0} KB]" : "";
            string tag = v != null ? " " : "*";
            Console.WriteLine($"  [{i + 1}] {tag} {title}{sizeInfo}{(source != null ? $"  ({source})" : "")}");
        }

        if (displayList.Any(d => d.Verified == null))
        {
            Console.WriteLine("\n  * = opens in browser (requires manual download)");
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

        var selected = displayList[selectedIndex];

        if (selected.Verified != null)
        {
            // Already downloaded and verified — just save it
            string safeTitle = SanitizeFileName(selected.Title);
            string filePath = Path.Combine(cacheDir, $"{safeTitle}.mid");
            await File.WriteAllBytesAsync(filePath, selected.Verified.MidiBytes);
            Console.WriteLine($"\n  Saved to: {filePath} ({selected.Verified.MidiBytes.Length:N0} bytes)");
            return filePath;
        }
        else if (selected.BrowserResult != null)
        {
            // MidiShow — can't download directly, open in browser
            Console.WriteLine($"\nOpening in browser: {selected.Title}");
            Console.WriteLine($"  URL: {selected.BrowserResult.PageUrl}");
            Console.WriteLine($"\nDownload the MIDI file from the page, then convert it with:");
            Console.WriteLine($"  musicplayer convert <downloaded.mid>");
            Console.WriteLine($"  (or: dotnet run --project src/MusicPlayer -- convert <downloaded.mid>)");

            try { OpenInBrowser(selected.BrowserResult.PageUrl); }
            catch { /* URL is already printed */ }

            return null;
        }

        return null;
    }

    private class VerifiedResult
    {
        public SearchResult Result { get; set; } = null!;
        public byte[] MidiBytes { get; set; } = null!;
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
    // FreeMidi — large catalog, free direct downloads
    // ──────────────────────────────────────────────

    private async Task<List<SearchResult>> SearchFreeMidiAsync(string query)
    {
        var results = new List<SearchResult>();

        string encodedQuery = Uri.EscapeDataString(query);
        string searchUrl = $"https://freemidi.org/search?q={encodedQuery}";

        var html = await _httpClient.GetStringAsync(searchUrl);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Results are in cards with <a href="download3-{id}-{slug}">Title</a>
        var links = doc.DocumentNode.SelectNodes("//h5[contains(@class,'card-title')]/a[starts-with(@href, 'download3-')]");
        if (links == null) return results;

        foreach (var link in links.Take(10))
        {
            string href = link.GetAttributeValue("href", "");
            if (string.IsNullOrEmpty(href)) continue;

            string title = HtmlEntity.DeEntitize(link.GetAttributeValue("title", "")?.Trim()
                ?? link.InnerText?.Trim() ?? "");
            if (string.IsNullOrEmpty(title) || title.Length < 2) continue;

            // Get the artist from the sibling card-text div
            var card = link.ParentNode?.ParentNode; // h5 -> card-body
            var artistNode = card?.SelectSingleNode(".//div[@class='card-text']/a");
            string artist = artistNode != null
                ? HtmlEntity.DeEntitize(artistNode.InnerText?.Trim() ?? "")
                : "";
            string displayTitle = string.IsNullOrEmpty(artist)
                ? title
                : $"{title} - {artist}";

            // Extract ID from href: download3-{id}-{slug}
            var match = Regex.Match(href, @"download3-(\d+)-");
            if (!match.Success) continue;
            string id = match.Groups[1].Value;

            string pageUrl = $"https://freemidi.org/{href}";
            string downloadUrl = $"https://freemidi.org/getter-{id}";

            results.Add(new SearchResult
            {
                Title = displayTitle,
                PageUrl = pageUrl,
                DownloadUrl = downloadUrl,
                Source = "FreeMidi",
                CanDownload = true
            });
        }

        return results;
    }

    // ──────────────────────────────────────────────
    // MidiWorld — curated MIDI collection, direct downloads
    // ──────────────────────────────────────────────

    private async Task<List<SearchResult>> SearchMidiWorldAsync(string query)
    {
        var results = new List<SearchResult>();

        string encodedQuery = Uri.EscapeDataString(query);
        string searchUrl = $"https://www.midiworld.com/search/?q={encodedQuery}";

        var html = await _httpClient.GetStringAsync(searchUrl);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Results are in <ul class="midi-results"> with lines like:
        //   "Title (Artist) - <a href="https://www.midiworld.com/download/{id}">download</a>"
        var downloadLinks = doc.DocumentNode.SelectNodes("//a[starts-with(@href, 'https://www.midiworld.com/download/')]");
        if (downloadLinks == null) return results;

        foreach (var link in downloadLinks.Take(10))
        {
            string downloadUrl = link.GetAttributeValue("href", "");
            if (string.IsNullOrEmpty(downloadUrl)) continue;

            // The text before the download link contains "Title (Artist) - "
            var parent = link.ParentNode;
            if (parent == null) continue;

            // Get the full text of the parent, extract title/artist
            string fullText = HtmlEntity.DeEntitize(parent.InnerText?.Trim() ?? "");
            // Remove " - download", "Please install flash ...", and trailing noise
            string title = Regex.Replace(fullText, @"\s*-?\s*download.*$", "", RegexOptions.IgnoreCase | RegexOptions.Singleline).Trim();
            if (string.IsNullOrEmpty(title) || title.Length < 2) continue;

            string pageUrl = $"https://www.midiworld.com/search/?q={encodedQuery}";

            results.Add(new SearchResult
            {
                Title = title,
                PageUrl = pageUrl,
                DownloadUrl = downloadUrl,
                Source = "MidiWorld",
                CanDownload = true
            });
        }

        return results;
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
