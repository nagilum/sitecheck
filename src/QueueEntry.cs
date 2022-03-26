using System.Net;

namespace SiteCheck
{
    public class QueueEntry
    {
        /// <summary>
        /// URL to scan.
        /// </summary>
        public Uri Uri { get; set; }

        /// <summary>
        /// When was the request created.
        /// </summary>
        public DateTimeOffset? RequestCreated { get; set; }

        /// <summary>
        /// When did the request finish.
        /// </summary>
        public DateTimeOffset? RequestFinished { get; set; }

        /// <summary>
        /// How long did the request last.
        /// </summary>
        public TimeSpan? RequestLength
        {
            get
            {
                if (!this.RequestCreated.HasValue ||
                    !this.RequestFinished.HasValue)
                {
                    return null;
                }

                return this.RequestFinished.Value -
                       this.RequestCreated.Value;
            }
        }

        /// <summary>
        /// HTTP response status code.
        /// </summary>
        public HttpStatusCode? StatusCode { get; set; }

        /// <summary>
        /// HTTP response status description.
        /// </summary>
        public string StatusDescription { get; set; } = null!;

        /// <summary>
        /// All headers from the request.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new();

        /// <summary>
        /// All headers that was verified.
        /// </summary>
        public Dictionary<string, string?> HeadersVerified { get; set; } = new();

        /// <summary>
        /// All headers that could not be verified.
        /// </summary>
        public Dictionary<string, string?> HeadersNotVerified { get; set; } = new();

        /// <summary>
        /// List of reasons why this request failed some or all of the tests.
        /// </summary>
        public List<string> FailureReasons { get; set; } = new();

        /// <summary>
        /// Create new queue entry.
        /// </summary>
        /// <param name="uri">URL to scan.</param>
        public QueueEntry(Uri uri)
        {
            this.Uri = uri;
        }
    }
}