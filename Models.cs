using Newtonsoft.Json;
using System.Collections.Generic;

namespace BR_SteamPress
{
    internal sealed class SteamGame
    {
        internal uint AppId { get; set; }
        internal string Name { get; set; }
        internal long LastUpdated { get; set; }
    }

    internal sealed class NewsStory
    {
        internal uint AppId { get; set; }
        internal string GameName { get; set; }
        internal string Gid { get; set; }
        internal string Title { get; set; }
        internal string Author { get; set; }
        internal string FeedLabel { get; set; }
        internal string Url { get; set; }
        internal long PublishedUnix { get; set; }
        internal string Body { get; set; }
        internal string ImageUrl { get; set; }
        internal byte[] ImageBytes { get; set; }
    }

    internal sealed class SteamNewsEnvelope
    {
        [JsonProperty("appnews")]
        public SteamNewsResponse AppNews { get; set; }
    }

    internal sealed class SteamNewsResponse
    {
        [JsonProperty("appid")]
        public uint AppId { get; set; }

        [JsonProperty("newsitems")]
        public List<SteamNewsItem> NewsItems { get; set; }
    }

    internal sealed class SteamNewsItem
    {
        [JsonProperty("gid")]
        public string Gid { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("contents")]
        public string Contents { get; set; }

        [JsonProperty("feedlabel")]
        public string FeedLabel { get; set; }

        [JsonProperty("date")]
        public long Date { get; set; }
    }
}
