using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Oliver4.IconLibrary
{
    /// <summary>
    /// Runtime check of an SVG before it is written to Dataverse. The bundled icons are normalised at
    /// build time (build/build_catalogue.py); this checks the same rules again before anything is written.
    /// Rules (Microsoft guidance for SVG icon web resources):
    ///   - fills are currentColor (or none), no hard-coded colours
    ///   - width and height are 16, viewBox present
    ///   - no style sheets, classes, scripts or event attributes
    ///   - no more than 10 KB
    /// </summary>
    public static class SvgValidator
    {
        public const int MaxBytes = 10 * 1024;
        private static readonly Regex HexColour = new Regex(@"#[0-9a-fA-F]{3,8}\b", RegexOptions.Compiled);

        public static IList<string> Validate(string svg)
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(svg)) { problems.Add("SVG is empty"); return problems; }
            if (Encoding.UTF8.GetByteCount(svg) > MaxBytes) problems.Add("SVG is larger than 10 KB");

            XDocument doc;
            try { doc = XDocument.Parse(svg); }
            catch (Exception ex) { problems.Add("SVG is not well-formed XML: " + ex.Message); return problems; }

            var root = doc.Root;
            if (root == null || root.Name.LocalName != "svg") { problems.Add("root element is not <svg>"); return problems; }
            if ((string)root.Attribute("width") != "16" || (string)root.Attribute("height") != "16") problems.Add("width and height must be 16");
            if (root.Attribute("viewBox") == null) problems.Add("viewBox is missing");

            foreach (var el in root.DescendantsAndSelf())
            {
                var n = el.Name.LocalName;
                if (n == "script" || n == "style") problems.Add("<" + n + "> element is not allowed");
                foreach (var a in el.Attributes())
                {
                    var an = a.Name.LocalName;
                    if (an.StartsWith("on", StringComparison.OrdinalIgnoreCase)) problems.Add("event attribute " + an + " is not allowed");
                    if (an == "style" || an == "class") problems.Add("attribute " + an + " is not allowed");
                    if (an == "fill" || an == "stroke")
                    {
                        var v = a.Value.Trim();
                        if (!v.Equals("currentColor", StringComparison.OrdinalIgnoreCase) && !v.Equals("none", StringComparison.OrdinalIgnoreCase))
                            problems.Add(an + " must be currentColor or none (found " + v + ")");
                    }
                    if (HexColour.IsMatch(a.Value) && an != "d") problems.Add("hard-coded colour in attribute " + an);
                }
            }
            return problems.Distinct().ToList();
        }

        /// <summary>Whitespace-insensitive form used to compare an existing web resource with a catalogue icon.</summary>
        public static string Canonical(string svg)
        {
            if (svg == null) return string.Empty;
            // Strip a byte order mark by character; string.StartsWith("\uFEFF") is culture-sensitive and matches every string.
            var s = svg.Trim().TrimStart('\uFEFF').Trim();
            s = Regex.Replace(s, @"<\?xml[^>]*\?>", string.Empty);
            s = Regex.Replace(s, @">\s+<", "><");
            s = Regex.Replace(s, @"\s+", " ");
            return s.Trim();
        }
    }
}
