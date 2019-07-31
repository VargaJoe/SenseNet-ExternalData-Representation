using System;
using System.Web;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository;
using SenseNet.Portal.Virtualization;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using SenseNet.ContentRepository.Schema;
using SenseNet.Diagnostics;
using SenseNet.Portal.Handlers;
using SenseNet.Portal.UI.PortletFramework;

namespace SenseNet.ExternalDataRepresentation.ContentHandlers
{

    public static class ObjectExtensions
    {
        public static Stream GetXmlStream(this Content content, bool withChildren, bool allchildren = false)
        {
            Stream result = null;

            switch (content.ContentType.Name)
            {
                //case "CustomXml":
                //    result = content.ContentHandler.GetBinary("Binary").GetStream();
                //    break;
                default:
                    content.ChildrenDefinition.AllChildren = allchildren;
                    //Joe: optimalizalasi lehetoseg
                    //SerializationOptions asd = new SerializationOptions() { Fields =   };
                    result = content.GetXml(withChildren);
                    break;
            }

            return result;
        }

    }


    [ContentHandler]
    public class AdvancedXsltApplication : XsltApplication, IFile, IHttpHandler
    {
        // ================================================================ Required construction
        public AdvancedXsltApplication(Node parent)
            : this(parent, null)
        {
        }
        public AdvancedXsltApplication(Node parent, string nodeTypeName)
            : base(parent, nodeTypeName)
        {
        }
        protected AdvancedXsltApplication(NodeToken nt)
            : base(nt)
        {
        }

        // ================================================================ Overrided Functionality Properties

        public override bool WithChildren
        {
            get
            {
                bool result = false;
                switch (this.ChildrenSetting)
                {
                    case "WithChildren":
                    case "AllChildren":
                        result = true;
                        break;
                    case "None":
                    default:
                        break;
                }
                return result;
            }
        }

        // ================================================================ Advanced Functionality Properties
        [RepositoryProperty("UseOutputSettings", RepositoryDataType.Int)]
        public virtual bool UseOutputSettings
        {
            get { return (this.HasProperty("UseOutputSettings") && this.GetProperty<int>("UseOutputSettings") != 0); }
            set { this["UseOutputSettings"] = value ? 1 : 0; }
        }

        //[RepositoryProperty("SerializeFields", RepositoryDataType.String)]
        //public virtual string SerializeFields
        //{
        //    get { return (this.HasProperty("SerializeFields")) ? this.GetProperty<string>("SerializeFields") : string.Empty; }
        //    set { this["SerializeFields"] = value; }
        //}

        //[RepositoryProperty("SerializeScenario", RepositoryDataType.String)]
        //public virtual string SerializeScenario
        //{
        //    get { return (this.HasProperty("SerializeScenario")) ? this.GetProperty<string>("SerializeScenario") : string.Empty; }
        //    set { this["SerializeScenario"] = value; }
        //}

        [RepositoryProperty("ItemCount", RepositoryDataType.Int)]
        public virtual int ItemCount
        {
            get { return (this.HasProperty("ItemCount")) ? this.GetProperty<int>("ItemCount") : 0; }
            set { this["ItemCount"] = value; }
        }

        [RepositoryProperty("BindTarget", RepositoryDataType.String)]
        public string BindTarget
        {
            get { return this.GetProperty<string>("BindTarget"); }
            set { this["BindTarget"] = value; }
        }
        [RepositoryProperty("CustomRootPath", RepositoryDataType.String)]
        public string CustomRootPath
        {
            get { return this.GetProperty<string>("CustomRootPath"); }
            set { this["CustomRootPath"] = value; }
        }
        [RepositoryProperty("AncestorIndex", RepositoryDataType.Int)]
        public virtual int AncestorIndex
        {
            get { return this.GetProperty<int>("AncestorIndex"); }
            set { this["AncestorIndex"] = value; }
        }
        [RepositoryProperty("RelativeContentPath", RepositoryDataType.String)]
        public string RelativeContentPath
        {
            get { return this.GetProperty<string>("RelativeContentPath"); }
            set { this["RelativeContentPath"] = value; }
        }

        [RepositoryProperty("ChildrenSetting", RepositoryDataType.String)]
        public string ChildrenSetting
        {
            get { return this.GetProperty<string>("ChildrenSetting"); }
            set { this["ChildrenSetting"] = value; }
        }


        [RepositoryProperty("ConformanceLevel", RepositoryDataType.String)]
        public virtual string ConformanceLevel
        {
            get { return (this.HasProperty("ConformanceLevel")) ? this.GetProperty<string>("ConformanceLevel") ?? "Auto" : "Auto"; }
            set { this["ConformanceLevel"] = value; }
        }

