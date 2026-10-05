using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using McTools.Xrm.Connection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Xrm.Sdk;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Args;
using XrmToolBox.Extensibility.Interfaces;

namespace Oliver4.IconLibrary
{
    /// <summary>
    /// The XrmToolBox tool control. It is a thin WinForms shell: a WebView2 control fills the tab and the whole
    /// UI is the embedded HTML app. Dataverse work is delegated to <see cref="DataverseService"/> through
    /// <see cref="HostBridge"/>.
    /// </summary>
    public class IconLibraryControl : PluginControlBase, IGitHubPlugin, IHelpPlugin
    {
        private WebView2 _webView;
        private HostBridge _bridge;
        private DataverseService _dataverse;
        private System.Windows.Forms.Label _status;
        private bool _uiReady;

        public string RepositoryName => ToolInfo.GitHubRepo;
        public string UserName => ToolInfo.GitHubUser;
        public string HelpUrl => ToolInfo.Homepage;

        public IconLibraryControl()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            BackColor = Color.FromArgb(250, 251, 252);
            Name = "IconLibraryControl";
            Size = new Size(1240, 740);

            _status = new System.Windows.Forms.Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.FromArgb(86, 90, 98),
                Text = "Starting the Oliver4 Icon Library…"
            };
            Controls.Add(_status);

            Load += OnLoad;
            ResumeLayout(false);
        }

        private async void OnLoad(object sender, EventArgs e)
        {
            try
            {
                string webFolder;
                try { webFolder = WebAssets.EnsureExtracted(); }
                catch (Exception ex) { ShowFatal("The tool's UI files could not be extracted to your local app data folder.", ex); return; }

                _bridge = new HostBridge(PostToUi, () => _dataverse, GetContext, msg => LogInfo(msg));

                // The control stays visible while WebView2 initialises (it needs a window handle); the status label sits on top until the page has loaded.
                _webView = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };
                Controls.Add(_webView);
                _status.BringToFront();

                LogWebView2Sdk();
                Directory.CreateDirectory(WebAssets.UserDataFolder);
                var env = await CoreWebView2Environment.CreateAsync(null, WebAssets.UserDataFolder, new CoreWebView2EnvironmentOptions());
                await _webView.EnsureCoreWebView2Async(env);

                var core = _webView.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = Debugger.IsAttached;
                core.Settings.IsSwipeNavigationEnabled = false;
                core.SetVirtualHostNameToFolderMapping(WebAssets.VirtualHost, webFolder, CoreWebView2HostResourceAccessKind.Allow);
                core.WebMessageReceived += (s, a) =>
                {
                    string json;
                    try { json = a.TryGetWebMessageAsString(); } catch { json = a.WebMessageAsJson; }
                    _bridge.OnMessage(json);
                };
                core.NewWindowRequested += (s, a) =>
                {
                    a.Handled = true;
                    try { Process.Start(new ProcessStartInfo(a.Uri) { UseShellExecute = true }); } catch { }
                };
                core.NavigationCompleted += (s, a) =>
                {
                    _uiReady = a.IsSuccess;
                    if (a.IsSuccess) { _status.Visible = false; _webView.BringToFront(); }
                    else ShowFatal("The tool's UI could not be loaded (WebView2 navigation error " + a.WebErrorStatus + ").", null);
                };
                core.Navigate("https://" + WebAssets.VirtualHost + "/index.html");
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                ShowFatal("The Microsoft Edge WebView2 Runtime is not installed on this machine. Install the Evergreen runtime from https://developer.microsoft.com/microsoft-edge/webview2/ and reopen the tool.", ex);
            }
            catch (DllNotFoundException ex)
            {
                // WebView2Loader.dll comes with XrmToolBox, not with this tool
                ShowFatal("WebView2Loader.dll could not be found. This tool uses the WebView2 components that come with XrmToolBox. Update XrmToolBox to the latest version and reopen the tool.", ex);
            }
            catch (Exception ex)
            {
                ShowFatal("The Oliver4 Icon Library could not start.", ex);
            }
        }

        /// <summary>Record which WebView2 SDK copy is in use (the one XrmToolBox ships) to help with support.</summary>
        private void LogWebView2Sdk()
        {
            try
            {
                var asm = typeof(CoreWebView2Environment).Assembly;
                LogInfo("WebView2 SDK " + asm.GetName().Version + " loaded from " + asm.Location);
            }
            catch (Exception ex)
            {
                LogInfo("WebView2 SDK location unknown: " + ex.Message);
            }
        }

        private void ShowFatal(string message, Exception ex)
        {
            _status.Visible = true;
            _status.BringToFront();
            _status.Text = message + (ex != null ? Environment.NewLine + Environment.NewLine + ex.Message : string.Empty);
            if (ex != null) LogError(message + " " + ex);
        }

        private void PostToUi(string json)
        {
            if (IsDisposed || _webView == null) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(PostToUi), json); return; }
            try { if (_webView.CoreWebView2 != null) _webView.CoreWebView2.PostWebMessageAsString(json); }
            catch (Exception ex) { LogError("PostToUi failed: " + ex.Message); }
        }

        private ContextInfo GetContext()
        {
            var detail = ConnectionDetail;
            var connected = Service != null && detail != null;
            string url = null, name = null, user = null;
            if (connected)
            {
                try { name = detail.OrganizationFriendlyName; } catch { }
                try { url = TrimUrl(detail.WebApplicationUrl); } catch { }
                if (string.IsNullOrEmpty(url)) { try { url = TrimUrl(detail.OrganizationServiceUrl); } catch { } }
                try { user = detail.UserName; } catch { }
            }
            return new ContextInfo { Version = ToolInfo.Version, Connected = connected, OrgUrl = url, OrgName = name, UserName = user };
        }

        private static string TrimUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (Uri.TryCreate(url, UriKind.Absolute, out var u)) return u.Host;
            return url;
        }

        /// <summary>Called by XrmToolBox when the tool is opened while connected, and whenever the connection changes.</summary>
        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            base.UpdateConnection(newService, detail, actionName, parameter);
            _dataverse = newService != null ? new DataverseService(newService) : null;
            if (_bridge != null && _uiReady)
                _bridge.SendEvent("context", new { context = GetContext(), reload = true });
        }

        public override void ClosingPlugin(PluginCloseInfo info)
        {
            base.ClosingPlugin(info);
            if (info.Cancel) return;
            try { _webView?.Dispose(); } catch { }
            _webView = null;
        }
    }
}
