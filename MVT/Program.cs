using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using McMaster.Extensions.CommandLineUtils;
using NLog;
using NLog.Config;
using NLog.Targets;

[Command(
    ExtendedHelpText = @"
Remarks:
  Media Validation Tool

- Generate: Generate validation information about the contents of a directory based on file name/hash
- Validate: Validate presence of/hash of files in a directory (or just validate file exists with --hash false)
- Trash: Locate any trash files/folders as defined in 'Trash.txt'
- TrashDelete: Remove any trash files/folders found as defined in 'Trash.txt'

Features: 
1) Report files in VERSION file not in directory
2) Report files in directory not in VERSION file
3) Report hash mismatches (optional)
4) Report file names that do not match based on capitalization
"
)]
public class Program
{
    private static readonly string BaseDirectory = AppContext.BaseDirectory;// Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    
    [Required]
    [Option("-d|--dir",
        Description =
            "Required. The directory containing files to recursively process")]
    public string DirName { get; }

    [Option("-f|--file", Description = "Output file to write/read validation info to/from. If not supplied, defaults to writing/reading from -d")]
    public string FileName { get; }

    [Option("-t|--tag",
        Description =
            "Required with Generate. The 'Class-Revision' info to use in validation file, VERSION-{Tag}.txt. Any illegal characters will be replaced with _. Example: FOR498-20-2B")]
    public string Tag { get; }

    [Required]
    [Option(Description = "Required. The Operation to perform")]
    public OpType Operation { get; }

    [Option("--hash", CommandOptionType.SingleValue,
        Description = "If true, generate SHA256 for each file. If false, file list only. Default is TRUE")]
    public bool Hash { get; } = true;

    [Option("--debug", Description = "Show additional information while processing")]
    public bool Debug { get; }

    public static int Main(string[] args)
    {
        return CommandLineApplication.Execute<Program>(args);
    }
    private void DumpHeader()
    {
        var l = LogManager.GetLogger("MVT");

        l.Info($"MVT version {Assembly.GetExecutingAssembly().GetName().Version}");
        l.Info("Author: Eric Zimmerman (saericzimmerman@gmail.com)");
        l.Info("https://github.com/EricZimmerman/MVT");
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

        var filePattern = "VERSION-*.txt";
        string dirName;

        Stopwatch sw;

        switch (Operation)
        {
            case OpType.Trash:
                DumpHeader();

                if (Directory.Exists(DirName) == false)
                {
                    l.Fatal($"'{DirName}' does not exist! Exiting\r\n");
                    return;
                }

                if (File.Exists(Path.Combine(BaseDirectory, "Trash.txt")) == false)
                {
                    CreateDefaultTrashFile();
                }

                FindTrashFiles(DirName, false);

                break;
            case OpType.TrashDelete:
                DumpHeader();

                if (Directory.Exists(DirName) == false)
                {
                    l.Fatal($"'{DirName}' does not exist! Exiting\r\n");
                    return;
                }

                if (File.Exists(Path.Combine(BaseDirectory, "Trash.txt")) == false)
                {
                    CreateDefaultTrashFile();
                }

                FindTrashFiles(DirName, true);

                break;
            case OpType.Generate:
                DumpHeader();

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
                if (DirName.EndsWith(Path.DirectorySeparatorChar) == false)
                {
                    dirName = $"{dirName}{Path.DirectorySeparatorChar}";
                }

                sw = new Stopwatch();
                sw.Start();

                var fileNameOut = Path.Combine(DirName, $"VERSION-{tag}.txt");

                if (string.IsNullOrEmpty(FileName) == false)
                {
                    fileNameOut = FileName;
                    if (Directory.Exists(Path.GetDirectoryName(fileNameOut)) == false)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(fileNameOut));
                    }
                }

                l.Info($"Validation data will be written to '{fileNameOut}'");
                if (Hash)
                {
                    l.Error("\t--hash option enabled. SHA256 will be generated for each file found.");
                }

                l.Info($"\r\nIterating '{dirName}'...");

