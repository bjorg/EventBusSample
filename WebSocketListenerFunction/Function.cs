using System;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Demo.EventBus.Records;
using LambdaSharp;
using LambdaSharp.ApiGateway;

namespace Demo.EventBus.WebSocketListenerFunction {

    public sealed class Function : ALambdaApiGatewayFunction {

        //--- Fields ---
        private IAmazonSimpleNotificationService _snsClient;
        private DataTable _dataTable;
        private string _eventTopicArn;
        private string _broadcastFunctionArn;

        //--- Methods ---
        public override async Task InitializeAsync(LambdaConfig config) {

            // read configuration settings
            var dataTableName = config.ReadDynamoDBTableName("DataTable");
            _eventTopicArn = config.ReadText("EventTopic");
            _broadcastFunctionArn = config.ReadText("EventBroadcastFunction");

            // initialize AWS clients
            _snsClient = new AmazonSimpleNotificationServiceClient();
            _dataTable = new DataTable(dataTableName, new AmazonDynamoDBClient());
        }

        // [Route("$connect")]
        public async Task OpenConnectionAsync(APIGatewayProxyRequest request) {
            LogInfo($"Connected: {request.RequestContext.ConnectionId}");

            // subscribe websocket to SNS topic notifications
            var subscriptionArn = (await _snsClient.SubscribeAsync(new SubscribeRequest {
                Protocol = "lambda",
                Endpoint = _broadcastFunctionArn,
                TopicArn = _eventTopicArn
            })).SubscriptionArn;

            // store connection record
            try {
                await _dataTable.CreateConnectionAsync(new ConnectionRecord {
                    ConnectionId = request.RequestContext.ConnectionId,
                    SubscriptionArn = subscriptionArn
                });
            } catch {

                // safely delete subscription
                try {
                    await _snsClient.UnsubscribeAsync(new UnsubscribeRequest {
                        SubscriptionArn = subscriptionArn
                    });
                } catch(Exception e) {
                    LogError(e, "unable to delete SNS subscription '{0}' for topic '{1}'", subscriptionArn, _eventTopicArn);
                }
                throw;
            }
        }

        // [Route("$disconnect")]
        public async Task CloseConnectionAsync(APIGatewayProxyRequest request) {
            LogInfo($"Disconnected: {request.RequestContext.ConnectionId}");

            // retrieve websocket connection record
            var connection = await _dataTable.GetConnectionAsync(request.RequestContext.ConnectionId);
            if(connection == null) {
                LogInfo("Connection was removed");
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

                    // fetch all filters associated with SNS topic subscription
                    var filters = await _dataTable.GetSubscriptionFiltersAsync(connection.SubscriptionArn);

                    // delete filters
                    await _dataTable.DeleteAllFiltersAsync(filters);
                }),

                // delete websocket connection record
                _dataTable.DeleteConnectionAsync(connection.ConnectionId)
            });
        }

        // [Route("subscribe")]
        public async Task SubscribeRequestAsync(SubscribeFilterRequest request) {
            LogInfo($"Subscribe request from: {CurrentRequest.RequestContext.ConnectionId}");

            // retrieve websocket connection record
            var connection = await _dataTable.GetConnectionAsync(CurrentRequest.RequestContext.ConnectionId);
            if(connection == null) {
                LogInfo("Connection was removed");
                return;
            }

            // TODO: validate filter request

            // create or update event filter
            await _dataTable.CreateOrUpdateFilterAsync(new FilterRecord {
                FilterId = request.FilterId,
                FilterExpression = request.Filter,
                ConnectionId = connection.ConnectionId,
                SubscriptionArn = connection.SubscriptionArn
            });
        }

        // [Route("unsubscribe")]
        public async Task UnsubscribeRequestAsync(UnsubscribeFilterRequest request) {
            LogInfo($"Unsubscribe request from: {CurrentRequest.RequestContext.ConnectionId}");

            // delete event filter
            await _dataTable.DeleteFilterAsync(request.FilterId);
        }
    }
}