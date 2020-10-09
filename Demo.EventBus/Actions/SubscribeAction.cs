namespace Demo.EventBus.WebSocketListenerFunction.Actions {

    public sealed class SubscribeAction : ARuleAction {

        //--- Constructors ---
        public SubscribeAction() => Action = "Subscribe";

        //--- Properties ---
        public string Pattern { get; set; }
    }
}