                var fileOut = new StreamWriter(fileNameOut, false,Encoding.Unicode);
                fileOut.WriteLine($"; MVT version {Assembly.GetExecutingAssembly().GetName().Version}");
                fileOut.WriteLine($"; Generated on: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.ffff}");
                fileOut.WriteLine($"; Command line: {Environment.CommandLine}");
                fileOut.WriteLine($"; Username: {Environment.UserName}");
                fileOut.WriteLine("; Filename|SHA256");

                var fCount = 0;
                long byteCount = 0;

                var opt = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    ReturnSpecialDirectories = false,
                    
                    AttributesToSkip = 0, //Default is opt.AttributesToSkip = FileAttributes.Hidden | FileAttributes.System, but we do not want to skip anything unless we do not have access
                    IgnoreInaccessible = true
                };

                foreach (var fn in Directory.EnumerateFileSystemEntries(dirName, "*",opt))
                {
                    if (fn.Contains("VERSION-"))
                    {
                        continue;
                    }

                    if ((new FileInfo(fn).Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                    {
                        l.Debug($"Skipping directory '{fn}'");
                        continue;
                    }

                    fCount += 1;

                    l.Debug($"Processing '{fn}' to generate validation data");

                    byteCount += new FileInfo(fn).Length;

                    var hash = string.Empty;
                    if (Hash)
                    {
                        hash = $"|{GetSha256(fn)}";
                    }

                    fileOut.WriteLine($"{fn.Replace(dirName, "")}{hash}");

                    l.Debug($"Writing to file: '{fn.Replace(dirName, "")}'{hash}");
                }

                fileOut.Flush();
                fileOut.Close();

                sw.Stop();

                l.Info("");

                var Mb = byteCount / 1024 / 1024;

                l.Info(
                    $"Generate took {sw.Elapsed.TotalSeconds:N2} seconds ({Mb / sw.Elapsed.TotalSeconds:N3} MB/sec across {fCount:N0} files/{Mb:N0} MB). Results saved to '{fileNameOut}'\r\n");

                break;
            case OpType.Validate:
                DumpHeader();

                if (Directory.Exists(DirName) == false)
                {
                    l.Fatal($"'{DirName}' does not exist! Exiting\r\n");
                    return;
                }

                dirName = DirName;
                if (DirName.EndsWith(Path.DirectorySeparatorChar) == false)
                {
                    dirName = $"{dirName}{Path.DirectorySeparatorChar}";
                }

                var fileList = new Dictionary<string, string>();

                var filesNotInDirTree = new Dictionary<string, string>();

                var goodValidateFile = string.Empty;

                if (string.IsNullOrEmpty(FileName) == false)
                {
                   
                    var fline = File.ReadLines(FileName,Encoding.Unicode).First();

                    if (fline.Contains("MVT") == false)
                    {
                        l.Fatal(
                            $"'{FileName}' is not a valid validation file! Exiting\r\n");
                        return;
                    }
                    goodValidateFile = FileName;
                }
                else
                {
                    var validateFiles = Directory.GetFiles(dirName, filePattern);
                    if (validateFiles.Any() == false)
                    {
                        l.Fatal(
                            $"'{dirName}' does not contain any validation files ({filePattern})! Did you forget to generate one?! Exiting\r\n");
                        return;
                    }

                    foreach (var validateFile in validateFiles)
                    {
                        l.Debug($"Examining validation file '{validateFile}'...");
                        var fline = File.ReadLines(validateFile,Encoding.Unicode).First();

                        if (fline.Contains("MVT"))
                        {
                            goodValidateFile = validateFile;
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(goodValidateFile))
                    {
                        l.Warn("Did not find a validation file generated by MVT! Exiting\r\n");
                        return;
                    }
                }

                l.Info($"Found validation file '{goodValidateFile}'. Reading...");

                sw = new Stopwatch();
                sw.Start();

                foreach (var line in File.ReadLines(goodValidateFile,Encoding.Unicode))
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
                        fileList.Add(segs[0], segs[1]);
                        filesNotInDirTree.Add(segs[0], segs[1]);
                    }
                    else
                    {
                        if (Hash)
                        {
                            l.Warn(
                                $"\r\n'{goodValidateFile}' was not generated with hashes! Regenerate the file with hashes or remove --hash from command line\r\n");
                            return;
                        }

                        fileList.Add(line, string.Empty);
                        filesNotInDirTree.Add(line, string.Empty);
                    }
                }

                l.Info($"Found {fileList.Count:N0} files in validation file.\r\n");
                l.Info($"Iterating '{dirName}'...\r\n");

                var violationFound = false;

                foreach (var fn in Directory.EnumerateFileSystemEntries(dirName, "*", SearchOption.AllDirectories))
                {
                    if (fn.Contains("VERSION-"))
                    {
                        continue;
                    }

                    if ((new FileInfo(fn).Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                    {
                        l.Debug($"Skipping directory '{fn}'");
                        continue;
                    }

                    l.Debug($"Validating '{fn}'");

                    if (fileList.ContainsKey(fn.Replace(dirName, string.Empty)) == false)
                    {
                        l.Error($"File '{fn}' not found in validation file!");
                        violationFound = true;
                        continue;
                    }

                    filesNotInDirTree.Remove(fn.Replace(dirName, string.Empty));

                    if (!Hash)
                    {
                        continue;
                    }

                    var sha256 = GetSha256(fn);
                    l.Debug($"Calculated SHA256: {sha256}");

                    var key = fn.Replace(dirName, string.Empty);

                    if (sha256 == fileList[key])
                    {
                        continue;
                    }

                    l.Fatal($"Hash mismatch for '{fn}'! Expected: '{fileList[key]}', Actual: '{sha256}'");
                    violationFound = true;
                }

                if (filesNotInDirTree.Count > 0)
                {
                    violationFound = true;
                    l.Fatal(
                        "\r\nThe following files were found in the validation file, but are not in the directory tree:");
                    foreach (var ent in filesNotInDirTree)
                    {
                        l.Error($"{ent.Key}");
                    }

                    l.Info("");
                }

                sw.Stop();

                if (violationFound)
                {
                    l.Fatal("\r\nValidation failed! See output above for details...\r\n");
                }
                else
                {
                    l.Info("Validation successful! No discrepancies detected\r\n");
                }

                break;
        }
    }

    private void FindTrashFiles(string dirName, bool withDelete)
    {
        var l = LogManager.GetLogger("TrashFiles");

        l.Info($"Looking for trash in '{dirName}'...\r\n");
        
        var trashCan = File.ReadAllLines(Path.Combine(BaseDirectory, "Trash.txt"),Encoding.Unicode).ToList();

        var trashHash = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var can in trashCan)
        {
            trashHash.Add(can);
        }

        var foundFiles = 0;

        var filesToDelete = new List<string>();
        var dirsToDelete = new List<string>();

        foreach (var fn in Directory.EnumerateFileSystemEntries(dirName, "*", SearchOption.AllDirectories))
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
            File.Delete(file);
        }

        foreach (var dir in dirsToDelete)
        {
            l.Debug($"Deleting directory '{dir}'...");
            Directory.Delete(dir, true);
        }

        if (withDelete == false && foundFiles > 0)
        {
            l.Info("\r\nTo automatically delete these files, run the 'TrashDelete' option\r\n");
        }

        if (foundFiles == 0)
        {
            l.Info("\r\nNo trash files found! Congrats!\r\n");
        }
    }

    private void CreateDefaultTrashFile()
    {
        var l = LogManager.GetLogger("MVT");

        l.Warn("'Trash.txt' file missing. Creating default Trash.txt file...");

        var outPath = Path.Combine(BaseDirectory, "Trash.txt");

        var contents = new List<string>
        {
            "desktop.ini",
            ".DS_Store",
            ".Trashes",
            "._",
            ".fseventsd",
            ".Spotlight-V100",
            "System Volume Information"
        };

        File.WriteAllLines(outPath, contents,Encoding.Unicode);
    }

    public string GetSha256(string filename)
    {
        using var sha = new SHA256Managed();

        using var fs = File.OpenRead(filename);

        var h = sha.ComputeHash(fs);

        return BytesToString(h);
    }

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