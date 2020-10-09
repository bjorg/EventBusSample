# EventBus for Blazor WebAssembly apps

**TODO:**
[ ] Use Lambda destinations to chain HTTP API receiver function to broadcast function
[ ] Allow optional validation function between receiver function and broadcast function
[ ] Add JWT authorizer for WebSocket connection
[ ] Add "Send" action to send a CloudWatch event

## Sample WebSocket Actions

### Announce client

```json
{
    "Action": "Hello"
}
```

### Subscribe to a CloudWatch event pattern

```json
{
    "Action": "Subscribe",
    "Rule": "abc",
    "Pattern": "xyz"
}
```