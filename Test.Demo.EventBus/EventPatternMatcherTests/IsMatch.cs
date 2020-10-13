using Demo.EventBus;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Test.Demo.EventBus.EventPatternMatcherTests {

    public class IsMatch {

        //--- Methods ---

        [Fact]
        public void Empty_event_is_not_matched() {

            // arrange
            var evt = JObject.Parse(@"{}");
            var pattern = JObject.Parse(@"{
                ""Foo"": [ ""Bar"" ]
            }");

            // act
            var isMatch = EventPatternMatcher.IsMatch(evt, pattern);

            // assert
            isMatch.Should().BeFalse();
        }

        [Fact]
        public void Event_with_literal_is_matched() {

            // arrange
            var evt = JObject.Parse(@"{
                ""Foo"": ""Bar""
            }");
            var pattern = JObject.Parse(@"{
                ""Foo"": [ ""Bar"" ]
            }");

            // act
            var isMatch = EventPatternMatcher.IsMatch(evt, pattern);

            // assert
            isMatch.Should().BeTrue();
        }

        [Fact]
        public void Event_with_list_is_matched() {

            // arrange
            var evt = JObject.Parse(@"{
                ""Foo"": [ ""Bar"" ]
            }");
            var pattern = JObject.Parse(@"{
                ""Foo"": [ ""Bar"" ]
            }");

            // act
            var isMatch = EventPatternMatcher.IsMatch(evt, pattern);

            // assert
            isMatch.Should().BeTrue();
        }

        [Fact]
        public void Event_with_empty_is_not_matched() {

            // arrange
            var evt = JObject.Parse(@"{
                ""Foo"": [ ]
            }");
            var pattern = JObject.Parse(@"{
                ""Foo"": [ ""Bar"" ]
            }");

            // act
            var isMatch = EventPatternMatcher.IsMatch(evt, pattern);

            // assert
            isMatch.Should().BeFalse();
        }

        [Fact]
        public void Event_with_prefix_is_matched() {

            // arrange
            var evt = JObject.Parse(@"{
                ""Foo"": ""Bar""
            }");
            var pattern = JObject.Parse(@"{
                ""Foo"": [ { ""prefix"": ""B"" } ]
            }");

            // act
            var isMatch = EventPatternMatcher.IsMatch(evt, pattern);

            // assert
            isMatch.Should().BeTrue();
        }

        [Fact]
        public void Event_with_prefix_is_not_matched() {

            // arrange
            var evt = JObject.Parse(@"{
                ""Foo"": ""Bar""
            }");
            var pattern = JObject.Parse(@"{
                ""Foo"": [ { ""prefix"": ""F"" } ]
            }");

            // act
            var isMatch = EventPatternMatcher.IsMatch(evt, pattern);

            // assert
            isMatch.Should().BeFalse();
        }

        [Fact]
        public void Event_with_anything_but_is_matched() {

            // arrange
            var evt = JObject.Parse(@"{
                ""Foo"": ""Bar""
            }");
            var pattern = JObject.Parse(@"{
                ""Foo"": [ { ""anything-but"": { ""prefix"": ""F"" } } ]
            }");

            // act
            var isMatch = EventPatternMatcher.IsMatch(evt, pattern);

            // assert
            isMatch.Should().BeTrue();
        }

        [Fact]
        public void Event_with_anything_but_is_not_matched() {

            // arrange
            var evt = JObject.Parse(@"{
                ""Foo"": ""Bar""
            }");
            var pattern = JObject.Parse(@"{
                ""Foo"": [ { ""anything-but"": { ""prefix"": ""B"" } } ]
            }");

            // act
            var isMatch = EventPatternMatcher.IsMatch(evt, pattern);

            // assert
            isMatch.Should().BeFalse();
        }
    }
}
