using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

namespace Oliver4.IconLibrary
{
    /// <summary>
    /// Loads the bundled icon catalogue (embedded resource Web/catalogue.json.gz) so the host can
    /// look up the SVG for an icon id independently of the UI.
    /// </summary>
    public sealed class IconCatalogue
    {
        private static readonly Lazy<IconCatalogue> _instance = new Lazy<IconCatalogue>(Load);
        public static IconCatalogue Instance => _instance.Value;

        public Catalogue Data { get; }
        private readonly Dictionary<string, CatalogueIcon> _byId;
        private readonly Dictionary<string, CatalogueIcon> _bySvg;

        private IconCatalogue(Catalogue data)
        {
            Data = data;
            _byId = data.Icons.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
            _bySvg = new Dictionary<string, CatalogueIcon>(StringComparer.Ordinal);
            foreach (var i in data.Icons)
            {
                var key = SvgValidator.Canonical(i.Svg);
                if (!_bySvg.ContainsKey(key)) _bySvg[key] = i;
            }
        }

        public CatalogueIcon Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _byId.TryGetValue(id, out var icon) ? icon : null;
        }

        /// <summary>Finds the catalogue icon whose normalised SVG matches the given SVG text, or null.</summary>
        public CatalogueIcon MatchSvg(string svg)
        {
            if (string.IsNullOrEmpty(svg)) return null;
            return _bySvg.TryGetValue(SvgValidator.Canonical(svg), out var icon) ? icon : null;
        }

        public static string ReadEmbeddedText(string logicalName)
        {
            using (var s = OpenEmbedded(logicalName))
            using (var r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        public static Stream OpenEmbedded(string logicalName)
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => string.Equals(n.Replace('\\', '/'), logicalName, StringComparison.OrdinalIgnoreCase));
            if (name == null) throw new FileNotFoundException("Embedded resource not found: " + logicalName);
            return asm.GetManifestResourceStream(name);
        }

        private static IconCatalogue Load()
        {
            using (var gz = OpenEmbedded("Web/catalogue.json.gz"))
            using (var un = new GZipStream(gz, CompressionMode.Decompress))
            using (var r = new StreamReader(un, Encoding.UTF8))
            {
                var json = r.ReadToEnd();
                var data = JsonConvert.DeserializeObject<Catalogue>(json);
                return new IconCatalogue(data);
            }
        }
    }
}
