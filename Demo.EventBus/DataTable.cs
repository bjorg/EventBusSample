/*
 * LambdaSharp (λ#)
 * Copyright (C) 2018-2020
 * lambdasharp.net
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Demo.EventBus.Records;

namespace Demo.EventBus {

    public sealed class DataTable {

        //--- Constants ---
        private const string VALID_SYMBOLS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        private const string CONNECTION_PREFIX = "WS#";
        private const string FILTER_PREFIX = "FILTER#";
        private const string SUBSCRIPTION_PREFIX = "SUB#";
        private const string INFO = "INFO";

        //--- Class Fields ---
        private readonly static Random _random = new Random();

        private readonly static PutItemOperationConfig CreateItemConfig = new PutItemOperationConfig {
            ConditionalExpression = new Expression {
                ExpressionStatement = "attribute_not_exists(#PK)",
                ExpressionAttributeNames = {
                    ["#PK"] = "PK"
                }
            }
        };

        //--- Class Methods ---
        public static string GetRandomString(int length)
            => new string(Enumerable.Repeat(VALID_SYMBOLS, length).Select(chars => chars[_random.Next(chars.Length)]).ToArray());

        private static T Deserialize<T>(Document record)
            => (record != null)
                ? JsonSerializer.Deserialize<T>(record.ToJson())
                : default;

        private async static Task<IEnumerable<T>> DoSearchAsync<T>(Search search, CancellationToken cancellationToken = default) {
            var results = new List<T>();
            do {
                var documents = await search.GetNextSetAsync(cancellationToken);
                results.AddRange(documents.Select(document => Deserialize<T>(document)));
            } while(!search.IsDone);
            return results;
        }

        //--- Fields ---
        private readonly IAmazonDynamoDB _dynamoDbClient;
        private readonly Table _table;

        //--- Constructors ---
        public DataTable(string tableName, IAmazonDynamoDB dynamoDbClient = null) {
            TableName = tableName ?? throw new System.ArgumentNullException(nameof(tableName));
            _dynamoDbClient = dynamoDbClient ?? new AmazonDynamoDBClient();
            _table = Table.LoadTable(dynamoDbClient, tableName);
        }

        //--- Properties ---
        public string TableName { get; }

        //--- Methods ---

        #region Connection Record
        public async Task<ConnectionRecord> GetConnectionAsync(string connectionId, CancellationToken cancellationToken = default)
            => Deserialize<ConnectionRecord>(await _table.GetItemAsync(CONNECTION_PREFIX + connectionId, INFO, cancellationToken));

        public Task CreateConnectionAsync(ConnectionRecord record, CancellationToken cancellationToken = default)
            => PutItemsAsync(
                record,
                pk: CONNECTION_PREFIX + record.ConnectionId,
                sk: INFO,
                gs1pk: SUBSCRIPTION_PREFIX + record.SubscriptionArn,
                gs1sk: CONNECTION_PREFIX + record.ConnectionId,
                CreateItemConfig,
                cancellationToken
            );

        public Task DeleteConnectionAsync(string connectionId, CancellationToken cancellationToken = default)
            => _table.DeleteItemAsync(CONNECTION_PREFIX + connectionId, INFO);
        #endregion

        #region Filter Record
        public Task CreateOrUpdateFilterAsync(FilterRecord record, CancellationToken cancellationToken = default)
            => PutItemsAsync(
                record,
                pk: FILTER_PREFIX + record.FilterId,
                sk: INFO,
                gs1pk: CONNECTION_PREFIX + record.ConnectionId,
                gs1sk: FILTER_PREFIX + record.FilterId,
                config: null,
                cancellationToken
            );
        public Task DeleteFilterAsync(string filterId, CancellationToken cancellationToken = default)
            => _table.DeleteItemAsync(FILTER_PREFIX + filterId, INFO);
        #endregion

        #region Record Queries
        public Task<IEnumerable<FilterRecord>> GetConnectionFiltersAsync(string connectionId, CancellationToken cancellationToken = default)
            => DoSearchAsync<FilterRecord>(_table.QueryGS1BeginsWith(CONNECTION_PREFIX + connectionId, FILTER_PREFIX), cancellationToken);

        public Task DeleteAllFiltersAsync(IEnumerable<FilterRecord> records, CancellationToken cancellationToken = default)
            => DeleteItemsAsync(records.Select(record => (PK: FILTER_PREFIX + record.FilterId, SK: INFO)));
        #endregion

        private Task PutItemsAsync<T>(T item, string pk, string sk, PutItemOperationConfig config, CancellationToken cancellationToken = default) {
            var document = Document.FromJson(JsonSerializer.Serialize(item));
            document["_Type"] = item.GetType().Name;
            document["PK"] = pk ?? throw new ArgumentNullException(nameof(pk));
            document["SK"] = sk ?? throw new ArgumentNullException(nameof(sk));
            return _table.PutItemAsync(document, config, cancellationToken);
        }

        private Task PutItemsAsync<T>(T item, string pk, string sk, string gs1pk, string gs1sk, PutItemOperationConfig config, CancellationToken cancellationToken = default) {
            var document = Document.FromJson(JsonSerializer.Serialize(item));
            document["_Type"] = item.GetType().Name;
            document["PK"] = pk ?? throw new ArgumentNullException(nameof(pk));
            document["SK"] = sk ?? throw new ArgumentNullException(nameof(sk));
            document["GS1PK"] = gs1pk ?? throw new ArgumentNullException(nameof(gs1pk));
            document["GS1SK"] = gs1sk ?? throw new ArgumentNullException(nameof(gs1sk));
            return _table.PutItemAsync(document, config, cancellationToken);
        }

        private Task DeleteItemsAsync(IEnumerable<(string PK, string SK)> keys, CancellationToken cancellationToken = default)
            => Task.WhenAll(keys.Select(key => _table.DeleteItemAsync(key.PK, key.SK)));
    }
}
