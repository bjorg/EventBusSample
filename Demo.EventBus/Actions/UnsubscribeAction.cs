namespace Demo.EventBus.WebSocketListenerFunction.Actions {

    public sealed class UnsubscribeAction : ARuleAction {

        //--- Constructors ---
        public UnsubscribeAction() => Action = "Unsubscribe";
    }
}