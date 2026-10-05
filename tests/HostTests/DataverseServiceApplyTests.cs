using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace Oliver4.IconLibrary.Tests
{
    /// <summary>Apply: processing, progress, failure reporting and refresh.</summary>
    public class DataverseServiceApplyTests
    {
        private readonly Fixture f = new Fixture();

        private Entity Wr(string name) => f.Org.WebResources.Single(w => w.GetAttributeValue<string>("name") == name);

        [Test("AP-01")] public void CreateWritesTheNormalisedIconIntoTheSolution()
        {
            var r = f.Service.Apply(f.Request("create"), null);
            Assert.True(r.Success, r.Error);
            Assert.Equal("webresource:done,solution:done,table:done,publish:done", Fixture.Statuses(r));
            var wr = Wr("contoso_/icons/stack_regular.svg");
            Assert.Equal(11, wr.GetAttributeValue<OptionSetValue>("webresourcetype").Value);
            Assert.Equal(Fixture.Svg("stack"), f.Org.SvgOf(wr.Id));
            Assert.Equal("Stack", wr.GetAttributeValue<string>("displayname"));
            Assert.True(f.Org.IsComponent(f.Solution, 61, wr.Id), "web resource must be in the solution");
            Assert.Equal("added when created", r.Steps[1].Message);
            Assert.Equal("contoso_/icons/stack_regular.svg", f.Org.Table("contoso_batch").IconVectorName);
            Assert.Equal(wr.Id, r.WebResourceId);
            Assert.True(r.TableUpdated);
        }

        [Test("AP-02")] public void CreateUsesTheDisplayNameGiven()
        {
            f.Service.Apply(f.Request("create", displayName: "  Batch icon  "), null);
            Assert.Equal("Batch icon", Wr("contoso_/icons/stack_regular.svg").GetAttributeValue<string>("displayname"));
        }

        [Test("AP-03")] public void TableIsUpdatedInTheSolutionContext()
        {
            f.Service.Apply(f.Request("create"), null);
            var ue = f.Org.CallsOf<UpdateEntityRequest>().Single();
            Assert.Equal("contoso_core", ue.SolutionUniqueName);
            Assert.Equal(true, ue.MergeLabels);
            Assert.Equal("contoso_batch", ue.Entity.LogicalName);
        }

        [Test("AP-04")] public void PublishIsTargetedAtTheTableAndWebResourceOnly()
        {
            var r = f.Service.Apply(f.Request("create"), null);
            var xml = f.Org.CallsOf<PublishXmlRequest>().Single().ParameterXml;
            Assert.Equal("<importexportxml><entities><entity>contoso_batch</entity></entities><webresources><webresource>{" + r.WebResourceId + "}</webresource></webresources></importexportxml>", xml);
            Assert.Equal(0, f.Org.Calls.OfType<OrganizationRequest>().Count(c => c.RequestName == "PublishAllXml" || c is PublishAllXmlRequest));
        }

        [Test("AP-05")] public void SaveWithoutPublishSkipsThePublishStep()
        {
            var r = f.Service.Apply(f.Request("create", publish: false), null);
            Assert.True(r.Success);
            Assert.Equal("skipped", r.Steps.Last().Status);
            Assert.Equal(0, f.Org.CallsOf<PublishXmlRequest>().Count());
        }

        [Test("AP-06")] public void DefaultSolutionCreatesWithoutSolutionParameters()
        {
            var r = f.Service.Apply(f.Request("create", defaultSolution: true, name: "cr7a2_/icons/stack_regular.svg"), null);
            Assert.True(r.Success, r.Error);
            Assert.False(f.Org.CallsOf<CreateRequest>().Single().Parameters.Contains("SolutionUniqueName"));
            Assert.Null(f.Org.CallsOf<UpdateEntityRequest>().Single().SolutionUniqueName);
            Assert.Equal(0, f.Org.CallsOf<AddSolutionComponentRequest>().Count());
            Assert.Equal("Default Solution", r.Steps[1].Message);
        }

        [Test("AP-07")] public void CreateRetryReusesTheSameNameWhenContentMatches()
        {
            // First attempt created the web resource but failed on the table (e.g. missing privilege).
            f.Org.FailOn["UpdateEntity"] = new InvalidOperationException("Principal user is missing prvWriteEntity privilege");
            var first = f.Service.Apply(f.Request("create"), null);
            Assert.False(first.Success);
            f.Org.FailOn.Clear();
            var retry = f.Service.Apply(f.Request("create"), null);
            Assert.True(retry.Success, retry.Error);
            Assert.Equal("already existed with this icon, reused", retry.Steps[0].Message);
            Assert.Equal(1, f.Org.WebResources.Count(w => w.GetAttributeValue<string>("name") == "contoso_/icons/stack_regular.svg"));
            Assert.Equal("already in solution", retry.Steps[1].Message);
        }

        [Test("AP-08")] public void CreateRefusesAnExistingNameWithDifferentContent()
        {
            var r = f.Service.Apply(f.Request("create", name: Fixture.PersonWrName), null);
            Assert.False(r.Success);
            Assert.Equal("webresource:failed,solution:pending,table:pending,publish:pending", Fixture.Statuses(r));
            Assert.Contains("already exists with different content", r.Steps[0].Message);
            Assert.Equal(Fixture.Svg("person"), f.Org.SvgOf(f.PersonWr), "existing web resource must not be touched");
        }

        [Test("AP-09")] public void UpdateReplacesContentAndDoesNotTouchSolutionMembership()
        {
            var r = f.Service.Apply(f.Request("update", table: "contoso_rule", name: Fixture.PersonWrName, webResourceId: f.PersonWr), null);
            Assert.True(r.Success, r.Error);
            Assert.Equal("webresource:done,table:done,publish:done", Fixture.Statuses(r));
            Assert.Equal(Fixture.Svg("stack"), f.Org.SvgOf(f.PersonWr));
            Assert.Equal("already pointed at this web resource", r.Steps[1].Message);
            Assert.Equal(0, f.Org.CallsOf<UpdateEntityRequest>().Count(), "no metadata update needed when the table already points at it");
        }

        [Test("AP-10")] public void UpdateWithoutIdFailsCleanly()
        {
            var r = f.Service.Apply(f.Request("update", table: "contoso_rule", name: Fixture.PersonWrName), null);
            Assert.False(r.Success);
            Assert.Contains("No web resource id", r.Steps[0].Message);
        }

        [Test("AP-11")] public void ReuseAddsTheExistingWebResourceToTheSolution()
        {
            var r = f.Service.Apply(f.Request("reuse", icon: "gavel", name: null, webResourceId: f.ManagedWr), null);
            Assert.True(r.Success, r.Error);
            Assert.True(f.Org.IsComponent(f.Solution, 61, f.ManagedWr));
            Assert.Equal(1, f.Org.CallsOf<AddSolutionComponentRequest>().Count());
            Assert.Equal(false, f.Org.CallsOf<AddSolutionComponentRequest>().Single().AddRequiredComponents);
            Assert.Equal("msdyn_/icons/gavel.svg", f.Org.Table("contoso_batch").IconVectorName, "table points at the reused web resource's real name");
        }

        [Test("AP-12")] public void ReuseRefusesWhenContentNoLongerMatches()
        {
            var r = f.Service.Apply(f.Request("reuse", icon: "stack", webResourceId: f.PersonWr), null);
            Assert.False(r.Success);
            Assert.Contains("no longer matches", r.Steps[0].Message);
            Assert.Null(f.Org.Table("contoso_batch").IconVectorName);
        }

        [Test("AP-13")] public void FailureReportsExactlyWhichStepsCompleted()
        {
            f.Org.FailOn["UpdateEntity"] = new InvalidOperationException("Principal user (Id=1) is missing prvWriteEntity privilege\r\nstack...");
            var r = f.Service.Apply(f.Request("create"), null);
            Assert.False(r.Success);
            Assert.False(r.TableUpdated);
            Assert.Equal("webresource:done,solution:done,table:failed,publish:pending", Fixture.Statuses(r));
            Assert.Equal("insufficient privileges", r.Steps[2].Message);
            Assert.Contains("Update table icon failed", r.Error);
            Assert.NotNull(r.Steps[2].Error);
            Assert.True(r.Steps[2].Error.Length <= 4000);
            Assert.NotNull(r.WebResourceId);
        }

        [Test("AP-14")] public void FailureAtPublishStillReportsTableUpdated()
        {
            f.Org.FailOn["PublishXml"] = new InvalidOperationException("publish timeout");
            var r = f.Service.Apply(f.Request("create"), null);
            Assert.False(r.Success);
            Assert.True(r.TableUpdated, "UI refreshes the table list when the table was changed");
            Assert.Equal("publish timeout", r.Steps[3].Message);
        }

        [Test("AP-15")] public void LongErrorMessagesAreShortened()
        {
            f.Org.FailOn["UpdateEntity"] = new InvalidOperationException(new string('x', 200));
            var r = f.Service.Apply(f.Request("create"), null);
            Assert.Equal(118, r.Steps[2].Message.Length);
        }

        [Test("AP-16")] public void ProgressReportsEachTransitionAsSnapshots()
        {
            var seen = new List<List<ApplyStep>>();
            f.Service.Apply(f.Request("create"), s => seen.Add(s));
            var runningKeys = seen.Select(s => s.FirstOrDefault(x => x.Status == "running")?.Key).Where(k => k != null).ToList();
            Assert.SequenceEqual(new[] { "webresource", "solution", "table", "publish" }, runningKeys);
            Assert.Equal("pending", seen[0][1].Status, "snapshots must not change after being sent");
        }

        [Test("AP-17")] public void UnknownIconOrActionIsRejected()
        {
            Assert.Throws<InvalidOperationException>(() => f.Service.Apply(f.Request("create", icon: "nope"), null));
            var r = f.Service.Apply(f.Request("delete"), null);
            Assert.False(r.Success);
            Assert.Contains("Unknown action", r.Steps[0].Message);
            Assert.Equal(0, f.Org.CallsOf<CreateRequest>().Count());
        }

        [Test("AP-18")] public void TableListReflectsTheChangeWithoutReReadingMetadata()
        {
            f.Service.GetTables(f.Solution, "contoso_core");
            f.Service.Apply(f.Request("create"), null);
            var t = f.Service.GetTables(f.Solution, "contoso_core").Tables.Single(x => x.LogicalName == "contoso_batch");
            Assert.Equal("custom", t.IconState);
            Assert.Equal(Fixture.Svg("stack"), t.WebResource.Svg);
            Assert.Equal(1, f.Org.RetrieveAllEntitiesCount);
        }

        [Test("AP-19")] public void AfterAFailureTheNextReadIsFresh()
        {
            f.Service.GetTables(f.Solution, "contoso_core");
            f.Org.FailOn["UpdateEntity"] = new InvalidOperationException("boom");
            f.Service.Apply(f.Request("create"), null);
            f.Service.GetTables(f.Solution, "contoso_core");
            Assert.Equal(2, f.Org.RetrieveAllEntitiesCount);
        }

        [Test("AP-20")] public void NoDescriptionIsWrittenOnCreateOrUpdate()
        {
            f.Service.Apply(f.Request("create"), null);
            Assert.False(Wr("contoso_/icons/stack_regular.svg").Contains("description"), "create must not set a description");
            f.Service.Apply(f.Request("update", table: "contoso_rule", name: Fixture.PersonWrName, webResourceId: f.PersonWr), null);
            Assert.False(Wr(Fixture.PersonWrName).Contains("description"), "update must not set a description");
        }
    }
}
