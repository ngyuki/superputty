﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using log4net;
using SuperPutty.Data;
using SuperPutty.Properties;
using SuperPutty.Utils;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPutty
{
    /// <summary>
    /// Represents the SuperPuTTY application itself
    /// </summary>
    public static class SuperPuTTY 
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SuperPuTTY));

        public static event EventHandler<LayoutChangedEventArgs> LayoutChanging;
        public static event EventHandler<LayoutChangedEventArgs> LayoutChanged;

        public static event Action<String> StatusEvent;

        static BindingList<LayoutData> layouts = new BindingList<LayoutData>();
        static Dictionary<string, SessionData> sessionsMap = new Dictionary<string, SessionData>();
        static BindingList<SessionData> sessionsList = new BindingList<SessionData>();

        public static void Initialize(string[] args)
        {
            Log.InfoFormat("Initializing.  UserSettings={0}, ={1}", Settings.SettingsFilePath, Settings.SettingsFolder);

            // parse command line args
            CommandLine = new CommandLineOptions(args);

            // handle settings upgrade
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            if (Settings.ApplicationVersion != version)
            {
                Log.InfoFormat("Upgrading Settings to {0}", version);
                Settings.Upgrade();
                Settings.ApplicationVersion = version;
                Settings.Save();
            }

            // load data
            LoadLayouts();
            LoadSessions();

            // determine starting layout, if any.  CLI has priority
            if (CommandLine.IsValid)
            {
                if (CommandLine.Layout != null)
                {
                    StartingLayout = FindLayout(CommandLine.Layout);
                    if (StartingLayout != null)
                    {
                        Log.InfoFormat("Starting with layout from command line, {0}", CommandLine.Layout);
                    }
                }
                else
                {
                    // ad-hoc session specified
                    SessionDataStartInfo sessionStartInfo = CommandLine.ToSessionStartInfo();
                    if (sessionStartInfo != null)
                    {
                        StartingSession = sessionStartInfo;
                        Log.InfoFormat("Starting adhoc Session from command line, {0}", StartingSession.Session.SessionId);
                    }
                }

            }

            // if nothing specified, then try the default layout
            if (StartingLayout == null && StartingSession == null)
            {
                StartingLayout = FindLayout(Settings.DefaultLayoutName);
                if (StartingLayout != null)
                {
                    Log.InfoFormat("Starting with default layout, {0}", Settings.DefaultLayoutName);
                }
            }

            // Register IpcChanncel for single instance support
            SingleInstanceHelper.RegisterRemotingService();

            Log.Info("Initialized");
        }



        public static void Shutdown()
        {
            Log.Info("Shutting down...");
            //SaveSessions();
        }

        public static void ReportStatus(String status, params Object[] args)
        {
            String msg = (args.Length > 0) ? String.Format(status, args) : status;
            Log.DebugFormat("STATUS: {0}", msg);

            if (StatusEvent != null)
            {
                StatusEvent(msg);
            }
        }

        #region Layouts

        public static bool IsLayoutChanging { get; private set; }

        public static void AddLayout(String file)
        {
            LayoutData layout = new LayoutData(file);
            if (FindLayout(layout.Name) == null)
            {
                layouts.Add(layout);
                LoadLayout(layout, true);
            }
        }

        public static void RemoveLayout(String name)
        {
            LayoutData layout = FindLayout(name);
            if (layout != null)
            {
                layouts.Remove(layout);
            }
        }

        public static LayoutData FindLayout(String name)
        {
            LayoutData target = null;
            foreach (LayoutData layout in layouts)
            {
                if (name == layout.Name)
                {
                    target = layout;
                    break;
                }
            }
            return target;
        }
        public static void LoadLayouts()
        {
            if (!String.IsNullOrEmpty(Settings.SettingsFolder))
            {
                if (Directory.Exists(LayoutsDir))
                {
                    List<LayoutData> newLayouts = new List<LayoutData>();
                    foreach (String file in Directory.GetFiles(LayoutsDir))
                    {
                        newLayouts.Add(new LayoutData(file));
                    }

                    layouts.Clear();
                    foreach (LayoutData layout in newLayouts)
                    {
                        layouts.Add(layout);
                    }
                    Log.InfoFormat("Loaded {0} layouts", newLayouts.Count);
                }
                else
                {
                    Log.InfoFormat("Creating layouts dir: " + SuperPuTTY.LayoutsDir);
                    Directory.CreateDirectory(SuperPuTTY.LayoutsDir);
                }
            }            
        }

        public static void LoadLayout(LayoutData layout)
        {
            LoadLayout(layout, false);

        }

        public static void LoadLayout(LayoutData layout, bool isNewLayoutAlreadyActive)
        {
            Log.InfoFormat("LoadLayout: layout={0}, isNewLayoutAlreadyActive={1}", layout == null ? "NULL" : layout.Name, isNewLayoutAlreadyActive);
            LayoutChangedEventArgs args = new LayoutChangedEventArgs
            {
                New = layout,
                Old = CurrentLayout,
                IsNewLayoutAlreadyActive = isNewLayoutAlreadyActive
            };

            try
            {
                IsLayoutChanging = true;

                if (LayoutChanging != null)
                {
                    LayoutChanging(typeof(SuperPuTTY), args);
                }

            }
            finally
            {
                IsLayoutChanging = false;
            }


            if (LayoutChanged != null)
            {
                CurrentLayout = layout;
                LayoutChanged(typeof(SuperPuTTY), args);
            }
        }

        public static void LoadLayoutInNewInstance(LayoutData layout)
        {
            ReportStatus("Starting new instance with layout, {0}", layout.Name);
            Process.Start(Assembly.GetExecutingAssembly().Location, "-layout \"" + layout + "\"");
        }

        public static void SetLayoutAsDefault(string layoutName)
        {
            if (!string.IsNullOrEmpty(layoutName))
            {
                LayoutData layout = FindLayout(layoutName);
                if (layout != null)
                {
                    ReportStatus("Setting {0} as default layout", layoutName);
                    SuperPuTTY.Settings.DefaultLayoutName = layoutName;
                    SuperPuTTY.Settings.Save();

                    // so gui change is propagated via events
                    LoadLayouts();
                }
            }
        }

        #endregion

        #region Sessions

        public static string SessionsFileName
        {
            get
            {
                return Path.Combine(Settings.SettingsFolder, "Sessions.XML");
            }
        }
        public static void LoadSessions()
        {
            string fileName = SessionsFileName;
            Log.InfoFormat("Loading all sessions.  file={0}", fileName);

            List<SessionData> sessions;
            if (!File.Exists(fileName))
            {
                Log.InfoFormat("Sessions file does not exist.  Attempting import from registry");
                sessions = SessionData.LoadSessionsFromRegistry();

                if (sessions != null)
                {
                    // create default
                    Log.InfoFormat("Imported {0} sessions.  Saving default file.", sessions.Count);
                    SessionData.SaveSessionsToFile(sessions, fileName);
                }
            }
            else
            {
                sessions = SessionData.LoadSessionsFromFile(fileName);
            }

            // remove old
            sessionsMap.Clear();
            sessionsList.Clear();

            foreach (SessionData session in sessions)
            {
                AddSession(session);
            }
        }

        public static void SaveSessions()
        {
            Log.InfoFormat("Saving all sessions");
            SessionData.SaveSessionsToFile(GetAllSessions(), SessionsFileName);
        }

        public static SessionData RemoveSession(string sessionId)
        {
            SessionData session = GetSessionById(sessionId);
            if (session != null)
            {
                sessionsMap.Remove(sessionId);
                sessionsList.Remove(session);
            }

            return session;
        }

        public static SessionData GetSessionById(string sessionId)
        {
            SessionData session = null;
            if (sessionId != null)
            {
                sessionsMap.TryGetValue(sessionId, out session);
            }
            return session;
        }

        public static bool AddSession(SessionData session)
        {
            bool success = false;
            if (GetSessionById(session.SessionId) == null)
            {
                Log.InfoFormat("Added Session, id={0}", session.SessionId);
                sessionsMap.Add(session.SessionId, session);
                sessionsList.Add(session);
                success = true;
            }
            else
            {
                Log.InfoFormat("Failed to Add Session, id={0}.  Session already exists", session.SessionId);
            }
            return success;
        }

        public static List<SessionData> GetAllSessions()
        {
            return sessionsMap.Values.ToList();
        }

        public static void OpenPuttySession(string sessionId)
        {
            OpenPuttySession(GetSessionById(sessionId));
        }

        public static void OpenPuttySession(SessionData session)
        {
            Log.InfoFormat("Opening putty session, id={0}", session == null ? "" : session.SessionId);
            if (session != null)
            {
                ctlPuttyPanel sessionPanel = ctlPuttyPanel.NewPanel(session);
                ApplyDockRestrictions(sessionPanel);
                sessionPanel.Show(MainForm.DockPanel, session.LastDockstate);
                SuperPuTTY.ReportStatus("Opened session: {0} [{1}]", session.SessionId, session.Proto);
            }
        }

        public static void OpenScpSession(string sessionId)
        {
            OpenScpSession(GetSessionById(sessionId));
        }

        public static void OpenScpSession(SessionData session)
        {
            Log.InfoFormat("Opening scp session, id={0}", session == null ? "" : session.SessionId);
            if (session != null)
            {
                RemoteFileListPanel panel = null;
                bool cancelShow = false;
                if (session != null)
                {
                    PuttyClosedCallback callback = delegate(bool error)
                    {
                        cancelShow = error;
                    };
                    PscpTransfer xfer = new PscpTransfer(session);
                    xfer.PuttyClosed = callback;

                    panel = new RemoteFileListPanel(xfer, SuperPuTTY.MainForm.DockPanel, session);
                    ApplyDockRestrictions(panel);
                    if (!cancelShow)
                    {
                        panel.Show(MainForm.DockPanel, session.LastDockstate);
                    }
                }

                SuperPuTTY.ReportStatus("Opened session: {0} [SCP]", session.SessionId);
            }
        }

        public static void ApplyDockRestrictions(DockContent panel)
        {
            if (SuperPuTTY.Settings.RestrictContentToDocumentTabs)
            {
                panel.DockAreas = DockAreas.Document | DockAreas.Float;
            }
        }

        public static void OpenSession(SessionDataStartInfo ssi)
        {
            if (MainForm.InvokeRequired)
            {
                MainForm.BeginInvoke(new Action<SessionDataStartInfo>(OpenSession), ssi);
                return;
            }

            if (ssi != null)
            {
                if (ssi.UseScp)
                {
                    SuperPuTTY.OpenScpSession(ssi.Session);
                }
                else
                {
                    SuperPuTTY.OpenPuttySession(ssi.Session);
                }
            }
        }

        public static void ImportSessionsFromFile(string fileName)
        {
            if (fileName == null) { return; }
            if (File.Exists(fileName))
            {
                Log.InfoFormat("Importing sessions from file, path={0}", fileName);
                List<SessionData> sessions = SessionData.LoadSessionsFromFile(fileName);
                foreach (SessionData session in sessions)
                {
                    // pre-pend session id with "Imported" to put them into an imported folder
                    session.SessionId = MakeUniqueSessionId(SessionData.CombineSessionIds("Imported", session.SessionId));
                    session.SessionName = SessionData.GetSessionNameFromId(session.SessionId);
                    AddSession(session);
                }
            }

        }

        public static string MakeUniqueSessionId(string sessionId)
        {
            String newSessionId = sessionId;

            for (int i = 1; i < 1000; i++)
            {
                SessionData sessionExisting = GetSessionById(newSessionId);
                if (sessionExisting == null)
                {
                    break;
                }                
                newSessionId = String.Format("{0}-{1}", sessionId, i);
            }

            return newSessionId;
        }

        #endregion

        #region Properties

        public static bool IsFirstRun
        {
            get
            {
                // check first load
                return string.IsNullOrEmpty(Settings.PuttyExe);
            }
        }

        public static frmSuperPutty MainForm { get; set; }

        internal static Settings Settings { get { return Settings.Default; } }

        public static string LayoutsDir { get { return Path.Combine(Settings.SettingsFolder, "layouts"); } }


        public static LayoutData CurrentLayout { get; private set; }

        public static LayoutData StartingLayout { get; private set; }
        public static SessionDataStartInfo StartingSession { get; private set; }

        public static BindingList<LayoutData> Layouts { get { return layouts; } }

        public static BindingList<SessionData> Sessions { get { return sessionsList; } }

        public static CommandLineOptions CommandLine { get; private set; }

        public static bool IsKiTTY
        {
            get
            {
                bool isKitty = false;
                if (File.Exists(Settings.PuttyExe))
                {
                    string exe = Path.GetFileName(Settings.PuttyExe);
                    isKitty = exe != null && exe.ToLower().StartsWith("kitty");
                }
                return isKitty;
            }
        }
        #endregion
    }

}
