namespace Sitecore.Support.Tasks
{
    using Sitecore;
    using Sitecore.Configuration;
    using Sitecore.Diagnostics;
    using Sitecore.IO;
    using Sitecore.Xml;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Xml;

    public class FileCleaner
    {
        private readonly bool _active;

        private readonly NameValueCollection _configSettings;

        private readonly string _folder;

        private readonly TimeSpan _maxAge;

        private readonly int _maxCount;

        private readonly TimeSpan _minAge;

        private readonly int _minCount;

        private readonly string _name;

        private readonly string _pattern;

        private readonly bool _recursive;

        private readonly bool _rolling;

        private readonly Hashtable _slotCounts;

        private readonly string _strategy;

        public bool Active => _active;

        public string Folder => _folder;

        public string Name => _name;

        public FileCleaner(XmlNode configNode)
        {
            Assert.ArgumentNotNull(configNode, "configNode");
            _configSettings = XmlUtil.GetAttributes(configNode);
            _folder = FileUtil.MapPath(StringUtil.GetString(_configSettings["folder"], Settings.DataFolder));
            _pattern = StringUtil.GetString(_configSettings["pattern"]);
            _recursive = (StringUtil.GetString(_configSettings["recursive"]) == "true");
            _active = (_configSettings["mode"] != "off" && !string.IsNullOrEmpty(_pattern));
            _name = StringUtil.GetString(_configSettings["name"], "[no name specified]");
            _minAge = DateUtil.ParseTimeSpan(_configSettings["minAge"], new TimeSpan(0, 30, 0), CultureInfo.InvariantCulture);
            _maxAge = DateUtil.ParseTimeSpan(_configSettings["maxAge"], TimeSpan.MaxValue, CultureInfo.InvariantCulture);
            if (_maxAge == TimeSpan.Zero)
            {
                _maxAge = TimeSpan.MaxValue;
            }
            _minCount = MainUtil.GetInt(_configSettings["minCount"], 0);
            _maxCount = MainUtil.GetInt(_configSettings["maxCount"], int.MaxValue);
            _rolling = MainUtil.GetBool(_configSettings["rolling"], false);
            _strategy = StringUtil.GetString(_configSettings["strategy"], "2,2,2,2,2");
            if (_rolling)
            {
                _slotCounts = GetSlotCounts(_strategy);
            }
        }

        public void AddFileToGroup(FileInfo file, string groupName, Hashtable fileGroups)
        {
            Assert.ArgumentNotNull(file, "file");
            Assert.ArgumentNotNull(groupName, "group");
            Assert.ArgumentNotNull(fileGroups, "fileGroups");
            if (fileGroups[groupName] == null)
            {
                fileGroups[groupName] = new ArrayList();
            }
            (fileGroups[groupName] as ArrayList)?.Add(file);
        }

        public void CleanupGroups(Hashtable fileGroups)
        {
            Assert.ArgumentNotNull(fileGroups, "fileGroups");
            foreach (string key in _slotCounts.Keys)
            {
                ArrayList arrayList = fileGroups[key] as ArrayList;
                if (arrayList != null)
                {
                    int num = (int)_slotCounts[key];
                    int num2 = arrayList.Count - num;
                    if (num > 0 && arrayList.Count > 0)
                    {
                        arrayList.RemoveAt(0);
                    }
                    if (num > 1 && arrayList.Count > 0)
                    {
                        arrayList.RemoveAt(arrayList.Count - 1);
                    }
                    while (num2 > 0 && arrayList.Count > 0)
                    {
                        int index = new Random().Next(0, arrayList.Count - 1);
                        FileInfo fileInfo = arrayList[index] as FileInfo;
                        if (fileInfo != null)
                        {
                            TimeSpan fileAge = GetFileAge(fileInfo);
                            if (fileAge > _minAge)
                            {
                                ReportDeletion(fileInfo, "Number of files in rolling slot '" + key + "' matching pattern exceeds " + num);
                                fileInfo.Delete();
                                num2--;
                            }
                        }
                        arrayList.RemoveAt(index);
                    }
                }
            }
        }

        public void Execute()
        {
            if (_active)
            {
                if (_rolling)
                {
                    RollingCleanup();
                }
                else
                {
                    SimpleCleanup();
                }
            }
        }

        public SortedList GetCandidateFiles(DirectoryInfo folder)
        {
            Assert.ArgumentNotNull(folder, "folder");
            SortedList sortedList = new SortedList(StringComparer.Ordinal);
            int num = 0;
            IEnumerable<FileSystemInfo> files = folder.GetFiles(_pattern);
            files = files.Concat(folder.GetDirectories(_pattern));
            foreach (FileSystemInfo item in files)
            {
                DateTime utcNow = DateTime.UtcNow;
                DateTime t = (_maxAge == TimeSpan.MaxValue) ? DateTime.MaxValue : (GetFileTime(item) + _maxAge);
                if (utcNow < t)
                {
                    string key = DateUtil.ToIsoDate(GetFileTime(item)) + num.ToString().PadLeft(6, '0');
                    sortedList.Add(key, item);
                    num++;
                }
                else
                {
                    FileInfo fileInfo = item as FileInfo;
                    DirectoryInfo directoryInfo = item as DirectoryInfo;
                    string text = (fileInfo != null) ? "file" : "folder";
                    ReportDeletion(item, StringUtil.Capitalize(text) + " is older than max age " + _maxAge);
                    if ((fileInfo != null && FileUtil.IsWritable(fileInfo)) || directoryInfo != null)
                    {
                        try
                        {
                            if (directoryInfo != null)
                            {
                                directoryInfo.Delete(true);
                            }
                            else
                            {
                                fileInfo.Delete();
                            }
                        }
                        catch (Exception exception)
                        {
                            Log.Error("Could not delete candidate " + text + ".", exception, this);
                        }
                    }
                    else
                    {
                        Log.Info(StringUtil.Capitalize(text) + " was skipped as it appears to be locked by another process.", this);
                    }
                }
            }
            return sortedList;
        }

