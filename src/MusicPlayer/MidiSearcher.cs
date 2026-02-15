using HtmlAgilityPack;
using Melanchall.DryWetMidi.Core;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MusicPlayer;

/// <summary>
/// Searches multiple MIDI sources and downloads files.
/// Primary: FreeMidi, MidiWorld (free direct download)
/// Secondary: Midis101, Ichigos, VGMusic (free direct download)
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
            ("Ichigos", SearchIchigosAsync),
            ("VGMusic", SearchVGMusicAsync),
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

        // Interleave results round-robin across sources so that validation
        // (which stops after N good files) doesn't starve slower sources.
        int maxLen = results.Max(r => r.Count);
        for (int i = 0; i < maxLen; i++)
        {
            foreach (var sourceResults in results)
            {
                if (i < sourceResults.Count)
                    all.Add(sourceResults[i]);
            }
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
            Console.WriteLine($"  ./MusicPlayer.exe convert <downloaded.mid>");
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

    // ──────────────────────────────────────────────
    // Ichigo's Sheet Music — anime & game piano MIDIs (direct download)
    // ──────────────────────────────────────────────

    private async Task<List<SearchResult>> SearchIchigosAsync(string query)
    {
        var results = new List<SearchResult>();

        // Ichigo's uses a POST form: action="/sheets", fields q=1&qtitle={query}
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("q", "1"),
            new KeyValuePair<string, string>("qtitle", query),
        });

        var response = await _httpClient.PostAsync("https://ichigos.com/sheets", content);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Page structure (flat children of <td class="content">):
        //   <span class='title2'><a>Game Name</a></span><br><br>
        //   Song Title (Transcribed by Author)<br>
        //   <i>instrument</i> | <a>pdf</a> | <a href='...type=midi...'>midi</a> | <br><br>
        //
        // Strategy: walk flat child nodes, track current game header,
        // and for each midi link find the song title text preceding it.

        var contentNode = doc.DocumentNode.SelectSingleNode("//td[@class='content']");
        if (contentNode == null) return results;

        var children = contentNode.ChildNodes.ToList();
        string currentGame = "";

        for (int i = 0; i < children.Count; i++)
        {
            var node = children[i];

            // Track game headers: <span class='title2'>
            if (node.Name == "span" && node.GetAttributeValue("class", "") == "title2")
            {
                currentGame = HtmlEntity.DeEntitize(node.InnerText?.Trim() ?? "");
                continue;
            }

            // Look for midi download links
            if (node.Name == "a" && node.GetAttributeValue("href", "").Contains("type=midi"))
            {
                string href = node.GetAttributeValue("href", "");
                string downloadUrl = href.StartsWith("http")
                    ? href
                    : $"https://ichigos.com{href}";

                // Walk backwards to find the song title text node.
                // Pattern before midi link: "Song Title<br><i>instr</i> | <a>pdf</a> | <a>midi</a>"
                // So we skip past: "|", other <a> links, <i>, <br> to reach the song title text.
                string songTitle = "";
                for (int j = i - 1; j >= 0 && j >= i - 15; j--)
                {
                    var prev = children[j];
                    // Skip whitespace-only text nodes, "|" text, <br>, <a>, <i> tags
                    string prevText = prev.InnerText?.Trim() ?? "";
                    if (prev.Name == "br" || prev.Name == "i" || prev.Name == "a"
                        || prevText == "|" || string.IsNullOrWhiteSpace(prevText))
                        continue;

                    // Skip if this is another game header
                    if (prev.Name == "span" && prev.GetAttributeValue("class", "") == "title2")
                        break;

                    // This should be the song title text node
                    songTitle = HtmlEntity.DeEntitize(prevText);
                    break;
                }

                // Clean up: remove "(Transcribed by ...)" and "(arranged by ...)"
                songTitle = Regex.Replace(songTitle, @"\s*\((Transcribed|arranged) by[^)]*\)", "",
                    RegexOptions.IgnoreCase).Trim();

                string displayTitle = !string.IsNullOrEmpty(currentGame) && !string.IsNullOrEmpty(songTitle)
                    ? $"{currentGame} - {songTitle}"
                    : !string.IsNullOrEmpty(songTitle) ? songTitle
                    : !string.IsNullOrEmpty(currentGame) ? currentGame
                    : $"Ichigos #{results.Count + 1}";

                results.Add(new SearchResult
                {
                    Title = displayTitle,
                    PageUrl = "https://ichigos.com/sheets",
                    DownloadUrl = downloadUrl,
                    Source = "Ichigos",
                    CanDownload = true
                });

                if (results.Count >= 15) break;
            }
        }

        return results;
    }

    // ──────────────────────────────────────────────
    // VGMusic — large videogame MIDI archive, organized by platform
    // ──────────────────────────────────────────────

    /// <summary>
    /// VGMusic sections to search (most popular game platforms for ACG content).
    /// Ordered by relevance — first 4 searched in parallel for speed.
    /// </summary>
    private static readonly string[] VGMusicSections =
    {
        "console/nintendo/snes",
        "console/nintendo/nes",
        "console/sony/ps1",
        "console/nintendo/n64",
        "console/nintendo/gba",
        "console/nintendo/gameboy",
    };

    private async Task<List<SearchResult>> SearchVGMusicAsync(string query)
    {
        var results = new List<SearchResult>();
        var queryWords = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Use a longer timeout for VGMusic since section pages are large (1-2 MB)
        using var vgClient = new HttpClient();
        vgClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        vgClient.Timeout = TimeSpan.FromSeconds(30);

        // Search platform sections in parallel
        var sectionTasks = VGMusicSections.Select(async section =>
        {
            try
            {
                string sectionUrl = $"https://www.vgmusic.com/music/{section}/";
                var html = await vgClient.GetStringAsync(sectionUrl);
                var sectionResults = SearchVGMusicHtml(html, sectionUrl, queryWords);
                return sectionResults;
            }
            catch (Exception)
            {
                return new List<SearchResult>();
            }
        }).ToArray();

        var sectionResults = await Task.WhenAll(sectionTasks);
        foreach (var r in sectionResults)
            results.AddRange(r);

        // Deduplicate by download URL and limit results
        return results
            .GroupBy(r => r.DownloadUrl)
            .Select(g => g.First())
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Search a VGMusic section HTML page for songs matching query words.
    /// Structure: game titles in &lt;td class="header"&gt;, MIDI links in &lt;td&gt;&lt;a href="file.mid"&gt;
    /// </summary>
    private static List<SearchResult> SearchVGMusicHtml(string html, string sectionUrl, string[] queryWords)
    {
        var results = new List<SearchResult>();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Find all game header cells and MIDI links
        var rows = doc.DocumentNode.SelectNodes("//tr");
        if (rows == null) return results;

        string currentGame = "";

        foreach (var row in rows)
        {
            // Check for game header
            var headerCell = row.SelectSingleNode(".//td[@class='header']");
            if (headerCell != null)
            {
                currentGame = HtmlEntity.DeEntitize(headerCell.InnerText?.Trim() ?? "");
                continue;
            }

            // Check for MIDI link
            var midiLink = row.SelectSingleNode(".//td/a[contains(@href, '.mid')]");
            if (midiLink == null) continue;

            string href = midiLink.GetAttributeValue("href", "");
            string songTitle = HtmlEntity.DeEntitize(midiLink.InnerText?.Trim() ?? "");
            if (string.IsNullOrEmpty(href) || string.IsNullOrEmpty(songTitle)) continue;

            // Match query words against game name + song title
            string searchText = $"{currentGame} {songTitle}".ToLowerInvariant();
            bool matches = queryWords.All(w => searchText.Contains(w));
            if (!matches) continue;

            string displayTitle = !string.IsNullOrEmpty(currentGame)
                ? $"{currentGame} - {songTitle}"
                : songTitle;

            string downloadUrl = href.StartsWith("http")
                ? href
                : $"{sectionUrl}{href}";

            results.Add(new SearchResult
            {
                Title = displayTitle,
                PageUrl = sectionUrl,
                DownloadUrl = downloadUrl,
                Source = "VGMusic",
                CanDownload = true
            });
        }

        return results;
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
