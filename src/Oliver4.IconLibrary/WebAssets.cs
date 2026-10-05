using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace Oliver4.IconLibrary
{
    /// <summary>
    /// Extracts the embedded HTML/CSS/JS/image/data files to a per-user, per-version folder so WebView2 can serve
    /// them through a virtual host name. The XrmToolBox install folder may be read-only, so nothing is
    /// written next to the DLL.
    /// </summary>
    public static class WebAssets
    {
        public const string VirtualHost = "iconlibrary.oliver4.local";

        public static string RootFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Oliver4", "IconLibrary");

        public static string UserDataFolder => Path.Combine(RootFolder, "WebView2");

        public static string WebFolder => Path.Combine(RootFolder, "web", ToolInfo.Version);

        /// <summary>Extracts all Web/* resources. Re-extracts when the DLL is newer than the last extraction.</summary>
        public static string EnsureExtracted()
        {
            var folder = WebFolder;
            var marker = Path.Combine(folder, ".extracted");
            var asmPath = Assembly.GetExecutingAssembly().Location;
            var asmTime = File.Exists(asmPath) ? File.GetLastWriteTimeUtc(asmPath) : DateTime.MinValue;

            if (File.Exists(marker) && File.GetLastWriteTimeUtc(marker) >= asmTime && File.Exists(Path.Combine(folder, "index.html")) && File.Exists(Path.Combine(folder, "catalogue.json")))
                return folder;

            Directory.CreateDirectory(folder);
            var asm = Assembly.GetExecutingAssembly();
            foreach (var res in asm.GetManifestResourceNames())
            {
                var name = res.Replace('\\', '/');
                if (!name.StartsWith("Web/", StringComparison.OrdinalIgnoreCase)) continue;
                var rel = name.Substring(4);
                var target = Path.Combine(folder, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var s = asm.GetManifestResourceStream(res))
                {
                    if (rel.Equals("catalogue.json.gz", StringComparison.OrdinalIgnoreCase))
                    {
                        // the UI fetches plain JSON; the DLL carries it compressed
                        using (var gz = new GZipStream(s, CompressionMode.Decompress))
                        using (var f = File.Create(Path.Combine(folder, "catalogue.json")))
                            gz.CopyTo(f);
                    }
                    else
                    {
                        using (var f = File.Create(target)) s.CopyTo(f);
                    }
                }
            }
            File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
            File.SetLastWriteTimeUtc(marker, DateTime.UtcNow);
            return folder;
        }
    }

    public static class ToolInfo
    {
        public const string Homepage = "https://www.oliver4-devtools.com";
        public const string Repository = "https://github.com/oliver4-devtools/icon-library";
        public const string GitHubUser = "oliver4-devtools";
        public const string GitHubRepo = "icon-library";

        public static string Version
        {
            get
            {
                var asm = Assembly.GetExecutingAssembly();
                var info = asm.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                    .OfType<AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
                if (!string.IsNullOrEmpty(info))
                {
                    var plus = info.IndexOf('+');
                    return plus > 0 ? info.Substring(0, plus) : info;
                }
                var v = asm.GetName().Version;
                return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }
    }
}
