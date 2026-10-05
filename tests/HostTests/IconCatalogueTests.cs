using System;
using System.Linq;

namespace Oliver4.IconLibrary.Tests
{
    /// <summary>The bundled catalogue loads and is internally consistent.</summary>
    public class IconCatalogueTests
    {
        private static Catalogue Data => IconCatalogue.Instance.Data;

        [Test("IC-01")] public void LoadsFromTheEmbeddedResource()
        {
            Assert.True(Data.Icons.Count > 2000, "icon count " + Data.Icons.Count);
            Assert.Equal("MIT", Data.Source.Licence);
            Assert.Contains("fluentui-system-icons", Data.Source.Repository);
        }

        [Test("IC-02")] public void IconIdsAreUnique() =>
            Assert.Equal(Data.Icons.Count, Data.Icons.Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        [Test("IC-03")] public void SizesAre16Or20Only() =>
            Assert.True(Data.Icons.All(i => i.Size == 16 || i.Size == 20));

        [Test("IC-04")] public void EveryIconHasACategoryThatExists()
        {
            var ids = Data.Categories.Select(c => c.Id).ToHashSet();
            Assert.True(Data.Icons.All(i => i.Categories != null && i.Categories.Count > 0 && i.Categories.All(ids.Contains)));
        }

        [Test("IC-05")] public void CategoryCountsMatchTheIcons()
        {
            foreach (var c in Data.Categories)
                Assert.Equal(Data.Icons.Count(i => i.Categories.Contains(c.Id)), c.Count, "category " + c.Id);
        }

        [Test("IC-06")] public void HasBetween15And25FiltersIncludingOther()
        {
            Assert.True(Data.Categories.Count >= 15 && Data.Categories.Count <= 25, "category count " + Data.Categories.Count);
            Assert.True(Data.Categories.Any(c => c.Id == "other"), "an 'Other' category is required so no icon is unreachable");
        }

        [Test("IC-07")] public void GetIsCaseInsensitiveAndNullSafe()
        {
            Assert.Equal("person", IconCatalogue.Instance.Get("PERSON").Id);
            Assert.Null(IconCatalogue.Instance.Get("no_such_icon"));
            Assert.Null(IconCatalogue.Instance.Get(null));
        }

        [Test("IC-08")] public void MatchSvgFindsAnIconDespiteFormatting()
        {
            var svg = IconCatalogue.Instance.Get("person").Svg.Replace("><", ">\n  <");
            Assert.Equal("person", IconCatalogue.Instance.MatchSvg(svg)?.Id);
            Assert.Null(IconCatalogue.Instance.MatchSvg(""));
        }

        [Test("IC-09")] public void EveryIconIsDrawnAt16WithTheSourceViewBox()
        {
            // Width/height are always 16; the viewBox is kept from the Fluent artwork (0 0 16 16 or 0 0 20 20 for
            // almost all, but a few Fluent sources are slightly wider, e.g. comment_edit is 0 0 17 16).
            foreach (var i in Data.Icons)
            {
                Assert.Contains("width=\"16\" height=\"16\"", i.Svg, i.Id);
                Assert.True(System.Text.RegularExpressions.Regex.IsMatch(i.Svg, "viewBox=\"0 0 \\d+(\\.\\d+)? \\d+(\\.\\d+)?\""), i.Id + " has no usable viewBox");
            }
        }
    }
}
