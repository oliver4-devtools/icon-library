using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Oliver4.IconLibrary
{
    // Data shapes exchanged with the HTML UI. Property names are camelCase on the wire (see HostBridge serializer settings).

    public class ContextInfo
    {
        public string Version { get; set; }
        public bool Connected { get; set; }
        public string OrgUrl { get; set; }
        public string OrgName { get; set; }
        public string UserName { get; set; }
    }

    public class SolutionInfo
    {
        public Guid Id { get; set; }
        public string UniqueName { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public Guid PublisherId { get; set; }
        public string PublisherName { get; set; }
        public string Prefix { get; set; }
        public bool IsDefault { get; set; }
    }

    public class PublisherInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string UniqueName { get; set; }
        public string Prefix { get; set; }
    }

    public class WebResourceInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public bool IsManaged { get; set; }
        public bool IsCustomizable { get; set; }
        /// <summary>Decoded SVG text. Null when the content could not be decoded.</summary>
        public string Svg { get; set; }
    }

    public class ExistingIconInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public bool IsManaged { get; set; }
        public bool InSolution { get; set; }
    }

    public class TableInfo
    {
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }
        public Guid MetadataId { get; set; }
        public string IconVectorName { get; set; }
        /// <summary>custom | none | broken</summary>
        public string IconState { get; set; }
        public WebResourceInfo WebResource { get; set; }
        public bool IsManaged { get; set; }
        public bool IsCustomizable { get; set; }
    }

    public class TablesResult
    {
        public List<TableInfo> Tables { get; set; } = new List<TableInfo>();
        public int HiddenSystemCount { get; set; }
    }

    public class TableRef
    {
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }
    }

    public class NameCheckResult
    {
        public bool Exists { get; set; }
        public Guid? Id { get; set; }
        public bool IsManaged { get; set; }
    }

    public class ApplyRequest
    {
        public string TableLogicalName { get; set; }
        public string TableDisplayName { get; set; }
        public string IconId { get; set; }
        /// <summary>create | update | reuse</summary>
        public string Action { get; set; }
        public bool Publish { get; set; }
        public Guid SolutionId { get; set; }
        public string SolutionUniqueName { get; set; }
        public bool IsDefaultSolution { get; set; }
        public string WebResourceName { get; set; }
        /// <summary>Display name for a newly created web resource. Null or blank falls back to the icon name.</summary>
        public string WebResourceDisplayName { get; set; }
        public Guid? WebResourceId { get; set; }
    }

    public class ApplyStep
    {
        public string Key { get; set; }
        public string Label { get; set; }
        /// <summary>pending | running | done | failed | skipped</summary>
        public string Status { get; set; } = "pending";
        public string Detail { get; set; }
        public string Message { get; set; }
        public string Error { get; set; }
    }

    public class ApplyResult
    {
        public bool Success { get; set; }
        public bool TableUpdated { get; set; }
        public List<ApplyStep> Steps { get; set; } = new List<ApplyStep>();
        public string Error { get; set; }
        public Guid? WebResourceId { get; set; }
    }

    public class AboutInfo
    {
        public string Version { get; set; }
        public string Notices { get; set; }
        public string Homepage { get; set; }
        public string Repository { get; set; }
    }

    // Catalogue (embedded catalogue.json.gz produced by build/build_catalogue.py)

    public class Catalogue
    {
        [JsonProperty("source")] public CatalogueSource Source { get; set; }
        [JsonProperty("categories")] public List<CatalogueCategory> Categories { get; set; }
        [JsonProperty("icons")] public List<CatalogueIcon> Icons { get; set; }
    }

    public class CatalogueSource
    {
        [JsonProperty("repository")] public string Repository { get; set; }
        [JsonProperty("commit")] public string Commit { get; set; }
        [JsonProperty("commitDate")] public string CommitDate { get; set; }
        [JsonProperty("licence")] public string Licence { get; set; }
    }

    public class CatalogueCategory
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("label")] public string Label { get; set; }
        [JsonProperty("count")] public int Count { get; set; }
    }

    public class CatalogueIcon
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("size")] public int Size { get; set; }
        [JsonProperty("keywords")] public List<string> Keywords { get; set; }
        [JsonProperty("categories")] public List<string> Categories { get; set; }
        [JsonProperty("svg")] public string Svg { get; set; }
    }
}
