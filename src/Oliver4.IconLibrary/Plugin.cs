using System.ComponentModel.Composition;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace Oliver4.IconLibrary
{
    /// <summary>
    /// XrmToolBox entry point. XrmToolBox discovers this class through MEF and shows it in the tool list.
    /// </summary>
    [Export(typeof(IXrmToolBoxPlugin)),
     ExportMetadata("Name", "Oliver4 Icon Library"),
     ExportMetadata("Description", "Apply a Fluent UI icon to a Dataverse table in a few clicks: pick the solution, the table and the icon, then create or update the SVG web resource, point the table at it and publish."),
     ExportMetadata("SmallImageBase64", PluginIcons.Small),
     ExportMetadata("BigImageBase64", PluginIcons.Big),
     ExportMetadata("BackgroundColor", "White"),
     ExportMetadata("PrimaryFontColor", "#101725"),
     ExportMetadata("SecondaryFontColor", "#5b6577")]
    public class Plugin : PluginBase
    {
        static Plugin()
        {
            // Bind to the WebView2 SDK that XrmToolBox ships before any type that uses it is touched.
            WebView2Assemblies.Install();
        }

        public override IXrmToolBoxPluginControl GetControl()
        {
            return new IconLibraryControl();
        }
    }
}
