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
using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;

namespace Demo.EventBus {

    public static class EventPatternMatcher {

        //--- Class Methos ---
        private static bool IsTextToken(JToken token) => token.Type == JTokenType.String;
        private static bool IsBooleanToken(JToken token) => token.Type == JTokenType.Boolean;
        private static bool IsNumericToken(JToken token) => (token.Type == JTokenType.Integer) || (token.Type == JTokenType.Float);

        private static bool IsOperatorToken(JToken token)
            => (token is JValue literal)
                && literal.Value switch {
                    "<" => true,
                    "<=" => true,
                    "=" => true,
                    ">" => true,
                    ">=" => true,
                    _ => false
                };

        private static bool TryGetText(JToken token, out string text) {
            if(token.Type == JTokenType.String) {
                text = (string)((JValue)token).Value;
                return true;
            } else {
                text = null;
                return false;
            }
        }

        private static bool TryGetNumeric(JToken token, out double numeric) {
            switch(token.Type) {
            case JTokenType.Integer:
            case JTokenType.Float:
                numeric = (double)Convert.ChangeType(((JValue)token).Value, typeof(double));
                return true;
            default:
                numeric = 0.0;
                return false;
            }
        }

        private static bool TryGetBoolean(JToken token, out bool boolean) {
            if(token.Type == JTokenType.Boolean) {
                boolean = (bool)((JValue)token).Value;
                return true;
            } else {
                boolean = false;
                return false;
            }
        }

        //--- Methods ---
        public static bool IsPatternValid(JObject pattern) {
            if(!pattern.Properties().Any()) {

                // pattern cannot be empty
                return false;
            }
            foreach(var kv in pattern) {
                switch(kv.Value) {
                case JArray allowedValues:

                    // only allow literals and content-patterns
                    foreach(var allowedValue in allowedValues) {
                        switch(allowedValue) {
                        case JArray _:

                            // nested array is not allowed
                            return false;
                        case JObject contentBasedPattern:
                            if(!IsContentPatternValid(contentBasedPattern)) {
                                return false;
                            }
                            break;
                        case JValue _:
                            break;
                        }
                    }
                    break;
                case JObject nestedPattern:
                    if(!IsPatternValid(nestedPattern)) {
                        return false;
                    }
                    break;
                case JValue _:

                    // event pattern contains invalid value (can only be a nonempty array or nonempty object)
                    return false;
                default:
                    throw new ArgumentException($"invalid pattern type: {kv.Value?.GetType().FullName ?? "<null>"}");
                }
            }
            return true;

            // local functions
            bool IsContentPatternValid(JObject contentPattern) {
                var contentPatternOperation = contentPattern.Properties().SingleOrDefault();
                if(contentPatternOperation == null) {

                    // content-based pattern must have exactly one property
                    return false;
                }

                // validate content-based filter
                switch(contentPatternOperation.Name) {
                case "prefix":
                    if(IsTextToken(contentPatternOperation.Value)) {

                        // { "prefix": "TEXT" }
                        return true;
                    }
                    break;
                case "anything-but":
                    if(IsTextToken(contentPatternOperation.Value) || IsNumericToken(contentPatternOperation.Value)) {

                        // { "anything-but": "TEXT" }
                        // { "anything-but": NUMERIC }
                        return true;
                    } else if(
                        (contentPatternOperation.Value is JArray anythingButValues)
                        && anythingButValues.Any()
                        && anythingButValues.All(value => IsTextToken(value) || IsNumericToken(value))
                    ) {

                        // { "anything-but": [ "TEXT"+ ] }
                        // { "anything-but": [ NUMERIC+ ] }
                        return true;
                    } else if(
                        (contentPatternOperation.Value is JObject anythingButContentPattern)
                        && IsContentPatternValid(anythingButContentPattern)
                    ) {

                        // { "anything-but": { ... } }
                        return true;
                    }
                    break;
                case "numeric":
                    if(contentPatternOperation.Value is JArray numericFilterValues) {
                        if(numericFilterValues.Count() == 2) {
                            if(
                                IsOperatorToken(numericFilterValues[0])
                                && IsNumericToken(numericFilterValues[1])
                            ) {

                                // { "numeric": [ "<", NUMERIC ] }
                                return true;
                            }
                        } else if(numericFilterValues.Count() == 4) {
                            if(
                                IsOperatorToken(numericFilterValues[0])
                                && IsNumericToken(numericFilterValues[1])
                                && IsOperatorToken(numericFilterValues[2])
                                && IsNumericToken(numericFilterValues[3])
                            ) {

                                // { "numeric": [ ">", NUMERIC, "<=", NUMERIC ] }
                                return true;
                            }
                        }
                    }
                    break;
                case "cidr":
                    if((contentPatternOperation.Value is JValue cidrFilterValue) && (cidrFilterValue.Type == JTokenType.String)) {

                        // Sample value: "10.0.0.0/24"
                        var parts = ((string)cidrFilterValue.Value).Split('/');
                        if(
                            (parts.Length == 2)
                            && int.TryParse(parts[1], out var prefixValue)
                            && (prefixValue >= 0)
                            && (prefixValue < 32)
                        ) {

                            // valid ip prefix
                            var ipBytes = parts[0].Split('.');
                            if(
                                (ipBytes.Length == 4)
                                && ipBytes.All(ipByte =>
                                    int.TryParse(ipByte, out var ipByteValue)
                                    && (ipByteValue >= 0)
                                    && (ipByteValue < 256)
                                )
                            ) {

                                // { "cidr": "10.0.0.0/24" }
                                return true;
                            }
                        }
                    }
                    break;
                case "exists":
                    if(IsBooleanToken(contentPatternOperation.Value)) {

                        // { "exists": BOOLEAN  }
                        return true;
                    }
                    break;
                }

                // unrecognized content filter
                return false;
            }
        }

