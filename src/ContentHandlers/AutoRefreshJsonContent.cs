﻿using System;
using System.Collections.Generic;
using System.Text;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Schema;
using SenseNet.ContentRepository.Storage.Security;
using System.Net;
using System.Text.RegularExpressions;
using System.IO;
using System.Web;
using System.Web.Caching;
using SenseNet.Diagnostics;
using System.Configuration;
using SenseNet.ContentRepository;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace SenseNet.ExternalDataRepresentation.ContentHandlers
{
    [ContentHandler]
    public class AutoRefreshJsonContent : SenseNet.ContentRepository.File
    {
        public AutoRefreshJsonContent(Node parent) : this(parent, null) { }
        public AutoRefreshJsonContent(Node parent, string nodeTypeName) : base(parent, nodeTypeName) { }
        protected AutoRefreshJsonContent(NodeToken nt) : base(nt) { }

        //private static object locked = new object() { };
        private static List<int> lockList = new List<int>();

        //****************************************** Start of Repository Properties *********************************************//
        [RepositoryProperty("CustomUrl")]
        public string CustomUrl
        {
            get { return base.GetProperty<string>("CustomUrl"); }
            set { base.SetProperty("CustomUrl", value); }
        }

        // should be called ExternalJsonContent
        // field for inner repository reference, have to consider if we need this
        //[RepositoryProperty("InnerNode", RepositoryDataType.Reference)]
        //public Node InnerNode
        //{
        //    get { return base.GetReference<Node>("InnerNode"); }
        //    set { base.SetReference("InnerNode", value); }
        //}

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

        [RepositoryProperty("JsonUpdateInterval")]
        public int JsonUpdateInterval
        {
            get
            {
                int result = base.GetProperty<int>("JsonUpdateInterval");

                //Techincal Debt: if inner node reference is set, interval could be zero
                if (result < 1 && !string.IsNullOrWhiteSpace(this.CustomUrl))
                {
                    result = MinUpdateInterval ?? 1;
                }

                return result;
            }
            set { base.SetProperty("XmlUpdateInterval", value); }
        }

        [RepositoryProperty("JsonLastUpdate", RepositoryDataType.DateTime)]
        private DateTime JsonLastUpdate
        {
            get { return base.GetProperty<DateTime>("JsonLastUpdate"); }
            set { base.SetProperty("JsonLastUpdate", value); }
        }

        [RepositoryProperty("JsonLastSyncDate", RepositoryDataType.DateTime)]
        private DateTime JsonLastSyncDate
        {
            get { return base.GetProperty<DateTime>("JsonLastSyncDate"); }
            set { base.SetProperty("JsonLastSyncDate", value); }
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
            get { return "JsonContentCache_" + this.Id; }
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
                return this.JsonLastSyncDate < DateTime.UtcNow.ToUniversalTime().AddMinutes(-this.JsonUpdateInterval);
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
            // Technical Debt: "should we refresh" logic, consider if node is checked out or in edit mode or browse, headonly etc? it could be async
            get
            {
                bool result = false;
                //Technical Debt: explain in detail, when and what we check 

                result = (this.IsPersistable && this.IsExpired) || (this.IsCacheable && !this.IsInCache);

                //if (this.InnerNode == null && string.IsNullOrWhiteSpace(this.CustomUrl))
                if (string.IsNullOrWhiteSpace(this.CustomUrl))
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

            if (!SenseNet.Configuration.RepositoryEnvironment.WorkingMode.Importing)
                DoRefreshLogic();

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
                //case "Document":
                //    return this.Document;
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

        //****************************************** Start of Helpers *********************************************//
        private JObject GetDocumentFromCache()
        {
            JObject cachedDocument = null;
            object cachedObject = HttpContext.Current.Cache.Get(this.CacheId);
            if (cachedObject != null)
                cachedDocument = cachedObject as JObject;
            return cachedDocument;
        }

        private BinaryData GetBinaryFromCache()
        {
            BinaryData binData = new BinaryData();

            //string stringDocument = this.Document.CreateNavigator().OuterXml;
            JObject cachedDocument = GetDocumentFromCache();
            if (cachedDocument != null)
            {
                string stringDocument = cachedDocument.ToString(); // TechDebt: ez vajh mukodik?
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

            JObject document = null;
            try
            {
                // question: should we save referenced binary to this node's binary, xpathdocument conversion checks it is real xml or not
                //if (this.InnerNode != null)
                //{// if reference is set, xml is load from referred node's binary
                //    saveIt = true;
                //    document = new XPathDocument(((SenseNet.ContentRepository.File)this.InnerNode).Binary.GetStream());
                //}
                //else 
                if (!string.IsNullOrWhiteSpace(this.CustomUrl))
                {// if external source is set, we get immediately as xpathdocumentkent, it would make binary streamet from it
                    saveIt = true;
                    Regex regex = new Regex("((.+)://)((.*)@)*(.*)");
                    //MatchCollection macthes = regex.Matches(CustomUrl);
                    //string stripedUrl = macthes[0].Groups[1].ToString() + macthes[0].Groups[5].ToString();
                    //string[] kredenc = macthes[0].Groups[4].ToString().Split(':');
                    //string userName = kredenc[0];
                    //string pass = (kredenc.Length > 1) ? kredenc[1] : string.Empty;
                    //WebRequest feedRequest = WebRequest.Create(HttpUtility.HtmlDecode(stripedUrl));
                    HttpWebRequest feedRequest = (HttpWebRequest)WebRequest.Create(HttpUtility.HtmlDecode(CustomUrl));
                    feedRequest.AutomaticDecompression = DecompressionMethods.GZip;

                    feedRequest.Timeout = Timeout ?? 3000; //ezt lehetne venni configbol (milliseconds)
                    //if (!string.IsNullOrWhiteSpace(userName))
                    //{
                    //    feedRequest.PreAuthenticate = true;
                    //    NetworkCredential networkCredential = new NetworkCredential(userName, pass);
                    //    feedRequest.Credentials = networkCredential;
                    //}

                    string sb = string.Empty;
                    using (HttpWebResponse response = (HttpWebResponse)feedRequest.GetResponse())
                    {
                        using (Stream streamData = response.GetResponseStream())
                        {
                            using (StreamReader reader = new StreamReader(streamData))
                            {
                                string json = reader.ReadToEnd();
                                document = Newtonsoft.Json.Linq.JObject.Parse(json);
                            }
                        }
                    }

                }
                //else if (IsUseCache)
                //{// if external source is not available, get node's binary
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
            {// if we want to use cache and there was not an error or, there was but it's relevant to us (there is no error handling with cache!!!)
                SetDocumentToCache(document);
            }

            if (saveIt && this.IsPersistable)
            {// if we want save to binary
                if (!this.ErrorOccured || this.IsErrorRelevant)
                { // and there was no error or the error message is an xml
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

        private void SetDocumentToCache(JObject document)
        {
            HttpContext.Current.Cache.Insert(this.CacheId, document, null, DateTime.Now.AddMinutes(this.JsonUpdateInterval), System.Web.Caching.Cache.NoSlidingExpiration, CacheItemPriority.Normal, null);
        }

        private void SetDocumentToBinary(JObject document)
        {
            string sb = document.ToString(); //Tech Debt: should this work?
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
            this.JsonLastUpdate = now;
            this.JsonLastSyncDate = now;
            SaveAsTechnicalUser();
        }

        private void SetLastSyncDate()
        {
            DateTime now = DateTime.Now;
            this.JsonLastSyncDate = now;
            SaveAsTechnicalUser();
        }


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

                    // Start of Save Logic
                    var count = 3;
                    var ok = false;
                    Exception ex = null;
                    //SenseNet.ContentRepository.Storage.Caching.Dependency.NodeIdDependency.FireChanged(this.Id);
                    //var nodeToSave = this;
                    var nodeToSave = Node.Load<AutoRefreshJsonContent>(this.Id);
                    while (!ok && count > 0)
                    {
                        try
                        {
                            if (nodeToSave == null || nodeToSave != this)
                            {
                                nodeToSave = Node.Load<AutoRefreshJsonContent>(this.Id);
                                nodeToSave.SetBinary("Binary", this.Binary);
                                nodeToSave["JsonLastUpdate"] = this.JsonLastUpdate;
                                nodeToSave["JsonLastSyncDate"] = this.JsonLastSyncDate;
                                nodeToSave.ModificationDate = DateTime.Now;
                            }
                            nodeToSave.Save();
                            ok = true;
                        }
                        catch (NodeIsOutOfDateException e)
                        {
                            count--;
                            ex = e;
                            nodeToSave = null;
                            Thread.Sleep(1500);
                        }
                    }

                    if (!ok)
                    {
                        throw new ApplicationException("JsonContent - Update exception: ", ex);
                    }
                    // End of Save Logic
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



        private JObject getErrorDocument(string exceptionMessage, string exceptionInnerMessage = "")
        {
            JObject result = null;
            // Tech Debt: Json error message should be here
            //string errMsgXmlStr = string.Format(ERRORXMLSTR, exceptionMessage, exceptionInnerMessage);
            //using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(errMsgXmlStr)))
            //{
            //    result = new XPathDocument(stream);
            //}

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
            context.Response.ContentType = "application/json";//string.IsNullOrEmpty(ContentType) ? "application/xml" : ContentType;
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
