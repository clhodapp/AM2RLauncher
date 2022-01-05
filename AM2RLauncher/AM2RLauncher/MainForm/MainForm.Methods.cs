﻿using AM2RLauncher.Helpers;
using AM2RLauncher.XML;
using Eto;
using Eto.Drawing;
using Eto.Forms;
using LibGit2Sharp;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace AM2RLauncher
{
    public partial class MainForm : Form
    {
        /// <summary>
        /// Git Pulls from the repository.
        /// </summary>
        private void PullPatchData()
        {
            log.Info("Attempting to pull repository " + currentMirror + "...");
            using (var repo = new Repository(CrossPlatformOperations.CURRENTPATH + "/PatchData"))
            {
                // Permanently undo commits not pushed to remote
                Branch originMaster = repo.Branches["origin/master"];

                if (originMaster == null)
                {
                    // Directory exists, but seems corrupted, we delete it and prompt the user to download it again.
                    MessageBox.Show(Language.Text.CorruptPatchData, Language.Text.ErrorWindowTitle, MessageBoxType.Error);
                    HelperMethods.DeleteDirectory(CrossPlatformOperations.CURRENTPATH + "/PatchData");
                    throw new UserCancelledException();
                }

                repo.Reset(ResetMode.Hard, originMaster.Tip);

                // Credential information to fetch

                PullOptions options = new PullOptions();
                options.FetchOptions = new FetchOptions();
                options.FetchOptions.OnTransferProgress += TransferProgressHandlerMethod;

                // User information to create a merge commit
                var signature = new Signature("null", "null", DateTimeOffset.Now);

                // Pull
                try
                {
                    Commands.Pull(repo, signature, options);
                }
                catch
                {
                    log.Error("Repository pull attempt failed!");
                    return;
                }
            }
            log.Info("Repository pulled successfully.");
        }

        /// <summary>
        /// Method that updates <see cref="progressBar"/>.
        /// </summary>
        /// <param name="value">The value that <see cref="progressBar"/> should be set to.</param>
        /// <param name="min">The min value that <see cref="progressBar"/> should be set to.</param>
        /// <param name="max">The max value that <see cref="progressBar"/> should be set to.</param>
        private void UpdateProgressBar(int value, int min = 0, int max = 100)
        {
            Application.Instance.Invoke(new Action(() =>
            {
                progressBar.MinValue = min;
                progressBar.MaxValue = max;
                progressBar.Value = value;
            }));
        }

        /// <summary>
        /// Checks if <paramref name="profile"/> is installed.
        /// </summary>
        /// <param name="profile">The <see cref="ProfileXML"/> that should be checked for installation.</param>
        /// <returns><see langword="true"/> if yes, <see langword="false"/> if not.</returns>
        private bool IsProfileInstalled(ProfileXML profile)
        {
            if (Platform.IsWinForms) return File.Exists(CrossPlatformOperations.CURRENTPATH + "/Profiles/" + profile.Name + "/AM2R.exe");
            if (Platform.IsGtk) return File.Exists(CrossPlatformOperations.CURRENTPATH + "/Profiles/" + profile.Name + "/AM2R.AppImage");
            return false;
        }

        /// <summary>
        /// Safety check function before accessing <see cref="profileIndex"/>.
        /// </summary>
        /// <returns><see langword="true"/> if it is valid, <see langword="false"/> if not.</returns>
        private bool IsProfileIndexValid()
        {
            return profileIndex != null;
        }

        /// <summary>
        /// Deletes the given <paramref name="profile"/>. Reloads the <see cref="profileList"/> if <paramref name="reloadProfileList"/> is true.
        /// </summary>
        private void DeleteProfile(ProfileXML profile, bool reloadProfileList = true)
        {
            log.Info("Attempting to delete profile " + profile.Name + "...");

            // Delete folder in Mods
            if (Directory.Exists(CrossPlatformOperations.CURRENTPATH + profile.DataPath))
            {
                HelperMethods.DeleteDirectory(CrossPlatformOperations.CURRENTPATH + profile.DataPath);
            }

            // Delete the zip file in Mods
            if (File.Exists(CrossPlatformOperations.CURRENTPATH + profile.DataPath + ".zip"))
            {
                File.SetAttributes(CrossPlatformOperations.CURRENTPATH + profile.DataPath + ".zip", FileAttributes.Normal); // For some reason, it was set at read only, so we undo that here
                File.Delete(CrossPlatformOperations.CURRENTPATH + profile.DataPath + ".zip");
            }

            // Delete folder in Profiles
            if (Directory.Exists(CrossPlatformOperations.CURRENTPATH + "/Profiles/" + profile.Name))
            {
                HelperMethods.DeleteDirectory(CrossPlatformOperations.CURRENTPATH + "/Profiles/" + profile.Name);
            }

            if (reloadProfileList)
                LoadProfiles();

            log.Info("Succesfully deleted profile " + profile.Name + ".");
        }

        /// <summary>
        /// Scans the PatchData and Mods folders for valid profile entries, and loads them.
        /// </summary>
        private void LoadProfiles()
        {
            log.Info("Loading profiles...");

            // Reset loaded profiles
            profileDropDown.Items.Clear();
            profileList.Clear();
            profileIndex = null;

            // Check for and add the Community Updates profile
            if (File.Exists(CrossPlatformOperations.CURRENTPATH + "/PatchData/profile.xml"))
            {
                profileList.Add(Serializer.Deserialize<ProfileXML>(File.ReadAllText(CrossPlatformOperations.CURRENTPATH + "/PatchData/profile.xml")));
                profileList[0].DataPath = "/PatchData/data";
            }

            // Safety check to generate the Mods folder if it does not exist
            if (!Directory.Exists(CrossPlatformOperations.CURRENTPATH + "/Mods"))
                Directory.CreateDirectory(CrossPlatformOperations.CURRENTPATH + "/Mods");

            // Get Mods folder info
            DirectoryInfo modsDir = new DirectoryInfo(CrossPlatformOperations.CURRENTPATH + "/Mods");

            // Add all extracted profiles in Mods to the profileList.
            foreach (DirectoryInfo dir in modsDir.GetDirectories())
            {
                foreach (FileInfo file in dir.GetFiles())
                {
                    if (file.Name == "profile.xml")
                    {
                        ProfileXML prof = Serializer.Deserialize<ProfileXML>(File.ReadAllText(dir.FullName + "/profile.xml"));
                        if (prof.Installable == true || IsProfileInstalled(prof)) // Safety check for non-installable profiles
                        {
                            prof.DataPath = "/Mods/" + dir.Name;
                            profileList.Add(prof);
                        }
                        else if (!IsProfileInstalled(prof)) // If not installable and isn't installed, remove it
                        {
                            prof.DataPath = "/Mods/" + dir.Name;
                            DeleteProfile(prof, false);
                        }
                    }
                }
            }

            // Add profile names to the profileDropDown
            foreach (ProfileXML profile in profileList)
            {
                // Archive version notes
                if (!profile.Installable)
                    profile.ProfileNotes = Language.Text.ArchiveNotes;

                profileDropDown.Items.Add(profile.Name);
            }

            // Read the value from the config
            string profIndexString = CrossPlatformOperations.ReadFromConfig("ProfileIndex");

            // Check if either no profile was found or the setting says that the last current profile didn't exist
            if (profileDropDown.Items.Count == 0)
                profileIndex = null;
            else
            {
                // We know that profiles exist at this point, so we're going to point it to 0 instead so the following code doesn't fail
                if (profIndexString == "null")
                    profIndexString = "0";

                // We parse from the settings, and check if profiles got deleted from the last time the launcher has been selected. if yes, we revert the last selection to 0;
                int intParseResult = Int32.Parse(profIndexString);
                profileIndex = intParseResult;
                if (profileIndex >= profileDropDown.Items.Count)
                    profileIndex = 0;
                profileDropDown.SelectedIndex = profileIndex.Value;
            }

            // Update stored profiles in the Profile Settings tab
            settingsProfileDropDown.Items.Clear();
            settingsProfileDropDown.Items.AddRange(profileDropDown.Items);
            settingsProfileDropDown.SelectedIndex = profileDropDown.Items.Count != 0 ? 0 : -1;

            log.Info("Profiles loaded.");

            // Refresh the author and version label on the main tab
            if (profileList.Count > 0)
            {
                profileAuthorLabel.Text = Language.Text.Author + " " + profileList[profileDropDown.SelectedIndex].Author;
                profileVersionLabel.Text = Language.Text.VersionLabel + " " + profileList[profileDropDown.SelectedIndex].Version;
            }

            UpdateStateMachine();
        }

        /// <summary>
        /// Installs <paramref name="profile"/>.
        /// </summary>
        /// <param name="profile"><see cref="ProfileXML"/> to be installed.</param>
        private void InstallProfile(ProfileXML profile)
        {
            log.Info("Installing profile " + profile.Name + "...");

            // Check if xdelta is installed on linux, by searching all folders in PATH
            if (Platform.IsGtk && !CrossPlatformOperations.CheckIfXdeltaIsInstalled())
            {
                Application.Instance.Invoke(new Action(() =>
                {
                    MessageBox.Show(Language.Text.XdeltaNotFound, Language.Text.WarningWindowTitle, MessageBoxButtons.OK);
                }));
                SetPlayButtonState(UpdateState.Install);
                UpdateStateMachine();
                log.Error("Xdelta not found. Aborting installing a profile...");
                return;
            }

            string profilesHomePath = CrossPlatformOperations.CURRENTPATH + "/Profiles";
            string profilePath = profilesHomePath + "/" + profile.Name;

            // Failsafe for Profiles directory
            if (!Directory.Exists(profilesHomePath))
                Directory.CreateDirectory(profilesHomePath);


            // This failsafe should NEVER get triggered, but Miepee's broken this too much for me to trust it otherwise.
            if (Directory.Exists(profilePath))
                Directory.Delete(profilePath, true);


            // Create profile directory
            Directory.CreateDirectory(profilePath);

            // Switch profilePath on Gtk
            if (Platform.IsGtk)
            {
                if (Directory.Exists(profilePath + "/assets"))
                    Directory.Delete(profilePath + "/assets", true);

                profilePath += "/assets";
                Directory.CreateDirectory(profilePath);
            }

            // Extract 1.1
            ZipFile.ExtractToDirectory(CrossPlatformOperations.CURRENTPATH + "/AM2R_11.zip", profilePath);

            // Extracted 1.1
            UpdateProgressBar(33);
            log.Info("Profile folder created and AM2R_11.zip extracted.");

            string dataPath;

            // Set local datapath for installation files
            dataPath = CrossPlatformOperations.CURRENTPATH + profile.DataPath;

            string datawin = null, exe = null;

            if (Platform.IsWinForms)
            {
                datawin = "data.win";
                exe = "AM2R.exe";
            }
            else if (Platform.IsGtk)
            {
                datawin = "game.unx";
                // Use the exe name based on the desktop file in the appimage, rather than hardcoding it.
                string desktopContents = File.ReadAllText(CrossPlatformOperations.CURRENTPATH + "/PatchData/data/AM2R.AppDir/AM2R.desktop");
                exe = Regex.Match(desktopContents, @"(?<=Exec=).*").Value;
                log.Info("According to AppImage desktop file, using \"" + exe + "\" as game name.");
            }

            log.Info("Attempting to patch in " + profilePath);

            if (Platform.IsWinForms)
            {
                // Patch game executable
                if (profile.UsesYYC)
                {
                    CrossPlatformOperations.ApplyXdeltaPatch(profilePath + "/data.win", dataPath + "/AM2R.xdelta", profilePath + "/" + exe);

                    // Delete 1.1's data.win, we don't need it anymore!
                    File.Delete(profilePath + "/data.win");
                }
                else
                {
                    CrossPlatformOperations.ApplyXdeltaPatch(profilePath + "/data.win", dataPath + "/data.xdelta", profilePath + "/" + datawin);
                    CrossPlatformOperations.ApplyXdeltaPatch(profilePath + "/AM2R.exe", dataPath + "/AM2R.xdelta", profilePath + "/" + exe);
                }
            }
            else if (Platform.IsGtk)    // YYC and VM look exactly the same on Linux so we're all good here.
            {
                CrossPlatformOperations.ApplyXdeltaPatch(profilePath + "/data.win", dataPath + "/game.xdelta", profilePath + "/" + datawin);
                CrossPlatformOperations.ApplyXdeltaPatch(profilePath + "/AM2R.exe", dataPath + "/AM2R.xdelta", profilePath + "/" + exe);
                // Just in case the resulting file isn't chmoddded...
                Process.Start("chmod", "+x  \"" + profilePath + "/" + exe + "\"").WaitForExit();

                // These are not needed by linux at all, so we delete them
                File.Delete(profilePath + "/data.win");
                File.Delete(profilePath + "/AM2R.exe");
                File.Delete(profilePath + "/D3DX9_43.dll");

                // Move exe one directory out
                File.Move(profilePath + "/" + exe, profilePath.Substring(0, profilePath.LastIndexOf("/")) + "/" + exe);
            }

            // Applied patch
            if (Platform.IsWinForms) UpdateProgressBar(66);
            else if (Platform.IsGtk) UpdateProgressBar(44); // Linux will take a bit longer, due to appimage creation
            log.Info("xdelta patch(es) applied.");

            // Install new datafiles
            HelperMethods.DirectoryCopy(dataPath + "/files_to_copy", profilePath);

            // HQ music
            if (!profile.UsesCustomMusic && (bool)hqMusicPCCheck.Checked)
                HelperMethods.DirectoryCopy(CrossPlatformOperations.CURRENTPATH + "/PatchData/data/HDR_HQ_in-game_music", profilePath);


            // Linux post-process
            if (Platform.IsGtk)
            {
                string assetsPath = profilePath;
                profilePath = profilePath.Substring(0, profilePath.LastIndexOf("/"));

                // Rename all songs to lowercase
                foreach (var file in new DirectoryInfo(assetsPath).GetFiles())
                    if (file.Name.EndsWith(".ogg") && !File.Exists(file.DirectoryName + "/" + file.Name.ToLower()))
                        File.Move(file.FullName, file.DirectoryName + "/" + file.Name.ToLower());

                // Copy AppImage template to here
                HelperMethods.DirectoryCopy(CrossPlatformOperations.CURRENTPATH + "/PatchData/data/AM2R.AppDir", profilePath + "/AM2R.AppDir/");

                // Safety checks, in case the folders don't exist
                Directory.CreateDirectory(profilePath + "/AM2R.AppDir/usr/bin/");
                Directory.CreateDirectory(profilePath + "/AM2R.AppDir/usr/bin/assets/");

                // Copy game assets to the appimageDir
                HelperMethods.DirectoryCopy(assetsPath, profilePath + "/AM2R.AppDir/usr/bin/assets/");
                File.Copy(profilePath + "/" + exe, profilePath + "/AM2R.AppDir/usr/bin/" + exe);

                UpdateProgressBar(66);
                log.Info("Gtk-specific formatting finished.");

                // Temp save the currentWorkingDirectory and console.error, change it to profilePath and null, call the script, and change it back.
                string workingDir = Directory.GetCurrentDirectory();
                TextWriter cliError = Console.Error;
                Directory.SetCurrentDirectory(profilePath);
                Console.SetError(new StreamWriter(Stream.Null));
                Environment.SetEnvironmentVariable("ARCH", "x86_64");
                Process.Start(CrossPlatformOperations.CURRENTPATH + "/PatchData/utilities/appimagetool-x86_64.AppImage", "-n AM2R.AppDir").WaitForExit();
                Directory.SetCurrentDirectory(workingDir);
                Console.SetError(cliError);

                // Clean files
                Directory.Delete(profilePath + "/AM2R.AppDir", true);
                Directory.Delete(assetsPath, true);
                File.Delete(profilePath + "/" + exe);
                if (File.Exists(profilePath + "/AM2R.AppImage")) File.Delete(profilePath + "/AM2R.AppImage");
                File.Move(profilePath + "/" + "AM2R-x86_64.AppImage", profilePath + "/AM2R.AppImage");
            }

            // Copy profile.xml so we can grab data to compare for updates later!
            // tldr; check if we're in PatchData or not
            if (new DirectoryInfo(dataPath).Parent.Name == "PatchData")
                File.Copy(dataPath + "/../profile.xml", profilePath + "/profile.xml");
            else File.Copy(dataPath + "/profile.xml", profilePath + "/profile.xml");

            // Installed datafiles
            UpdateProgressBar(100);
            log.Info("Successfully installed profile " + profile.Name + ".");

            // This is just for visuals because the average windows end user will ask why it doesn't go to the end otherwise.
            if (Platform.IsWinForms)
                Thread.Sleep(1000);
        }

        /// <summary>
        /// Creates an APK of the selected <paramref name="profile"/>.
        /// </summary>
        /// <param name="profile"><see cref="ProfileXML"/> to be compiled into an APK.</param>
        private void CreateAPK(ProfileXML profile)
        {
            // Overall safety check just in case of bad situations
            if (!profile.SupportsAndroid) return;
            log.Info("Creating Android APK for profile " + profile.Name + ".");

            // Check for java, exit safely with a warning if not found!
            if (!CrossPlatformOperations.IsJavaInstalled())
            {
                // Message box show needs to be done on main thread
                Application.Instance.Invoke(new Action(() =>
                {
                    MessageBox.Show(Language.Text.JavaNotFound, Language.Text.WarningWindowTitle, MessageBoxButtons.OK);
                }));
                SetApkButtonState(ApkButtonState.Create);
                UpdateStateMachine();
                log.Error("Java not found! Aborting Android APK creation.");
                return;
            }
            // Check if xdelta is installed on linux
            if (Platform.IsGtk && !CrossPlatformOperations.CheckIfXdeltaIsInstalled())
            {
                // Message box show needs to be done on main thread
                Application.Instance.Invoke(new Action(() =>
                {
                    MessageBox.Show(Language.Text.XdeltaNotFound, Language.Text.WarningWindowTitle, MessageBoxButtons.OK);
                }));
                SetApkButtonState(ApkButtonState.Create);
                UpdateStateMachine();
                log.Error("Xdelta not found. Aborting Android APK creation...");
                return;
            }

            // Create working dir after some cleanup
            string apktoolPath = CrossPlatformOperations.CURRENTPATH + "/PatchData/utilities/android/apktool.jar",
                   uberPath = CrossPlatformOperations.CURRENTPATH + "/PatchData/utilities/android/uber-apk-signer.jar",
                   tempDir = new DirectoryInfo(CrossPlatformOperations.CURRENTPATH + "/temp").FullName,
                   dataPath = CrossPlatformOperations.CURRENTPATH + profile.DataPath;
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);

            log.Info("Cleanup, variables, and working directory created.");
            UpdateProgressBar(14);

            // Decompile AM2RWrapper.apk
            CrossPlatformOperations.RunJavaJar("\"" + apktoolPath + "\" d \"" + dataPath + "/android/AM2RWrapper.apk\"", tempDir);
            log.Info("AM2RWrapper decompiled.");
            UpdateProgressBar(28);

            // Add datafiles: 1.1, new datafiles, hq music, am2r.ini
            string workingDir = tempDir + "/AM2RWrapper/assets";
            ZipFile.ExtractToDirectory(CrossPlatformOperations.CURRENTPATH + "/AM2R_11.zip", workingDir);
            HelperMethods.DirectoryCopy(dataPath + "/files_to_copy", workingDir);
            if (hqMusicAndroidCheck.Checked == true)
                HelperMethods.DirectoryCopy(CrossPlatformOperations.CURRENTPATH + "/PatchData/data/HDR_HQ_in-game_music", workingDir);
            // Yes, I'm aware this is dumb. If you've got any better ideas for how to copy a seemingly randomly named .ini from this folder to the APK, please let me know.
            foreach (FileInfo file in new DirectoryInfo(dataPath).GetFiles().Where(f => f.Name.EndsWith("ini")))
                File.Copy(file.FullName, workingDir + "/" + file.Name); 
            
            log.Info("AM2R_11.zip extracted and datafiles copied into AM2RWrapper.");
            UpdateProgressBar(42);

            // Patch data.win to game.droid
            CrossPlatformOperations.ApplyXdeltaPatch(workingDir + "/data.win", dataPath + "/droid.xdelta", workingDir + "/game.droid");
            log.Info("game.droid successfully patched.");
            UpdateProgressBar(56);

            // Delete unnecessary files
            File.Delete(workingDir + "/AM2R.exe");
            File.Delete(workingDir + "/D3DX9_43.dll");
            File.Delete(workingDir + "/explanations.txt");
            File.Delete(workingDir + "/modifiers.ini");
            File.Delete(workingDir + "/readme.txt");
            File.Delete(workingDir + "/data.win");
            Directory.Delete(workingDir + "/mods", true);
            Directory.Delete(workingDir + "/lang/headers", true);
            if (Platform.IsGtk) File.Delete(workingDir + "/icon.png");
            // Modify apktool.yml to NOT compress ogg files
            string apktoolText = File.ReadAllText(workingDir + "/../apktool.yml");
            apktoolText = apktoolText.Replace("doNotCompress:", "doNotCompress:\n- ogg");
            File.WriteAllText(workingDir + "/../apktool.yml", apktoolText);
            log.Info("Unnecessary files removed, apktool.yml modified to prevent ogg compression.");
            UpdateProgressBar(70);

            // Rebuild APK
            CrossPlatformOperations.RunJavaJar("\"" + apktoolPath + "\" b AM2RWrapper -o \"" + profile.Name + ".apk\"", tempDir);
            log.Info("AM2RWrapper rebuilt into " + profile.Name + ".apk.");
            UpdateProgressBar(84);

            // Debug-sign APK
            CrossPlatformOperations.RunJavaJar("\"" + uberPath + "\" -a \"" + profile.Name + ".apk\"", tempDir);

            // Extra file cleanup
            File.Copy(tempDir + "/" + profile.Name + "-aligned-debugSigned.apk", CrossPlatformOperations.CURRENTPATH + "/" + profile.Name + ".apk", true);
            log.Info(profile.Name + ".apk signed and moved to " + CrossPlatformOperations.CURRENTPATH + "/" + profile.Name + ".apk.");
            HelperMethods.DeleteDirectory(tempDir);

            // Done
            UpdateProgressBar(100);
            log.Info("Successfully created Android APK for profile " + profile.Name + ".");
            CrossPlatformOperations.OpenFolderAndSelectFile(CrossPlatformOperations.CURRENTPATH + "/" + profile.Name + ".apk");
        }

        /// <summary>
        /// Runs the Game, works cross platform.
        /// </summary>
        private void RunGame()
        {
            if (IsProfileIndexValid())
            {
                ProfileXML profile = profileList[profileIndex.Value];

                // These are used on both windows and linux for game logging
                string savePath = Platform.IsWinForms ? profile.SaveLocation.Replace("%localappdata%", Environment.GetEnvironmentVariable("LOCALAPPDATA"))
                                                      : profile.SaveLocation.Replace("~", CrossPlatformOperations.NIXHOME);
                DirectoryInfo logDir = new DirectoryInfo(savePath + "/logs");
                string date = string.Join("-", DateTime.Now.ToString().Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

                log.Info("Launching game profile " + profile.Name + ".");

                if (Platform.IsWinForms)
                {
                    // Sets the arguments to empty, or to the profiles save path/logs and create time based logs. Creates the folder if necessary.
                    string arguments = "";

                    // Game logging
                    bool isLoggingEnabled = false;
                    Application.Instance.Invoke(new Action(() => isLoggingEnabled = (bool)profileDebugLogCheck.Checked));
                    
                    if (isLoggingEnabled)
                    {
                        log.Info("Performing logging setup for profile " + profile.Name + ".");

                        if (!Directory.Exists(logDir.FullName))
                            Directory.CreateDirectory(logDir.FullName);

                        if (File.Exists(logDir.FullName + "/" + profile.Name + ".txt"))
                            HelperMethods.RecursiveRollover(logDir.FullName + "/" + profile.Name + ".txt", 5);

                        StreamWriter stream = File.AppendText(logDir.FullName + "/" + profile.Name + ".txt");

                        stream.WriteLine("AM2RLauncher " + VERSION + " log generated at " + date);

                        if (isThisRunningFromWine)
                            stream.WriteLine("Using WINE!");

                        stream.Flush();

                        stream.Close();

                        arguments = "-debugoutput \"" + logDir.FullName + "/" + profile.Name + ".txt\" -output \"" + logDir.FullName + "/" + profile.Name + ".txt\"";
                    }

                    ProcessStartInfo proc = new ProcessStartInfo();

                    proc.WorkingDirectory = CrossPlatformOperations.CURRENTPATH + "/Profiles/" + profile.Name;
                    proc.FileName = proc.WorkingDirectory + "/AM2R.exe";
                    proc.Arguments = arguments;

                    log.Info("CWD of Profile is " + proc.WorkingDirectory);

                    using (var p = Process.Start(proc))
                    {
                        SetForegroundWindow(p.MainWindowHandle);
                        p.WaitForExit();
                    }
                }
                else if (Platform.IsGtk)
                {

                    ProcessStartInfo startInfo = new ProcessStartInfo();

                    log.Info("Is the environment textbox null or whitespace = " + string.IsNullOrWhiteSpace(customEnvVarTextBox.Text));

                    if (!string.IsNullOrWhiteSpace(customEnvVarTextBox.Text))
                    {
                        string envVars = customEnvVarTextBox.Text;

                        for (int i = 0; i < customEnvVarTextBox.Text.Count(f => f == '='); i++)
                        {
                            // Env var variable
                            string variable = envVars.Substring(0, envVars.IndexOf('='));
                            envVars = envVars.Replace(variable + "=", "");

                            // This thing here is the value parser. Since values are sometimes in quotes, i need to compensate for them.
                            int valueSubstringLength = 0;
                            if (envVars[0] != '"')               // If value is not embedded in "", check if there are spaces left. If yes, get the index of the space, if not that was the last
                            {
                                if (envVars.IndexOf(' ') >= 0)
                                    valueSubstringLength = envVars.IndexOf(' ') + 1;
                                else
                                    valueSubstringLength = envVars.Length;
                            }
                            else                                // If value is embedded in "", check if there are spaces after the "". if yes, get index of that, if not that was the last
                            {
                                int secondQuoteIndex = envVars.IndexOf('"', envVars.IndexOf('"') + 1);
                                if (envVars.IndexOf(' ', secondQuoteIndex) >= 0)
                                    valueSubstringLength = envVars.IndexOf(' ', secondQuoteIndex) + 1;
                                else
                                    valueSubstringLength = envVars.Length;
                            }
                            // Env var value
                            string value = envVars.Substring(0, valueSubstringLength);
                            envVars = envVars.Substring(value.Length);

                            log.Info("Adding variable \"" + variable + "\" with value \"" + value + "\"");
                            startInfo.EnvironmentVariables[variable] = value;
                        }
                    }

                    // If we're supposed to log profiles, add events that track those and append them to this var. otherwise keep it null
                    string terminalOutput = null;

                    startInfo.UseShellExecute = false;
                    startInfo.WorkingDirectory = CrossPlatformOperations.CURRENTPATH + "/Profiles/" + profile.Name;
                    startInfo.FileName = startInfo.WorkingDirectory + "/AM2R.AppImage";

                    log.Info("CWD of Profile is " + startInfo.WorkingDirectory);

                    log.Info("Launching game with following variables: ");
                    foreach (System.Collections.DictionaryEntry item in startInfo.EnvironmentVariables)
                    {
                        log.Info("Key: \"" + item.Key + "\" Value: \"" + item.Value + "\"");
                    }

                    using (Process p = new Process())
                    {
                        p.StartInfo = startInfo;
                        if ((bool)profileDebugLogCheck.Checked)
                        {
                            p.StartInfo.RedirectStandardOutput = true;
                            p.OutputDataReceived += new DataReceivedEventHandler((sender, e) => { terminalOutput += e.Data + "\n"; });

                            p.StartInfo.RedirectStandardError = true;
                            p.ErrorDataReceived += new DataReceivedEventHandler((sender, e) => { terminalOutput += e.Data + "\n"; });
                        }

                        p.Start();

                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();

                        p.WaitForExit();
                    }

                    if (terminalOutput != null)
                    {
                        log.Info("Performed logging setup for profile " + profile.Name + ".");

                        if (!Directory.Exists(logDir.FullName))
                            Directory.CreateDirectory(logDir.FullName);

                        if (File.Exists(logDir.FullName + "/" + profile.Name + ".txt"))
                            HelperMethods.RecursiveRollover(logDir.FullName + "/" + profile.Name + ".txt", 5);

                        StreamWriter stream = File.AppendText(logDir.FullName + "/" + profile.Name + ".txt");

                        // Write general info
                        stream.WriteLine("AM2RLauncher " + VERSION + " log generated at " + date);

                        // Write what was in the terminal
                        stream.WriteLine(terminalOutput);

                        stream.Flush();

                        stream.Close();
                    }

                }

                log.Info("Profile " + profile.Name + " process exited.");
            }
        }
    }
}
