using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using Amazon.DynamoDBv2;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.SNSEvents;
using Amazon.Runtime;
using Demo.EventBus.Actions;
using LambdaSharp;

namespace Demo.EventBus.BroadcastFunction {

    public sealed class Function : ALambdaFunction<APIGatewayHttpApiV2ProxyRequest, APIGatewayHttpApiV2ProxyResponse> {

        //--- Fields ---
        private IAmazonApiGatewayManagementApi _amaClient;
        private DataTable _dataTable;
        private string _eventTopicArn;
        private string _keepAliveRuleArn;
        private string _httpApiToken;

        //--- Methods ---
        public override async Task InitializeAsync(LambdaConfig config) {

            // read configuration settings
            var dataTableName = config.ReadDynamoDBTableName("DataTable");
            var webSocketUrl = config.ReadText("Module::WebSocket::Url");
            _eventTopicArn = config.ReadText("EventTopic");
            _keepAliveRuleArn = config.ReadText("KeepAliveRuleArn");
            _httpApiToken = config.ReadText("HttpApiInvocationToken");

            // initialize AWS clients
            _amaClient = new AmazonApiGatewayManagementApiClient(new AmazonApiGatewayManagementApiConfig {
                ServiceURL = webSocketUrl
            });
            _dataTable = new DataTable(dataTableName, new AmazonDynamoDBClient());
        }

        public override async Task<APIGatewayHttpApiV2ProxyResponse> ProcessMessageAsync(APIGatewayHttpApiV2ProxyRequest request) {
            LogInfo($"Message received at {request.RequestContext.Http.Method}:{request.RawPath}?{request.RawQueryString}");

            // validate invocation method
            if(request.RequestContext.Http.Method != "POST") {
                LogInfo("Unsupported request method {0}", request.RequestContext.Http.Method);
                return BadRequest();
            }

            // validate request token
            if(
                !request.QueryStringParameters.TryGetValue("token", out var token)
                || (token != _httpApiToken)
            ) {
                LogInfo("Missing or invalid request token");
                return BadRequest();
            }

            // validate request websocket
            if(
                !request.QueryStringParameters.TryGetValue("ws", out var connectionId)
                || string.IsNullOrEmpty(connectionId)
            ) {
                LogInfo("Invalid websocket connection id");
                return BadRequest();
            }

            // check if request is a subscription confirmation
            var topicSubscription = LambdaSerializer.Deserialize<TopicSubscriptionPayload>(request.Body);
            if(topicSubscription.Type == "SubscriptionConfirmation") {

                // confirm it's for the expected topic ARN
                if(topicSubscription.TopicArn != _eventTopicArn) {
                    LogWarn("Wrong Topic ARN for subscription confirmation (Expected: {0}, Received: {1})", _eventTopicArn, topicSubscription.TopicArn);
                    return BadRequest();
                }

                // confirm subscription
                await HttpClient.GetAsync(topicSubscription.SubscribeURL);

                // send welcome action to websocket connection
                await SendMessageToConnection(new WelcomeAction(), connectionId);
                return Success("Confirmed");
            }

            // validate SNS message
            var snsMessage = LambdaSerializer.Deserialize<SNSEvent.SNSMessage>(request.Body);
            if(snsMessage.Message == null) {
                LogWarn("Invalid SNS message received: {0}", request.Body);
                return BadRequest();
            }

            // validate CloudWatch event
            var cloudWatchEvent = LambdaSerializer.Deserialize<CloudWatchEventPayload>(snsMessage.Message);
            if(
                (cloudWatchEvent.Source == null)
                || (cloudWatchEvent.DetailType == null)
                || (cloudWatchEvent.Resources == null)
            ) {
                LogInfo("Invalid CloudWatch event received: {0}", snsMessage.Message);
                return BadRequest();
            }

            // check if the keep-alive event was received
            if(
                (cloudWatchEvent.Source == "aws.events")
                && (cloudWatchEvent.DetailType == "Scheduled Event")
                && (cloudWatchEvent.Resources.Count == 1)
                && (cloudWatchEvent.Resources[0] == _keepAliveRuleArn)
            ) {

                // send keep-alive action to websocket connection
                await SendMessageToConnection(new KeepAliveAction(), connectionId);
                return Success("Ok");
            }

            // TODO: async invoke rule-match lambda

            // determine what rules are matching
            var rules = await _dataTable.GetConnectionRulesAsync(connectionId);
            var matchedRules = rules
                .Where(rule => {

                    // TODO: check which rules, if any, apply to message
                    return true;
                }).Select(rule => rule.Rule)
                .ToList();
            if(matchedRules.Any()) {
                await SendMessageToConnection(
                    new EventAction {
                        Rules = matchedRules,
                        Source = cloudWatchEvent.Source,
                        Type = cloudWatchEvent.DetailType,
                        Event = snsMessage.Message
                    },
                    connectionId
                );
            }
            return Success("Ok");

            // local functions
            APIGatewayHttpApiV2ProxyResponse Success(string message)
                => new APIGatewayHttpApiV2ProxyResponse {
                    Body = message,
                    Headers = new Dictionary<string, string> {
                        ["Content-Type"] = "text/plain"
                    },
                    StatusCode = (int)HttpStatusCode.OK
                };

            APIGatewayHttpApiV2ProxyResponse BadRequest()
                => new APIGatewayHttpApiV2ProxyResponse {
                    Body = "Bad Request",
                    Headers = new Dictionary<string, string> {
                        ["Content-Type"] = "text/plain"
                    },
                    StatusCode = (int)HttpStatusCode.BadRequest
                };
        }

        private Task SendMessageToConnection(AnAction action, string connectionId)
            => SendMessageToConnection(Encoding.UTF8.GetBytes(LambdaSerializer.Serialize<object>(action)), connectionId);

        private async Task SendMessageToConnection(byte[] messageBytes, string connectionId) {

            // attempt to send serialized message to connection
            try {
                LogInfo($"Post to connection: {connectionId}");
                await _amaClient.PostToConnectionAsync(new PostToConnectionRequest {
                    ConnectionId = connectionId,
                    Data = new MemoryStream(messageBytes)
                });
            } catch(AmazonServiceException e) when(e.StatusCode == System.Net.HttpStatusCode.Gone) {

                // HTTP Gone status code indicates the connection has been closed; nothing to do
            } catch(Exception e) {
                LogErrorAsWarning(e, "PostToConnectionAsync() failed on connection {0}", connectionId);
            }
        }
    }
}