using HtmlAgilityPack;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;

namespace SiteCheck
{
    public class Program
    {
        /// <summary>
        /// Base URI.
        /// </summary>
        private static Uri? BaseUri { get; set; }

        /// <summary>
        /// Main download manager.
        /// </summary>
        private static HttpClient DownloadClient { get; } = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        /// <summary>
        /// All queued items.
        /// </summary>
        private static List<QueueEntry> QueueEntries { get; } = new();

        /// <summary>
        /// Init all the things..
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        private static async Task Main(string[] args)
        {
            // Do we have an argument?
            if (args.Length == 0)
            {
                ConsoleEx.WriteObjects(
                    ConsoleColor.Red,
                    "Error: ",
                    (byte) 0x00,
                    "Requires first argument to be URL to start scanning from.",
                    Environment.NewLine);

                return;
            }

            // Is the argument a valid URI?
            try
            {
                BaseUri = new Uri(args[0]);
                QueueEntries.Add(new QueueEntry(BaseUri));
            }
            catch (Exception ex)
            {
                ConsoleEx.WriteException(ex);
                return;
            }

            // Let's kick it off.
            ConsoleEx.WriteObjects(
                "SiteCheck v0.1",
                Environment.NewLine);

            ConsoleEx.WriteObjects(
                "Scanning: ",
                ConsoleColor.Blue,
                BaseUri,
                (byte) 0x00,
                Environment.NewLine,
                Environment.NewLine);

            var start = DateTimeOffset.Now;

            // Init the endless loop for the entire queue.
            var index = -1;

            while (true)
            {
                index++;

                if (index == QueueEntries.Count)
                {
                    break;
                }

                await ProcessQueueEntry(index);
            }

            // We're done.
            Console.WriteLine();

            var end = DateTimeOffset.Now;
            var duration = end - start;

            // Generate a report based on the queue and other metadata.
            await WriteReport(
                start,
                end,
                duration);

            // Write some stats to console.
            ConsoleEx.WriteObjects(
                "Run started ",
                ConsoleColor.Blue,
                start,
                Environment.NewLine,
                (byte) 0x00);

            ConsoleEx.WriteObjects(
                "Run ended ",
                ConsoleColor.Blue,
                end,
                Environment.NewLine,
                (byte) 0x00);

            ConsoleEx.WriteObjects(
                "Run took ",
                ConsoleColor.Blue,
                duration,
                Environment.NewLine,
                (byte) 0x00,
                Environment.NewLine);

            ConsoleEx.WriteObjects(
                "Total URLs scanned ",
                ConsoleColor.Blue,
                QueueEntries.Count,
                Environment.NewLine,
                (byte) 0x00);

            var average = QueueEntries
                .Where(n => n.RequestLength.HasValue)
                .Sum(n => n.RequestLength.Value.TotalMilliseconds) /
                QueueEntries.Count;

            ConsoleEx.WriteObjects(
                "Average response time (ms) ",
                ConsoleColor.Blue,
                (int) average,
                (byte) 0x00,
                Environment.NewLine);
        }

        /// <summary>
        /// Process the indexed queue entry.
        /// </summary>
        /// <param name="index">Index in queue.</param>
        private static async Task ProcessQueueEntry(int index)
        {
            var entry = QueueEntries[index];

            try
            {
                // Mark the request as started.
                entry.RequestCreated = DateTimeOffset.Now;

                // Make request.
                using var response = await DownloadClient.GetAsync(entry.Uri);
                var ms = new MemoryStream();
                await response.Content.CopyToAsync(ms);

                // Keep the status code.
                entry.StatusCode = response.StatusCode;
                entry.StatusDescription = ReasonPhrases.GetReasonPhrase((int) response.StatusCode);

                // Mark the request as finished.
                entry.RequestFinished = DateTimeOffset.Now;

                // Analyze HTML and extract new links to scan.
                ExtractLinksFromHtml(entry.Uri, ms.ToArray());
            }
            catch (Exception ex)
            {
                ConsoleEx.WriteException(ex);
                return;
            }

            // Status code.
            var statusCode = ((int) entry.StatusCode).ToString();

            ConsoleColor statusCodeColor;

            if (statusCode.StartsWith("3"))
            {
                statusCodeColor = ConsoleColor.Yellow;
            }
            else if (statusCode.StartsWith("2"))
            {
                statusCodeColor = ConsoleColor.Green;
            }
            else
            {
                statusCodeColor = ConsoleColor.Red;
            }

            // Response time.
            var responseTime = entry.RequestLength.HasValue
                ? $"{(int) entry.RequestLength.Value.TotalMilliseconds}ms"
                : string.Empty;

            // Write to console.
            ConsoleEx.WriteObjects(
                ConsoleColor.Blue,
                " * ",
                (byte) 0x00,
                "[",
                ConsoleColor.Blue,
                index + 1,
                (byte) 0x00,
                "/",
                ConsoleColor.Blue,
                QueueEntries.Count,
                (byte) 0x00,
                "] [",
                statusCodeColor,
                statusCode,
                (byte) 0x00,
                "] ",
                ConsoleColor.Blue,
                responseTime,
                (byte) 0x00,
                " ",
                entry.Uri,
                Environment.NewLine);
        }

