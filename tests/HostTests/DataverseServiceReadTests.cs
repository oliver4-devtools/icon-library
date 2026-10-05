using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Oliver4.IconLibrary.Tests
{
    /// <summary>What the tool reads from Dataverse and how it presents it.</summary>
    public class DataverseServiceReadTests
    {
        private readonly Fixture f = new Fixture();

        [Test("DS-01")] public void SolutionsAreUnmanagedAndVisibleOnlyWithDefaultLast()
        {
            var s = f.Service.GetSolutions();
            Assert.SequenceEqual(new[] { "contoso_core", "Default" }, s.Select(x => x.UniqueName));
            Assert.True(s.Last().IsDefault);
            Assert.Equal("contoso", s[0].Prefix);
            Assert.Equal("Contoso", s[0].PublisherName);
            Assert.Equal(f.ContosoPub, s[0].PublisherId);
        }

        [Test("DS-02")] public void PublishersExcludeReadOnly()
        {
            var p = f.Service.GetPublishers();
            Assert.SequenceEqual(new[] { "Contoso", "Default Publisher" }, p.Select(x => x.Name));
        }

        [Test("DS-03")] public void TablesAreCustomTablesInTheSolutionOnly()
        {
            var r = f.Service.GetTables(f.Solution, "contoso_core");
            Assert.SequenceEqual(new[] { "Audit", "Batch", "Result", "Rule" }, r.Tables.Select(t => t.DisplayName));
            Assert.Equal(1, r.HiddenSystemCount);
        }

        [Test("DS-04")] public void DefaultSolutionShowsEveryCustomTable()
        {
            var r = f.Service.GetTables(f.DefaultSolution, "Default");
            Assert.SequenceEqual(new[] { "Audit", "Batch", "Elsewhere", "Result", "Rule" }, r.Tables.Select(t => t.DisplayName));
            Assert.Equal(2, r.HiddenSystemCount);
        }

        [Test("DS-05")] public void IconStatesAreCustomNoneOrBroken()
        {
            var t = f.Service.GetTables(f.Solution, "contoso_core").Tables.ToDictionary(x => x.LogicalName);
            Assert.Equal("none", t["contoso_batch"].IconState);
            Assert.Equal("custom", t["contoso_rule"].IconState);
            Assert.Equal(Fixture.PersonWrName, t["contoso_rule"].WebResource.Name);
            Assert.Equal(Fixture.Svg("person"), t["contoso_rule"].WebResource.Svg);
            Assert.Equal("broken", t["contoso_result"].IconState);
            Assert.Equal("custom", t["contoso_audit"].IconState, "IconVectorName with stray spaces still resolves");
        }

        [Test("DS-06")] public void TableListReadsContentOnlyForIconsItShows()
        {
            f.Service.GetTables(f.Solution, "contoso_core");
            Assert.Equal(1, f.Org.ContentReads, "only the one web resource used by the listed tables should be read with content");
        }

        [Test("DS-07")] public void MetadataIsCachedAcrossCalls()
        {
            f.Service.WarmCaches();
            f.Service.GetTables(f.Solution, "contoso_core");
            f.Service.GetTables(f.DefaultSolution, "Default");
            Assert.Equal(1, f.Org.RetrieveAllEntitiesCount);
            f.Service.InvalidateCaches();
            f.Service.GetTables(f.Solution, "contoso_core");
            Assert.Equal(2, f.Org.RetrieveAllEntitiesCount);
        }

        [Test("DS-08")] public void TablesUsingAWebResourceSpanAllSolutions()
        {
            var r = f.Service.GetTablesUsingWebResource(Fixture.PersonWrName);
            Assert.SequenceEqual(new[] { "contoso_audit", "contoso_elsewhere", "contoso_rule" }, r.Select(x => x.LogicalName));
        }

        [Test("DS-09")] public void NameCheckReportsExistsAndManaged()
        {
            Assert.False(f.Service.CheckWebResourceName("contoso_/icons/new.svg").Exists);
            var hit = f.Service.CheckWebResourceName("msdyn_/icons/gavel.svg");
            Assert.True(hit.Exists); Assert.True(hit.IsManaged); Assert.Equal(f.ManagedWr, hit.Id);
        }

        [Test("DS-10")] public void FindExistingIconMatchesContentAndSolutionMembership()
        {
            var person = f.Service.FindExistingIcon("person", f.Solution, "contoso_core");
            Assert.Equal(1, person.Count); Assert.True(person[0].InSolution);
            var gavel = f.Service.FindExistingIcon("gavel", f.Solution, "contoso_core");
            Assert.Equal(1, gavel.Count); Assert.False(gavel[0].InSolution); Assert.True(gavel[0].IsManaged);
            Assert.Equal(0, f.Service.FindExistingIcon("stack", f.Solution, "contoso_core").Count);
        }

        [Test("DS-11")] public void FindExistingIconInDefaultSolutionTreatsEverythingAsMember() =>
            Assert.True(f.Service.FindExistingIcon("gavel", f.DefaultSolution, "Default").Single().InSolution);

        [Test("DS-12")] public void FindExistingIconMatchesAReformattedCopy()
        {
            f.Org.AddWebResource("contoso_/icons/stack_pretty.svg", "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" + Fixture.Svg("stack").Replace("><", ">\r\n  <"));
            Assert.Equal(1, f.Service.FindExistingIcon("stack", f.Solution, "contoso_core").Count, "an SVG saved with an XML declaration and line breaks is still the same icon");
        }

        [Test("DS-13")] public void FindExistingIconRejectsUnknownIcon() =>
            Assert.Throws<InvalidOperationException>(() => f.Service.FindExistingIcon("nope", f.Solution, "contoso_core"));

        [Test("DS-14")] public void SolutionQueryFiltersManagedAndInvisible()
        {
            f.Service.GetSolutions();
            var q = f.Org.CallsOf<QueryExpression>().First(x => x.EntityName == "solution");
            var conds = q.Criteria.Conditions.ToDictionary(c => c.AttributeName, c => c.Values[0]);
            Assert.Equal(false, conds["ismanaged"]); Assert.Equal(true, conds["isvisible"]);
        }

        [Test("DS-15")] public void WarmSvgContentReadsInBatchesThenTheReuseCheckReadsNothing()
        {
            var total = f.Org.WebResources.Count(w => w.GetAttributeValue<OptionSetValue>("webresourcetype")?.Value == 11);
            Assert.True(total >= 2, "fixture needs at least two SVG web resources");
            var first = f.Service.WarmSvgContent(1);
            Assert.Equal(total - 1, first, "one batch of one read");
            var guard = 0;
            while (f.Service.WarmSvgContent(1) > 0 && guard++ < 100) { }
            Assert.Equal(0, f.Service.WarmSvgContent(1), "nothing left once everything is read");
            var reads = f.Org.ContentReads;
            Assert.Equal(1, f.Service.FindExistingIcon("person", f.Solution, "contoso_core").Count);
            Assert.Equal(reads, f.Org.ContentReads, "the reuse check must use the warmed content");
        }
    }
}
