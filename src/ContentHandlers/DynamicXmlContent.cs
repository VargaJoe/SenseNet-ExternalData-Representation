using System;
using System.Collections.Generic;
using System.Text;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Schema;
using SenseNet.ContentRepository.Storage.Security;
using System.Xml.XPath;
using System.Net;
using System.Text.RegularExpressions;
using System.IO;
using System.Web;
using System.Web.Caching;
using SenseNet.Diagnostics;
using System.Configuration;
using SenseNet.ContentRepository;
using System.Threading;
using SenseNet.Portal.Virtualization;

namespace SenseNet.ExternalDataRepresentation.ContentHandlers
{
    [ContentHandler]
    public class DynamicXmlContent : SenseNet.ContentRepository.File, SenseNet.ContentRepository.IFile, IHttpHandler//, IWorkflowShoudWorkOnDis
    {
        // should be called ExternalXmlContent
        // possible future implementations:
        // - do not use cache in Explore
        // - set webstream response encoding manually 
        // - checkbox for cache external sources -> write in binary (default is cache, if interval is not 0)
        // - checkbox for cache reference ->write in binary (default is not cache)
        // - set response encoding (default UTF-8) -> rss app GetEncoding()
        // - set response xsl transformer path (client browser: xsl) -> renderer field + checkbox if send to response
        // - response stylesheet (client browser: css) -> style reference?

        public DynamicXmlContent(Node parent) : this(parent, null) { }
        public DynamicXmlContent(Node parent, string nodeTypeName) : base(parent, nodeTypeName) { }
        protected DynamicXmlContent(NodeToken nt) : base(nt) { }

        //private static object locked = new object() { };
        private static List<int> lockList = new List<int>();

        //****************************************** Start of Repository Properties *********************************************//
        [RepositoryProperty("CustomUrl")]
        public string CustomUrl
        {
            get { return base.GetProperty<string>("CustomUrl"); }
            set { base.SetProperty("CustomUrl", value); }
        }

        [RepositoryProperty("InnerNode", RepositoryDataType.Reference)]
        public Node InnerNode
        {
            get { return base.GetReference<Node>("InnerNode"); }
            set { base.SetReference("InnerNode", value); }
        }

        [RepositoryProperty("IsCacheable", RepositoryDataType.Int)]
        public virtual bool IsCacheable
        {
            get { return (this.HasProperty("IsCacheable")) ? (this.GetProperty<int>("IsCacheable") != 0) : false; }
            set { this["IsCacheable"] = value ? 1 : 0; }
        }

        [RepositoryProperty("IsPersistable", RepositoryDataType.Int)]
        public virtual bool IsPersistable
        {
            get { return (this.HasProperty("IsPersistable")) ? (this.GetProperty<int>("IsPersistable") != 0) : false; }
            set { this["IsPersistable"] = value ? 1 : 0; }
        }

        [RepositoryProperty("IsErrorRelevant", RepositoryDataType.Int)]
        public virtual bool IsErrorRelevant
        {
            get { return (this.HasProperty("IsErrorRelevant")) ? (this.GetProperty<int>("IsErrorRelevant") != 0) : false; }
            set { this["IsErrorRelevant"] = value ? 1 : 0; }
        }

        [RepositoryProperty("XmlUpdateInterval")]
        public int XmlUpdateInterval
        {
            get
            {
                int result = base.GetProperty<int>("XmlUpdateInterval");

                //Techincal Debt: if inner node reference is set, interval could be zero
                if (result < 1 && !string.IsNullOrWhiteSpace(this.CustomUrl))
                {
                    result = MinUpdateInterval ?? 1;
                }

                return result;
            }
            set { base.SetProperty("XmlUpdateInterval", value); }
        }

        [RepositoryProperty("XmlLastUpdate", RepositoryDataType.DateTime)]
        private DateTime XmlLastUpdate
        {
            get { return base.GetProperty<DateTime>("XmlLastUpdate"); }
            set { base.SetProperty("XmlLastUpdate", value); }
        }

        [RepositoryProperty("XmlLastSyncDate", RepositoryDataType.DateTime)]
        private DateTime XmlLastSyncDate
        {
            get { return base.GetProperty<DateTime>("XmlLastSyncDate"); }
            set { base.SetProperty("XmlLastSyncDate", value); }
        }