        /// <summary>
        /// Analyze HTML and extract new links to scan.
        /// </summary>
        /// <param name="baseUri">Content source.</param>
        /// <param name="bytes">Content to analyze.</param>
        private static void ExtractLinksFromHtml(Uri baseUri, byte[] bytes)
        {
            var doc = new HtmlDocument();
            HtmlNodeCollection nodes;

            try
            {
                var html = Encoding.UTF8.GetString(bytes);

                doc.LoadHtml(html);
                nodes = doc.DocumentNode.SelectNodes("//a[@href]");

                if (nodes == null)
                {
                    return;
                }
            }
            catch
            {
                return;
            }

            foreach (var link in nodes)
            {
                var href = link.GetAttributeValue("href", null);

                try
                {
                    if (BaseUri != null)
                    {
                        var uri = new Uri(baseUri, href);

                        if (BaseUri.IsBaseOf(uri) &&
                            !QueueEntries.Any(n => n.Uri == uri))
                        {
                            QueueEntries.Add(new QueueEntry(uri));
                        }
                    }
                }
                catch
                {
                    //
                }
            }
        }

        /// <summary>
        /// Generate a report based on the queue and other metadata.
        /// </summary>
        private static async Task WriteReport(
            DateTimeOffset start,
            DateTimeOffset end,
            TimeSpan duration)
        {
            var html = new StringBuilder();

            html.Append("<!doctype html>");
            html.Append("<html lang=\"en\">");
            html.Append("<head>");

            // Head.
            html.Append("<meta charset=\"utf-8\">");
            html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            html.Append($"<title>SiteCheck: {BaseUri}</title>");

            html.Append("</head>");
            html.Append("<body>");

            // Body.
            html.Append($"<h1>SiteCheck: {BaseUri}</h1>");

            // Stats.
            var average = QueueEntries
                .Where(n => n.RequestLength.HasValue)
                .Sum(n => n.RequestLength.Value.TotalMilliseconds) /
                QueueEntries.Count;

            html.Append("<h2>Stats</h2>");
            html.Append("<ul>");
            html.Append($"<li>Run started: <strong>{start}</strong></li>");
            html.Append($"<li>Run ended: <strong>{end}</strong></li>");
            html.Append($"<li>Run took: <strong>{duration}</strong></li>");
            html.Append($"<li>Total URLs scanned: <strong>{QueueEntries.Count}</strong></li>");
            html.Append($"<li>Average response time: <strong>{(int) average}ms</strong></li>");
            html.Append("</ul>");

            // Response codes.
            var codes = QueueEntries
                .Where(n => n.StatusCode.HasValue)
                .Select(n => (int) n.StatusCode.Value)
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            html.Append("<h2>HTTP Response Codes Hits</h2>");
            html.Append("<ul>");
            
            foreach (var code in codes)
            {
                var count = QueueEntries
                    .Count(n => n.StatusCode.HasValue &&
                                (int)n.StatusCode.Value == code);

                html.Append($"<li>{code}: {count}</li>");
            }

            html.Append("</ul>");

            // List of URLs.
            html.Append("<table border=1>");
            html.Append("<thead><tr>");

            html.Append("<th>URL</th>");
            html.Append("<th>Request Started</th>");
            html.Append("<th>Request Finished</th>");
            html.Append("<th>Response Time</th>");
            html.Append("<th>HTTP Status</th>");

            html.Append("</tr></thead>");
            html.Append("<tbody>");

            foreach (var entry in QueueEntries)
            {
                html.Append("<tr>");

                // URL.
                html.Append($"<td>{entry.Uri}</td>");

                // Request Started.
                html.Append($"<td>{entry.RequestCreated}</td>");

                // Request Finished.
                html.Append($"<td>{entry.RequestFinished}</td>");

                // Response Time.
                html.Append($"<td>{entry.RequestLength}</td>");

                // HTTP Status Code.
                html.Append($"<td>{(int) entry.StatusCode} {entry.StatusDescription}</td>");

                // Done.
                html.Append("</tr>");
            }

            html.Append("</tbody>");
            html.Append("</table>");
            html.Append("</body>");
            html.Append("</html>");

            // Write to file.
            try
            {
                var path = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    $"report-{DateTimeOffset.Now:yyyy-MM-dd-HH-mm-ss}-{BaseUri?.Host}.html");

                await File.WriteAllTextAsync(
                    path,
                    html.ToString());

                ConsoleEx.WriteObjects(
                    "Wrote report to ",
                    ConsoleColor.Black,
                    path,
                    (byte) 0x00,
                    Environment.NewLine);
            }
            catch (Exception ex)
            {
                ConsoleEx.WriteException(ex);
            }
        }
    }
}