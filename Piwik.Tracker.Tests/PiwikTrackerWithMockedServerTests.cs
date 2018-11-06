﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using WireMock.Logging;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Settings;

namespace Piwik.Tracker.Tests
{
    [TestFixture]
    internal class PiwikTrackerWithMockedServerTests
    {
        private PiwikTracker _sut;
        private FluentMockServer _mockedPiwikServer;
        private const string UA = "Firefox";
        private const string PiwikBaseUrl = "http://localhost:1122/";
        private const int SiteId = 1;

        private static readonly NameValueCollection DefaultRequestParameter = new NameValueCollection
        {
            { "idsite",SiteId.ToString()},
            { "rec","1"},
            { "apiv","1"},
            { "url","http://unknown" },
        };

        private static readonly string[] DefaultRequestParameterKeysToRemoveFromComparison =
        {
            "r", // random value
            "_idts" // _createTs from cookie
        };

        [OneTimeSetUp]
        public void SetUpFixture()
        {
            _mockedPiwikServer = FluentMockServer.Start(new FluentMockServerSettings
            {
                Urls = new[] { PiwikBaseUrl },
                StartAdminInterface = true,
                ReadStaticMappings = true,
                WatchStaticMappings = true,
                PreWireMockMiddlewareInit = app => { System.Console.WriteLine($"PreWireMockMiddlewareInit : {app.GetType()}"); },
                PostWireMockMiddlewareInit = app => { System.Console.WriteLine($"PostWireMockMiddlewareInit : {app.GetType()}"); },
                Logger = new WireMockConsoleLogger(),
            });

            _mockedPiwikServer.AllowPartialMapping();

            this.CreateAllGetOkRequestBehavior();
            this.CreateAllPostOkRequestBehavior();
        }

        [OneTimeTearDown]
        public void TearDownFixture()
        {
            _mockedPiwikServer.Stop();
        }

        [SetUp]
        public void SetUpTest()
        {
            _sut = new PiwikTracker(SiteId, PiwikBaseUrl + "piwik.php");
        }

