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

- Generate: Generate validation information about the contents of a directory based on file name
- Validate: Validate all hashes of files (with --hash) in a directory or just validate file exists
- Trash: Locate any trash files/folders as defined in Trash.txt
- TrashDelete: Remove any trash files found

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

   
    [Option("-t|--tag",Description = "Required with Generate. The class-revision info to use in validation file, VERSION-{Tag}.txt. Any illegal characters will be replaced with _. Example: FOR498-20-2B")]
    public string Tag { get; }

    [Required]
    [Option(Description = "Required. The Operation to perform")]
    public OpType Operation { get; }

    [Option("--hash",Description = "If true, generate SHA256 for each file. If false, file list only. Default is false")]
    public bool Hash { get; }

    [Option("--debug",Description = "Show additional information while processing")]
    public bool Debug { get; }


    private void DumpHeader()
    {
        var l = LogManager.GetLogger("MVT");

        l.Info($"MVT version {Assembly.GetExecutingAssembly().GetName().Version}");
        l.Info($"Author: Eric Zimmerman (saericzimmerman@gmail.com)");
        l.Info("Some url here for github repo");
        l.Info("");
    }

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

        var filePattern = "VERSION-*.txt";
        string dirName;

        Stopwatch sw;

        switch (Operation)
        {
            case OpType.Trash:
                DumpHeader();

                if (string.IsNullOrEmpty(DirName))
                {
                    l.Fatal("-d is required when using 'Generate' operation! Exiting\r\n");
                    return;
                }

                if (Directory.Exists(DirName) == false)
                {
                    l.Fatal($"'{DirName}' does not exist! Exiting\r\n");
                    return;
                }

                if (File.Exists(Path.Combine(BaseDirectory, "Trash.txt")) == false)
                {
                    CreateDefaultTrashFile();
                }

                FindTrashFiles(DirName,false);

                break;
            case OpType.TrashDelete:
                DumpHeader();

                if (string.IsNullOrEmpty(DirName))
                {
                    l.Fatal("-d is required when using 'Generate' operation! Exiting\r\n");
                    return;
                }

                if (Directory.Exists(DirName) == false)
                {
                    l.Fatal($"'{DirName}' does not exist! Exiting\r\n");
                    return;
                }

                if (File.Exists(Path.Combine(BaseDirectory, "Trash.txt")) == false)
                {
                    CreateDefaultTrashFile();
                }

                FindTrashFiles(DirName,true);

                break;
            case OpType.Generate:
                DumpHeader();

                if (string.IsNullOrEmpty(DirName))
                {
                    l.Fatal("-d is required when using 'Generate' operation! Exiting\r\n");
                    return;
                }

                if (Directory.Exists(DirName) == false)
                {
                    l.Fatal($"'{DirName}' does not exist! Exiting\r\n");
                    return;
                }

                if (string.IsNullOrEmpty(Tag))
                {
                    l.Fatal("-t is required when using'Generate' operation! Exiting\r\n");
                    return;
                }

                var tag = string.Join("_", Tag.Split(Path.GetInvalidFileNameChars()));  

                dirName = DirName;
                if (DirName.EndsWith(Path.DirectorySeparator) == false)
                {
                    dirName = $"{dirName}{Path.DirectorySeparator}";
                }

                sw = new Stopwatch();
                sw.Start();

                var fileNameOut = Path.Combine(DirName, $"VERSION-{tag}.txt");

                l.Info($"Validation data will be written to '{fileNameOut}'");
                if (Hash)
                {
                   l.Info(" --hash option present. SHA256 will be generated for each file found.");
                }
                l.Info($"\r\nIterating '{dirName}'...");

                var fileOut = new StreamWriter(fileNameOut, false);
                fileOut.WriteLine($"; MVT version {Assembly.GetExecutingAssembly().GetName().Version}");
                fileOut.WriteLine($"; Generated on: {DateTimeOffset.UtcNow:yyyyMMddHHmmss.ffff}");
                fileOut.WriteLine($"; Command line: {Environment.CommandLine}");
                fileOut.WriteLine($"; Username: {Environment.UserName}");
                fileOut.WriteLine("; Filename|SHA256");

                var fCount = 0;
                long byteCount = 0;
                foreach (var fn in Directory.EnumerateFileSystemEntries(dirName,dirOptions))
                {
                    if (fn.Contains("VERSION-"))
                    {
                        continue;
                    }

                    fCount += 1;

                    l.Debug($"Processing '{fn}' to generate validation data");

                    byteCount += new FileInfo(fn).Length;

                    var hash = string.Empty;
                    if (Hash)
                    {
                        hash = $"|{ GetSha256(fn)}";
                    }
                    fileOut.WriteLine($"{fn.Replace(dirName,"")}{hash}");
                    
                    l.Debug($"'{fn.Replace(dirName,"")}'{hash}");
                }

                fileOut.Flush();
                fileOut.Close();

                sw.Stop();

                l.Info("");

                var Mb = byteCount / 1024 /1024;

                l.Info($"Generate took {sw.Elapsed.TotalSeconds:N5} seconds ({(Mb/sw.Elapsed.TotalSeconds):N3} MB/sec across {fCount:N0} files). Results saved to '{fileNameOut}'\r\n");

                break;
            case OpType.Validate:
                DumpHeader();

                if (string.IsNullOrEmpty(DirName))
                {
                    l.Fatal("-d is required when using 'Validate' operation! Exiting\r\n");
                    return;
                }

                if (Directory.Exists(DirName) == false)
                {
                    l.Fatal($"'{DirName}' does not exist! Exiting\r\n");
                    return;
                }

                dirName = DirName;
                if (DirName.EndsWith(Path.DirectorySeparator) == false)
                {
                    dirName = $"{dirName}{Path.DirectorySeparator}";
                }

                var validateFiles = Directory.GetFiles(dirName, filePattern);
                if (validateFiles.Any() == false)
                {
                    l.Fatal($"'{dirName}' does not contain any validation files ({filePattern})! Did you forget to generate one?! Exiting\r\n");
                    return;
                }

                var fileList = new Dictionary<string, string>();
                
                var filesNotInDirTree = new Dictionary<string, string>();

                var goodValidateFile = string.Empty;

                foreach (var validateFile in validateFiles)
                {
                    l.Debug($"Examining validation file '{validateFile}'...");
                    var fline = File.ReadLines(validateFile).First();

                    if (fline.Contains("MVT"))
                    {
                        goodValidateFile = validateFile;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(goodValidateFile) == true)
                {
                    l.Warn($"Did not find a validation file generated by MVT! Exiting\r\n");
                    return;
                }

                l.Info($"Found validation file '{goodValidateFile}'. Reading...");

                sw = new Stopwatch();
                sw.Start();

                foreach (var line in File.ReadLines(goodValidateFile))
                {
                    if (line.StartsWith(";"))
                    {
                        continue;
                    }

                    if (line.Trim().Length == 0)
                    {
                        continue;
                    }
                    
                    if (line.Contains("|"))
                    {
                        var segs = line.Split("|");
                        fileList.Add(segs[0],segs[1]);
                        filesNotInDirTree.Add(segs[0],segs[1]);
                    }
                    else
                    {
                        if (Hash)
                        {
                            l.Warn($"\r\n'{goodValidateFile}' was not generated with hashes! Regenerate the file with hashes or remove --hash from command line\r\n");
                            return;
                        }
                        fileList.Add(line,string.Empty);
                        filesNotInDirTree.Add(line,string.Empty);
                    }
                }

                l.Info($"Found {fileList.Count:N0} files in validation file.\r\n");
                l.Info($"Iterating '{dirName}'...\r\n");

                var violationFound = false;

                foreach (var fn in Directory.EnumerateFileSystemEntries(dirName, dirOptions))
                {
                    if (fn.Contains("VERSION-"))
                    {
                        continue;
                    }

                    l.Debug($"Validating '{fn}'");

                    if (fileList.ContainsKey(fn.Replace(dirName,string.Empty)) == false)
                    {
                        l.Fatal($"'{fn}' not found in validation file!");
                        violationFound = true;
                        continue;
                    }

                    filesNotInDirTree.Remove(fn.Replace(dirName,string.Empty));

                    if (!Hash)
                    {
                        continue;
                    }

                    var sha1 = GetSha256(fn);

                    var key = fn.Replace(dirName, string.Empty);

                    if (sha1 != fileList[key])
                    {
                        l.Fatal($"Hash mismatch for '{fn}'! Expected: '{fileList[key]}', Actual: '{sha1}'");
                        violationFound = true;
                    }
                }

                if (filesNotInDirTree.Count > 0)
                {
                    violationFound = true;
                    l.Fatal("\r\nThe following files were found in the validation file, but are not in the directory tree");
                    foreach (var ent in filesNotInDirTree)
                    {
                        l.Info($"{ent.Key}");
                    }
                    l.Info("");
                }

                sw.Stop();

                if (violationFound)
                {
                    l.Fatal($"\r\nValidation failed! See output above for details...\r\n");
                }
                else
                {
                    l.Info($"\r\nValidation successful! No discrepancies detected\r\n");
                }

                break;
        }
    }

    private void FindTrashFiles(string dirName, bool withDelete)
    {
        var l = LogManager.GetLogger("TrashFiles");

        l.Info($"Looking for trash in '{dirName}'...\r\n");

        var dirOptions = DirectoryEnumerationOptions.BasicSearch | DirectoryEnumerationOptions.Recursive |
                         DirectoryEnumerationOptions.FilesAndFolders;

        var trashCan = File.ReadAllLines(Path.Combine(BaseDirectory, "Trash.txt")).ToList();

        var trashHash = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var can in trashCan)
        {
            trashHash.Add(can);
        }

        var foundFiles = 0;

        var filesToDelete = new List<string>();
        var dirsToDelete = new List<string>();

        foreach (var fn in Directory.EnumerateFileSystemEntries(dirName, dirOptions))
        {
            var a = new FileInfo(fn);

            if ((a.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
            {
                var dName = Path.GetFileName(fn);

                if (!trashHash.Contains(dName))
                {
                    continue;
                }

                l.Info("Found trash directory:".PadRight(24) + $" '{fn}'");
                foundFiles += 1;

                if (withDelete)
                {
                    dirsToDelete.Add(fn);
                }
            }
            else
            {
                var fName = Path.GetFileName(fn);

                if (!trashHash.Contains(fName))
                {
                    continue;
                }

                l.Info("Found trash file:".PadRight(24) + $" '{fn}'");
                foundFiles += 1;

                if (withDelete)
                {
                    filesToDelete.Add(fn);
                }
            }
        }

        if (withDelete && foundFiles > 0)
        {
            var suff = foundFiles == 1 ? "" : "s";
            l.Info($"\r\nFound {foundFiles:N0} item{suff} to delete. Deleting...");
        }

        foreach (var file in filesToDelete)
        {
            l.Debug($"Deleting file '{file}'...");
            File.Delete(file,true);
        }

        foreach (var dir in dirsToDelete)
        {
            l.Debug($"Deleting directory '{dir}'...");
            Directory.Delete(dir,true,true);
        }
        
        if (withDelete == false && foundFiles > 0)
        {
            l.Info($"\r\nTo automatically delete these files, run the 'TrashDelete' option\r\n");
        }

        if (foundFiles == 0)
        {
            l.Info($"\r\nNo trash files found! Congrats!\r\n");
        }
    }

    private void CreateDefaultTrashFile()
    {
        var l = LogManager.GetLogger("MVT");

        l.Warn("'Trash.txt' file missing. Creating default Trash.txt file...");

        var outPath = Path.Combine(BaseDirectory, "Trash.txt");

        var contents = new List<string>();
        contents.Add("desktop.ini");
        contents.Add(".DS_Store");
        contents.Add(".Trashes");
        contents.Add("._");
        contents.Add(".fseventsd");
        contents.Add(".Spotlight-V100");
        contents.Add("System Volume Information");

        File.WriteAllLines(outPath,contents);
    }

    public string GetSha256(string filename)
    {
        using var sha = new System.Security.Cryptography.SHA256Managed();

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
    Validate,
    Trash,
    TrashDelete
}