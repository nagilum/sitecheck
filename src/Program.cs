using HtmlAgilityPack;
using Microsoft.AspNetCore.WebUtilities;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

[assembly: AssemblyVersion("1.0.*")]

namespace SiteCheck
{
    public static class Program
    {
        /// <summary>
        /// All queued items.
        /// </summary>
        public static List<QueueEntry> QueueEntries { get; } = new();

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
        /// Directory to write the report to.
        /// </summary>
        private static string ReportPath { get; set; } = Directory.GetCurrentDirectory();

        /// <summary>
        /// Headers to verify pr. request.
        /// </summary>
        private static Dictionary<string, string?> VerifyHeaders { get; } = new();

        /// <summary>
        /// Init all the things..
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        private static async Task Main(string[] args)
        {
            // Do we have an argument?
            if (args.Length == 0)
            {
                // Show the apps usage info and parameters.
                ShowAppUsageInfo();
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
                // Show error.
                ConsoleEx.WriteException(ex);
                Console.WriteLine();

                // Show the apps usage info and parameters.
                ShowAppUsageInfo();

                return;
            }

            // Analyze the remaining command-line arguments.
            try
            {
                AnalyzeCommandLineArguments(args.Skip(1).ToArray());
            }
            catch (Exception ex)
            {
                // Show error.
                ConsoleEx.WriteException(ex);
                Console.WriteLine();

                // Show the apps usage info and parameters.
                ShowAppUsageInfo();

                return;
            }

            // Let's kick it off.
            ConsoleEx.WriteObjects(
                "SiteCheck v",
                GetVersion(),
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
                .Sum(n => n.RequestLength?.TotalMilliseconds ?? 0) /
                QueueEntries.Count;

            ConsoleEx.WriteObjects(
                "Average response time (ms) ",
                ConsoleColor.Blue,
                (int) average,
                (byte) 0x00,
                Environment.NewLine);
        }

        /// <summary>
        /// Analyze the command-line arguments.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        private static void AnalyzeCommandLineArguments(string[] args)
        {
            if (args.Length < 2)
            {
                return;
            }

            for (var i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    // -t <milliseconds>    Set the timeout of the HTTP requests.
                    case "-t":
                        if (!double.TryParse(
                                args[i + 1],
                                NumberStyles.Any,
                                new CultureInfo("en-US"),
                                out var milliSeconds))
                        {
                            throw new Exception($"Unable to parse {args[i + 1]} to milliseconds.");
                        }

                        DownloadClient.Timeout = TimeSpan.FromMilliseconds(milliSeconds);
                        break;

                    // -h <header>          Verify the existence of a header.
                    // -h <header>:<value>  Verify the header and its value. Value can be a regex.
                    case "-h":
                        var value = args[i + 1];
                        var sp = value.IndexOf(':');

                        var key = sp == -1
                            ? value.ToLower()
                            : value.Substring(0, sp).ToLower();

                        value = sp == -1
                            ? null
                            : value.Substring(sp + 1);

                        VerifyHeaders[key] = value;
                        break;

                    // -p <path>            Give path where to write the report.
                    case "-p":
                        if (!Directory.Exists(args[i + 1]))
                        {
                            throw new Exception($"Specified path does not exist: {args[i + 1]}");
                        }

                        ReportPath = args[i + 1];
                        break;
                }
            }
        }

        /// <summary>
        /// Analyze headers against the user input.
        /// </summary>
        /// <param name="entry">Current queue entry.</param>
        private static void AnalyzeHeaders(QueueEntry entry)
        {
            foreach (var item in VerifyHeaders)
            {
                // Verify existence.
                if (item.Value == null)
                {
                    if (entry.Headers.ContainsKey(item.Key))
                    {
                        entry.HeadersVerified.Add(item.Key, item.Value);
                    }
                    else
                    {
                        entry.HeadersNotVerified.Add(item.Key, item.Value);
                    }
                }

                // Verify header and value.
                else
                {
                    if (entry.Headers.ContainsKey(item.Key))
                    {
                        var value = entry.Headers[item.Key];

                        if (value != null)
                        {
                            var regex = new System.Text.RegularExpressions.Regex(item.Value);

                            if (regex.Matches(value).Count > 0)
                            {
                                entry.HeadersVerified.Add(item.Key, item.Value);
                            }
                            else
                            {
                                entry.HeadersNotVerified.Add(item.Key, item.Value);
                            }
                        }
                        else
                        {
                            entry.HeadersNotVerified.Add(item.Key, item.Value);
                        }
                    }
                    else
                    {
                        entry.HeadersNotVerified.Add(item.Key, item.Value);
                    }
                }
            }
        }