        [RepositoryProperty("Renderer", RepositoryDataType.Reference)]
        public Node Renderer
        {
            get { return base.GetReference<Node>("Renderer"); }
            set { base.SetReference("Renderer", value); }
        }


        [RepositoryProperty("CustomQueryFilter", RepositoryDataType.Text)]
        public virtual string CustomQueryFilter
        {
            get { return this.GetProperty<string>("CustomQueryFilter"); }
            set { this["CustomQueryFilter"] = value; }
        }

        private SenseNet.Portal.UI.PortletFramework.Xslt.XslTransformExecutionContext _xsltTransformer;
        private SenseNet.Portal.UI.PortletFramework.Xslt.XslTransformExecutionContext XsltTransformer
        {
            get
            {
                if (_xsltTransformer == null)
                {
                    string xsltPath = this.Path;
                    if (this.Renderer != null)
                        xsltPath = this.Renderer.Path;
                    _xsltTransformer = Xslt.GetXslt(xsltPath, true);
                }
                return _xsltTransformer;
            }
        }

        // ================================================================ GetProperty - SetProperty
        public override object GetProperty(string name)
        {
            switch (name)
            {
                case "ConformanceLevel":
                    return this.ConformanceLevel;
                case "UseOutputSettings":
                    return this.UseOutputSettings;
                //case "SerializeFields":
                //    return this.SerializeFields;
                //case "SerializeScenario":
                //    return this.SerializeScenario;
                case "ItemCount": return this.ItemCount;
                case "BindTarget": return this.BindTarget;
                case "ChildrenSetting": return this.ChildrenSetting;
                case "CustomRootPath": return this.CustomRootPath;
                case "AncestorIndex": return this.AncestorIndex;
                case "RelativeContentPath": return this.RelativeContentPath;
                case "Renderer": return this.Renderer;
                default:
                    return base.GetProperty(name);
            }
        }
        public override void SetProperty(string name, object value)
        {
            switch (name)
            {
                case "ConformanceLevel":
                    this.ConformanceLevel = value.ToString();
                    break;
                case "UseOutputSettings":
                    this.UseOutputSettings = (bool)value;
                    break;
                //case "SerializeFields":
                //    this.SerializeFields = value.ToString();
                //    break;
                //case "SerializeScenario":
                //    this.SerializeScenario = value.ToString();
                //    break;
                case "ItemCount":
                    this.ItemCount = (int)value;
                    break;
                case "BindTarget": this.BindTarget = (string)value; break;
                case "ChildrenSetting": this.ChildrenSetting = (string)value; break;
                case "CustomRootPath": this.CustomRootPath = (string)value; break;
                case "AncestorIndex": this.AncestorIndex = (int)value; break;
                case "RelativeContentPath": this.RelativeContentPath = (string)value; break;
                case "Renderer":
                    this.Renderer = (Node)value;
                    break;
                default:
                    base.SetProperty(name, value);
                    break;
            }
        }

        private Content _contextNode;
        /// <summary>
        /// Gets the portal Node the portlet is bound to. The value of the ContextNode is set by the combined values
        /// of the BindTarget and AncestorIndex properties.
        /// </summary>
        public virtual Content ContextNode
        {
            get
            {
                return _contextNode ?? (_contextNode = GetContextNode());
            }
        }

        protected virtual Content GetContextNode()
        {
            Node node = GetBindingRoot();
            if (node == null)
                throw new InvalidOperationException("BindingRoot cannot be null.");

            node = AncestorIndex == 0 ? node : node.GetAncestor(AncestorIndex);

            if (!string.IsNullOrEmpty(RelativeContentPath))
                node = Node.LoadNode(RepositoryPath.Combine(node.Path, RelativeContentPath));

            return Content.Create(node);
        }

