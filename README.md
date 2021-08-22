# MVT (Media Validation Tool)

## Command Line Interface

     Usage: MVT [options]
    
     Options:
       -d|--dir                    Required. The directory containing files to recursively process
       -f|--file                   Output file to write/read validation info to/from. If not supplied, defaults to
                                   writing/reading from -d
       -t|--tag                    Required with Generate. The 'Class-Revision' info to use in validation file,
                                   VERSION-{Tag}.txt. Any illegal characters will be replaced with _. Example: FOR498-20-2B
       -o|--operation <OPERATION>  Required. The Operation to perform
                                   Allowed values are: Generate, Validate, Trash, TrashDelete
       --hash                      If true, generate SHA256 for each file. If false, file list only. Default is TRUE
       --debug                     Show additional information while processing
       -?|-h|--help                Show help information

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

## Documentation

Given a directory, generate a validation file with optional SHA256 hashes.

Once a validation file is generated, the directory can be compared to a previously generated file and any discrepancies noted:

- file in validation list that is not in the directory
- file in the directory that is not in validation list
- when hashing, a hash mismatch between directory file and validation file

Can also locate non-desirable files and delete them.

This is a **.NET 5** project. Tested on both Windows 10, macOS, and Ubuntu 20.04!

Note that the Release binaries for 0.5.0.1 are self contained and no runtime needs to be installed on end points. This is why they are large. Releases may not always be that way, but its an experiment and so far, its worked.

# Download Eric Zimmerman's Tools

All of Eric Zimmerman's tools can be downloaded [here](https://ericzimmerman.github.io/#!index.md). Use the [Get-ZimmermanTools](https://f001.backblazeb2.com/file/EricZimmermanTools/Get-ZimmermanTools.zip) PowerShell script to automate the download and updating of the EZ Tools suite. Additionally, you can automate each of these tools using [KAPE](https://www.kroll.com/en/services/cyber-risk/incident-response-litigation-support/kroll-artifact-parser-extractor-kape)!

# Special Thanks

Open Source Development funding and support provided by the following contributors: [SANS Institute](http://sans.org/) and [SANS DFIR](http://dfir.sans.org/).
