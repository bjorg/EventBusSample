using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using Amazon.DynamoDBv2;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.SNSEvents;
using Amazon.Runtime;
using LambdaSharp;

namespace Demo.EventBus.EventBroadcastFunction {

    public sealed class TopicSubscriptionPayload {

        //--- Properties ---
        public string Type { get; set; }
        public string TopicArn { get; set; }
        public string SubscribeURL { get; set; }
    }

    public sealed class Function : ALambdaFunction<APIGatewayHttpApiV2ProxyRequest, APIGatewayHttpApiV2ProxyResponse> {

        //--- Fields ---
        private IAmazonApiGatewayManagementApi _amaClient;
        private DataTable _dataTable;
        private string _eventTopicArn;

        //--- Methods ---
        public override async Task InitializeAsync(LambdaConfig config) {

            // read configuration settings
            var dataTableName = config.ReadDynamoDBTableName("DataTable");
            var webSocketUrl = config.ReadText("Module::WebSocket::Url");
            _eventTopicArn = config.ReadText("EventTopic");

            // initialize AWS clients
            _amaClient = new AmazonApiGatewayManagementApiClient(new AmazonApiGatewayManagementApiConfig {
                ServiceURL = webSocketUrl
            });
            _dataTable = new DataTable(dataTableName, new AmazonDynamoDBClient());
        }

        public override async Task<APIGatewayHttpApiV2ProxyResponse> ProcessMessageAsync(APIGatewayHttpApiV2ProxyRequest request) {
            LogInfo($"Message received from {request.RawPath}");
            if(
                (request.RawPath.Length <= 1)
                || !request.RawPath.StartsWith("/", StringComparison.Ordinal)
                || (request.RequestContext.Http.Method != "POST")
            ) {
                return BadRequest();
            }
            var connectionId = request.RawPath.Substring(1);

            // check if request is a for a subscription
            var topicSubscription = LambdaSerializer.Deserialize<TopicSubscriptionPayload>(request.Body);
            if(topicSubscription.Type == "SubscriptionConfirmation") {

                // confirm it's for the expected topic ARN
                if(topicSubscription.TopicArn != _eventTopicArn) {
                    return BadRequest();
                }

                // confirm subscription
                await HttpClient.GetAsync(topicSubscription.SubscribeURL);
                return Success("Confirmed");
            }

            // inspect SNS message
            var snsMessage = LambdaSerializer.Deserialize<SNSEvent.SNSMessage>(request.Body);
            if(snsMessage.Message == null) {
                return BadRequest();
            }

            // broadcast message for all matching filters
            var messageBytes = Encoding.UTF8.GetBytes(snsMessage.Message);
            var filters = await _dataTable.GetConnectionFiltersAsync(connectionId);
            if(filters.Any()) {
                await Task.WhenAll(filters.Select(filter => {

                    // TODO: only send message if the filter allows it
                    LogInfo($"sending message to connection '{connectionId}' connections");
                    return SendMessageToConnection(messageBytes, connectionId);
                }));
            }
            return Success("Ok");

            // local functions
            APIGatewayHttpApiV2ProxyResponse Success(string message)
                => new APIGatewayHttpApiV2ProxyResponse {
                    Body = message,
                    Headers = new Dictionary<string, string> {
                        ["Content-Type"] = "text/plain"
                    },
                    StatusCode = 200
                };

            APIGatewayHttpApiV2ProxyResponse BadRequest()
                => new APIGatewayHttpApiV2ProxyResponse {
                    Body = "Bad Request",
                    Headers = new Dictionary<string, string> {
                        ["Content-Type"] = "text/plain"
                    },
                    StatusCode = 400
                };
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
