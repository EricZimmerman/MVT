# MVT

### Media Validation tool

Given a directory, generate a validation file with optional SHA256 hashes.

Once a validation file is generated, the directory can be compared to a previously generated file and any discrepancies noted:

- file in validation list that is not in the directory
- file in the directory that is not in validation list
- when hashing, a hash mismatch between directory file and validation file

Can also locate non-desirable files and delete them.

This is a **.net 5** project. Tested on both Windows 10, mac, and Ubuntu 20.04

Note that the Release binaries for 0.5.0.1 are self contained and no runtime needs to be installed on end points. This is why they are large. Releases may not always be that way, but its an experiment and so far, its worked