        /// <summary>
        /// Analyze HTML and extract new links to scan.
        /// </summary>
        /// <param name="entry">Current queue entry.</param>
        /// <param name="bytes">Content to analyze.</param>
        private static void ExtractLinksFromHtml(QueueEntry entry, byte[] bytes)
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
                        var uri = new Uri(entry.Uri, href);

                        if (BaseUri.IsBaseOf(uri))
                        {
                            var temp = QueueEntries
                                .Find(n => n.Uri == uri);

                            if (temp != null)
                            {
                                entry.LinksTo.Add(temp.Id);
                            }
                            else
                            {
                                temp = new QueueEntry(uri);
                                entry.LinksTo.Add(temp.Id);
                                QueueEntries.Add(temp);
                            }
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
        /// Generate the HTML content for the report.
        /// </summary>
        /// <param name="start">When the scan started.</param>
        /// <param name="end">When the scan ended.</param>
        /// <param name="duration">How long the scan took.</param>
        /// <returns>Generated HTML.</returns>
        private static async Task<string> GenerateHtmlReport(
            DateTimeOffset start,
            DateTimeOffset end,
            TimeSpan duration)
        {
            // Get templates.
            var html = await File.ReadAllTextAsync(
                    Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "TemplateReport.html"));

            var templateRequest = await File.ReadAllTextAsync(
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "TemplateRequest.html"));

            // BaseUri.
            html = html.Replace(
                "{{BaseUri}}",
                BaseUri?.ToString());

            // AppVersion.
            html = html.Replace(
                "{{AppVersion}}",
                GetVersion());

            // Metadata.
            html = html
                .Replace(
                    "{{ScanStarted}}",
                    start.ToString("yyyy-MM-dd HH:mm:ss"))
                .Replace(
                    "{{ScanEnded}}",
                    end.ToString("yyyy-MM-dd HH:mm:ss"))
                .Replace(
                    "{{ScanDuration}}",
                    duration.ToString());

            // Requests.
            var requests = new List<string>();

            foreach (var entry in QueueEntries)
            {
                // Clone the template.
                var request = templateRequest;

                // Uri.
                request = request
                    .Replace(
                        "{{Uri}}",
                        entry.Uri.ToString());

                // ReuqestTime.
                var rl = entry.RequestLength ??
                         new TimeSpan(0);

                var rtf =
                    (rl.Hours > 0 ? rl.Hours + "h " : string.Empty) +
                    (rl.Minutes > 0 ? rl.Minutes + "m " : string.Empty) +
                    (rl.Seconds > 0 ? rl.Seconds + "s " : string.Empty) +
                    (rl.Milliseconds > 0 ? rl.Milliseconds + "ms" : string.Empty);

                request = request
                    .Replace(
                        "{{ReuqestTimeToolTip}}",
                        rl.ToString())
                    .Replace(
                        "{{ReuqestTimeFormatted}}",
                        rtf.Trim());

                // StatusCode.
                var statusCode = entry.StatusCode.HasValue
                    ? ((int) entry.StatusCode.Value).ToString()
                    : "-";

                var statusCodeCssClass = "red";

                if (statusCode.StartsWith("2"))
                {
                    statusCodeCssClass = "green";
                }
                else if (statusCode.StartsWith("3"))
                {
                    statusCodeCssClass = "yellow";
                }

                request = request
                    .Replace(
                        "{{StatusCode}}",
                        statusCode)
                    .Replace(
                        "{{StatusDescription}}",
                        entry.StatusDescription)
                    .Replace(
                        "{{StatusCodeCssClass}}",
                        statusCodeCssClass);

                // ChecksStatus.
                if (entry.FailureReasons.Count > 0)
                {
                    request = request
                        .Replace(
                            "{{ChecksStatusToolTip}}",
                            "Request Failed Some or All Checks")
                        .Replace(
                            "{{ChecksStatusCssClass}}",
                            "red")
                        .Replace(
                            "{{ChecksStatusShorthand}}",
                            "Failed");
                }
                else
                {
                    request = request
                        .Replace(
                            "{{ChecksStatusToolTip}}",
                            "Request Passed Checks")
                        .Replace(
                            "{{ChecksStatusCssClass}}",
                            "green")
                        .Replace(
                            "{{ChecksStatusShorthand}}",
                            "Passed");
                }

                // Tools.

                // Done.
                requests.Add(request);
            }

