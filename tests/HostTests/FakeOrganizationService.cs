using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Oliver4.IconLibrary.Tests
{
    /// <summary>
    /// In-memory stand-in for a Dataverse environment. Implements only what DataverseService uses:
    /// QueryExpression over solution / publisher / solutioncomponent / webresource (Equal and In conditions,
    /// one inner link for solution -> publisher, TopCount), Retrieve/Update of webresource, and the
    /// RetrieveAllEntities, RetrieveEntity, UpdateEntity, Create, AddSolutionComponent and PublishXml messages.
    /// Every call is recorded so tests can assert what the tool sent.
    /// </summary>
    public sealed class FakeOrganizationService : IOrganizationService
    {
        public readonly List<Entity> Solutions = new List<Entity>();
        public readonly List<Entity> Publishers = new List<Entity>();
        public readonly List<Entity> SolutionComponents = new List<Entity>();
        public readonly List<Entity> WebResources = new List<Entity>();
        public readonly List<EntityMetadata> Tables = new List<EntityMetadata>();

        public readonly List<object> Calls = new List<object>();          // OrganizationRequest or QueryExpression or "Update:webresource" etc.
        public readonly Dictionary<string, Exception> FailOn = new Dictionary<string, Exception>(); // request name -> exception to throw
        public int RetrieveAllEntitiesCount;
        public int ContentReads;          // number of webresource rows returned with the content column
        public int Delay;                 // ms added to every call (used by the concurrency test)
        private int _inFlight;
        public int MaxConcurrent;

        // ------------------------------------------------------------------ seeding helpers

        public Guid AddPublisher(string name, string prefix, bool readOnly = false)
        {
            var e = new Entity("publisher", Guid.NewGuid());
            e["friendlyname"] = name; e["uniquename"] = name.Replace(" ", ""); e["customizationprefix"] = prefix; e["isreadonly"] = readOnly;
            Publishers.Add(e);
            return e.Id;
        }

        public Guid AddSolution(string uniqueName, string name, Guid publisherId, bool managed = false, bool visible = true)
        {
            var e = new Entity("solution", Guid.NewGuid());
            e["uniquename"] = uniqueName; e["friendlyname"] = name; e["version"] = "1.0.0.0";
            e["publisherid"] = new EntityReference("publisher", publisherId); e["ismanaged"] = managed; e["isvisible"] = visible;
            Solutions.Add(e);
            return e.Id;
        }

        public void AddComponent(Guid solutionId, int componentType, Guid objectId)
        {
            var e = new Entity("solutioncomponent", Guid.NewGuid());
            e["solutionid"] = new EntityReference("solution", solutionId); e["componenttype"] = new OptionSetValue(componentType); e["objectid"] = objectId;
            SolutionComponents.Add(e);
        }

        public Guid AddWebResource(string name, string svg, bool managed = false, int type = 11)
        {
            var e = new Entity("webresource", Guid.NewGuid());
            e["name"] = name; e["displayname"] = name; e["webresourcetype"] = new OptionSetValue(type);
            e["ismanaged"] = managed; e["iscustomizable"] = new BooleanManagedProperty(!managed);
            e["content"] = svg == null ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
            WebResources.Add(e);
            return e.Id;
        }

        public EntityMetadata AddTable(string logicalName, string displayName, bool custom = true, string icon = null,
            bool intersect = false, bool customizable = true, bool managed = false, Guid? inSolution = null)
        {
            var md = new EntityMetadata { LogicalName = logicalName, SchemaName = logicalName, MetadataId = Guid.NewGuid(), IconVectorName = icon };
            md.DisplayName = new Label(new LocalizedLabel(displayName, 1033), new[] { new LocalizedLabel(displayName, 1033) });
            md.IsCustomizable = new BooleanManagedProperty(customizable);
            SetHidden(md, "IsCustomEntity", (bool?)custom);
            SetHidden(md, "IsIntersect", (bool?)intersect);
            SetHidden(md, "IsManaged", (bool?)managed);
            SetHidden(md, "IsLogicalEntity", (bool?)false);
            Tables.Add(md);
            if (inSolution.HasValue) AddComponent(inSolution.Value, 1, md.MetadataId.Value);
            return md;
        }

        /// <summary>EntityMetadata has non-public setters for read-only platform properties; tests set them by reflection.</summary>
        public static void SetHidden(object target, string property, object value)
        {
            var p = target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var setter = p?.GetSetMethod(true);
            if (setter != null) { setter.Invoke(target, new[] { value }); return; }
            var field = target.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(f => f.Name.IndexOf(property, StringComparison.OrdinalIgnoreCase) >= 0);
            if (field == null) throw new InvalidOperationException("Cannot set " + property);
            field.SetValue(target, value);
        }

        public string SvgOf(Guid webResourceId)
        {
            var c = WebResources.First(w => w.Id == webResourceId).GetAttributeValue<string>("content");
            return c == null ? null : Encoding.UTF8.GetString(Convert.FromBase64String(c));
        }

        public EntityMetadata Table(string logicalName) => Tables.First(t => t.LogicalName == logicalName);

        public bool IsComponent(Guid solutionId, int type, Guid objectId) =>
            SolutionComponents.Any(c => c.GetAttributeValue<EntityReference>("solutionid").Id == solutionId
                                     && c.GetAttributeValue<OptionSetValue>("componenttype").Value == type
                                     && c.GetAttributeValue<Guid>("objectid") == objectId);

        public IEnumerable<T> CallsOf<T>() => Calls.OfType<T>();

        // ------------------------------------------------------------------ IOrganizationService

        private T Track<T>(object call, string name, Func<T> body)
        {
            var now = Interlocked.Increment(ref _inFlight);
            lock (Calls) { Calls.Add(call); if (now > MaxConcurrent) MaxConcurrent = now; }
            try
            {
                if (Delay > 0) Thread.Sleep(Delay);
                if (name != null && FailOn.TryGetValue(name, out var ex)) throw ex;
                return body();
            }
            finally { Interlocked.Decrement(ref _inFlight); }
        }

        public Guid Create(Entity entity) => ((CreateResponse)Execute(new CreateRequest { Target = entity })).id;

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => Track("Retrieve:" + entityName, "Retrieve", () =>
        {
            var e = Store(entityName).FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException(entityName + " With Id = " + id + " Does Not Exist");
            return Project(e, columnSet);
        });

        public void Update(Entity entity) => Track<object>("Update:" + entity.LogicalName, "Update", () =>
        {
            var e = Store(entity.LogicalName).FirstOrDefault(x => x.Id == entity.Id) ?? throw new InvalidOperationException("Does Not Exist");
            foreach (var a in entity.Attributes) e[a.Key] = a.Value;
            return null;
        });

        public void Delete(string entityName, Guid id) => throw new NotSupportedException("the tool never deletes");
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();

        public EntityCollection RetrieveMultiple(QueryBase query) => Track(query, "RetrieveMultiple:" + ((QueryExpression)query).EntityName, () =>
        {
            var q = (QueryExpression)query;
            IEnumerable<Entity> rows = Store(q.EntityName).Where(e => Matches(e, q.Criteria));
            var list = new List<Entity>();
            foreach (var e in rows)
            {
                var p = Project(e, q.ColumnSet);
                var ok = true;
                foreach (var link in q.LinkEntities)
                {
                    var from = e[link.LinkFromAttributeName];
                    var fromId = from is EntityReference er ? er.Id : (Guid)from;
                    var target = Store(link.LinkToEntityName).FirstOrDefault(x => x.Id == fromId);
                    if (target == null) { if (link.JoinOperator == JoinOperator.Inner) ok = false; continue; }
                    foreach (var col in link.Columns.Columns)
                        if (target.Contains(col)) p[link.EntityAlias + "." + col] = new AliasedValue(link.LinkToEntityName, col, target[col]);
                }
                if (!ok) continue;
                if (q.EntityName == "webresource" && q.ColumnSet.Columns.Contains("content")) ContentReads++;
                list.Add(p);
            }
            foreach (var order in q.Orders.AsEnumerable().Reverse())
                list = (order.OrderType == OrderType.Ascending
                    ? list.OrderBy(x => x.GetAttributeValue<object>(order.AttributeName)?.ToString(), StringComparer.OrdinalIgnoreCase)
                    : list.OrderByDescending(x => x.GetAttributeValue<object>(order.AttributeName)?.ToString(), StringComparer.OrdinalIgnoreCase)).ToList();
            if (q.TopCount.HasValue) list = list.Take(q.TopCount.Value).ToList();
            return new EntityCollection(list) { EntityName = q.EntityName, MoreRecords = false };
        });

        public OrganizationResponse Execute(OrganizationRequest request) => Track<OrganizationResponse>(request, request.RequestName ?? request.GetType().Name, () =>
        {
            switch (request)
            {
                case RetrieveAllEntitiesRequest _:
                {
                    RetrieveAllEntitiesCount++;
                    var r = new RetrieveAllEntitiesResponse();
                    r.Results["EntityMetadata"] = Tables.Select(CloneMetadata).ToArray();
                    return r;
                }
                case RetrieveEntityRequest re:
                {
                    var md = Tables.FirstOrDefault(t => t.LogicalName == re.LogicalName) ?? throw new InvalidOperationException("Could not find entity " + re.LogicalName);
                    var r = new RetrieveEntityResponse();
                    r.Results["EntityMetadata"] = CloneMetadata(md);
                    return r;
                }
                case UpdateEntityRequest ue:
                {
                    var md = Tables.First(t => t.LogicalName == ue.Entity.LogicalName);
                    md.IconVectorName = ue.Entity.IconVectorName;
                    if (!string.IsNullOrEmpty(ue.SolutionUniqueName))
                    {
                        var sol = SolutionByName(ue.SolutionUniqueName);
                        if (!IsComponent(sol.Id, 1, md.MetadataId.Value)) AddComponent(sol.Id, 1, md.MetadataId.Value);
                    }
                    return new UpdateEntityResponse();
                }
                case CreateRequest cr:
                {
                    var t = cr.Target;
                    if (t.LogicalName == "webresource" && WebResources.Any(w => string.Equals(w.GetAttributeValue<string>("name"), t.GetAttributeValue<string>("name"), StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("A record with these values already exists.");
                    var copy = new Entity(t.LogicalName, t.Id == Guid.Empty ? Guid.NewGuid() : t.Id);
                    foreach (var a in t.Attributes) copy[a.Key] = a.Value;
                    if (!copy.Contains("ismanaged")) copy["ismanaged"] = false;
                    if (!copy.Contains("iscustomizable")) copy["iscustomizable"] = new BooleanManagedProperty(true);
                    Store(t.LogicalName).Add(copy);
                    if (cr.Parameters.Contains("SolutionUniqueName"))
                        AddComponent(SolutionByName((string)cr.Parameters["SolutionUniqueName"]).Id, 61, copy.Id);
                    var r = new CreateResponse();
                    r.Results["id"] = copy.Id;
                    return r;
                }
                case AddSolutionComponentRequest ac:
                {
                    var sol = SolutionByName(ac.SolutionUniqueName);
                    if (!IsComponent(sol.Id, ac.ComponentType, ac.ComponentId)) AddComponent(sol.Id, ac.ComponentType, ac.ComponentId);
                    return new AddSolutionComponentResponse();
                }
                case PublishXmlRequest _:
                    return new PublishXmlResponse();
                default:
                    throw new NotSupportedException("Fake does not support " + request.GetType().Name);
            }
        });

        // ------------------------------------------------------------------ internals

        private Entity SolutionByName(string uniqueName) =>
            Solutions.FirstOrDefault(s => string.Equals(s.GetAttributeValue<string>("uniquename"), uniqueName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Solution " + uniqueName + " does not exist");

        private List<Entity> Store(string name)
        {
            switch (name)
            {
                case "solution": return Solutions;
                case "publisher": return Publishers;
                case "solutioncomponent": return SolutionComponents;
                case "webresource": return WebResources;
                default: throw new NotSupportedException("Fake has no table " + name);
            }
        }

        private static Entity Project(Entity e, ColumnSet cs)
        {
            var p = new Entity(e.LogicalName, e.Id);
            foreach (var a in e.Attributes)
                if (cs == null || cs.AllColumns || cs.Columns.Contains(a.Key)) p[a.Key] = a.Value;
            return p;
        }

        private static object Normalise(object v)
        {
            switch (v)
            {
                case EntityReference er: return er.Id;
                case OptionSetValue os: return os.Value;
                case BooleanManagedProperty b: return b.Value;
                default: return v;
            }
        }

        private static bool Matches(Entity e, FilterExpression f)
        {
            if (f == null) return true;
            var results = f.Conditions.Select(c =>
            {
                // the primary key column (e.g. webresourceid) is always queryable, as in Dataverse
                var actual = c.AttributeName == e.LogicalName + "id" ? e.Id : Normalise(e.Contains(c.AttributeName) ? e[c.AttributeName] : null);
                switch (c.Operator)
                {
                    case ConditionOperator.Equal:
                        var expected = Normalise(c.Values[0]);
                        return actual is string s ? string.Equals(s, expected as string, StringComparison.OrdinalIgnoreCase) : Equals(actual, expected);
                    case ConditionOperator.In:
                        return c.Values.Select(Normalise).Any(v => Equals(v, actual));
                    default:
                        throw new NotSupportedException("Fake does not support operator " + c.Operator);
                }
            }).Concat(f.Filters.Select(sub => Matches(e, sub))).ToList();
            return f.FilterOperator == LogicalOperator.And ? results.All(x => x) : results.Any(x => x);
        }

        private static EntityMetadata CloneMetadata(EntityMetadata md)
        {
            var c = new EntityMetadata { LogicalName = md.LogicalName, SchemaName = md.SchemaName, MetadataId = md.MetadataId, IconVectorName = md.IconVectorName, DisplayName = md.DisplayName, IsCustomizable = md.IsCustomizable };
            SetHidden(c, "IsCustomEntity", md.IsCustomEntity);
            SetHidden(c, "IsIntersect", md.IsIntersect);
            SetHidden(c, "IsManaged", md.IsManaged);
            SetHidden(c, "IsLogicalEntity", md.IsLogicalEntity);
            return c;
        }
    }
}
