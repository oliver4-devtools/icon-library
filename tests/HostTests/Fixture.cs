using System;
using System.Collections.Generic;
using System.Linq;

namespace Oliver4.IconLibrary.Tests
{
    /// <summary>A small, realistic environment used by most service tests.</summary>
    public sealed class Fixture
    {
        public readonly FakeOrganizationService Org = new FakeOrganizationService();
        public readonly DataverseService Service;
        public Guid ContosoPub, DefaultPub, MsPub, Solution, DefaultSolution, ManagedSolution;
        public Guid PersonWr, ManagedWr, OtherSvgWr;
        public const string PersonWrName = "contoso_/icons/person_regular.svg";

        public static string Svg(string iconId) => IconCatalogue.Instance.Get(iconId).Svg;

        public Fixture()
        {
            ContosoPub = Org.AddPublisher("Contoso", "contoso");
            DefaultPub = Org.AddPublisher("Default Publisher", "cr7a2");
            MsPub = Org.AddPublisher("Microsoft", "msdyn", readOnly: true);
            Solution = Org.AddSolution("contoso_core", "Core", ContosoPub);
            DefaultSolution = Org.AddSolution("Default", "Default Solution", DefaultPub);
            ManagedSolution = Org.AddSolution("msdyn_managed", "Some Managed", MsPub, managed: true);
            Org.AddSolution("Active", "Active Solution", DefaultPub, visible: false);

            PersonWr = Org.AddWebResource(PersonWrName, Svg("person"));
            ManagedWr = Org.AddWebResource("msdyn_/icons/gavel.svg", Svg("gavel"), managed: true);
            OtherSvgWr = Org.AddWebResource("contoso_/icons/other.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\" viewBox=\"0 0 16 16\"><circle r=\"3\"/></svg>");
            Org.AddWebResource("contoso_/scripts/form.js", null, type: 3);
            Org.AddComponent(Solution, 61, PersonWr);

            Org.AddTable("contoso_batch", "Batch", inSolution: Solution);                                   // no icon
            Org.AddTable("contoso_rule", "Rule", icon: PersonWrName, inSolution: Solution);                 // custom
            Org.AddTable("contoso_result", "Result", icon: "contoso_/icons/gone.svg", inSolution: Solution); // broken
            Org.AddTable("contoso_audit", "Audit", icon: " " + PersonWrName + " ", inSolution: Solution);   // custom, padded name
            Org.AddTable("account", "Account", custom: false, inSolution: Solution);                        // system
            Org.AddTable("contoso_batch_rule", "Batch Rule", intersect: true, inSolution: Solution);        // N:N
            Org.AddTable("contoso_locked", "Locked", customizable: false, inSolution: Solution);            // not customisable
            Org.AddTable("contoso_elsewhere", "Elsewhere", icon: PersonWrName);                             // not in Core
            Org.AddTable("contact", "Contact", custom: false);

            Service = new DataverseService(Org);
        }

        public ApplyRequest Request(string action, string icon = "stack", string table = "contoso_batch", bool publish = true, bool defaultSolution = false,
            string name = "contoso_/icons/stack_regular.svg", Guid? webResourceId = null, string displayName = null) => new ApplyRequest
        {
            TableLogicalName = table,
            TableDisplayName = table,
            IconId = icon,
            Action = action,
            Publish = publish,
            SolutionId = defaultSolution ? DefaultSolution : Solution,
            SolutionUniqueName = defaultSolution ? "Default" : "contoso_core",
            IsDefaultSolution = defaultSolution,
            WebResourceName = name,
            WebResourceDisplayName = displayName,
            WebResourceId = webResourceId
        };

        public static string Statuses(ApplyResult r) => string.Join(",", r.Steps.Select(s => s.Key + ":" + s.Status));
    }
}
