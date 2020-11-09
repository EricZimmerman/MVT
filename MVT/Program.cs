using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Alphaleonis.Win32.Filesystem;
using Alphaleonis.Win32.Security;
using McMaster.Extensions.CommandLineUtils;
using NLog;
using NLog.Config;
using NLog.Targets;
using Directory = Alphaleonis.Win32.Filesystem.Directory;
using File = Alphaleonis.Win32.Filesystem.File;
using FileInfo = Alphaleonis.Win32.Filesystem.FileInfo;
using Path = Alphaleonis.Win32.Filesystem.Path;

[Command(
    ExtendedHelpText = @"
Remarks:
  Media Validation Tool

- Generate|validate the contents of a directory based on file name
- Calculate|validate all hashes of files in a directory.

This serves two purposes: 
1) Are all files located in a directory that are expected to be there?
2) Do all the file hashes in a directory match the expected hashes?
"
)]
public class Program
{
    public static int Main(string[] args)
        => CommandLineApplication.Execute<Program>(args);

    [Option("-d|--dir",Description = "The directory containing files to recursively process. When using Generate, this is a required field.")]
    public string DirName { get; }

    [Required]
    [Option("-f|--file",Description = "Required. The file name that contains information to validate, or the file to save generated data to")]
    public string FileName { get; }

    [Required]
    [Option(Description = "Required. The Operation to perform")]
    public OpType Operation { get; }

    [Option("--hash",Description = "If true, generate SHA-1 for each file. If false, file list only. Default is false")]
    public bool Hash { get; }

    [Option("--debug",Description = "Show additional information while processing")]
    public bool Debug { get; }

    [Option("-t|--trash",Description = "Look for trash files as contained in trash.txt. This should contain one filename per line")]
    public bool Trash { get; }

    [Option("-c|--clean",Description = "Delete any trash files as contained in trash.txt. This should contain one filename per line")]
    public bool CleanTrash { get; }

    private void OnExecute()
    {
        SetupNLog();
        var l = LogManager.GetLogger("MVT");

        if (Debug)
        {
            LogManager.Configuration.LoggingRules.First().EnableLoggingForLevel(LogLevel.Debug);
        }

        LogManager.ReconfigExistingLoggers();

        var dirOptions = DirectoryEnumerationOptions.BasicSearch | DirectoryEnumerationOptions.Recursive |
                         DirectoryEnumerationOptions.Files;

        switch (Operation)
        {
            case OpType.Generate:
                if (string.IsNullOrEmpty(DirName))
                {
                    l.Fatal("-d is required when using Generate operation! Exiting");
                    return;
                }

                var sw = new Stopwatch();
                sw.Start();

                var fileOut = new StreamWriter(FileName, false);
                fileOut.WriteLine($"; MVT version { GetType().Assembly.GetName().Version.ToString()}");
                fileOut.WriteLine($"; Generated on: {DateTimeOffset.UtcNow:yyyyMMddHHmmss.ffff}");
                fileOut.WriteLine($"; Directory processed: '{DirName}'");
                fileOut.WriteLine($"; Username: {Environment.UserName}");
                fileOut.WriteLine("; Filename|SHA-1");

                var fCount = 0;
                long byteCount = 0;
                foreach (var fn in Directory.EnumerateFileSystemEntries(DirName,dirOptions))
                {
                    fCount += 1;

                    byteCount += new FileInfo(fn).Length;

                    var hash = string.Empty;
                    if (Hash)
                    {
                        hash = $"|{ GetSha1(fn)}";
                    }
                    fileOut.WriteLine($"{fn.Replace(DirName,"")}{hash}");
                    
                    l.Debug($"'{fn.Replace(DirName,"")}'{hash}");
                }

                fileOut.Flush();
                fileOut.Close();

                sw.Stop();

                l.Info("");

                var Mb = byteCount / 1024 /1024;

                l.Info($"Generate took {sw.Elapsed.TotalSeconds:N5} seconds ({(Mb/sw.Elapsed.TotalSeconds):N3} MB/sec across {fCount:N0} files). Results saved to '{FileName}'");

                break;
            case OpType.Validate:
                if (File.Exists(FileName) == false)
                {
                    Console.WriteLine($"'{FileName}' does not exist! Exiting");
                    return;
                }

                var fileList = new Dictionary<string, string>();
                
                var filesNotInDirTree = new Dictionary<string, string>();

                foreach (var fn in File.ReadAllLines(FileName))
                {
                    if (fn.StartsWith(";"))
                    {
                        continue;
                    }

                    if (fn.Contains("|"))
                    {
                        var segs = fn.Split("|");
                        fileList.Add(segs[0],segs[1]);
                        filesNotInDirTree.Add(segs[0],segs[1]);
                    }
                    else
                    {
                        if (Hash)
                        {
                            l.Warn($"'{FileName}' was not generated with hashes! Regenerate the file with hashes or remove --hash from command line");
                            return;
                        }
                        fileList.Add(fn,string.Empty);
                        filesNotInDirTree.Add(fn,string.Empty);
                    }
                }

                foreach (var fn in Directory.EnumerateFileSystemEntries(DirName, dirOptions))
                {
                    if (fileList.ContainsKey(fn.Replace(DirName,string.Empty)) == false)
                    {
                        l.Warn($"'{fn}' not found in validation file!");
                        continue;
                    }

                    filesNotInDirTree.Remove(fn.Replace(DirName,string.Empty));

                    if (!Hash)
                    {
                        continue;
                    }

                    var sha1 = GetSha1(fn);

                    var key = fn.Replace(DirName, string.Empty);

                    if (sha1 != fileList[key])
                    {
                        l.Fatal($"Hash mismatch for '{fn}'! Expected: '{fileList[key]}', Actual: '{sha1}'");
                    }
                }

                if (filesNotInDirTree.Count > 0)
                {
                    l.Warn("\r\nThe following files were not found in the directory tree, but are in the validation file");
                    foreach (var ent in filesNotInDirTree)
                    {
                        l.Info($"{ent.Key}");
                    }
                    l.Info("");
                }

                break;
            
        }
    }

    public string GetSha1(string filename)
    {
        using var sha = new System.Security.Cryptography.SHA1Managed();

        using var fs = File.OpenRead(filename);

        var h= sha.ComputeHash(fs);

        return BytesToString(h);
    }

    private static readonly string BaseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

    private static void SetupNLog()
    {
        if (File.Exists(Path.Combine(BaseDirectory, "Nlog.config")))
        {
            return;
        }

        var config = new LoggingConfiguration();
        var loglevel = LogLevel.Info;

        var layout = @"${message}";

        var consoleTarget = new ColoredConsoleTarget();

        config.AddTarget("console", consoleTarget);

        consoleTarget.Layout = layout;

        var rule1 = new LoggingRule("*", loglevel, consoleTarget);
        config.LoggingRules.Add(rule1);

        LogManager.Configuration = config;
    }

    public string BytesToString(byte[] array)
    {
        var sb = new StringBuilder();

        foreach (var t in array)
        {
            sb.Append($"{t:x2}");
        }

        return sb.ToString();
    }
}

public enum OpType
{
    Generate,
    Validate
}