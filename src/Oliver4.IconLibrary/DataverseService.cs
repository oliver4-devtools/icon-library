using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Oliver4.IconLibrary
{
    /// <summary>
    /// All Dataverse reads and writes used by the tool. One instance per connection.
    /// Methods are synchronous and are called from a background thread by the bridge.
    /// </summary>
    public sealed class DataverseService
    {
        private const int WebResourceTypeSvg = 11;
        private const int ComponentTypeEntity = 1;
        private const int ComponentTypeWebResource = 61;
        private const string DefaultSolutionUniqueName = "Default";

        private readonly IOrganizationService _service;
        private readonly object _lock = new object();

        // Per-connection caches. Cleared by InvalidateCaches and after Apply.
        private List<EntityMetadata> _entities;
        private List<WebResourceInfo> _svgIndex;                 // every SVG web resource, content not yet read
        private HashSet<Guid> _contentLoaded = new HashSet<Guid>(); // ids whose Svg has been filled in
        private bool _allContentLoaded;

        public DataverseService(IOrganizationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        // ------------------------------------------------------------------ solutions and publishers

        public List<SolutionInfo> GetSolutions()
        {
            var q = new QueryExpression("solution")
            {
                ColumnSet = new ColumnSet("solutionid", "friendlyname", "uniquename", "version", "publisherid", "ismanaged", "isvisible"),
                NoLock = true
            };
            q.Criteria.AddCondition("ismanaged", ConditionOperator.Equal, false);
            q.Criteria.AddCondition("isvisible", ConditionOperator.Equal, true);
            var pub = q.AddLink("publisher", "publisherid", "publisherid", JoinOperator.Inner);
            pub.EntityAlias = "pub";
            pub.Columns = new ColumnSet("friendlyname", "customizationprefix", "uniquename");
            q.AddOrder("friendlyname", OrderType.Ascending);

            var list = new List<SolutionInfo>();
            foreach (var e in RetrieveAll(q))
            {
                var unique = e.GetAttributeValue<string>("uniquename");
                list.Add(new SolutionInfo
                {
                    Id = e.Id,
                    UniqueName = unique,
                    Name = e.GetAttributeValue<string>("friendlyname"),
                    Version = e.GetAttributeValue<string>("version"),
                    PublisherId = e.GetAttributeValue<EntityReference>("publisherid")?.Id ?? Guid.Empty,
                    PublisherName = Alias<string>(e, "pub.friendlyname"),
                    Prefix = Alias<string>(e, "pub.customizationprefix"),
                    IsDefault = string.Equals(unique, DefaultSolutionUniqueName, StringComparison.OrdinalIgnoreCase)
                });
            }
            // Default Solution last, everything else alphabetical
            return list.OrderBy(s => s.IsDefault ? 1 : 0).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public List<PublisherInfo> GetPublishers()
        {
            var q = new QueryExpression("publisher")
            {
                ColumnSet = new ColumnSet("publisherid", "friendlyname", "uniquename", "customizationprefix", "isreadonly"),
                NoLock = true
            };
            q.Criteria.AddCondition("isreadonly", ConditionOperator.Equal, false);
            q.AddOrder("friendlyname", OrderType.Ascending);
            return RetrieveAll(q).Select(e => new PublisherInfo
            {
                Id = e.Id,
                Name = e.GetAttributeValue<string>("friendlyname"),
                UniqueName = e.GetAttributeValue<string>("uniquename"),
                Prefix = e.GetAttributeValue<string>("customizationprefix")
            }).ToList();
        }

        // ------------------------------------------------------------------ tables

        /// <summary>
        /// Custom tables that are components of the solution, with their icon state.
        /// System tables are hidden because their icons cannot be changed (IsCustomEntity = false).
        /// </summary>
        public TablesResult GetTables(Guid solutionId, string solutionUniqueName)
        {
            var all = GetAllEntities(false);
            HashSet<Guid> inSolution = null;
            if (!string.Equals(solutionUniqueName, DefaultSolutionUniqueName, StringComparison.OrdinalIgnoreCase))
            {
                var q = new QueryExpression("solutioncomponent") { ColumnSet = new ColumnSet("objectid"), NoLock = true };
                q.Criteria.AddCondition("solutionid", ConditionOperator.Equal, solutionId);
                q.Criteria.AddCondition("componenttype", ConditionOperator.Equal, ComponentTypeEntity);
                inSolution = new HashSet<Guid>(RetrieveAll(q).Select(e => e.GetAttributeValue<Guid>("objectid")));
            }

            var webResources = GetSvgIndex(false).ToDictionary(w => w.Name, w => w, StringComparer.OrdinalIgnoreCase);
            var result = new TablesResult();
            foreach (var md in all)
            {
                if (md.MetadataId == null) continue;
                if (inSolution != null && !inSolution.Contains(md.MetadataId.Value)) continue;
                if (md.IsCustomEntity != true) { result.HiddenSystemCount++; continue; }
                if (md.IsIntersect == true || md.IsLogicalEntity == true) continue;
                // Hide tables whose definition cannot be customised (typically managed tables locked by their publisher)
                if (md.IsCustomizable != null && md.IsCustomizable.Value == false) continue;

                result.Tables.Add(ToTableInfo(md, webResources));
            }
            // The list only draws the icons the tables point at, so read the content for those and no others.
            LoadContent(result.Tables.Where(t => t.WebResource != null).Select(t => t.WebResource));
            result.Tables = result.Tables.OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            return result;
        }

        private static TableInfo ToTableInfo(EntityMetadata md, IDictionary<string, WebResourceInfo> webResources)
        {
            var t = new TableInfo
            {
                LogicalName = md.LogicalName,
                DisplayName = md.DisplayName?.UserLocalizedLabel?.Label ?? md.SchemaName ?? md.LogicalName,
                MetadataId = md.MetadataId ?? Guid.Empty,
                IconVectorName = md.IconVectorName,
                IsManaged = md.IsManaged == true,
                IsCustomizable = md.IsCustomizable?.Value ?? true
            };
            if (string.IsNullOrWhiteSpace(md.IconVectorName))
            {
                t.IconState = "none";
            }
            else if (webResources.TryGetValue(md.IconVectorName.Trim(), out var wr))
            {
                t.IconState = "custom";
                t.WebResource = wr;
            }
            else
            {
                t.IconState = "broken";
            }
            return t;
        }

        /// <summary>All custom tables (any solution) that point at the given web resource name.</summary>
        public List<TableRef> GetTablesUsingWebResource(string webResourceName)
        {
            return GetAllEntities(false)
                .Where(md => md.IsCustomEntity == true && string.Equals(md.IconVectorName?.Trim(), webResourceName, StringComparison.OrdinalIgnoreCase))
                .Select(md => new TableRef { LogicalName = md.LogicalName, DisplayName = md.DisplayName?.UserLocalizedLabel?.Label ?? md.LogicalName })
                .OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<EntityMetadata> GetAllEntities(bool refresh)
        {
            lock (_lock)
            {
                if (_entities != null && !refresh) return _entities;
                var resp = (RetrieveAllEntitiesResponse)_service.Execute(new RetrieveAllEntitiesRequest
                {
                    EntityFilters = EntityFilters.Entity,
                    RetrieveAsIfPublished = true
                });
                _entities = resp.EntityMetadata.ToList();
                return _entities;
            }
        }

        // ------------------------------------------------------------------ web resources

        /// <summary>
        /// Every SVG web resource in the environment, names and flags only (cached per connection).
        /// Content is read separately by <see cref="LoadContent"/> because the content column is the expensive part.
        /// </summary>
        private List<WebResourceInfo> GetSvgIndex(bool refresh)
        {
            lock (_lock)
            {
                if (_svgIndex != null && !refresh) return _svgIndex;
                var q = new QueryExpression("webresource")
                {
                    ColumnSet = new ColumnSet("webresourceid", "name", "displayname", "ismanaged", "iscustomizable"),
                    NoLock = true
                };
                q.Criteria.AddCondition("webresourcetype", ConditionOperator.Equal, WebResourceTypeSvg);
                q.AddOrder("name", OrderType.Ascending);
                _svgIndex = RetrieveAll(q).Select(e => new WebResourceInfo
                {
                    Id = e.Id,
                    Name = e.GetAttributeValue<string>("name"),
                    DisplayName = e.GetAttributeValue<string>("displayname"),
                    IsManaged = e.GetAttributeValue<bool?>("ismanaged") ?? false,
                    IsCustomizable = e.GetAttributeValue<BooleanManagedProperty>("iscustomizable")?.Value ?? true
                }).ToList();
                _contentLoaded = new HashSet<Guid>();
                _allContentLoaded = false;
                return _svgIndex;
            }
        }

        /// <summary>Fills in the Svg property of the given web resources, reading only the ones not read yet.</summary>
        private void LoadContent(IEnumerable<WebResourceInfo> items)
        {
            lock (_lock)
            {
                var byId = (_svgIndex ?? new List<WebResourceInfo>()).ToDictionary(w => w.Id, w => w);
                var need = items.Where(w => w != null && !_contentLoaded.Contains(w.Id)).Select(w => w.Id).Distinct().ToList();
                if (need.Count == 0) return;
                const int chunkSize = 250;
                for (var i = 0; i < need.Count; i += chunkSize)
                {
                    var chunk = need.Skip(i).Take(chunkSize).ToList();
                    var q = new QueryExpression("webresource")
                    {
                        ColumnSet = new ColumnSet("webresourceid", "content"),
                        NoLock = true
                    };
                    q.Criteria.AddCondition("webresourceid", ConditionOperator.In, chunk.Cast<object>().ToArray());
                    foreach (var e in RetrieveAll(q))
                    {
                        if (byId.TryGetValue(e.Id, out var wr)) wr.Svg = DecodeContent(e.GetAttributeValue<string>("content"));
                    }
                    foreach (var id in chunk) _contentLoaded.Add(id);
                }
            }
        }

        /// <summary>
        /// All SVG web resources with decoded content. Only needed where the content has to be compared
        /// across the whole environment (the reuse check on the confirmation panel).
        /// </summary>
        public List<WebResourceInfo> GetSvgWebResources(bool refresh)
        {
            lock (_lock)
            {
                var index = GetSvgIndex(refresh);
                if (!_allContentLoaded)
                {
                    LoadContent(index);
                    _allContentLoaded = true;
                }
                return index;
            }
        }

        /// <summary>
        /// Reads the two slow, environment-wide lists (table metadata and the web resource index) into the
        /// cache. Called in the background while the solution list is on screen so the table screen is quick.
        /// </summary>
        public void WarmCaches()
        {
            GetAllEntities(false);
            GetSvgIndex(false);
        }

        /// <summary>
        /// Reads the content of the next batch of SVG web resources not read yet and returns how many are still
        /// unread. The UI calls this repeatedly in the background after the table screen loads, so the reuse check
        /// on the confirmation panel finds the content already cached. Each call is short so other requests
        /// (which share one gate) never wait long behind it.
        /// </summary>
        public int WarmSvgContent(int batchSize)
        {
            if (batchSize < 1) batchSize = 250;
            lock (_lock)
            {
                var index = GetSvgIndex(false);
                if (_allContentLoaded) return 0;
                LoadContent(index.Where(w => !_contentLoaded.Contains(w.Id)).Take(batchSize).ToList());
                var remaining = index.Count(w => !_contentLoaded.Contains(w.Id));
                if (remaining == 0) _allContentLoaded = true;
                return remaining;
            }
        }

        public NameCheckResult CheckWebResourceName(string name)
        {
            var q = new QueryExpression("webresource") { ColumnSet = new ColumnSet("webresourceid", "ismanaged"), TopCount = 1, NoLock = true };
            q.Criteria.AddCondition("name", ConditionOperator.Equal, name);
            var e = _service.RetrieveMultiple(q).Entities.FirstOrDefault();
            return e == null
                ? new NameCheckResult { Exists = false }
                : new NameCheckResult { Exists = true, Id = e.Id, IsManaged = e.GetAttributeValue<bool?>("ismanaged") ?? false };
        }

        /// <summary>SVG web resources whose content is exactly this catalogue icon (candidates for reuse).</summary>
        public List<ExistingIconInfo> FindExistingIcon(string iconId, Guid solutionId, string solutionUniqueName)
        {
            var icon = IconCatalogue.Instance.Get(iconId) ?? throw new InvalidOperationException("Unknown icon: " + iconId);
            var wanted = SvgValidator.Canonical(icon.Svg);
            var matches = GetSvgWebResources(false).Where(w => w.Svg != null && SvgValidator.Canonical(w.Svg) == wanted).ToList();
            if (!matches.Any()) return new List<ExistingIconInfo>();

            var members = GetSolutionComponentIds(solutionId, solutionUniqueName, ComponentTypeWebResource, matches.Select(m => m.Id));
            return matches.Select(m => new ExistingIconInfo { Id = m.Id, Name = m.Name, IsManaged = m.IsManaged, InSolution = members.Contains(m.Id) }).ToList();
        }

        private HashSet<Guid> GetSolutionComponentIds(Guid solutionId, string solutionUniqueName, int componentType, IEnumerable<Guid> objectIds)
        {
            var ids = objectIds.Distinct().ToList();
            if (string.Equals(solutionUniqueName, DefaultSolutionUniqueName, StringComparison.OrdinalIgnoreCase))
                return new HashSet<Guid>(ids); // everything is in the Default Solution
            if (!ids.Any()) return new HashSet<Guid>();
            var q = new QueryExpression("solutioncomponent") { ColumnSet = new ColumnSet("objectid"), NoLock = true };
            q.Criteria.AddCondition("solutionid", ConditionOperator.Equal, solutionId);
            q.Criteria.AddCondition("componenttype", ConditionOperator.Equal, componentType);
            q.Criteria.AddCondition("objectid", ConditionOperator.In, ids.Cast<object>().ToArray());
            return new HashSet<Guid>(RetrieveAll(q).Select(e => e.GetAttributeValue<Guid>("objectid")));
        }

        // ------------------------------------------------------------------ apply

        /// <summary>
        /// Runs the create/update/reuse, add-to-solution, table update and optional publish steps in order.
        /// Reports each step through <paramref name="progress"/>. Never rolls back: on failure the result says
        /// exactly which steps completed.
        /// </summary>
        public ApplyResult Apply(ApplyRequest req, Action<List<ApplyStep>> progress)
        {
            var icon = IconCatalogue.Instance.Get(req.IconId) ?? throw new InvalidOperationException("Unknown icon: " + req.IconId);
            var svg = icon.Svg;
            var problems = SvgValidator.Validate(svg);
            if (problems.Any()) throw new InvalidOperationException("The bundled SVG failed validation: " + string.Join("; ", problems));

            var result = new ApplyResult();
            var steps = result.Steps;
            var stepWr = new ApplyStep
            {
                Key = "webresource",
                Label = req.Action == "create" ? "Create web resource" : req.Action == "update" ? "Update web resource" : "Verify existing web resource",
                Detail = req.WebResourceName
            };
            steps.Add(stepWr);
            ApplyStep stepSol = null;
            if (req.Action != "update")
            {
                stepSol = new ApplyStep { Key = "solution", Label = "Add to solution", Detail = req.SolutionUniqueName };
                steps.Add(stepSol);
            }
            var stepTable = new ApplyStep { Key = "table", Label = "Update table icon", Detail = req.TableLogicalName };
            steps.Add(stepTable);
            var stepPublish = new ApplyStep { Key = "publish", Label = req.Publish ? "Publish web resource and table" : "Publish", Status = req.Publish ? "pending" : "skipped", Detail = req.Publish ? "" : "not requested" };
            steps.Add(stepPublish);
            void Report() => progress?.Invoke(steps.Select(s => new ApplyStep { Key = s.Key, Label = s.Label, Status = s.Status, Detail = s.Detail, Message = s.Message, Error = s.Error }).ToList());

            Guid webResourceId;
            var createdInSolution = false;
            var isDefault = string.Equals(req.SolutionUniqueName, DefaultSolutionUniqueName, StringComparison.OrdinalIgnoreCase);

            // 1. web resource
            stepWr.Status = "running"; Report();
            try
            {
                switch (req.Action)
                {
                    case "create":
                    {
                        // If a retry finds the name already taken by the same icon, reuse it instead of failing.
                        var existing = CheckWebResourceName(req.WebResourceName);
                        if (existing.Exists)
                        {
                            var current = GetSvgWebResources(true).FirstOrDefault(w => w.Id == existing.Id);
                            if (current != null && SvgValidator.Canonical(current.Svg) == SvgValidator.Canonical(svg))
                            {
                                webResourceId = existing.Id.Value;
                                stepWr.Message = "already existed with this icon, reused";
                            }
                            else throw new InvalidOperationException("A web resource named " + req.WebResourceName + " already exists with different content.");
                        }
                        else
                        {
                            var wr = new Entity("webresource");
                            wr["name"] = req.WebResourceName;
                            // The UI makes the display name mandatory; fall back to the icon name so one is never blank.
                            wr["displayname"] = string.IsNullOrWhiteSpace(req.WebResourceDisplayName)
                                ? icon.Name
                                : req.WebResourceDisplayName.Trim();
                            wr["webresourcetype"] = new OptionSetValue(WebResourceTypeSvg);
                            wr["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
                            var create = new CreateRequest { Target = wr };
                            if (!isDefault) create.Parameters["SolutionUniqueName"] = req.SolutionUniqueName; // created straight into the solution
                            webResourceId = ((CreateResponse)_service.Execute(create)).id;
                            createdInSolution = !isDefault;
                        }
                        break;
                    }
                    case "update":
                    {
                        if (req.WebResourceId == null) throw new InvalidOperationException("No web resource id supplied for update.");
                        webResourceId = req.WebResourceId.Value;
                        var wr = new Entity("webresource", webResourceId);
                        wr["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
                        _service.Update(wr);
                        break;
                    }
                    case "reuse":
                    {
                        if (req.WebResourceId == null) throw new InvalidOperationException("No web resource id supplied for reuse.");
                        webResourceId = req.WebResourceId.Value;
                        var e = _service.Retrieve("webresource", webResourceId, new ColumnSet("name", "content"));
                        var existingSvg = DecodeContent(e.GetAttributeValue<string>("content"));
                        if (SvgValidator.Canonical(existingSvg) != SvgValidator.Canonical(svg))
                            throw new InvalidOperationException("The web resource content no longer matches the selected icon.");
                        req.WebResourceName = e.GetAttributeValue<string>("name");
                        break;
                    }
                    default:
                        throw new InvalidOperationException("Unknown action: " + req.Action);
                }
                stepWr.Status = "done"; Report();
            }
            catch (Exception ex)
            {
                return Fail(result, stepWr, ex, Report);
            }
            result.WebResourceId = webResourceId;

            // 2. solution membership
            if (stepSol != null)
            {
                stepSol.Status = "running"; Report();
                try
                {
                    if (isDefault)
                    {
                        stepSol.Message = "Default Solution";
                    }
                    else
                    {
                        var members = GetSolutionComponentIds(req.SolutionId, req.SolutionUniqueName, ComponentTypeWebResource, new[] { webResourceId });
                        if (members.Contains(webResourceId))
                        {
                            stepSol.Message = createdInSolution ? "added when created" : "already in solution";
                        }
                        else
                        {
                            _service.Execute(new AddSolutionComponentRequest
                            {
                                ComponentType = ComponentTypeWebResource,
                                ComponentId = webResourceId,
                                SolutionUniqueName = req.SolutionUniqueName,
                                AddRequiredComponents = false
                            });
                        }
                    }
                    stepSol.Status = "done"; Report();
                }
                catch (Exception ex)
                {
                    return Fail(result, stepSol, ex, Report);
                }
            }

            // 3. table
            stepTable.Status = "running"; Report();
            try
            {
                var resp = (RetrieveEntityResponse)_service.Execute(new RetrieveEntityRequest
                {
                    LogicalName = req.TableLogicalName,
                    EntityFilters = EntityFilters.Entity,
                    RetrieveAsIfPublished = true
                });
                var md = resp.EntityMetadata;
                if (string.Equals(md.IconVectorName, req.WebResourceName, StringComparison.Ordinal))
                {
                    stepTable.Message = "already pointed at this web resource";
                }
                else
                {
                    md.IconVectorName = req.WebResourceName;
                    var update = new UpdateEntityRequest { Entity = md, MergeLabels = true };
                    if (!isDefault) update.SolutionUniqueName = req.SolutionUniqueName;
                    _service.Execute(update);
                }
                stepTable.Status = "done"; result.TableUpdated = true; Report();
            }
            catch (Exception ex)
            {
                return Fail(result, stepTable, ex, Report);
            }

            // 4. targeted publish
            if (req.Publish)
            {
                stepPublish.Status = "running"; Report();
                try
                {
                    var xml = "<importexportxml>" +
                              "<entities><entity>" + System.Security.SecurityElement.Escape(req.TableLogicalName) + "</entity></entities>" +
                              "<webresources><webresource>{" + webResourceId + "}</webresource></webresources>" +
                              "</importexportxml>";
                    _service.Execute(new PublishXmlRequest { ParameterXml = xml });
                    stepPublish.Status = "done"; Report();
                }
                catch (Exception ex)
                {
                    return Fail(result, stepPublish, ex, Report);
                }
            }

            result.Success = true;
            // Only this table and this web resource changed, so patch the caches instead of dropping them.
            PatchCachesAfterApply(req, webResourceId, svg);
            return result;
        }

        private ApplyResult Fail(ApplyResult result, ApplyStep step, Exception ex, Action report)
        {
            step.Status = "failed";
            step.Message = ShortMessage(ex);
            step.Error = ex.ToString().Length > 4000 ? ex.ToString().Substring(0, 4000) : ex.ToString();
            result.Success = false;
            result.Error = step.Label + " failed: " + ex.Message;
            report();
            InvalidateCaches();
            return result;
        }

        private static string ShortMessage(Exception ex)
        {
            var m = ex.Message ?? ex.GetType().Name;
            if (m.IndexOf("privilege", StringComparison.OrdinalIgnoreCase) >= 0 || m.IndexOf("prv", StringComparison.OrdinalIgnoreCase) >= 0) return "insufficient privileges";
            var first = m.Split('\r', '\n')[0];
            return first.Length > 120 ? first.Substring(0, 117) + "…" : first;
        }

        /// <summary>
        /// Applies the one metadata change and the one web resource change to the cached lists, so the table
        /// screen can be redrawn without re-reading the environment.
        /// </summary>
        private void PatchCachesAfterApply(ApplyRequest req, Guid webResourceId, string svg)
        {
            lock (_lock)
            {
                var md = _entities?.FirstOrDefault(e => string.Equals(e.LogicalName, req.TableLogicalName, StringComparison.OrdinalIgnoreCase));
                if (md != null) md.IconVectorName = req.WebResourceName;

                if (_svgIndex != null)
                {
                    var wr = _svgIndex.FirstOrDefault(w => w.Id == webResourceId);
                    if (wr == null)
                    {
                        wr = new WebResourceInfo { Id = webResourceId, IsManaged = false, IsCustomizable = true };
                        _svgIndex.Add(wr);
                    }
                    wr.Name = req.WebResourceName;
                    if (!string.IsNullOrWhiteSpace(req.WebResourceDisplayName)) wr.DisplayName = req.WebResourceDisplayName.Trim();
                    wr.Svg = svg;
                    _contentLoaded.Add(webResourceId);
                }
            }
        }

        public void InvalidateCaches()
        {
            lock (_lock) { _entities = null; _svgIndex = null; _contentLoaded = new HashSet<Guid>(); _allContentLoaded = false; }
        }

        // ------------------------------------------------------------------ helpers

        private IEnumerable<Entity> RetrieveAll(QueryExpression query)
        {
            query.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };
            while (true)
            {
                var page = _service.RetrieveMultiple(query);
                foreach (var e in page.Entities) yield return e;
                if (!page.MoreRecords) yield break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
        }

        private static T Alias<T>(Entity e, string name)
        {
            return e.Contains(name) && e[name] is AliasedValue av && av.Value is T t ? t : default;
        }

        private static string DecodeContent(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            try
            {
                var bytes = Convert.FromBase64String(base64);
                var text = Encoding.UTF8.GetString(bytes);
                return text.TrimStart('﻿');
            }
            catch { return null; }
        }
    }
}
