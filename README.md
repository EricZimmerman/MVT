# MVT

### Media Validation tool

Given a directory, generate a validation file with optional SHA256 hashes.

Once a validation file is generated, the directory can be compared to a previously generated file and any discrepancies noted:

- file in validation list that is not in the directory
- file in the directory that is not in validation list
- when hashing, a hash mismatch between directory file and validation file

Can also locate non-desirable files and delete them.

This is a .net Core 3.1 project. Tested on both Windows 10 and Ubuntu 20.04
