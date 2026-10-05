using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Oliver4.IconLibrary.Tests
{
    /// <summary>The JSON bridge between the HTML UI and the host.</summary>
    public class HostBridgeTests
    {
        private readonly BlockingCollection<JObject> _posted = new BlockingCollection<JObject>();
        private readonly List<string> _log = new List<string>();
        private readonly Fixture f = new Fixture();
        private DataverseService _service;
        private readonly HostBridge _bridge;

        public HostBridgeTests()
        {
            _service = f.Service;
            _bridge = new HostBridge(s => _posted.Add(JObject.Parse(s)), () => _service,
                () => new ContextInfo { Version = "9.9.9", Connected = _service != null, OrgUrl = "org.crm11.dynamics.com" }, m => { lock (_log) _log.Add(m); });
        }

        private JObject Next(Func<JObject, bool> match = null)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
                if (_posted.TryTake(out var m, 200) && (match == null || match(m))) return m;
            throw new AssertionException("no message from the host within 10 s");
        }

        private JObject Call(string method, object prms = null, int id = 1)
        {
            _bridge.OnMessage(new JObject { ["id"] = id, ["method"] = method, ["params"] = prms == null ? new JObject() : JObject.FromObject(prms) }.ToString());
            return Next(m => (int?)m["id"] == id);
        }

        [Test("HB-01")] public void ContextIsReturnedInCamelCase()
        {
            var r = Call("getContext");
            Assert.Equal(true, (bool)r["ok"]);
            Assert.Equal("9.9.9", (string)r["result"]["version"]);
            Assert.Equal(true, (bool)r["result"]["connected"]);
        }

        [Test("HB-02")] public void UnknownMethodReturnsAnError()
        {
            var r = Call("dropDatabase");
            Assert.Equal(false, (bool)r["ok"]);
            Assert.Contains("Unknown method", (string)r["error"]);
        }

        [Test("HB-03")] public void DataverseMethodsNeedAConnection()
        {
            _service = null;
            var r = Call("getSolutions");
            Assert.Equal(false, (bool)r["ok"]);
            Assert.Equal("Not connected to an environment.", (string)r["error"]);
        }

        [Test("HB-04")] public void MalformedOrIncompleteMessagesAreIgnored()
        {
            _bridge.OnMessage("{ not json");
            _bridge.OnMessage("{\"method\":\"getContext\"}");
            _bridge.OnMessage("{\"id\":5}");
            Assert.False(_posted.TryTake(out _, 500), "nothing should be posted back");
            lock (_log) Assert.True(_log.Any(l => l.Contains("unreadable message")));
        }

        [Test("HB-05")] public void GetTablesRoundTripsGuidsAndShapes()
        {
            var r = Call("getTables", new { solutionId = f.Solution.ToString(), solutionUniqueName = "contoso_core" });
            Assert.Equal(true, (bool)r["ok"], (string)r["error"]);
            var tables = (JArray)r["result"]["tables"];
            Assert.Equal(4, tables.Count);
            var rule = tables.First(t => (string)t["logicalName"] == "contoso_rule");
            Assert.Equal("custom", (string)rule["iconState"]);
            Assert.Contains("<svg", (string)rule["webResource"]["svg"]);
            Assert.Equal(1, (int)r["result"]["hiddenSystemCount"]);
        }

        [Test("HB-06")] public void ApplySendsProgressEventsThenTheResult()
        {
            var id = 7;
            _bridge.OnMessage(new JObject
            {
                ["id"] = id, ["method"] = "apply",
                ["params"] = JObject.FromObject(new
                {
                    tableLogicalName = "contoso_batch", iconId = "stack", action = "create", publish = false,
                    solutionId = f.Solution.ToString(), solutionUniqueName = "contoso_core", isDefaultSolution = false,
                    webResourceName = "contoso_/icons/stack_regular.svg", webResourceDisplayName = (string)null, webResourceId = (string)null
                })
            }.ToString());
            var events = new List<JObject>();
            JObject result;
            while (true)
            {
                var m = Next();
                if ((string)m["event"] == "progress") { events.Add(m); continue; }
                result = m; break;
            }
            Assert.Equal(true, (bool)result["ok"], (string)result["error"]);
            Assert.Equal(true, (bool)result["result"]["success"]);
            Assert.True(events.Count >= 6, "progress events: " + events.Count);
            Assert.Equal("webresource", (string)events[0]["steps"][0]["key"]);
            Assert.Equal("contoso_/icons/stack_regular.svg", f.Org.Table("contoso_batch").IconVectorName);
        }

        [Test("HB-07")] public void RequestsRunOneAtATime()
        {
            f.Org.Delay = 60;
            for (var i = 1; i <= 4; i++)
                _bridge.OnMessage(new JObject { ["id"] = i, ["method"] = "checkWebResourceName", ["params"] = new JObject { ["name"] = "x" + i } }.ToString());
            for (var i = 0; i < 4; i++) Next();
            Assert.Equal(1, f.Org.MaxConcurrent, "the IOrganizationService must never be used concurrently");
        }

        [Test("HB-08")] public void ErrorsAreReducedToTheirFirstLine()
        {
            f.Org.FailOn["RetrieveMultiple:publisher"] = new InvalidOperationException("First line\r\nSecond line with stack");
            var r = Call("getPublishers");
            Assert.Equal("First line", (string)r["error"]);
        }

        [Test("HB-09")] public void OpenUrlIgnoresNonWebSchemes()
        {
            var r = Call("openUrl", new { url = "file:///etc/passwd" });
            Assert.Equal(true, (bool)r["ok"]);
        }

        [Test("HB-10")] public void AboutIncludesTheFluentMitLicence()
        {
            var r = Call("getAbout");
            var notices = (string)r["result"]["notices"];
            Assert.Contains("MIT", notices);
            Assert.Contains("Microsoft Corporation", notices);
            Assert.Contains("fluentui-system-icons", notices);
        }

        [Test("HB-11")] public void GetSolutionsAlsoRefreshesCaches()
        {
            f.Service.GetTables(f.Solution, "contoso_core");
            Call("getSolutions");
            f.Service.GetTables(f.Solution, "contoso_core");
            Assert.Equal(2, f.Org.RetrieveAllEntitiesCount, "Reload solutions must pick up tables created since the tool opened");
        }

        [Test("HB-12")] public void EventsCarryTheEventName()
        {
            _bridge.SendEvent("context", new { context = new ContextInfo { Connected = false }, reload = true });
            var m = Next();
            Assert.Equal("context", (string)m["event"]);
            Assert.Equal(false, (bool)m["context"]["connected"]);
            Assert.Equal(true, (bool)m["reload"]);
        }
    }
}
