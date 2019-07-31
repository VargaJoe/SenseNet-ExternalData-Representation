using SenseNet.ExternalDataRepresentation.Helper;
using SenseNet.ApplicationModel;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Schema;
using SenseNet.ContentRepository.Storage;
using SenseNet.Portal.Virtualization;
using System.Linq;
using System.Web;
using System.Web.Script.Serialization;

namespace SenseNet.ExternalDataRepresentation.ContentHandlers
{
    [ContentHandler]
    public class SyncFileApplication : Application, IHttpHandler
    {
        public SyncFileApplication(Node parent) : this(parent, null) { }
        public SyncFileApplication(Node parent, string nodeTypeName) : base(parent, nodeTypeName) { }
        protected SyncFileApplication(NodeToken nt) : base(nt) { }

        // sync: file, network, url
        // type can be anything: binary, json, xml -> binary field
        // if type is string type -> can save to longtext or other?

        // =================== IHttpHandler members ===================
        bool IHttpHandler.IsReusable
        {
            get { return false; }
        }

        public void ProcessRequest(HttpContext context)
        {
            var contentToSync = Content.Load(PortalContext.Current.ContextNodePath);
            var result = new SyncFile(contentToSync).Sync();

            context.Response.Clear();
            context.Response.ClearHeaders();
            context.Response.ContentType = "application/json";
            context.Response.BufferOutput = true;
            context.Response.Write(
                new JavaScriptSerializer().Serialize(new
                {
                    result = result.Select(c => c.ContentPath + "\r\n")
                })
            );

            context.Response.End();
        }

    }
}