        protected virtual Node GetBindingRoot()
        {
            if (BindTarget != null)
            {
                var value = (SenseNet.Portal.UI.PortletFramework.BindTarget)Enum.Parse(typeof(SenseNet.Portal.UI.PortletFramework.BindTarget), BindTarget);
                switch (value)
                {
                    case SenseNet.Portal.UI.PortletFramework.BindTarget.Unselected:
                        return Content.CreateNew("Folder", Repository.Root, "DummyNode").ContentHandler;
                    case SenseNet.Portal.UI.PortletFramework.BindTarget.CurrentSite:
                        //return PortalContext.Current.Site;
                        return SenseNet.Portal.Site.GetSiteByNode(PortalContext.Current.ContextNode);
                    case SenseNet.Portal.UI.PortletFramework.BindTarget.CurrentPage:
                        return PortalContext.Current.Page;
                    case SenseNet.Portal.UI.PortletFramework.BindTarget.CurrentUser:
                        return HttpContext.Current.User.Identity as User;
                    case SenseNet.Portal.UI.PortletFramework.BindTarget.CustomRoot:
                        return Node.LoadNode(this.CustomRootPath);
                    case SenseNet.Portal.UI.PortletFramework.BindTarget.CurrentStartPage:
                        return PortalContext.Current.Site.StartPage as Node ?? PortalContext.Current.Site as Node;
                    case SenseNet.Portal.UI.PortletFramework.BindTarget.Breadcrumb:
                    case SenseNet.Portal.UI.PortletFramework.BindTarget.CurrentContent:
                        return PortalContext.Current.ContextNode ?? Repository.Root;
                    case SenseNet.Portal.UI.PortletFramework.BindTarget.CurrentWorkspace:
                        return (Node)PortalContext.Current.ContextWorkspace ?? PortalContext.Current.Site;
                    case SenseNet.Portal.UI.PortletFramework.BindTarget.CurrentList:
                        return ContentList.GetContentListByParentWalk(PortalContext.Current.ContextNode);
                    default:
                        throw new NotImplementedException(BindTarget.ToString());
                }
            }
            else
                return PortalContext.Current.ContextNode ?? Repository.Root;
        }

        // ================================================================ IHttpHandler members
        public new void ProcessRequest(HttpContext context)
        {
            context.Response.Clear();
            context.Response.ContentType = string.IsNullOrEmpty(MimeType) ? "application/xml" : MimeType;
            context.Response.ContentEncoding = GetEncoding();


            var withChildrenParam = context.Request.Params["withchildren"];
            bool withChildren = string.IsNullOrEmpty(withChildrenParam)
                                    ? this.WithChildren
                                    : withChildrenParam.ToLower() == "true";


            if (!CanCache || !Cacheable)
            {
                //render
                Render(context.Response.Output, withChildren);
            }
            else if (IsInCache)
            {
                context.Response.Write(GetCachedOutput());
            }
            else
            {
                using (var sw = new EncodableStringWriter(context.Response.ContentEncoding))
                {
                    Render(sw, withChildren);
                    var output = sw.ToString();
                    InsertOutputIntoCache(output);
                    context.Response.Write(output);
                }
            }


            context.Response.End();
        }


        // ================================================================ Misc
        protected override Encoding GetEncoding()
        {
            Encoding encoding = Encoding.UTF8;
            if (UseOutputSettings && XsltTransformer.XslCompiledTransform.OutputSettings.Encoding != null)
            {
                return XsltTransformer.XslCompiledTransform.OutputSettings.Encoding;
            }
            else
                if (string.IsNullOrEmpty(this.ResponseEncoding))
                return encoding;

            try
            {
                encoding = Encoding.GetEncoding(this.ResponseEncoding);
            }
            catch (ArgumentException ex)
            {
                encoding = Encoding.UTF8;
                SnLog.WriteException(ex);
            }
            return encoding;

        }
        protected override void Render(TextWriter outputFileName, bool withChildren)
        {
            XmlWriterSettings outputSettings = (this.UseOutputSettings) ? XsltTransformer.XslCompiledTransform.OutputSettings.Clone() :
                new XmlWriterSettings
                {
                    Indent = true,
                    Encoding = GetEncoding(),
                    OmitXmlDeclaration = this.OmitXmlDeclaration
                };

            outputSettings.ConformanceLevel = (ConformanceLevel)Enum.Parse(typeof(ConformanceLevel), this.ConformanceLevel);

            using (XmlWriter writer = XmlWriter.Create(outputFileName, outputSettings))
            {
                Content content = this.ContextNode;
                //using (Stream response = content.GetXmlStream(withChildren, this.ChildrenSetting == "AllChildren"))
                content.ChildrenDefinition.AllChildren = this.ChildrenSetting == "AllChildren";
                content.ChildrenDefinition.ContentQuery = this.CustomQueryFilter;
                using (Stream response = content.GetXml(withChildren))
                {
                    var xml = new XPathDocument(response);
                    var xsltArguments = GetXsltArgumentList();
                    XsltTransformer.Transform(xml, xsltArguments, writer);
                }
            }

        }

        public class EncodableStringWriter : StringWriter
        {
            private Encoding _encoding;

            public EncodableStringWriter()
                : base()
            {
            }

            public EncodableStringWriter(StringBuilder sb)
                : base(sb)
            {
            }

            public EncodableStringWriter(Encoding encoding)
                : base()
            {
                _encoding = encoding;
            }

            public override Encoding Encoding
            {
                get
                {
                    return (null == _encoding) ? base.Encoding : _encoding;
                }
            }
        }
    }
}