        public TimeSpan GetFileAge(FileSystemInfo file)
        {
            Assert.ArgumentNotNull(file, "file");
            return DateTime.UtcNow - GetFileTime(file);
        }

        public DateTime GetFileTime(FileSystemInfo file)
        {
            Assert.ArgumentNotNull(file, "file");
            if (file.LastWriteTimeUtc > file.CreationTimeUtc)
            {
                return file.LastWriteTimeUtc;
            }
            return file.CreationTimeUtc;
        }

        public Hashtable GetSlotCounts(string strategy)
        {
            Assert.ArgumentNotNull(strategy, "strategy");
            Hashtable hashtable = new Hashtable();
            string[] array = new string[5]
            {
            "hour",
            "day",
            "week",
            "month",
            "year"
            };
            string[] array2 = strategy.Split(',');
            for (int i = 0; i < array.Length; i++)
            {
                hashtable[array[i]] = ((array2.Length > i) ? System.Convert.ToInt32(array2[i].Trim()) : 2);
            }
            return hashtable;
        }

        public void ReportDeletion(FileSystemInfo file, string reason)
        {
            Assert.ArgumentNotNull(file, "file");
            Assert.ArgumentNotNull(reason, "reason");
            TimeSpan fileAge = GetFileAge(file);
            string str = ((file is FileInfo) ? "Filename: " : "Directory name: ") + file.FullName + ", " + ((file is FileInfo) ? "file" : "directory") + " date: " + DateUtil.ToServerTime(GetFileTime(file)) + ", age: " + fileAge + " (min age: " + _minAge + ", max age: " + _maxAge + "). Reason: " + reason;
            Log.Info(((file is FileInfo) ? "File" : "Directory") + " is being deleted by cleanup task: " + str, this);
        }

        public void RollingCleanup()
        {
            if (FileUtil.FolderExists(_folder))
            {
                RollingCleanup(new DirectoryInfo(_folder), _recursive);
            }
            else
            {
                Log.Warn("Folder to clean up was not found: " + _folder, this);
            }
        }

        public void SimpleCleanup()
        {
            if (FileUtil.FolderExists(_folder))
            {
                SimpleCleanup(new DirectoryInfo(_folder), _recursive);
            }
            else
            {
                Log.Warn("Folder to clean up was not found: " + _folder, this);
            }
        }

        private void RollingCleanup(DirectoryInfo folder, bool recursive)
        {
            RollingCleanup(folder);
            if (recursive)
            {
                DirectoryInfo[] directories = folder.GetDirectories();
                DirectoryInfo[] array = directories;
                foreach (DirectoryInfo folder2 in array)
                {
                    RollingCleanup(folder2, recursive);
                }
            }
        }

        private void RollingCleanup(DirectoryInfo folder)
        {
            SortedList candidateFiles = GetCandidateFiles(folder);
            Hashtable fileGroups = new Hashtable();
            foreach (FileInfo value in candidateFiles.Values)
            {
                TimeSpan fileAge = GetFileAge(value);
                if (fileAge.TotalHours < 1.0)
                {
                    AddFileToGroup(value, "hour", fileGroups);
                }
                else if (fileAge.TotalDays < 1.0)
                {
                    AddFileToGroup(value, "day", fileGroups);
                }
                else if (fileAge.TotalDays < 7.0)
                {
                    AddFileToGroup(value, "week", fileGroups);
                }
                else if (fileAge.TotalDays < 30.0)
                {
                    AddFileToGroup(value, "month", fileGroups);
                }
                else if (fileAge.TotalDays < 365.0)
                {
                    AddFileToGroup(value, "year", fileGroups);
                }
                else
                {
                    ReportDeletion(value, "File in rolling cleanup is more than one year old");
                    value.Delete();
                }
            }

            CleanupGroups(fileGroups);
        }

        private void SimpleCleanup(DirectoryInfo folder, bool recursive)
        {
            SimpleCleanup(folder);
            if (recursive)
            {
                DirectoryInfo[] directories = folder.GetDirectories();
                DirectoryInfo[] array = directories;
                foreach (DirectoryInfo folder2 in array)
                {
                    SimpleCleanup(folder2, recursive);
                }
            }
        }

        private void SimpleCleanup(DirectoryInfo folder)
        {
            SortedList candidateFiles = GetCandidateFiles(folder);
            if (candidateFiles.Count <= _minCount)
            {
                return;
            }
            int num = candidateFiles.Count - _maxCount;
            if (num <= 0)
            {
                return;
            }
            for (int i = 0; i < num; i++)
            {
                FileInfo fileInfo = candidateFiles.GetByIndex(i) as FileInfo;
                if (fileInfo != null)
                {
                    TimeSpan fileAge = GetFileAge(fileInfo);
                    if (fileAge < _minAge)
                    {
                        break;
                    }
                    ReportDeletion(fileInfo, "Number of files matching pattern exceeds " + _maxCount);
                    fileInfo.Delete();
                }
            }
        }
    }
}