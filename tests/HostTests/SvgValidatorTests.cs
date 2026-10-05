using System.Linq;

namespace Oliver4.IconLibrary.Tests
{
    /// <summary>Every SVG written to Dataverse follows Microsoft's SVG icon rules.</summary>
    public class SvgValidatorTests
    {
        private const string Good = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\" viewBox=\"0 0 16 16\" fill=\"currentColor\"><path d=\"M1 1h14v14H1z\"/></svg>";

        private static string With(string replaceFrom, string replaceTo) => Good.Replace(replaceFrom, replaceTo);

        [Test("SV-01")] public void AcceptsANormalisedIcon() => Assert.Equal(0, SvgValidator.Validate(Good).Count, string.Join("; ", SvgValidator.Validate(Good)));

        [Test("SV-02")] public void AcceptsFillNone() => Assert.Equal(0, SvgValidator.Validate(With("<path ", "<path fill=\"none\" stroke=\"currentColor\" ")).Count);

        [Test("SV-03")] public void RejectsEmpty() => Assert.Contains("empty", SvgValidator.Validate("  ").Single());

        [Test("SV-04")] public void RejectsMalformedXml() => Assert.Contains("well-formed", SvgValidator.Validate("<svg><path></svg>").Single());

        [Test("SV-05")] public void RejectsNonSvgRoot() => Assert.Contains("not <svg>", SvgValidator.Validate("<html/>").Single());

        [Test("SV-06")] public void RejectsWrongSize() => Assert.Contains("width and height must be 16", string.Join("|", SvgValidator.Validate(With("width=\"16\"", "width=\"20\""))));

        [Test("SV-07")] public void RejectsMissingViewBox() => Assert.Contains("viewBox is missing", string.Join("|", SvgValidator.Validate(With(" viewBox=\"0 0 16 16\"", ""))));

        [Test("SV-08")] public void RejectsScriptElement() => Assert.Contains("<script>", string.Join("|", SvgValidator.Validate(With("</svg>", "<script>alert(1)</script></svg>"))));

        [Test("SV-09")] public void RejectsStyleElement() => Assert.Contains("<style>", string.Join("|", SvgValidator.Validate(With("</svg>", "<style>path{}</style></svg>"))));

        [Test("SV-10")] public void RejectsEventAttributes() => Assert.Contains("onload", string.Join("|", SvgValidator.Validate(With("<svg ", "<svg onload=\"x()\" "))));

        [Test("SV-11")] public void RejectsStyleAndClassAttributes()
        {
            var p = string.Join("|", SvgValidator.Validate(With("<path ", "<path style=\"a:b\" class=\"c\" ")));
            Assert.Contains("attribute style", p); Assert.Contains("attribute class", p);
        }

        [Test("SV-12")] public void RejectsHardCodedFill() => Assert.Contains("fill must be currentColor", string.Join("|", SvgValidator.Validate(With("<path ", "<path fill=\"#212121\" "))));

        [Test("SV-13")] public void RejectsHardCodedStroke() => Assert.Contains("stroke must be currentColor", string.Join("|", SvgValidator.Validate(With("<path ", "<path stroke=\"red\" "))));

        [Test("SV-14")] public void RejectsOver10KB()
        {
            var big = With("<path ", "<path data-pad=\"" + new string('a', 11 * 1024) + "\" ");
            Assert.Contains("larger than 10 KB", string.Join("|", SvgValidator.Validate(big)));
        }

        [Test("SV-15")] public void EveryBundledIconPassesValidation()
        {
            var bad = IconCatalogue.Instance.Data.Icons.Select(i => (i.Id, p: SvgValidator.Validate(i.Svg))).Where(x => x.p.Any()).ToList();
            Assert.Equal(0, bad.Count, "icons failing: " + string.Join(", ", bad.Take(5).Select(b => b.Id + " (" + string.Join("; ", b.p) + ")")));
        }

        // Canonical() decides whether an existing web resource "is" a catalogue icon (reuse detection, retry after partial failure).

        [Test("SV-16")] public void CanonicalIgnoresWhitespaceBetweenElements() =>
            Assert.Equal(SvgValidator.Canonical(Good), SvgValidator.Canonical(Good.Replace("><", ">\r\n   <")));

        [Test("SV-17")] public void CanonicalKeepsTheMarkupIntact() =>
            Assert.True(SvgValidator.Canonical(Good).StartsWith("<svg"), "Canonical must not drop characters from the SVG: " + SvgValidator.Canonical(Good).Substring(0, 12));

        [Test("SV-18")] public void CanonicalIgnoresXmlDeclaration() =>
            Assert.Equal(SvgValidator.Canonical(Good), SvgValidator.Canonical("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + Good));

        [Test("SV-19")] public void CanonicalIgnoresByteOrderMark() =>
            Assert.Equal(SvgValidator.Canonical(Good), SvgValidator.Canonical("﻿" + Good));

        [Test("SV-20")] public void CanonicalOfNullIsEmpty() => Assert.Equal("", SvgValidator.Canonical(null));
    }
}
