namespace Demo.EventBus.WebSocketListenerFunction.Actions {

    public sealed class EventAction : AnAction {

        //--- Constructors ---
        public EventAction() => Action = "Event";

        //--- Properties ---
        public string Source { get; set; }
        public string Type { get; set; }
        public string Event { get; set; }
    }
}