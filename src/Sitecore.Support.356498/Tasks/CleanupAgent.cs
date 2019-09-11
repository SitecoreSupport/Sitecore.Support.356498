namespace Sitecore.Support.Tasks
{
    using Sitecore.Diagnostics;
    using Sitecore.Diagnostics.PerformanceCounters;
    using Sitecore.Tasks;
    using System;
    using System.Collections;
    using System.Xml;

    public class CleanupAgent
    {
        private readonly ArrayList _fileCleaners = new ArrayList();

        private bool m_logActivity = true;

        public bool LogActivity
        {
            get
            {
                return m_logActivity;
            }
            set
            {
                m_logActivity = value;
            }
        }

        public void Run()
        {
            LogInfo("Scheduling.CleanupAgent started. FileCleaner count: " + _fileCleaners.Count);
            foreach (FileCleaner fileCleaner in _fileCleaners)
            {
                try
                {
                    fileCleaner.Execute();
                }
                catch (Exception exception)
                {
                    Log.Error("Exception in Scheduling.CleanupAgent. Folder: " + fileCleaner.Folder, exception, this);
                }
            }
            LogInfo("Scheduling.CleanupAgent done");
            JobsCount.TasksFileCleanups.Increment(1L);
        }

        internal void AddCommand(XmlNode configNode)
        {
            Assert.ArgumentNotNull(configNode, "configNode");
            FileCleaner value = new FileCleaner(configNode);
            _fileCleaners.Add(value);
        }

        private void LogInfo(string message)
        {
            Assert.ArgumentNotNull(message, "message");
            if (LogActivity)
            {
                Log.Info(message, this);
            }
        }
    }
}