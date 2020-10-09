namespace Demo.EventBus.WebSocketListenerFunction.Actions {

    public sealed class AcknowledgeAction : ARuleAction {

        //--- Constructors ---
        public AcknowledgeAction() => Action = "Ack";

        //--- Properties ---
        public string Status { get; set; }
    }
}