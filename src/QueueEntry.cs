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
        /// Create new queue entry.
        /// </summary>
        /// <param name="uri">URL to scan.</param>
        public QueueEntry(Uri uri)
        {
            this.Uri = uri;
        }
    }
}