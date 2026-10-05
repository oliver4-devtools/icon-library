using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Oliver4.IconLibrary
{
    /// <summary>
    /// JSON request/response bridge between the HTML UI and the host.
    ///
    /// UI -> host:   { id, method, params }
    /// host -> UI:   { id, ok: true, result } | { id, ok: false, error }
    /// host -> UI:   { event: "progress", steps } / { event: "context", context, reload }
    ///
    /// Requests run on the thread pool, one at a time, so the WebView2 UI thread is never blocked by Dataverse
    /// calls and the IOrganizationService is never used concurrently.
    /// </summary>
    public sealed class HostBridge
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Include,
            Formatting = Formatting.None
        };

        private readonly Action<string> _postToUi;          // must marshal to the UI thread
        private readonly Func<DataverseService> _getService; // null when not connected
        private readonly Func<ContextInfo> _getContext;
        private readonly Action<string> _log;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public HostBridge(Action<string> postToUi, Func<DataverseService> getService, Func<ContextInfo> getContext, Action<string> log)
        {
            _postToUi = postToUi;
            _getService = getService;
            _getContext = getContext;
            _log = log ?? (_ => { });
        }

        public static string Serialize(object o) => JsonConvert.SerializeObject(o, Settings);

        public void SendEvent(string name, object payload)
        {
            var jo = JObject.FromObject(payload ?? new object(), JsonSerializer.Create(Settings));
            jo["event"] = name;
            _postToUi(jo.ToString(Formatting.None));
        }

        /// <summary>Handle one raw message from the UI. Safe to call on the UI thread; work happens off it.</summary>
        public void OnMessage(string json)
        {
            JObject msg;
            try { msg = JObject.Parse(json); }
            catch (Exception ex) { _log("Bridge: unreadable message: " + ex.Message); return; }

            var id = msg["id"];
            var method = (string)msg["method"];
            var p = msg["params"] as JObject ?? new JObject();
            if (id == null || string.IsNullOrEmpty(method)) return;

            Task.Run(async () =>
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    var result = Dispatch(method, p);
                    _postToUi(Serialize(new { id, ok = true, result }));
                }
                catch (Exception ex)
                {
                    var inner = ex is AggregateException ae ? ae.InnerException ?? ae : ex;
                    _log("Bridge: " + method + " failed: " + inner);
                    _postToUi(Serialize(new { id, ok = false, error = Friendly(inner) }));
                }
                finally { _gate.Release(); }
            });
        }

        private object Dispatch(string method, JObject p)
        {
            switch (method)
            {
                case "getContext":
                    return _getContext();

                case "getAbout":
                    return new AboutInfo
                    {
                        Version = ToolInfo.Version,
                        Notices = IconCatalogue.ReadEmbeddedText("Web/THIRD-PARTY-NOTICES.txt"),
                        Homepage = ToolInfo.Homepage,
                        Repository = ToolInfo.Repository
                    };

                case "openUrl":
                {
                    var url = (string)p["url"];
                    if (!string.IsNullOrEmpty(url) && (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    return true;
                }

                case "getSolutions":
                {
                    var svc = Service();
                    svc.InvalidateCaches(); // "Reload solutions" also refreshes metadata and web resource caches
                    return svc.GetSolutions();
                }

                case "warmCaches":
                    // Fire-and-forget from the UI. Requests are gated one at a time, so a getTables that
                    // arrives while this is running simply queues behind it and finds the cache warm.
                    Service().WarmCaches();
                    return true;

                case "warmSvgContent":
                    return Service().WarmSvgContent(p["batchSize"] != null ? (int)p["batchSize"] : 250);

                case "getTables":
                    return Service().GetTables(Guid.Parse((string)p["solutionId"]), (string)p["solutionUniqueName"]);

                case "getPublishers":
                    return Service().GetPublishers();

                case "getTablesUsingWebResource":
                    return Service().GetTablesUsingWebResource((string)p["name"]);

                case "checkWebResourceName":
                    return Service().CheckWebResourceName((string)p["name"]);

                case "findExistingIcon":
                    return Service().FindExistingIcon((string)p["iconId"], Guid.Parse((string)p["solutionId"]), (string)p["solutionUniqueName"]);

                case "apply":
                {
                    var req = p.ToObject<ApplyRequest>(JsonSerializer.Create(Settings));
                    return Service().Apply(req, steps => SendEvent("progress", new { steps }));
                }

                default:
                    throw new InvalidOperationException("Unknown method: " + method);
            }
        }

        private DataverseService Service()
        {
            return _getService() ?? throw new InvalidOperationException("Not connected to an environment.");
        }

        private static string Friendly(Exception ex)
        {
            // FaultException<OrganizationServiceFault> messages are the useful part; keep the first line
            var m = ex.Message ?? ex.GetType().Name;
            var line = m.Split('\r', '\n')[0];
            return line.Length > 400 ? line.Substring(0, 397) + "…" : line;
        }
    }
}
