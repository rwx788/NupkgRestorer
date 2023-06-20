# NupkgRestorer
Command line tool to unpack nupkg files to given director

## Usage
Description:
  Unpacks nupkgs from packages directory to offline feed folder

Usage:
  NupkgRestorer [options]

Options:
  --feed <feed> (REQUIRED)          The offline feed directory
  --packages <packages> (REQUIRED)  The package directory
  --version                         Show version information
  -?, -h, --help                    Show help and usage information

## Release binaries
`build.sh` script publishes self-contained executables for Linux, MacOS, Windows for x64 and arm64 architectures and packs those to zip.
`zip` and `dotnet` tool are required to be installed. 

NOTE: As of now version of the package is hardcoded in the shell script and not shared with csproj.