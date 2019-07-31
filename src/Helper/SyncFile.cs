using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Security;
using SenseNet.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;

namespace SenseNet.ExternalDataRepresentation.Helper
{
    public enum SyncResult { Default, SyncSuccess, SyncFailed, NoSyncToDo, SourceDirectoryNotFound, SourceFileNotFound }

    public class SyncResultObject
    {
        public string Id;
        public string ContentPath;
        public SyncResult Result;

        public SyncResultObject()
        {
            Id = Guid.NewGuid().ToString();
        }

        public SyncResultObject(string path, SyncResult result)
            : this()
        {
            ContentPath = path;
            Result = result;
        }
    }

    public class SyncFile
    {
        //****************************************** Constructor *********************************************//
        public SyncFile(Content contentToSync)
        {
            this.ContentToSync = contentToSync;
        }


        //****************************************** Start of Settings Properties *********************************************//
        public User TechnicalUser
        {
            get
            {
                var techUserPath = Settings.GetValue<string>(SETTINGNAME, TECHNICALUSERFIELD, ContentToSync.Path, string.Empty);
                return Node.Load<User>(techUserPath);
            }
        }

        public int MinUpdateInterval
        {
            get
            {
                int _minUpdateInterval;
                _minUpdateInterval = Settings.GetValue<int>(SETTINGNAME, MININTERVALKEYFIELD, ContentToSync.Path, 2);
                if (_minUpdateInterval < 1)
                    _minUpdateInterval = 2;
                return _minUpdateInterval;
            }
        }

        //****************************************** Start of Repository Properties *********************************************//
        private const string ASPECTNAME = "FileSync";
        private const string MAPPEDPATHASPECT = "FileSync.MapPath";
        private const string UPDATEINTERVALASPECT = "FileSync.UpdateInterval";
        private const string LASTSYNCDATEASPECT = "FileSync.LastSyncDate";
        private const string LASTUPDATEASPECT = "FileSync.LastUpdate";
        private const string NEXTSYNCDATEASPECT = "FileSync.NextSyncDate";
        private const string ISFOLDERSYNCASPECT = "FileSync.IsFolderSync";

        //****************************************** Start of Config Properties *********************************************//
        private const string SETTINGNAME = "SyncFile";
        private const string TECHNICALUSERFIELD = "TechnicalUser";
        private const string MININTERVALKEYFIELD = "MinimumUpdateInterval";
        private const string BINARYFIELD = "Binary";


        //****************************************** Start of Properties *********************************************//
        public Content ContentToSync { get; private set; }
        private static List<int> lockList = new List<int>();

        private int _updateInterval;
        public int UpdateInterval
        {
            get
            {
                if (_updateInterval == 0 &&
                    ContentToSync.AspectFields != null &&
                    ContentToSync.AspectFields.ContainsKey(UPDATEINTERVALASPECT) &&
                    ContentToSync[UPDATEINTERVALASPECT] != null)
                    _updateInterval = (int)ContentToSync[UPDATEINTERVALASPECT];

                if (_updateInterval < 1)
                    _updateInterval = MinUpdateInterval;

                return _updateInterval;
            }
        }

        private bool? _isExpired;
        private bool IsExpired
        {
            get
            {
                bool result = false;

                if (_isExpired == null)
                {
                    if (ContentToSync.AspectFields != null &&
                       ContentToSync.AspectFields.ContainsKey(NEXTSYNCDATEASPECT) &&
                       ContentToSync[NEXTSYNCDATEASPECT] != null)
                        result = (DateTime)ContentToSync[NEXTSYNCDATEASPECT] < DateTime.UtcNow;
                    else
                        result = true;

                    _isExpired = result;
                }
                else
                    result = _isExpired.Value;

                return result;
            }
        }

