namespace Demo.EventBus.EventBroadcastFunction {

    public sealed class TopicSubscriptionPayload {

        //--- Properties ---
        public string Type { get; set; }
        public string TopicArn { get; set; }
        public string SubscribeURL { get; set; }
    }
}