        [RepositoryProperty("Binary", RepositoryDataType.Binary)]
        public override BinaryData Binary
        {
            get
            {
                //return this.GetBinary("Binary");
                BinaryData result = null;

                if (this.IsCacheable)
                    result = GetBinaryFromCache();

                if (result == null)
                    result = this.GetBinary("Binary");

                return result;
            }
            set { this.SetBinary("Binary", value); }
        }

        [RepositoryProperty("ResponseEncoding", RepositoryDataType.String)]
        public virtual string ResponseEncoding
        {
            get { return this.GetProperty<string>("ResponseEncoding"); }
            set { this["ResponseEncoding"] = value; }
        }

        [RepositoryProperty("TechnicalUser", RepositoryDataType.Reference)]
        public User TechnicalUser
        {
            get { return base.GetReference<User>("TechnicalUser"); }
            set { base.SetReference("TechnicalUser", value); }
        }

        //[RepositoryProperty("ErrorMessages", RepositoryDataType.Text)]
        //public XPathDocument ErrorMessages
        //{
        //    get
        //    {
        //        XPathDocument result = null;
        //        if (this.HasProperty("ErrorMessages"))
        //        {
        //        string errString = base.GetProperty<string>("ErrorMessages");
        //        if (!string.IsNullOrWhiteSpace(errString))
        //        {
        //            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(errString)))
        //            {
        //                result = new XPathDocument(stream);
        //            }
        //        }
        //        }
        //        return result;
        //    }
        //    set
        //    {
        //        if (value != null)
        //        {
        //            string persist = value.ToString();
        //            base.SetProperty("ErrorMessages", persist);
        //        }
        //    }
        //}

        /// <summary>
        /// PROXY CACHE REPO-PROPERTY
        /// </summary>
        public const string CACHECONTROL = "CacheControl";
        [RepositoryProperty(CACHECONTROL, RepositoryDataType.String)]
        public string CacheControl
        {
            get { return (this.HasProperty(CACHECONTROL)) ? this.GetProperty<string>(CACHECONTROL) : string.Empty; }
            set { this[CACHECONTROL] = value; }
        }

        /// <summary>
        /// PROXY CACHE REPO-PROPERTY
        /// </summary>
        public const string MAXAGE = "MaxAge";
        [RepositoryProperty(MAXAGE, RepositoryDataType.String)]
        public virtual string MaxAge
        {
            get { return (this.HasProperty(MAXAGE)) ? this.GetProperty<string>(MAXAGE) : string.Empty; }
            set { this[MAXAGE] = value; }
        }

        //****************************************** Start of Properties *********************************************//

        private string CacheId
        {
            get { return "AdvancedXmlContentCache_" + this.Id; }
        }

        private bool _errorOccured = false;
        private bool ErrorOccured
        {
            get { return _errorOccured; }
            set { _errorOccured = value; }
        }

        private bool IsExpired
        {
            get
            {
                //Technical Debt: should we allow 0 XmlUpdateInterval? webconfig settings - 0 is default value?
                return this.XmlLastSyncDate < DateTime.UtcNow.AddMinutes(-this.XmlUpdateInterval);
            }
        }

