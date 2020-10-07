namespace Demo.EventBus.WebSocketListenerFunction {
    public sealed class UnsubscribeFilterRequest : AMessageRequest {

        //--- Constructors ---
        public UnsubscribeFilterRequest() => Action = "unsubscribe";

        //--- Properties ---
        public string FilterId { get; set; }
    }
}