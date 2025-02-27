﻿using Eto.Forms;
using log4net;
using log4net.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AM2RLauncherLib;
using Application = Eto.Forms.Application;
using FileInfo = System.IO.FileInfo;
using log4net.Repository.Hierarchy;

// ReSharper disable LocalizableElement - we want hardcoded strings for console writes.

namespace AM2RLauncher.Gtk;

/// <summary>
/// The main class for the GTK project.
/// </summary>
internal static class MainClass
{
    /// <summary>
    /// The logger for <see cref="MainForm"/>, used to write any caught exceptions.
    /// </summary>
    private static readonly ILog log = LogManager.GetLogger(typeof(MainForm));
    /// <summary>
    /// The main method for the GTK project.
    /// </summary>
    [STAThread]
    public static void Main()
    {
        string launcherDataPath = CrossPlatformOperations.CurrentPath;

        // Make sure first, ~/.local/share/AM2RLauncher exists
        Directory.CreateDirectory(launcherDataPath);

        // Now, see if log4netConfig exists, if not write it again.
        if (!File.Exists($"{launcherDataPath}/log4net.config"))
            File.WriteAllText($"{launcherDataPath}/log4net.config", Properties.Resources.log4netContents.Replace("${DATADIR}", launcherDataPath));

        // Configure logger
        XmlConfigurator.Configure(new FileInfo($"{launcherDataPath}/log4net.config"));

        // if we're on debug, always set log level to debug
        #if DEBUG
        ((Logger)log.Logger).Level = log4net.Core.Level.Debug;
        #endif

        // Log distro and version (if it exists)
        if (File.Exists("/etc/os-release"))
        {
            string osRelease = File.ReadAllText("/etc/os-release");
            Regex lineRegex = new Regex(".*=.*");
            List<Match> results = lineRegex.Matches(osRelease).ToList();
            Match version = results.FirstOrDefault(x => x.Value.StartsWith("VERSION"));
            string distroName = results.FirstOrDefault(x => x.Value.StartsWith("NAME"))?.Value[5..].Replace("\"", "");
            string versionName = version == null ? "" : version.Value[8..].Replace("\"", "");
            log.Info($"Current Distro: {distroName} {versionName}");
        }
        else
            log.Error("Couldn't determine the currently running distro!");

        if (OS.IsThisRunningFromFlatpak)
            log.Info("Running from Flatpak!");
        
        #if NOAPPIMAGE
        log.Info("On \"No AppImage\" configuration.");
        #else
        log.Info("On AppImage configuration.");
        #endif

        try
        {
            Application gtkLauncher = new Application(Eto.Platforms.Gtk);
            LauncherUpdater.Main();
            gtkLauncher.UnhandledException += GTKLauncher_UnhandledException;
            gtkLauncher.Run(new MainForm());
        }
        catch (Exception e)
        {
            log.Error($"An unhandled exception has occurred: \n*****Stack Trace*****\n\n{e.StackTrace}");
            Console.WriteLine($"{Language.Text.UnhandledException}\n{e.Message}\n*****Stack Trace*****\n\n{e.StackTrace}");
            Console.WriteLine($"Check the logs at {launcherDataPath} for more info!");
        }
    }

    /// <summary>
    /// This method gets fired when an unhandled exception occurs in <see cref="MainForm"/>.
    /// </summary>
    private static void GTKLauncher_UnhandledException(object sender, Eto.UnhandledExceptionEventArgs e)
    {
        log.Error($"An unhandled exception has occurred: \n*****Stack Trace*****\n\n{e.ExceptionObject}");
        Application.Instance.Invoke(() =>
        {
            MessageBox.Show($"{Language.Text.UnhandledException}\n*****Stack Trace*****\n\n{e.ExceptionObject}", "GTK", MessageBoxType.Error);
        });
    }
}