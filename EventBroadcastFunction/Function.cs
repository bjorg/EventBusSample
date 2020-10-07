using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using LambdaSharp;
using LambdaSharp.SimpleNotificationService;

namespace Demo.EventBus.EventBroadcastFunction {

    public class Message { }

    public sealed class Function : ALambdaTopicFunction<Message> {

        //--- Fields ---
        private IAmazonApiGatewayManagementApi _amaClient;
        private DataTable _dataTable;

        //--- Methods ---
        public override async Task InitializeAsync(LambdaConfig config) {

            // read configuration settings
            var dataTableName = config.ReadDynamoDBTableName("DataTable");
            var webSocketUrl = config.ReadText("Module::WebSocket::Url");

            // initialize AWS clients
            _amaClient = new AmazonApiGatewayManagementApiClient(new AmazonApiGatewayManagementApiConfig {
                ServiceURL = webSocketUrl
            });
            _dataTable = new DataTable(dataTableName, new AmazonDynamoDBClient());
        }

        public override async Task ProcessMessageAsync(Message message) {
            LogInfo($"Message received from {CurrentRecord.UnsubscribeUrl}");

            var subscriptionArn = HttpUtility.ParseQueryString(CurrentRecord.UnsubscribeUrl).Get("SubscriptionArn");
            var filters = await _dataTable.GetSubscriptionFiltersAsync(subscriptionArn);

            var messageBytes = Encoding.UTF8.GetBytes(CurrentRecord.Message);
            await Task.WhenAll(filters.Select(filter => {

                // TODO: only send message if the filter allows it
                LogInfo($"sending message to connection '{filter.ConnectionId}' connections");
                return SendMessageToConnection(messageBytes, filter.ConnectionId);
            }));
        }

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
