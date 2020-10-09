using System.Collections.Generic;
using Newtonsoft.Json;

namespace Demo.EventBus.EventBroadcastFunction {

    public sealed class CloudWatchEventPayload {

        //--- Properties ---

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("detail-type")]
        public string DetailType { get; set; }

        [JsonProperty("resources")]
        public List<string> Resources { get; set; }
    }
}
