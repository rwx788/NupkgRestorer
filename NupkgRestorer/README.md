## NupkgRestorer
The application can be used to prepare offline feed with the restored packages.
As an input, application gets file with the packages list.

```bash
Description:
  Downloads and unpacks nupkgs from packages directory to offline feed folder

Usage:
  NupkgRestorer [options]

Options:
  --feed <feed> (REQUIRED)          The offline feed directory
  --packages <packages> (REQUIRED)  Path to the file with the packages list in 
                                    <package name> <package version>
  --source <source>                 URL of the online source feed to download 
                                    packages from
  --download-dir <download-dir>     Directory to temporary store downloaded 
                                    packages
  --token <token>                   Authentication token if downloading 
                                    requires it
  --verbose                         Print verbose logs
  --version                         Show version information
  -?, -h, --help                    Show help and usage information
```