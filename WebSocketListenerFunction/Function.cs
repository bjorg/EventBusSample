using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Demo.EventBus.Records;
using Demo.EventBus.WebSocketListenerFunction.Actions;
using LambdaSharp;
using LambdaSharp.ApiGateway;

namespace Demo.EventBus.WebSocketListenerFunction {

    public sealed class Function : ALambdaApiGatewayFunction {

        //--- Class Methods ---
        private static string ComputeMD5Hash(string text) {
            using var md5 = MD5.Create();
            return string.Concat(md5.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(x => x.ToString("X2")));
        }

        //--- Fields ---
        private IAmazonSimpleNotificationService _snsClient;
        private DataTable _dataTable;
        private string _eventTopicArn;
        private string _broadcastApiUrl;
        private string _httpApiToken;

        //--- Methods ---
        public override async Task InitializeAsync(LambdaConfig config) {

            // read configuration settings
            var dataTableName = config.ReadDynamoDBTableName("DataTable");
            _eventTopicArn = config.ReadText("EventTopic");
            _broadcastApiUrl = config.ReadText("EventBroadcastApiUrl");
            _httpApiToken = config.ReadText("HttpApiInvocationToken");

            // initialize AWS clients
            _snsClient = new AmazonSimpleNotificationServiceClient();
            _dataTable = new DataTable(dataTableName, new AmazonDynamoDBClient());
        }

        // [Route("$connect")]
        public async Task<APIGatewayProxyResponse> OpenConnectionAsync(APIGatewayProxyRequest request) {
            LogInfo($"Connected: {request.RequestContext.ConnectionId}");

            // verify presence of application ID query parameter
            string appId = null;
            if(
                !(request.QueryStringParameters?.TryGetValue("app", out appId) ?? false)
                || !Guid.TryParse(appId, out _)
            ) {

                // reject connection request
                return new APIGatewayProxyResponse {
                    StatusCode = (int)HttpStatusCode.BadRequest
                };
            }

            // create new connection record
            await _dataTable.CreateConnectionAsync(new ConnectionRecord {
                ConnectionId = request.RequestContext.ConnectionId,
                ApplicationId = appId,
                Bearer = request.RequestContext.Authorizer?.Claims
            });
            return new APIGatewayProxyResponse {
                StatusCode = (int)HttpStatusCode.OK
            };
        }

        // [Route("$disconnect")]
        public async Task CloseConnectionAsync(APIGatewayProxyRequest request) {
            LogInfo($"Disconnected: {request.RequestContext.ConnectionId}");

            // retrieve websocket connection record
            var connection = await _dataTable.GetConnectionAsync(request.RequestContext.ConnectionId);
            if(connection == null) {
                LogInfo("Connection was already removed");
                return;
            }

            // clean-up resources associated with websocket connection
            await Task.WhenAll(new Task[] {

                // unsubscribe from SNS topic notifications
                Task.Run(async () => {
                    if(connection.SubscriptionArn != null) {
                        await _snsClient.UnsubscribeAsync(new UnsubscribeRequest {
                            SubscriptionArn = connection.SubscriptionArn
                        });
                    }
                }),

                // delete all event rules for this websocket connection
                Task.Run(async () => {

                    // fetch all rules associated with websocket connection
                    var rules = await _dataTable.GetConnectionRulesAsync(request.RequestContext.ConnectionId);

                    // delete rules
                    await _dataTable.DeleteAllRulesAsync(rules);
                }),

                // delete websocket connection record
                _dataTable.DeleteConnectionAsync(connection.ConnectionId)
            });
        }

        // [Route("Hello")]
        public async Task HelloAsync(HelloAction action) {
            var connectionId = CurrentRequest.RequestContext.ConnectionId;
            LogInfo($"Hello: {connectionId}");

            // retrieve websocket connection record
            var connection = await _dataTable.GetConnectionAsync(connectionId);
            if(connection == null) {
                LogInfo("Connection was removed");
                return;
            }

            // subscribe websocket to SNS topic notifications
            connection.SubscriptionArn = (await _snsClient.SubscribeAsync(new SubscribeRequest {
                Protocol = "https",
                Endpoint = $"{_broadcastApiUrl}?ws={connectionId}&token={_httpApiToken}",
                ReturnSubscriptionArn = true,
                TopicArn = _eventTopicArn
            })).SubscriptionArn;

            // update connection record
            await _dataTable.UpdateConnectionAsync(connection);
        }

        // [Route("Subscribe")]
        public async Task<AcknowledgeAction> SubscribeAsync(SubscribeAction action) {
            var connectionId = CurrentRequest.RequestContext.ConnectionId;
            LogInfo($"Subscribe request from: {connectionId}");

            // validate request
            if(string.IsNullOrEmpty(action.Rule)) {
                return new AcknowledgeAction {
                    Status = "Error",
                    Message = "Missing or invalid rule name"
                };
            }

            // TODO: validate event pattern

            // retrieve websocket connection record
            var connection = await _dataTable.GetConnectionAsync(connectionId);
            if(connection == null) {
                LogInfo("Connection was removed");
                return new AcknowledgeAction {
                    Rule = action.Rule,
                    Status = "Gone"
                };
            }

            // create or update event rule
            await _dataTable.CreateOrUpdateRuleAsync(new RuleRecord {
                Rule = action.Rule,
                Pattern = action.Pattern,
                ConnectionId = connection.ConnectionId
            });
            return new AcknowledgeAction {
                Rule = action.Rule,
                Status = "Ok"
            };
        }

        // [Route("Unsubscribe")]
        public async Task<AcknowledgeAction> UnsubscribeAsync(UnsubscribeAction action) {
            var connectionId = CurrentRequest.RequestContext.ConnectionId;
            LogInfo($"Unsubscribe request from: {connectionId}");
            if(action.Rule != null) {

                // delete event rule
                await _dataTable.DeleteRuleAsync(connectionId, action.Rule);
            }
            return new AcknowledgeAction {
                Rule = action.Rule,
                Status = "Ok"
            };
        }
    }
}