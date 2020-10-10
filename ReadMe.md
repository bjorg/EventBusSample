# EventBus for Blazor WebAssembly apps

**TODO:**
* [ ] Use Lambda destinations to chain HTTP API receiver function to broadcast function
* [ ] Allow optional validation function between receiver function and broadcast function
* [ ] Add JWT authorizer for WebSocket connection
* [ ] Add "Send" action to send a CloudWatch event
* [ ] Ability to send message to a specific WebSocket
* [ ] Ability to send message to all WebSocket connections belonging a certain user
* [ ] Ability to send message to all WebSocket connections for the application


## Sample WebSocket Actions

### Announce client

After the WebSocket connection is opened, the client must announce itself with a _Hello_ action.

**Request:**
```json
{
    "Action": "Hello"
}
```

**Response:**
```json
{
    "Action": "Welcome"
}
```


### Subscribe Action

**Request:**
```json
{
    "Action": "Subscribe",
    "Rule": "abc",
    "Pattern": "xyz"
}
```

**Response: SUCCESS**
```json
{
  "Action": "Ack",
  "Rule": "abc",
  "Status": "Ok"
}
```

**Response: ERROR**
```json
{
  "Action": "Ack",
  "Rule": "abc",
  "Status": "Error",
  "Message": "Error diagnostic message"
}
```

### Unsubscribe Action

**Request:**
```json
{
    "Action": "Unsubscribe",
    "Rule": "abc"
}
```

**Response: SUCCESS**
```json
{
  "Action": "Ack",
  "Rule": "abc",
  "Status": "Ok"
}
```

**Response: ERROR**
```json
{
  "Action": "Ack",
  "Rule": "abc",
  "Status": "Error",
  "Message": "Error diagnostic message"
}
```

### Event Notification

**Response:**
```json
{
    "Action": "Event",
    "Rules": [ "123", "456" ],
    "Source": "Event source",
    "Type": "Event detail type",
    "Event": "..."
}
```


### Keep-Alive Notification

Periodically, the server will send a keep-alive message on the WebSocket connection to prevent it from closing due to inactivity.

**Response:**
```json
{
    "Action": "KeepAlive"
}
```


### Sample CloudWatch Event

```json
{
    "Action": "Event",
    "Source": "LambdaSharp.Sample",
    "Type": "MyFirstEvent",
    "Event": "{\"version\":\"0\",\"id\":\"ab26e799-0b3b-637c-2f96-6a428401fdf3\",\"detail-type\":\"MyFirstEvent\",\"source\":\"LambdaSharp.Sample\",\"account\":\"xyz\",\"time\":\"2020-04-18T01:15:39Z\",\"region\":\"us-west-2\",\"resources\":[\"lambdasharp:stack:SteveBvNext-Sample-Event\",\"lambdasharp:module:Sample.Event\",\"lambdasharp:tier:SteveBvNext\"],\"detail\":{\"Message\":\"hello world!\"}}"
}
```

#### CloudWatch Event Payload

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