        public XPathDocument Document
        {
            get
            {
                //Techincal Debt: read from cache anyway or not, and cache own binary at all
                XPathDocument result = null;
                if (this.IsCacheable)
                    result = GetDocumentFromCache();

                if (result == null)
                {
                    using (Stream streamData = this.Binary.GetStream())
                    {
                        return new XPathDocument(streamData);
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// PROXY CACHE PROPERTY
        /// </summary>
        public HttpCacheability CacheControlEnumValue
        {
            get
            {
                var strprop = this.CacheControl;
                if (string.IsNullOrEmpty(strprop) || strprop == "Nondefined")
                    return HttpCacheability.Public;

                return (HttpCacheability)Enum.Parse(typeof(HttpCacheability), strprop, true);
            }
            //set { this.CacheControl = value.HasValue ? value.ToString() : "Nondefined"; }
        }

        /// <summary>
        /// PROXY CACHE PROPERTY
        /// </summary>
        public int NumericMaxAge
        {
            get
            {
                var strprop = this.MaxAge;
                if (!string.IsNullOrWhiteSpace(strprop))
                {
                    int val;
                    if (Int32.TryParse(strprop, out val))
                        return val;
                }
                return 0;
            }
        }

        private bool IsInCache
        {
            get
            {
                var result = HttpContext.Current.Cache.Get(this.CacheId);
                return result != null;
            }
        }

        private bool IsRefreshTime
        {
            //Technical Debt: "should we refresh" logic, consider if node is checked out or in edit mode or browse, headonly etc? it could be async
            get
            {
                bool result = false;
                //Technical Debt: explain in detail, when and what we check      

                result = (this.IsPersistable && this.IsExpired) || (this.IsCacheable && !this.IsInCache);

                if (this.InnerNode == null && string.IsNullOrWhiteSpace(this.CustomUrl))
                {
                    result = false;
                }

                return result;
            }
        }

        public string InnerText
        {
            get
            {
                return this.ToString();
            }
        }


        //****************************************** Start of Timeout *********************************************//
        private const string ERRORXMLSTR = "<Exception><Message>{0}</Message><InnerMessage>{1}</InnerMessage></Exception>";
        private const string TIMEOUTKEY = "XmlContentTimeoutInMilliseconds";
        private const string INTERVALKEY = "XmlContentMinimumUpdateIntervalInMinutes";
        private static object __timeoutSync = new object();
        private static object __intervalSync = new object();
        private static int? _timeout;
        public static int? Timeout
        {
            get
            {
                if (!_timeout.HasValue)
                {
                    lock (__timeoutSync)
                    {
                        if (!_timeout.HasValue)
                        {
                            var setting = ConfigurationManager.AppSettings[TIMEOUTKEY];
                            int value;
                            if (!string.IsNullOrEmpty(setting) && Int32.TryParse(setting, out value))
                                _timeout = value;
                        }
                    }
                }
                return _timeout;
            }
        }

        private static int? _minUpdateInterval;
        public static int? MinUpdateInterval
        {
            get
            {
                if (!_minUpdateInterval.HasValue)
                {
                    lock (__intervalSync)
                    {
                        if (!_minUpdateInterval.HasValue)
                        {
                            var setting = ConfigurationManager.AppSettings[INTERVALKEY];
                            int value;
                            if (!string.IsNullOrEmpty(setting) && Int32.TryParse(setting, out value))
                                _minUpdateInterval = value;
                        }
                    }
                }
                return _minUpdateInterval;
            }
        }



        //****************************************** Start of Overrides *********************************************//

        protected override void OnLoaded(object sender, SenseNet.ContentRepository.Storage.Events.NodeEventArgs e)
        {
            base.OnLoaded(sender, e);

            bool import = SenseNet.Configuration.RepositoryEnvironment.WorkingMode.Importing;
            bool export = SenseNet.Configuration.RepositoryEnvironment.WorkingMode.Exporting;
            bool popo = SenseNet.Configuration.RepositoryEnvironment.WorkingMode.Populating;
            bool sna = SenseNet.Configuration.RepositoryEnvironment.WorkingMode.SnAdmin;
            bool special = PortalContext.Current != null && PortalContext.Current.ActionName != null && PortalContext.Current.ActionName != "Browse";
            if (!import && !export && !popo && !sna && !special) {
                DoRefreshLogic();
            }

        }

        private void DoRefreshLogic()
        {
            if (this.IsRefreshTime)
            {
                //lock (locked)
                if (ReclaimLock(this.Id))
                {
                    try
                    {
                        if (this.IsRefreshTime)
                        {
                            RefreshDocument();
                        }
                    }
                    catch (Exception e)
                    {
                        SnLog.WriteException(e);
                    }
                    finally
                    {
                        ReleaseLock(this.Id);
                    }
                }
            }
        }

        private bool ReclaimLock(int id)
        {
            bool result = false;
            if (!lockList.Contains(id))
            {
                lock (lockList)
                {
                    if (!lockList.Contains(id))
                    {
                        lockList.Add(id);
                        result = true;
                    }
                }
            }
            return result;
        }

        private void ReleaseLock(int id)
        {
            if (lockList.Contains(id))
            {
                lock (lockList)
                {
                    lockList.Remove(id);
                }
            }
        }

        public override object GetProperty(string name)
        {
            switch (name)
            {
                case "Binary":
                    return this.Binary;
                case "Document":
                    return this.Document;
                case "ResponseEncoding":
                    return this.ResponseEncoding;
                case CACHECONTROL:
                    return this.CacheControl;
                case MAXAGE:
                    return this.MaxAge;
                case "Stream":
                    return this.Binary.GetStream();
                //case "InnerNode":
                //    return this.InnerNode;
                case "TechnicalUser":
                    return this.TechnicalUser;
                case "IsCacheable":
                    return this.IsCacheable;
                case "IsPersistable":
                    return this.IsPersistable;
                case "IsErrorRelevant":
                    return this.IsErrorRelevant;
                case "InnerText":
                    return this.InnerText;
                default:
                    break;
            }
            return base.GetProperty(name);
        }

        //public override object GetPropertySafely(string propertyName)
        //{
        //    switch (propertyName)
        //    {
        //        case "Binary":
        //            return this.Binary;
        //        case "Document":
        //            return this.Document;
        //        case "Stream":
        //            return this.Binary.GetStream();
        //        default:
        //            break;
        //    }
        //    return base.GetPropertySafely(propertyName);
        //}

        public override void SetProperty(string name, object value)
        {
            switch (name)
            {
                case "ResponseEncoding":
                    this.ResponseEncoding = value.ToString();
                    break;
                //case "InnerNode":
                //    InnerNode = (IEnumerable<Node>)value;
                //    break;
                case "TechnicalUser":
                    TechnicalUser = (User)value;
                    break;
                case CACHECONTROL:
                    this.CacheControl = (string)value;
                    break;
                case MAXAGE:
                    this.MaxAge = (string)value;
                    break;
                case "IsCacheable":
                    this.IsCacheable = (bool)value;
                    break;
                case "IsPersistable":
                    this.IsPersistable = (bool)value;
                    break;
                case "IsErrorRelevant":
                    this.IsErrorRelevant = (bool)value;
                    break;
                default:
                    base.SetProperty(name, value);
                    break;
            }
        }

        public override string ToString()
        {
            return (Document != null) ? Document.CreateNavigator().OuterXml : "hiba";
        }


        //****************************************** Start of Helpers *********************************************//
        private XPathDocument GetDocumentFromCache()
        {
            XPathDocument cachedDocument = null;
            object cachedObject = HttpContext.Current.Cache.Get(this.CacheId);
            if (cachedObject != null)
                cachedDocument = cachedObject as XPathDocument;
            return cachedDocument;
        }

        private BinaryData GetBinaryFromCache()
        {
            BinaryData binData = new BinaryData();

            //string stringDocument = this.Document.CreateNavigator().OuterXml;
            XPathDocument cachedDocument = GetDocumentFromCache();
            if (cachedDocument != null)
            {
                string stringDocument = cachedDocument.CreateNavigator().OuterXml;
                byte[] byteArrayDocument = Encoding.UTF8.GetBytes(stringDocument);

                MemoryStream streamDocument = new MemoryStream(byteArrayDocument);
                if (streamDocument != null)
                {
                    binData.SetStream(streamDocument);
                }
            }
            return binData;
        }

        public bool RefreshDocument()
        {
            this.ErrorOccured = false;
            bool saveIt = false;

            XPathDocument document = null;
            try
            {
                // question: should we save referenced binary to this node's binary, xpathdocument conversion checks it is real xml or not
                if (this.InnerNode != null)
                {// if reference is set, xml is load from referred node's binary
                    saveIt = true;
                    document = new XPathDocument(((SenseNet.ContentRepository.File)this.InnerNode).Binary.GetStream());
                }
                else if (!string.IsNullOrWhiteSpace(this.CustomUrl))
                {// if external source is set, we get immediately as xpathdocumentkent, it would make binary streamet from it
                    saveIt = true;
                    Regex regex = new Regex("((.+)://)((.*)@)*(.*)");
                    MatchCollection macthes = regex.Matches(CustomUrl);
                    string stripedUrl = macthes[0].Groups[1].ToString() + macthes[0].Groups[5].ToString();
                    string[] kredenc = macthes[0].Groups[4].ToString().Split(':');
                    string userName = kredenc[0];
                    string pass = (kredenc.Length > 1) ? kredenc[1] : string.Empty;
                    WebRequest feedRequest = WebRequest.Create(HttpUtility.HtmlDecode(stripedUrl));
                    feedRequest.Timeout = Timeout ?? 3000; //ezt lehetne venni configbol (milliseconds)
                    if (!string.IsNullOrWhiteSpace(userName))
                    {
                        feedRequest.PreAuthenticate = true;
                        NetworkCredential networkCredential = new NetworkCredential(userName, pass);
                        feedRequest.Credentials = networkCredential;
                    }

                    string sb = string.Empty;
                    using (HttpWebResponse response = (HttpWebResponse)feedRequest.GetResponse())
                    {
                        using (Stream streamData = response.GetResponseStream())
                        {
                            document = new XPathDocument(streamData);
                        }
                    }

                }
                //else if (IsUseCache)
                //{//  if external source is not available, get node's binary
                //    document = new XPathDocument(base.Binary.GetStream());
                //}

            }
            catch (Exception e)
            {
                string excMsg = e.Message;
                string inExcMsg = (e.InnerException != null) ? e.InnerException.Message : string.Empty;
                document = getErrorDocument(excMsg, inExcMsg);
                this.ErrorOccured = true;
            }

            if (saveIt && this.IsCacheable && (!this.ErrorOccured || this.IsErrorRelevant))
            {//  if we want to use cache and there was not an error or, there was but it's relevant to us (there is no error handling with cache!!!)
                SetDocumentToCache(document);
            }

            if (saveIt && this.IsPersistable)
            {// if we want save to binary
                if (!this.ErrorOccured || this.IsErrorRelevant)
                { //  and there was no error or the error message is an xml
                    SetDocumentToBinary(document);
                }
                else
                {// any other cases we save the last sync on the node
                    SetLastSyncDate();
                }
            }

            //if (this.ErrorOccured)
            //{
            //    this.ErrorMessages = document;
            //}

            return !this.ErrorOccured;
        }

        private void SetDocumentToCache(XPathDocument document)
        {
            HttpContext.Current.Cache.Insert(this.CacheId, document, null, DateTime.Now.AddMinutes(this.XmlUpdateInterval), System.Web.Caching.Cache.NoSlidingExpiration, CacheItemPriority.Normal, null);
        }

        private void SetDocumentToBinary(XPathDocument document)
        {
            string sb = document.CreateNavigator().OuterXml;
            byte[] byteArray = Encoding.UTF8.GetBytes(sb);
            BinaryData binData = new BinaryData();
            MemoryStream setStream = new MemoryStream(byteArray);
            if (setStream != null)
            {
                binData.SetStream(setStream);
                this.SetBinary("Binary", binData);
                this.Binary = binData;
            }

            DateTime now = DateTime.Now;
            this.XmlLastUpdate = now;
            this.XmlLastSyncDate = now;
            SaveAsTechnicalUser();
        }

        private void SetLastSyncDate()
        {
            DateTime now = DateTime.Now;
            this.XmlLastSyncDate = now;
            SaveAsTechnicalUser();
        }

        //private void SaveAsTechnicalUser()
        //{
        //    var oldUSer = User.Current;
        //    try
        //    {
        //        if (TechnicalUser != null)
        //            User.Current = TechnicalUser;

        //        using (new SystemAccount())
        //        {
        //            Guid eiei = Guid.NewGuid();
        //            Logger.WriteInformation(eiei+" "+this.Name + " " + this.Id + " " + this.NodeTimestamp + " ");

        //            try
        //            {
        //                this.Save();
        //            }
        //            catch (NodeIsOutOfDateException e)
        //            {
        //                //SenseNet.ContentRepository.Storage.Caching.Dependency.NodeIdDependency.FireChanged(this.Id);
        //                Node saveThis = Node.LoadNode(this.Id);
        //                saveThis.SetBinary("Binary", this.Binary);
        //                saveThis["XmlLastUpdate"] = this.XmlLastUpdate;
        //                saveThis["XmlLastSyncDate"] = this.XmlLastSyncDate;
        //                Logger.WriteInformation(eiei + " " + saveThis.Name + " " + saveThis.Id + " " + saveThis.NodeTimestamp + " ");
        //                saveThis.Save();
        //                Logger.WriteInformation(eiei + " " + saveThis.Name + " " + saveThis.Id + " " + saveThis.NodeTimestamp + " ");
        //            }                  

        //        }
        //    }
        //    finally
        //    {
        //        if (TechnicalUser != null)
        //        {
        //            User.Current = oldUSer;
        //        }
        //        //locked = false;
        //    }
        //}


        private void SaveAsTechnicalUser()
        {
            var oldUSer = User.Current;
            try
            {
                if (TechnicalUser != null)
                    User.Current = TechnicalUser;

                if (User.Current as Node == null)
                    User.Current = User.Administrator;

                using (new SystemAccount())
                {
                    //Guid eiei = Guid.NewGuid();
                    //Logger.WriteInformation(eiei + " " + this.Name + " " + this.Id + " " + this.NodeTimestamp + " ");
                    //Logger.WriteInformation(eiei + " " + saveThis.Name + " " + saveThis.Id + " " + saveThis.NodeTimestamp + " ");

                    //Start of Mentési logika
                    var count = 3;
                    var ok = false;
                    Exception ex = null;
                    //SenseNet.ContentRepository.Storage.Caching.Dependency.NodeIdDependency.FireChanged(this.Id);
                    //var nodeToSave = this;
                    var nodeToSave = Node.Load<DynamicXmlContent>(this.Id);
                    while (!ok && count > 0)
                    {
                        try
                        {
                            if (nodeToSave == null || nodeToSave != this)
                            {
                                nodeToSave = Node.Load<DynamicXmlContent>(this.Id);
                                nodeToSave.SetBinary("Binary", this.Binary);
                                nodeToSave["XmlLastUpdate"] = this.XmlLastUpdate;
                                nodeToSave["XmlLastSyncDate"] = this.XmlLastSyncDate;
                            }
                            nodeToSave.Save();
                            ok = true;
                        }
                        catch (NodeIsOutOfDateException e)
                        {
                            //Logger.WriteInformation("AdvancedXml - update exception: " + nodeToSave.Name + " - " + nodeToSave.Id + " - " + nodeToSave.NodeTimestamp + " ");
                            count--;
                            ex = e;
                            nodeToSave = null;
                            Thread.Sleep(1500);
                        }
                    }

                    if (!ok)
                    {
                        throw new ApplicationException("AdvancedXml - Frissítési hiba: ", ex);
                    }
                    //End of Mentési logika
                }
            }
            finally
            {
                //if (TechnicalUser != null)
                //{
                User.Current = oldUSer;
                //}
                //locked = false;
            }
        }


        protected virtual Encoding GetEncoding()
        {
            Encoding encoding = Encoding.UTF8;
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



        private XPathDocument getErrorDocument(string exceptionMessage, string exceptionInnerMessage = "")
        {
            XPathDocument result;
            string errMsgXmlStr = string.Format(ERRORXMLSTR, exceptionMessage, exceptionInnerMessage);
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(errMsgXmlStr)))
            {
                result = new XPathDocument(stream);
            }

            return result;
        }


        //****************************************** Start of IHttpHandler *********************************************//
        public bool IsReusable
        {
            get { throw new NotImplementedException(); }
        }

        public void ProcessRequest(HttpContext context)
        {
            context.Response.Clear();
            context.Response.ClearHeaders();
            context.Response.ContentType = "application/xml";//string.IsNullOrEmpty(ContentType) ? "application/xml" : ContentType;
            //context.Response.ContentEncoding = Encoding.UTF8;
            context.Response.ContentEncoding = this.GetEncoding();
            context.Response.Write("<?xml version='1.0' encoding='UTF-8'?>\n");
            //context.Response.Write("<?xml-stylesheet type='text/xsl' href='/Root/Global/renderers/XmlDumper.xslt'?>\n");            

            //************* START OF PROXY CACHE CONTROL
            context.Response.Cache.SetCacheability(CacheControlEnumValue);
            context.Response.Cache.SetMaxAge(new TimeSpan(0, 0, this.NumericMaxAge));
            context.Response.Cache.SetSlidingExpiration(true);  // max-age does not appear in response header without this...
            //************* END OF PROXY CACHE CONTROL

            string receivestream = this.ToString();
            Byte[] byteArray = Encoding.UTF8.GetBytes(receivestream);

            context.Response.BufferOutput = true;
            context.Response.BinaryWrite(byteArray);
            context.Response.End();
        }

    }
}