        public static bool IsPatternMatch(JObject data, JObject pattern) {

            // TODO: fix for supporting { "exists": false } constraint

            foreach(var kv in pattern) {
                switch(kv.Value) {
                case JArray allowedValues:

                    // check key exists
                    switch(data[kv.Key]) {
                    case JObject _:

                        // array can never match an object
                        return false;
                    case JArray array:
                        if(!allowedValues.Any(value => IsContentMatch(value, allowedValues))) {
                            return false;
                        }
                        break;
                    case JValue value:
                        if(!IsContentMatch(value, allowedValues)) {
                            return false;
                        }
                        break;
                    default:
                        throw new ArgumentException($"unexpected pattern type: {data[kv.Key]?.GetType().FullName ?? "<null>"}");
                    }
                    break;
                case JObject nestedPattern:

                    // check key exists and matches pattern
                    if(
                        !(data[kv.Key] is JObject nestedData)
                        || !IsPatternMatch(nestedData, nestedPattern)
                    ) {
                        return false;
                    }
                    break;
                default:
                    throw new ArgumentException($"unexpected pattern type: {kv.Value?.GetType().FullName ?? "<null>"}");
                }
            }
            return true;

            // local functions
            bool IsContentMatch(JToken data, JToken pattern) {

                // data must be a literal value
                if(!(data is JValue dataValue)) {
                    return false;
                }
                if(pattern is JValue literalPattern) {

                    // check for an exact match
                    return dataValue.Value == literalPattern.Value;
                } else if(pattern is JObject contentPattern) {

                    // check content-based filter operation
                    var contentPatternOperation = contentPattern.Properties().Single();
                    switch(contentPatternOperation.Name) {
                    case "prefix":
                        if(dataValue.Type != JTokenType.String) {
                            return false;
                        }

                        // { "prefix": "TEXT" }
                        return ((string)dataValue.Value).StartsWith((string)contentPatternOperation.Value, StringComparison.Ordinal);
                    case "anything-but":
                        if(contentPatternOperation.Value is JArray disallowedValues) {

                            // { "anything-but": [ DISALLOWED-VALUE ] }
                            return !disallowedValues.Any(disallowedValue => IsContentMatch(data, disallowedValue));
                        } else {

                            // { "anything-but": DISALLOWED-VALUE }
                            return !IsContentMatch(data, contentPatternOperation.Value);
                        }
                    case "numeric": {

                        // check if data is numeric
                        if(!TryGetNumeric(dataValue, out var dataNumeric)) {
                            return false;
                        }
                        var numericFilterValues = (JArray)contentPatternOperation.Value;
                        switch(numericFilterValues.Count) {
                        case 2:

                            // { "numeric": [ "<", NUMERIC ] }
                            return CheckNumericOperation(
                                (string)((JValue)numericFilterValues[0]),
                                (double)Convert.ChangeType((JValue)numericFilterValues[1], typeof(double))
                            );
                        case 4:

                            // { "numeric": [ ">", NUMERIC, "<=", NUMERIC ] }
                            return CheckNumericOperation(
                                (string)((JValue)numericFilterValues[0]),
                                (double)Convert.ChangeType((JValue)numericFilterValues[1], typeof(double))
                            ) && CheckNumericOperation(
                                (string)((JValue)numericFilterValues[2]),
                                (double)Convert.ChangeType((JValue)numericFilterValues[3], typeof(double))
                            );
                        default:
                            throw new Exception("invalid content pattern");
                        }

                        // local functions
                        bool CheckNumericOperation(string operation, double comparand)
                            => operation switch {
                                "<" => dataNumeric < comparand,
                                "<=" => dataNumeric <= comparand,
                                "=" => dataNumeric == comparand,
                                ">=" => dataNumeric >= comparand,
                                ">" => dataNumeric > comparand,
                                _ => throw new Exception($"invalid operation: {operation ?? "<null>"}")
                            };
                    }
                    case "cidr":
                        if(dataValue.Type != JTokenType.String) {
                            return false;
                        }

                        // { "cidr": "10.0.0.0/24" }
                        return IsInCidrRange((string)dataValue.Value, (string)contentPatternOperation.Value);
                    case "exists":

                        // TODO: implement
                        throw new NotImplementedException();
                    }

                    // unrecognized content filter
                    return false;
                } else {
                    throw new ArgumentException($"unexpected pattern type: {pattern.GetType().FullName ?? "<null>"}");
                }
            }

            bool IsInCidrRange(string ipValue, string cidrRange) {
                var ipAndPrefix = cidrRange.Split('/');
                var ipAddress = BitConverter.ToInt32(IPAddress.Parse(ipAndPrefix[0]).GetAddressBytes(), 0);
                var cidrAddress = BitConverter.ToInt32(IPAddress.Parse(ipValue).GetAddressBytes(), 0);
                var cidrPrefix = IPAddress.HostToNetworkOrder(-1 << (32 - int.Parse(ipAndPrefix[1])));
                return ((ipAddress & cidrPrefix) == (cidrAddress & cidrPrefix));
            }
        }
    }
}
