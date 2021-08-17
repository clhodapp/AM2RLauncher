﻿using Eto;
using Eto.Forms;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using log4net;
using AM2RLauncher.Helpers;

namespace AM2RLauncher
{
    /// <summary>
    /// Class that checks for Updates and then Updates the Launcher.
    /// </summary>
    public class LauncherUpdater
    {
        /// <summary>The Version that identifies this current release.</summary>
        static readonly public string VERSION = "2.1.0";

        /// <summary>The current Running platform.</summary>
        static readonly private Platform currentPlatform = Platform.Instance;   //needs to be declared here as well, because I can't access the one from eto
                                                                                //since it's not loaded at this point

        /// <summary>The Path of the oldConfig. Only gets used Windows-only</summary>
        static readonly private string oldConfigPath = CrossPlatformOperations.CURRENTPATH + "/" + CrossPlatformOperations.LAUNCHERNAME + ".oldCfg";

        // Load reference to logger
        /// <summary>
        /// Our log object, that handles logging the current execution to a file.
        /// </summary>
        private static readonly ILog log = LogManager.GetLogger(typeof(MainForm));

        /// <summary>
        /// Performs the entire AM2RLauncher update procedure. 
        /// </summary>
        public static void Main()
        {
            log.Info("Running update check...");

            string version = VERSION.Replace(".", "");

            //update section

            //delete old files that have been left
            if (File.Exists(CrossPlatformOperations.CURRENTPATH + "/AM2RLauncher.bak"))
            {
                log.Info("AM2RLauncher.bak detected. Removing file.");
                File.Delete(CrossPlatformOperations.CURRENTPATH + "/AM2RLauncher.bak");
            }
            if (currentPlatform.IsWinForms && File.Exists(oldConfigPath))
            {
                log.Info(CrossPlatformOperations.LAUNCHERNAME + ".oldCfg detected. Removing file.");
                File.Delete(oldConfigPath);
            }
            if (currentPlatform.IsWinForms && Directory.Exists(CrossPlatformOperations.CURRENTPATH + "/oldLib"))
            {
                log.Info("Old lib folder detected, removing folder.");
                Directory.Delete(CrossPlatformOperations.CURRENTPATH + "/oldLib", true);
            }

            //check settings if autoUpdate is set to true
            bool autoUpdate = bool.Parse(CrossPlatformOperations.ReadFromConfig("AutoUpdate"));

            if (autoUpdate)
            {
                log.Info("AutoUpdate set to true!");

                //this is supposed to fix the updater throwing an exception on windows 7 and earlier(?)
                //see this for information: https://stackoverflow.com/questions/2859790/the-request-was-aborted-could-not-create-ssl-tls-secure-channel and https://stackoverflow.com/a/50977774
                if (currentPlatform.IsWinForms)
                {
                    ServicePointManager.Expect100Continue = true;
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                }

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://github.com/AM2R-Community-Developers/AM2RLauncher/releases/latest");
                HttpWebResponse response = null;
                try
                {        
                    response = (HttpWebResponse)request.GetResponse();
                }
                catch (WebException)
                {
                    log.Error("WebException caught! Displaying MessageBox.");
                    MessageBox.Show(Language.Text.NoInternetConnection);
                    return;
                }

                Uri realUri = response.ResponseUri;
                string onlineVersion = realUri.AbsoluteUri.Substring(realUri.AbsoluteUri.LastIndexOf('/') + 1);
                bool isCurrentVersionOutdated = false;

                string[] localVersionArray = VERSION.Split('.');
                string[] onlineVersionArray = onlineVersion.Split('.');

                for (int i = 0; i < localVersionArray.Length; i++)
                {
                    if (int.Parse(onlineVersionArray[i]) > int.Parse(localVersionArray[i]))
                    {
                        isCurrentVersionOutdated = true;
                        break;
                    }
                }

                if (isCurrentVersionOutdated)
                {
                    log.Info("Current version (" + VERSION + ") is outdated! Initiating update for version " + onlineVersion + ".");

                    string tmpUpdatePath = CrossPlatformOperations.CURRENTPATH + "/tmpupdate/";
                    string zipPath = CrossPlatformOperations.CURRENTPATH + "/launcher.zip";

                    try
                    { 
                        using (var client = new WebClient())
                        {
                            string platformSuffix = "";
                            if (currentPlatform.IsWinForms) platformSuffix = "_win";
                            else if (currentPlatform.IsGtk) platformSuffix = "_lin";

                            log.Info("Downloading https://github.com/AM2R-Community-Developers/AM2RLauncher/releases/latest/download/AM2RLauncher_" + onlineVersion + platformSuffix + ".zip to " + zipPath + ".");
                            
                            client.DownloadFile("https://github.com/AM2R-Community-Developers/AM2RLauncher/releases/latest/download/AM2RLauncher_" + onlineVersion + platformSuffix + ".zip", zipPath);

                            log.Info("File successfully downloaded.");
                        }
                    }
                    catch(UnauthorizedAccessException)
                    {
                        log.Error("UnauthorizedAccessException caught! Displaying MessageBox.");
                        MessageBox.Show(Language.Text.UnauthorizedAccessMessage);
                        return;
                    }

                    if (Directory.Exists(tmpUpdatePath))        //auto-updating issue fixes
                        Directory.Delete(tmpUpdatePath);

                    if (!Directory.Exists(tmpUpdatePath))        // just in case it exists already
                        Directory.CreateDirectory(tmpUpdatePath);

                    ZipFile.ExtractToDirectory(zipPath, tmpUpdatePath);
                    log.Info("Updates successfully extracted to " + tmpUpdatePath);

                    File.Delete(zipPath);
                    File.Move(CrossPlatformOperations.CURRENTPATH + "/" + CrossPlatformOperations.LAUNCHERNAME, CrossPlatformOperations.CURRENTPATH + "/AM2RLauncher.bak");
                    if (currentPlatform.IsWinForms) File.Move(CrossPlatformOperations.LAUNCHERNAME + ".config", CrossPlatformOperations.LAUNCHERNAME + ".oldCfg");

                    foreach (var file in new DirectoryInfo(tmpUpdatePath).GetFiles())
                    {
                        log.Info("Moving " +  file.FullName + " to " + CrossPlatformOperations.CURRENTPATH + "/" + file.Name);
                        File.Copy(file.FullName, CrossPlatformOperations.CURRENTPATH + "/" + file.Name, true);
                    }
                    // for windows, the actual application is in "AM2RLauncher.dll". Which means, we need to update the lib folder as well.
                    if (currentPlatform.IsWinForms)
                    {
                        Directory.Move(CrossPlatformOperations.CURRENTPATH + "/lib", CrossPlatformOperations.CURRENTPATH + "/oldLib");
                        HelperMethods.DirectoryCopy(tmpUpdatePath + "lib", CrossPlatformOperations.CURRENTPATH + "/lib", true);
                    }
                    
                    Directory.Delete(tmpUpdatePath, true);

                    CrossPlatformOperations.CopyOldConfigToNewConfig();

                    log.Info("Files extracted. Preparing to restart executable...");

                    if (currentPlatform.IsGtk) System.Diagnostics.Process.Start("chmod", "+x ./AM2RLauncher.Gtk");

                    System.Diagnostics.Process.Start(CrossPlatformOperations.CURRENTPATH + "/" + CrossPlatformOperations.LAUNCHERNAME);
                    Environment.Exit(0);
                }
            }
            else
            {
                log.Info("AutoUpdate set to false. Exiting update check.");
            }
        }
    }
}