            html = html.Replace(
                "{{Requests}}",
                string.Join("\r\n", requests));

            // Done.
            return html;
        }

        /// <summary>
        /// Get application version.
        /// </summary>
        /// <returns>Application version.</returns>
        private static string GetVersion()
        {
            return Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString()
                ?? string.Empty;
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

                // Mark the request as finished.
                entry.RequestFinished = DateTimeOffset.Now;

                // Keep the status code.
                entry.StatusCode = response.StatusCode;
                entry.StatusDescription = ReasonPhrases.GetReasonPhrase((int) response.StatusCode);

                // Headers.
                entry.Headers = response.Headers
                    .ToDictionary(n => n.Key.ToLower(),
                                  n => string.Join(" ", n.Value));

                foreach (var item in response.Content.Headers)
                {
                    entry.Headers[item.Key.ToLower()] = string.Join(" ", item.Value);
                }

                // Analyze headers?
                if (VerifyHeaders.Count > 0)
                {
                    AnalyzeHeaders(entry);
                }

                // Analyze HTML and extract new links to scan.
                ExtractLinksFromHtml(entry, ms.ToArray());
            }
            catch (Exception ex)
            {
                entry.FailureReasons.Add(ex.Message);
            }

            // Status code.
            var statusCode = entry.StatusCode.HasValue
                ? ((int) entry.StatusCode).ToString()
                : "---";

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
        /// Show the apps usage info and parameters.
        /// </summary>
        private static void ShowAppUsageInfo()
        {
            ConsoleEx.WriteObjects(
                "SiteCheck v",
                GetVersion(),
                Environment.NewLine,
                Environment.NewLine);

            ConsoleEx.WriteObjects(
                "Usage: sitecheck <url> [options]",
                Environment.NewLine,
                Environment.NewLine);

            ConsoleEx.WriteObjects(
                "  -t <milliseconds>    Set the timeout of the HTTP requests. Defaults to 10 seconds.",
                Environment.NewLine,
                "  -h <header>          Verify the existence of a header.",
                Environment.NewLine,
                "  -h <header>:<value>  Verify the header and its value. Value can be a regex.",
                Environment.NewLine,
                "  -p <path>            Give path where to write the report. Defaults to working directory.",
                Environment.NewLine,
                Environment.NewLine);
        }

        /// <summary>
        /// Generate a report based on the queue and other metadata.
        /// </summary>
        /// <param name="start">When the scan started.</param>
        /// <param name="end">When the scan ended.</param>
        /// <param name="duration">How long the scan took.</param>
        private static async Task WriteReport(
            DateTimeOffset start,
            DateTimeOffset end,
            TimeSpan duration)
        {
            try
            {
                var dt = DateTime.Now;

                // Compile HTML.
                var html = await GenerateHtmlReport(
                    start,
                    end,
                    duration);

                // Write to disk.
                var path = Path.Combine(
                    ReportPath,
                    $"report-{dt:yyyy-MM-dd-HH-mm-ss}-{BaseUri?.Host}.html");

                await File.WriteAllTextAsync(
                    path,
                    html);

                path = Path.Combine(
                    ReportPath,
                    $"report-{dt:yyyy-MM-dd-HH-mm-ss}-{BaseUri?.Host}.json");

                await File.WriteAllTextAsync(
                    path,
                    JsonSerializer.Serialize(
                        new
                        {
                            meta = new
                            {
                                start,
                                end,
                                duration
                            },
                            config = new
                            {
                                BaseUri,
                                VerifyHeaders
                            },
                            uris = QueueEntries
                        },
                        new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                            WriteIndented = true
                        }));

                ConsoleEx.WriteObjects(
                    "Wrote reports to ",
                    ConsoleColor.Black,
                    ReportPath,
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