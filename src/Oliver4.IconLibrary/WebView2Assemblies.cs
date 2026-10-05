using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Oliver4.IconLibrary
{
    /// <summary>
    /// The tool uses the WebView2 SDK that XrmToolBox ships (Microsoft.Web.WebView2.Core / .WinForms and
    /// WebView2Loader.dll).
    ///
    /// The tool is compiled against the minimum WebView2 version XrmToolBoxPackage requires. If the copy XrmToolBox
    /// ships is newer, .NET Framework will not bind the strong-named reference on its own, so this handler hands back
    /// the copy already loaded in the process, or the one next to XrmToolBox.exe.
    ///
    /// Install() must run before any method that references WebView2 types is JIT-compiled. Plugin's static
    /// constructor does that, and it runs when XrmToolBox instantiates the plugin, before GetControl().
    /// </summary>
    public static class WebView2Assemblies
    {
        private static readonly object Gate = new object();
        private static bool _installed;

        public static void Install()
        {
            lock (Gate)
            {
                if (_installed) return;
                _installed = true;
                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            }
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var simple = new AssemblyName(args.Name).Name;
            if (!simple.StartsWith("Microsoft.Web.WebView2", StringComparison.OrdinalIgnoreCase)) return null;

            // already loaded (by XrmToolBox or another tool)? hand that back rather than loading a second copy
            var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => string.Equals(a.GetName().Name, simple, StringComparison.OrdinalIgnoreCase));
            if (loaded != null) return loaded;

            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, simple + ".dll");
            if (!File.Exists(path)) return null;
            try { return Assembly.LoadFrom(path); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("WebView2Assemblies: LoadFrom failed for " + path + ": " + ex.Message);
                return null;
            }
        }
    }
}
