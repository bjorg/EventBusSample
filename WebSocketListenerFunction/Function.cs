using System;
using System.Linq;
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

        //--- Methods ---
        public override async Task InitializeAsync(LambdaConfig config) {

            // read configuration settings
            var dataTableName = config.ReadDynamoDBTableName("DataTable");
            _eventTopicArn = config.ReadText("EventTopic");
            _broadcastApiUrl = config.ReadText("EventBroadcastApiUrl");

            // initialize AWS clients
            _snsClient = new AmazonSimpleNotificationServiceClient();
            _dataTable = new DataTable(dataTableName, new AmazonDynamoDBClient());
        }

        // [Route("$connect")]
        public async Task OpenConnectionAsync(APIGatewayProxyRequest request) {
            LogInfo($"Connected: {request.RequestContext.ConnectionId}");
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
                _snsClient.UnsubscribeAsync(new UnsubscribeRequest {
                    SubscriptionArn = connection.SubscriptionArn
                }),

                // delete all SNS topic subscription filters for this websocket connection
                Task.Run(async () => {

                    // fetch all filters associated with websocket connection
                    var filters = await _dataTable.GetConnectionFiltersAsync(request.RequestContext.ConnectionId);

                    // delete filters
                    await _dataTable.DeleteAllFiltersAsync(filters);
                }),

                // delete websocket connection record
                _dataTable.DeleteConnectionAsync(connection.ConnectionId)
            });
        }

        // [Route("Hello")]
        public async Task HelloAsync(HelloAction action) {
            var connectionId = CurrentRequest.RequestContext.ConnectionId;
            LogInfo($"Init: {connectionId}");

            // create new connection record (fails if record already exists)
            var connectionRecord = new ConnectionRecord {
                ConnectionId = connectionId
            };
            await _dataTable.CreateConnectionAsync(connectionRecord);

            // subscribe websocket to SNS topic notifications
            connectionRecord.SubscriptionArn = (await _snsClient.SubscribeAsync(new SubscribeRequest {
                Protocol = "https",
                Endpoint = $"{_broadcastApiUrl}/{connectionId}",
                ReturnSubscriptionArn = true,
                TopicArn = _eventTopicArn
            })).SubscriptionArn;

            // update connection record
            await _dataTable.UpdateConnectionAsync(connectionRecord);
        }

        // [Route("Subscribe")]
        public async Task<AcknowledgeAction> SubscribeAsync(SubscribeAction action) {
            var connectionId = CurrentRequest.RequestContext.ConnectionId;
            LogInfo($"Subscribe request from: {connectionId}");

            // validate request
            if(action.Rule == null) {
                return new AcknowledgeAction {
                    Status = "BadRequest"
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

            // create or update event filter
            await _dataTable.CreateOrUpdateFilterAsync(new FilterRecord {
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

                // delete event filter
                await _dataTable.DeleteFilterAsync(connectionId, action.Rule);
            }
            return new AcknowledgeAction {
                Rule = action.Rule,
                Status = "Ok"
            };
        }
    }
}