        private string _fileSystemPath;
        private string FileSystemPath
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_fileSystemPath))
                {
                    string givenPath = ContentToSync[MAPPEDPATHASPECT] as string;

                    if (string.IsNullOrWhiteSpace(givenPath))
                        return string.Empty;

                    if (givenPath.StartsWith("\\\\") || Regex.IsMatch(givenPath, "^[a-zA-Z]:")) //or starts with drive letter
                        _fileSystemPath = givenPath;
                    else
                        _fileSystemPath = Path.Combine(ServerWebfolderPath, givenPath.TrimStart('~').TrimStart('\\'));
                    //_fileSystemPath = System.Web.Hosting.HostingEnvironment.MapPath(givenPath);


                }

                return _fileSystemPath;
            }
        }

        private string _serverWebfolderPath;
        private string ServerWebfolderPath
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_serverWebfolderPath))
                {
                    _serverWebfolderPath = HttpContext.Current.Server.MapPath("~");
                }

                return _serverWebfolderPath;
            }
        }

        public SyncFile SetServerWebfolderPath(string serverWebfolderPath)
        {
            _serverWebfolderPath = serverWebfolderPath;
            return this;
        }


        //****************************************** Public Methods *********************************************//


        public List<SyncResultObject> Sync()
        {
            List<SyncResultObject> result = new List<SyncResultObject>();
            if (this.IsExpired)
            {
                if (ReclaimLock(ContentToSync.Id))
                {
                    try
                    {
                        if (this.IsExpired)
                        {
                            result = SyncLogic();
                        }
                    }
                    catch (Exception e)
                    {
                        SnLog.WriteException(e);
                        result.Add(new SyncResultObject(ContentToSync.Path, SyncResult.SyncFailed));
                    }
                    finally
                    {
                        ReleaseLock(ContentToSync.Id);
                    }
                }
            }
            return result;
        }


        //****************************************** Start of Helpers *********************************************//      
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

        private List<SyncResultObject> SyncLogic()
        {
            var fileSyncResultDict = new List<SyncResultObject>();

            try
            {
                if (ContentToSync.AspectFields.ContainsKey(ISFOLDERSYNCASPECT))
                {
                    bool fileSync = ContentToSync[ISFOLDERSYNCASPECT] == null || (bool)ContentToSync[ISFOLDERSYNCASPECT] == false;

                    if (fileSync)
                    {
                        byte[] fileByteArray = null;
                        var fileSyncResult = SyncResult.Default;// SyncResult.NoSyncToDo;

                        try
                        {
                            if (fileByteArray == null && !string.IsNullOrWhiteSpace(FileSystemPath))
                            {
                                if (!new FileInfo(FileSystemPath).Exists)
                                    fileSyncResult = SyncResult.SourceFileNotFound;
                                else
                                    fileByteArray = System.IO.File.ReadAllBytes(FileSystemPath);
                            }

                            if (fileByteArray != null)
                            {
                                using (Stream mSteam = new MemoryStream(fileByteArray))
                                {
                                    if (mSteam != null)
                                    {
                                        // if we want to save in binary
                                        SaveAsTechnicalUser(ContentToSync, mSteam);
                                        fileSyncResult = SyncResult.SyncSuccess;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            fileSyncResult = SyncResult.SyncFailed;
                        }

                        fileSyncResultDict.Add(new SyncResultObject(ContentToSync.Path, fileSyncResult));

                        // delete file
                        try
                        {
                            if (fileSyncResult == SyncResult.SyncSuccess && !string.IsNullOrWhiteSpace(FileSystemPath) && new FileInfo(FileSystemPath).Exists)
                                //Logger.WriteInformation(40002, "FILESYNC delete 393 " + FileSystemPath);
                                System.IO.File.Delete(FileSystemPath);
                            //igy dictionarykent a torlest is logolhatjuk a syncresultba, ha szukseges
                        }
                        catch (Exception ex)
                        {
                            SnLog.WriteException(ex);
                        }
                    }
                    else
                    {
                        // if it's a folder children folders have to be sync as well
                        fileSyncResultDict = GetFoldersAndSync(ContentToSync, FileSystemPath);
                        // if it's a folder children files have to be sync as well?
                        //saved = GetFilesAndSync(ContentToSync, FileSystemPath);
                        //return saved;
                    }
                }
                else
                {
                    fileSyncResultDict.Add(new SyncResultObject(ContentToSync.Path, SyncResult.NoSyncToDo));
                }
            }
            catch (DirectoryNotFoundException e)
            {
                //Logger.WriteException(e);
                fileSyncResultDict.Add(new SyncResultObject(ContentToSync.Path, SyncResult.SourceDirectoryNotFound));
            }
            catch (Exception e)
            {
                SnLog.WriteException(e);
                fileSyncResultDict.Add(new SyncResultObject(ContentToSync.Path, SyncResult.SyncFailed));
            }

            return fileSyncResultDict;
        }

        private List<SyncResultObject> GetFoldersAndSync(Content cntToSync, string filePath)
        {
            var result = new List<SyncResultObject>();

            // update files
            result.AddRange(GetFilesAndSync(cntToSync, filePath));

            // now the folders
            DirectoryInfo[] folderInfos = null;
            try
            {
                DirectoryInfo dirInfo = new DirectoryInfo(filePath);
                folderInfos = dirInfo.GetDirectories();
            }
            catch
            {
                // no problem, there are no folder's here. so what?
            }

            // nothing use this
            //IEnumerable<Content> children = cntToSync.Children.Where(c => c.TypeIs("Folder"));

            if (folderInfos != null && folderInfos.Length > 0)
            {
                foreach (var folder in folderInfos)
                {
                    string folderName = string.Empty;
                    try
                    {
                        folderName = ContentNamingHelper.GetNameFromDisplayName(folder.Name);
                        Content folderContent = cntToSync.Children.Where(c => c.Name == folderName).FirstOrDefault();
                        if (folderContent == null)
                        {
                            var fileSyncAspect = Aspect.LoadAspectByPathOrName(ASPECTNAME);
                            folderContent = Content.CreateNew("Folder", cntToSync.ContentHandler, folderName);
                            folderContent.DisplayName = folder.Name;
                            folderContent.AddAspects(fileSyncAspect);
                            folderContent.Save();
                        }

                        result.AddRange(GetFoldersAndSync(folderContent, folder.FullName));
                    }
                    catch
                    {
                        result.Add(new SyncResultObject(string.Concat(cntToSync.Path, "/", folderName), SyncResult.SyncFailed));
                    }
                }
            }

            //// save sync date on parent
            SaveAsTechnicalUser(cntToSync, null, true);

            return result;
        }

        private List<SyncResultObject> GetFilesAndSync(Content cntToSync, string filePath)
        {
            var result = new List<SyncResultObject>();

            DirectoryInfo dirInfo = new DirectoryInfo(filePath);
            IEnumerable<Content> children = cntToSync.Children.Where(c => c.TypeIs("File"));
            //Content lastContent = children.OrderByDescending(c => c.CreationDate).FirstOrDefault();
            //DateTime lastContentDate = (lastContent != null) ? lastContent.CreationDate : DateTime.MinValue;
            //technical debt: I think creationdate won't be good here, we should probably use last syncdate

            var fileInfos = dirInfo.GetFiles();
            //if (fileInfos.Length == 0)
            //    result.Add(new SyncResultObject(filePath, SyncResult.NoSyncToDo));
            //else
            if (fileInfos.Length > 0)
            {
                foreach (var file in fileInfos)
                {
                    var fileSynced = false;
                    string fileName = file.Name;
                    try
                    {
                        using (Stream fileStream = file.Open(FileMode.Open, FileAccess.Read)) //Open the file ReadOnly mode
                        {
                            fileName = ContentNamingHelper.GetNameFromDisplayName(file.Name);

                            using (new SystemAccount())
                            {//Technical Debt: as for now we do not check if file needs to be updated or not 
                                Content fileContent = cntToSync.Children.Where(c => c.Name == fileName).FirstOrDefault();
                                if (fileContent == null)
                                {
                                    // create new
                                    SenseNet.ContentRepository.File newFile = new SenseNet.ContentRepository.File(cntToSync.ContentHandler);
                                    newFile.Name = ContentNamingHelper.GetNameFromDisplayName(file.Name);
                                    newFile.DisplayName = file.Name;
                                    newFile.Save();
                                    fileContent = Content.Load(newFile.Id);
                                    var fileSyncAspect = Aspect.LoadAspectByPathOrName(ASPECTNAME);
                                    fileContent.Save(); // ez miert? elo is kell menteni?
                                    fileContent.AddAspects(fileSyncAspect);
                                }

                                SaveAsTechnicalUser(fileContent, fileStream);
                                result.Add(new SyncResultObject(fileContent.Path, SyncResult.SyncSuccess));
                            }
                            fileSynced = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        SnLog.WriteException(ex);
                        result.Add(new SyncResultObject(string.Concat(cntToSync.Path, "/", fileName), SyncResult.SyncFailed));
                    }

                    // result add would be better at here

                    // delete file
                    try
                    {
                        if (fileSynced)
                            //Logger.WriteInformation(40002, "FILESYNC delete 545 " + file.Name);
                            file.Delete();
                        // should we log deletion?
                    }
                    catch (Exception ex)
                    {
                        SnLog.WriteException(ex);
                    }
                }
            }

            //// save refresh date on parent
            //SaveAsTechnicalUser(cntToSync, null, true);

            return result;
        }

        private void SetLastUpdateAndSave(Content content, bool syncSuccess)
        {
            //automaticly synced folder does not have aspect fields
            if (content.AspectFields != null && content.AspectFields.ContainsKey(LASTSYNCDATEASPECT))
            {
                DateTime now = DateTime.UtcNow;
                content[LASTSYNCDATEASPECT] = now;
                if (syncSuccess)
                {
                    content[LASTUPDATEASPECT] = now;
                    content[NEXTSYNCDATEASPECT] = GetLastTime().AddMinutes(this.UpdateInterval);
                }
            }
        }

        private void SetDocumentToBinary(Content content, Stream setStream)
        {
            BinaryData binData = new BinaryData() { FileName = content.DisplayName };

            if (setStream != null)
            {
                binData.SetStream(setStream);
                content.ContentHandler.SetBinary(BINARYFIELD, binData);
                content.ModificationDate = DateTime.UtcNow;
            }
        }

        private void SaveAsTechnicalUser(Content content, Stream stream, bool onlyRefreshTimeStamps = false)
        {
            var oldUser = User.Current;
            try
            {
                if (TechnicalUser != null)
                    User.Current = TechnicalUser;

                if (User.Current as Node == null)
                    User.Current = User.Administrator;

                using (new SystemAccount())
                {
                    var count = 3;
                    var ok = false;
                    Exception ex = null;
                    Content contentToSave = content;
                    while (!ok && count > 0)
                    {
                        try
                        {
                            if (contentToSave == null)
                            {
                                contentToSave = Content.Load(content.Id);
                            }

                            if (!onlyRefreshTimeStamps)
                                SetDocumentToBinary(content, stream);

                            SetLastUpdateAndSave(content, true);
                            contentToSave.Save();
                            ok = true;
                        }
                        catch (NodeIsOutOfDateException e)
                        {
                            count--;
                            ex = e;
                            contentToSave = null;
                            Thread.Sleep(1500);
                        }
                    }

                    if (!ok)
                    {
                        throw new ApplicationException("SyncFileContent - File update error: ", ex);
                    }
                }
            }
            finally
            {
                User.Current = oldUser;
            }
        }
        // always should response UTC!
        private DateTime GetLastTime()
        {
            // Local Times ! Not UTC!!! (update: I think latest sensenet response utc for default now)
            var rawTimes = Settings.GetValue<List<string>>("SyncFile", "FileSyncTimes");

            var times = new List<DateTime>();
            foreach (var time in rawTimes)
            {
                DateTime t;
                if (DateTime.TryParse(time, out t))
                {
                    times.Add(t);
                }
            }
            if (times.Count == 0)
            {
                return DateTime.UtcNow;
            }
            DateTime resultDateTime = DateTime.MinValue;
            // if no time has passed we use the last time of yesterday
            if (!times.Any(d => d < DateTime.Now))
            {
                // if there aren't any earlier item, then yesterdays last sync will be last of ascending date with minus 1 day 
                resultDateTime = times.OrderBy(d => d).Last().AddDays(-1);
            }
            else
            {
                // if there is an earlier item
                resultDateTime = times.Where(d => d < DateTime.Now).OrderBy(d => d).LastOrDefault();
            }
            // convert to UTC
            return resultDateTime.ToUniversalTime();
        }

        public static DateTime GetNextTime()
        {
            var rawTimes = Settings.GetValue<List<string>>("SyncFile", "FileSyncTimes");

            var times = new List<DateTime>();
            foreach (var time in rawTimes)
            {
                DateTime t;
                if (DateTime.TryParse(time, out t))
                {
                    times.Add(t);
                }
            }
            if (times.Count == 0)
            {
                return DateTime.Now.AddDays(1);
            }

            // if no time is remaining we use the earliest time of tomorrow
            if (!times.Any(d => d > DateTime.Now))
            {
                return times.OrderBy(d => d).First().AddDays(1);
            }
            else
            {
                return times.Where(d => d > DateTime.Now).OrderBy(d => d).FirstOrDefault();
            }
        }

    }
}