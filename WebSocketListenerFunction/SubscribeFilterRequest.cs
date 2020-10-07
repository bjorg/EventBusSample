namespace Demo.EventBus.WebSocketListenerFunction {

    public sealed class SubscribeFilterRequest : AMessageRequest {

        //--- Constructors ---
        public SubscribeFilterRequest() => Action = "subscribe";

        //--- Properties ---
        public string FilterId { get; set; }
        public string Filter { get; set; }
    }
}