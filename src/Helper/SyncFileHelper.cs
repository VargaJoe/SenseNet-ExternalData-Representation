using System.Collections.Generic;
using System.Linq;

namespace SenseNet.ExternalDataRepresentation.Helper
{
    public static class SyncFileHelper
    {
        public static string ResultToString(this List<KeyValuePair<string, SyncResult>> result, string KvSeparator, string rowSeparator)
        {
            return string.Join(rowSeparator, result.Select(f => string.Format("{0}{1}{2}{3}{4}", f.Value == SyncResult.SyncSuccess ? "" : "<b>", f.Value, KvSeparator, f.Key, f.Value == SyncResult.SyncSuccess ? "" : "</b>")));
        }

        public static string ResultToString(this List<SyncResultObject> result, string KvSeparator, string rowSeparator)
        {
            List<KeyValuePair<string, SyncResult>> l = result.Select(n => new KeyValuePair<string, SyncResult>(n.ContentPath, n.Result)).ToList();
            return l.ResultToString(KvSeparator, rowSeparator);
            // return string.Join(rowSeparator, result.Select(f => string.Format("{0}{1}{2}{3}{4}", f.Result == SyncResult.SyncSuccess ? "" : "<b>", f.Result, KvSeparator, f.ContentPath, f.Result == SyncResult.SyncSuccess ? "" : "</b>")));
        }
    }
}