        [Test]
        [TestCase("myPage")]
        [TestCase("myPage/?Ü&")]
        public void DoTrackPageView_Test(string documentTitle)
        {
            //Arrange
            //Act
            var actual = _sut.DoTrackPageView(documentTitle);
            //Assert
            var actualRequest = this._mockedPiwikServer.LogEntries.Last();
            Assert.That(actual.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(actualRequest.RequestMessage.Method, Is.EqualTo("GET"));
            Assert.PiwikRequestParameterMatch(actualRequest, new NameValueCollection
            {
                { "_idvc", "0" },// visit count
                { "_id", _sut.GetVisitorId() }, // visitor id
                { "action_name", documentTitle },
            });
        }

        [Test]
        [TestCase("myCategory", "myAction", "myName", "myValue")]
        [TestCase("myCategory", "myAction", "myName", "")]
        [TestCase("myCategory", "myAction", "", "myValue")]
        public void DoTrackEvent_Test(string category, string action, string name, string value)
        {
            //Arrange
            var expectedParameter = new NameValueCollection
            {
                {"_idvc", "0"}, // visit count
                {"_id", _sut.GetVisitorId()}, // visitor id
                {"e_c", category},
                {"e_a", action},
            };
            //Act
            var actual = _sut.DoTrackEvent(category, action, name, value);
            //Assert
            var actualRequest = this._mockedPiwikServer.LogEntries.Last();
            Assert.That(actual.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(actualRequest.RequestMessage.Method, Is.EqualTo("GET"));
            if (!string.IsNullOrEmpty(name))
            {
                expectedParameter.Add("e_n", name);
            }
            if (!string.IsNullOrEmpty(value))
            {
                expectedParameter.Add("e_v", value);
            }

            Assert.PiwikRequestParameterMatch(actualRequest, expectedParameter);
        }

        [Test]
        [TestCase("myCn", "mycp", "myct")]
        [TestCase("myCn", null, "myct")]
        [TestCase("myCn", "mycp", null)]
        public void DoTrackContentImpression_Test(string contentName, string contentPiece, string contentTarget)
        {
            //Arrange
            var expectedParameter = new NameValueCollection
            {
                {"_idvc", "0"}, // visit count
                {"_id", _sut.GetVisitorId()}, // visitor id
                { "c_n", contentName },
            };
            //Act
            var actual = _sut.DoTrackContentImpression(contentName, contentPiece, contentTarget);
            //Assert
            var actualRequest = this._mockedPiwikServer.LogEntries.Last();
            Assert.That(actual.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(actualRequest.RequestMessage.Method, Is.EqualTo("GET"));
            if (!string.IsNullOrEmpty(contentPiece))
            {
                expectedParameter.Add("c_p", contentPiece);
            }
            if (!string.IsNullOrEmpty(contentTarget))
            {
                expectedParameter.Add("c_t", contentTarget);
            }

            Assert.PiwikRequestParameterMatch(actualRequest, expectedParameter);
        }

        [Test]
        [TestCase("myInteraction", "myCn", "mycp", "myct")]
        [TestCase("myInteraction", "myCn", null, "myct")]
        [TestCase("myInteraction", "myCn", "mycp", null)]
        public void DoTrackContentInteraction_Test(string interaction, string contentName, string contentPiece, string contentTarget)
        {
            //Arrange
            var expectedParameter = new NameValueCollection
            {
                 { "_idvc", "0" },// visit count
                 { "_id", _sut.GetVisitorId() }, // visitor id
                 { "c_n", contentName },
                 { "c_i", interaction },
            };
            //Act
            var actual = _sut.DoTrackContentInteraction(interaction, contentName, contentPiece, contentTarget);
            //Assert
            var actualRequest = this._mockedPiwikServer.LogEntries.Last();
            Assert.That(actual.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(actualRequest.RequestMessage.Method, Is.EqualTo("GET"));
            if (!string.IsNullOrEmpty(contentPiece))
            {
                expectedParameter.Add("c_p", contentPiece);
            }
            if (!string.IsNullOrEmpty(contentTarget))
            {
                expectedParameter.Add("c_t", contentTarget);
            }

            Assert.PiwikRequestParameterMatch(actualRequest, expectedParameter);
        }

        [Test]
        [TestCase("myKey", "myCat", 0)]
        [TestCase("myKey", null, 1)]
        [TestCase("myKey", "myCat", null)]
        public void DoTrackSiteSearch_Test(string keyword, string category, int? countResults)
        {
            //Arrange
            var expectedParameter = new NameValueCollection
            {
                 { "_idvc", "0" },// visit count
                 { "_id", _sut.GetVisitorId() }, // visitor id
                 { "search", keyword },
            };
            //Act
            var actual = _sut.DoTrackSiteSearch(keyword, category, countResults);
            //Assert
            var actualRequest = this._mockedPiwikServer.LogEntries.Last();
            Assert.That(actual.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(actualRequest.RequestMessage.Method, Is.EqualTo("GET"));
            if (!string.IsNullOrEmpty(category))
            {
                expectedParameter.Add("search_cat", category);
            }
            if (countResults != null)
            {
                expectedParameter.Add("search_count", countResults.ToString());
            }

            Assert.PiwikRequestParameterMatch(actualRequest, expectedParameter);
        }

        [Test]
        [TestCase(1, 0.789f)]
        [TestCase(1, 0f)]
        [TestCase(120, 3655.55f)]
        public void DoTrackGoal_Test(int idGoal, float revenue)
        {
            //Arrange
            var expectedParameter = new NameValueCollection
            {
                 { "_idvc", "0" },// visit count
                 { "_id", _sut.GetVisitorId() }, // visitor id
                 { "idgoal", idGoal.ToString() },
            };
            //Act
            var actual = _sut.DoTrackGoal(idGoal, revenue);
            //Assert
            var actualRequest = this._mockedPiwikServer.LogEntries.Last();
            Assert.That(actual.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(actualRequest.RequestMessage.Method, Is.EqualTo("GET"));
            if (Math.Abs(revenue) > 0.05f)
            {
                expectedParameter.Add("revenue", revenue.ToString("0.##", CultureInfo.InvariantCulture));
            }
            Assert.PiwikRequestParameterMatch(actualRequest, expectedParameter);
        }

        [Test]
        [TestCase("https://myUrl.com/action?test=1&x=5", ActionType.Download)]
        [TestCase("", ActionType.Link)]
        public void DoTrackAction_Test(string actionUrl, ActionType actionType)
        {
            //Arrange
            //Act
            var actual = _sut.DoTrackAction(actionUrl, actionType);
            //Assert
            var actualRequest = this._mockedPiwikServer.LogEntries.Last();
            Assert.That(actual.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(actualRequest.RequestMessage.Method, Is.EqualTo("GET"));

            Assert.PiwikRequestParameterMatch(actualRequest, new NameValueCollection
            {
                    {"_idvc", "0"},
                    {"_id", _sut.GetVisitorId()},
                    {actionType.ToString(), actionUrl},
             });
        }

        [Test]
        [TestCase(1)]
        [TestCase(33465236365.4346)]
        public void DoTrackEcommerceCartUpdate_Test(double grandTotal)
        {
            //Arrange
            //Act
            var actual = _sut.DoTrackEcommerceCartUpdate(grandTotal);
            //Assert
            var actualRequest = this._mockedPiwikServer.LogEntries.Last();
            Assert.That(actual.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(actualRequest.RequestMessage.Method, Is.EqualTo("GET"));

            Assert.PiwikRequestParameterMatch(actualRequest, new NameValueCollection
                {
                    { "_idvc", "0" },
                    { "_id", _sut.GetVisitorId() },
                    { "idgoal", "0" },
                    { "revenue", grandTotal.ToString("0.##",CultureInfo.InvariantCulture) },
                });
        }

        [Test]
        [TestCase("mytoken")]
        [TestCase("")]
        public async Task DoBulkTrack_Test(string token)
        {
            //Arrange
            var language = "en-gb";
            var numberOfRequests = 20;
            //Act
            _sut.EnableBulkTracking();
            _sut.SetUserAgent(UA);
            _sut.SetBrowserLanguage(language);
            if (!string.IsNullOrEmpty(token))
            {
                _sut.SetTokenAuth(token);
            }
            for (int i = 0; i < numberOfRequests; i++)
            {
                _sut.DoTrackPageView("Page" + i);
            }
            var expectedUrls = _sut.GetStoredTrackingActions();
            Assert.That(_sut.GetStoredTrackingActions().Length, Is.EqualTo(numberOfRequests));
            var actual = _sut.DoBulkTrack();
            Assert.That(_sut.GetStoredTrackingActions().Length, Is.EqualTo(0));
            //Assert
            var actualRequest = this._mockedPiwikServer.LogEntries.Last();
            Assert.That(actual.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(actualRequest.RequestMessage.Method, Is.EqualTo("POST"));

            if (string.IsNullOrEmpty(token))
            {
                var body = JsonConvert.DeserializeObject<Dictionary<string, string[]>>(actualRequest.RequestMessage.BodyData.BodyAsString) as Dictionary<string, string[]>;
                Assert.That(body.Keys.Count, Is.EqualTo(1));
                Assert.That(body.Keys.First(), Is.EqualTo("requests"));
                Assert.That(body.First().Value, Is.EquivalentTo(expectedUrls));
            }
            else
            {
                var body = JsonConvert.DeserializeObject<Dictionary<string, object>>(actualRequest.RequestMessage.BodyData.BodyAsString) as Dictionary<string, object>;
                Assert.That(body.Keys.Count, Is.EqualTo(2));
                Assert.That(body["token_auth"], Is.EqualTo(token));
                Assert.That(((JArray)body["requests"]).Select(item => (string)item).ToArray(), Is.EquivalentTo(expectedUrls));
            }
        }

        [Test]
        [TestCase("sfaf&&Ä5", 32.32, 16.1667, 432.244, 234.324, 65.553)]
        public void DoTrackEcommerceOrder_Test(string orderId, double grandTotal, double subTotal, double tax, double shipping, double discount)
        {
            //Arrange
            //Act
            var actual = _sut.DoTrackEcommerceOrder(orderId, grandTotal, subTotal, tax, shipping, discount);
            //Assert
            var actualRequest = this._mockedPiwikServer.LogEntries.Last();
            Assert.That(actual.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(actualRequest.RequestMessage.Method, Is.EqualTo("GET"));

            Assert.PiwikRequestParameterMatch(actualRequest, new NameValueCollection
                {
                    { "_idvc", "0" },
                    { "_id", _sut.GetVisitorId() },
                    { "idgoal", "0" },
                    { "revenue", grandTotal.ToString("0.##",CultureInfo.InvariantCulture) },
                    {"ec_st", subTotal.ToString("0.##",CultureInfo.InvariantCulture) },
                    {"ec_tx", tax.ToString("0.##",CultureInfo.InvariantCulture)  },
                    {"ec_sh", shipping.ToString("0.##",CultureInfo.InvariantCulture) },
                    {"ec_dt", discount.ToString("0.##",CultureInfo.InvariantCulture) },
                    {"ec_id", orderId },
                });
        }

        [Test]
        public void DoPing_Test()
        {
            //Arrange
            //Act
            var actual = _sut.DoPing();
            //Assert
            var actualRequest = this._mockedPiwikServer.LogEntries.Last();
            Assert.That(actual.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(actualRequest.RequestMessage.Method, Is.EqualTo("GET"));

            Assert.PiwikRequestParameterMatch(actualRequest, new NameValueCollection
                {
                    { "_idvc", "0" },
                    { "_id", _sut.GetVisitorId() },
                    { "ping", "1" },
                });
        }

        private void CreateAllGetOkRequestBehavior()
        {
            this._mockedPiwikServer
                .Given(Request.Create()
                    .UsingGet()
                    .WithPath(new WildcardMatcher("*", true))
                )
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                );
        }

        private void CreateAllPostOkRequestBehavior()
        {
            this._mockedPiwikServer
                .Given(Request.Create()
                    .UsingPost()
                    .WithPath(new WildcardMatcher("*", true))
                )
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                );
        }

        internal class Assert : NUnit.Framework.Assert
        {
            public static void PiwikRequestParameterMatch(LogEntry actualRequest, NameValueCollection additionalExpectedParameter)
            {
                var baseUrl = actualRequest.RequestMessage.Protocol + "://" + actualRequest.RequestMessage.Host + ":" + actualRequest.RequestMessage.Port + "/";
                Assert.That(baseUrl, Is.EqualTo(PiwikBaseUrl));

                var queryStringPos = actualRequest.RequestMessage.AbsoluteUrl.IndexOf("?");
                string queryString;

                if (queryStringPos < 0)
                {
                    queryString = "";
                } else
                {
                    queryString = actualRequest.RequestMessage.AbsoluteUrl.Substring(queryStringPos);
                }

                var actualRequestParameter = System.Web.HttpUtility.ParseQueryString(queryString);
                var expectedRequestParameter = new NameValueCollection(DefaultRequestParameter) { additionalExpectedParameter };
                Assert.That(actualRequestParameter.Keys, Is.SupersetOf(DefaultRequestParameterKeysToRemoveFromComparison),
                    $"Request parameters must at least contain default Keys {string.Join(",", DefaultRequestParameterKeysToRemoveFromComparison)}.");

                // remove random default values!
                foreach (var key in DefaultRequestParameterKeysToRemoveFromComparison)
                {
                    actualRequestParameter.Remove(key);
                }

                Assert.That(actualRequestParameter.AllKeys, Is.EquivalentTo(expectedRequestParameter.AllKeys),
                    () => GetEnhancedMessage("Request parameter keys must be equivalent!", actualRequestParameter, expectedRequestParameter));
                Assert.That(actualRequestParameter.AllKeys.Select(k => expectedRequestParameter[k]), Is.EquivalentTo(expectedRequestParameter.AllKeys.Select(k => expectedRequestParameter[k])),
                    () => GetEnhancedMessage("Request parameter values must be equivalent!", actualRequestParameter, expectedRequestParameter));
            }

            private static string GetEnhancedMessage(string message, NameValueCollection actualParameter, NameValueCollection expectedParamters)
            {
                return message + Environment.NewLine +
                    $"Expected: equivalent to <{string.Join(", ", expectedParamters.ToKeyValuePairs().OrderBy(kvp => kvp.Key).Select(kvp => "\"" + kvp.Key + ":" + kvp.Value + "\""))}>" + Environment.NewLine +
                    $"But was: <{string.Join(", ", actualParameter.ToKeyValuePairs().OrderBy(kvp => kvp.Key).Select(kvp => "\"" + kvp.Key + ":" + kvp.Value + "\""))}>";
            }
        }
    }
}