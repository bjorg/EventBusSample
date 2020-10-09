# EventBus for Blazor WebAssembly apps

**TODO:**
* [ ] Use Lambda destinations to chain HTTP API receiver function to broadcast function
* [ ] Allow optional validation function between receiver function and broadcast function
* [ ] Add JWT authorizer for WebSocket connection
* [ ] Add "Send" action to send a CloudWatch event

## Types of communication
* Send message to a specific WebSocket
* Send message to all WebSocket connections belonging a certain user
* Send message to all WebSocket connections for the application

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

### Sample CloudWatch event

```json
{
  "version": "0",
  "id": "ab26e799-0b3b-637c-2f96-6a428401fdf3",
  "detail-type": "MyFirstEvent",
  "source": "LambdaSharp.Sample",
  "account": "xyz",
  "time": "2020-04-18T01:15:39Z",
  "region": "us-west-2",
  "resources": [
    "lambdasharp:stack:SteveBvNext-Sample-Event",
    "lambdasharp:module:Sample.Event",
    "lambdasharp:tier:SteveBvNext"
  ],
  "detail": {
    "Message": "hello world!"
  }
}
```