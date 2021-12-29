﻿using Eto.Forms;
using log4net;
using log4net.Config;
using System;
using System.IO;
using System.Reflection;

namespace AM2RLauncher.Gtk
{
    /// <summary>
    /// The main class for the GTK project.
    /// </summary>
    class MainClass
    {
        /// <summary>
        /// The logger for <see cref="MainForm"/>, used to write any caught exceptions.
        /// </summary>
        private static readonly ILog log = LogManager.GetLogger(typeof(MainForm));
        /// <summary>
        /// The main method for the GTK project.
        /// </summary>
        [STAThread]
        public static void Main(string[] args)
        {
            string launcherDataPath = GenerateCurrentPath();

            // Make sure first, ~/.local/share/AM2RLauncher exists
            if (!Directory.Exists(launcherDataPath))
                Directory.CreateDirectory(launcherDataPath);

            // Now, see if log4netConfig exists, if not write it again.
            if (!File.Exists(launcherDataPath + "/log4net.config"))
                File.WriteAllText(launcherDataPath + "/log4net.config", Properties.Resources.log4netContents.Replace("${DATADIR}", launcherDataPath));

            // Configure logger
            XmlConfigurator.Configure(new FileInfo(launcherDataPath + "/log4net.config"));

            try
            {
                Application GTKLauncher = new Application(Eto.Platforms.Gtk);
                LauncherUpdater.Main();
                GTKLauncher.UnhandledException += GTKLauncher_UnhandledException;
                GTKLauncher.Run(new MainForm());
            }
            catch (Exception e)
            {
                log.Error("An unhandled exception has occurred: \n*****Stack Trace*****\n\n" + e.StackTrace.ToString());
                Console.WriteLine(Language.Text.UnhandledException + "\n" + e.Message + "\n*****Stack Trace*****\n\n" + e.StackTrace.ToString());
                Console.WriteLine("Check the logs at " + launcherDataPath + " for more info!");
            }
        }

        /// <summary>
        /// This method gets fired when an unhandled excpetion occurs in <see cref="MainForm"/>.
        /// </summary>
        private static void GTKLauncher_UnhandledException(object sender, Eto.UnhandledExceptionEventArgs e)
        {
            log.Error("An unhandled exception has occurred: \n*****Stack Trace*****\n\n" + e.ExceptionObject.ToString());
            MessageBox.Show(Language.Text.UnhandledException + "\n*****Stack Trace*****\n\n" + e.ExceptionObject.ToString(), "GTK", MessageBoxType.Error);
        }

        // This is a duplicate of CrossPlatformOperations.GenerateCurrentPath, because trying to invoke that would cause a crash due to currentPlatform not being initialized.
        private static string GenerateCurrentPath()
        {
            string NIXHOME = Environment.GetEnvironmentVariable("HOME");
            // First, we check if the user has a custom AM2RLAUNCHERDATA env var
            string am2rLauncherDataEnvVar = Environment.GetEnvironmentVariable("AM2RLAUNCHERDATA");
            if (!String.IsNullOrWhiteSpace(am2rLauncherDataEnvVar))
            {
                try
                {
                    // This will create the directories recursively if they don't exist
                    Directory.CreateDirectory(am2rLauncherDataEnvVar);

                    // Our env var is now set and directories exist
                    return am2rLauncherDataEnvVar;
                }
                catch { }
            }

            // First check if XDG_DATA_HOME is set, if not we'll use ~/.local/share
            string xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(xdgDataHome))
                xdgDataHome = NIXHOME + "/.local/share";

            // Add AM2RLauncher to the end of the dataPath
            xdgDataHome += "/AM2RLauncher";

            try
            {
                // This will create the directories recursively if they don't exist
                Directory.CreateDirectory(xdgDataHome);

                // Our env var is now set and directories exist
                return xdgDataHome;
            }
            catch { }

            return Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory);
        }
    }
}
