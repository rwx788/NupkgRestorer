using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.PackageExtraction;
using NuGet.Packaging.Signing;
using NuGet.Protocol.Core.Types;
using System.CommandLine;
using System.Text.RegularExpressions;

namespace NupkgRestorer;

internal static class NupkgRestorer
{
    private const int SuccessExitCode = 0;
    private const int FailureExitCode = 1;
    private const int MaxRetries = 5;
    private const string NugetOrgGalleryUrl = "https://www.nuget.org/api/v3";
    private static volatile bool _successFlag = true;

    public static async Task Main(string[] args)
    {
        var offlineFeedOption = new Option<string>("--feed", "The offline feed directory");
        var onlineSourceFeedOption =
            new Option<string>("--source", "URL of the online source feed to download packages from");
        var sourcePackageDirOption =
            new Option<string>("--download-dir", "Directory to temporary store downloaded packages");
        var packageListOption =
            new Option<string>("--packages", "Path to the file with the packages list in <package name> <package version>");
        var authTokenOption = new Option<string>("--token", "Authentication token if downloading requires it");
        var verboseLogOption = new Option<bool>("--verbose", "Print verbose logs");

        offlineFeedOption.IsRequired = true;
        packageListOption.IsRequired = true;

        RootCommand rootCommand = new RootCommand("Unpacks nupkgs from packages directory to offline feed folder");
        rootCommand.AddOption(offlineFeedOption);
        rootCommand.AddOption(onlineSourceFeedOption);
        rootCommand.AddOption(sourcePackageDirOption);
        rootCommand.AddOption(packageListOption);
        rootCommand.AddOption(authTokenOption);
        rootCommand.AddOption(verboseLogOption);

        rootCommand.SetHandler(
            async (packageListFile, offlineFeedDirectory, onlineSourceFeed, authToken, sourcePackageDir, verboseLog) =>
            {
                await ExpandPackagesFromOfflineFeed(packageListFile, offlineFeedDirectory, onlineSourceFeed, authToken,
                    sourcePackageDir, verboseLog);
            },
            packageListOption,
            offlineFeedOption,
            onlineSourceFeedOption,
            authTokenOption,
            sourcePackageDirOption,
            verboseLogOption);

        await rootCommand.InvokeAsync(args);

        Environment.Exit(_successFlag ? SuccessExitCode : FailureExitCode);
    }

    private static async Task ExpandPackagesFromOfflineFeed(string packagesListFile,
        string offlineFeedDirectory,
        string? onlineSourceFeed,
        string? authToken = null,
        string? sourcePackageDirOption = null,
        bool verboseLog = false)
    {
        var packageSet = GetNuGetPackagesSet(packagesListFile);
        sourcePackageDirOption ??= Path.GetTempPath();
        onlineSourceFeed ??= NugetOrgGalleryUrl;
        Console.WriteLine($"Unpacking packages from {sourcePackageDirOption} to offline feed {offlineFeedDirectory}");
        var parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = 8 };
        var downloader = new FileDownloader();
        if (!string.IsNullOrEmpty(authToken))
        {
            downloader.SetAuthorizationToken(authToken);
        }

        await Parallel.ForEachAsync(packageSet, parallelOpts,
            async (nuGetPackage, token) =>
            {
                for (var i = 0; i < MaxRetries; i++)
                {
                    var packageFileName =
                        $"{nuGetPackage.Name}.{nuGetPackage.Version}{PackagingCoreConstants.NupkgExtension}";
                    var sourcePackageUrl =
                        $"{onlineSourceFeed}/{nuGetPackage.Name}/{nuGetPackage.Version}/{packageFileName}";
                    var sourcePackagePath = Path.Combine(sourcePackageDirOption, packageFileName);
                    try
                    {
                        await downloader.DownloadFileAsync(sourcePackageUrl, sourcePackagePath, token);
                        await ExpandPackageAsync(sourcePackagePath, offlineFeedDirectory, token);
                        if (verboseLog)
                        {
                            Console.WriteLine(
                                $"Package {nuGetPackage.Name} {nuGetPackage.Version} expanded successfully.");
                        }
                        return;
                    }
                    catch (SignatureException exception)
                    {
                        Console.Error.WriteLine(
                            $"Error during loading package {exception.PackageIdentity}: {exception.Code}, retrying");
                        foreach (var result in exception.Results)
                        {
                            Console.Error.WriteLine($"  Result: {result}");
                            foreach (var issue in result.Issues)
                            {
                                Console.Error.WriteLine($"    Issue: {issue.Level} {issue.Code} {issue.Message}");
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine(
                            $"Error during restoring package {nuGetPackage.Name} {nuGetPackage.Version}: {exception.Message}, retrying");
                        Thread.Sleep(5000);
                    }
                    finally
                    {
                        // Removing source file once unpacked, or if file is corrupted to retry
                        File.Delete(sourcePackagePath);
                    }
                }
                Console.Error.WriteLine(
                    $"Error during restoring package {nuGetPackage.Name} {nuGetPackage.Version} after {MaxRetries} retries");
                _successFlag = false;
            });
    }

    private static async Task ExpandPackageAsync(string packageFilePath, string packageDirectory, CancellationToken token)
    {
        var packageExtractionContext = new PackageExtractionContext(
            PackageSaveMode.Defaultv3,
            PackageExtractionBehavior.XmlDocFileSaveMode,
            ClientPolicyContext.GetClientPolicy(NullSettings.Instance, NullLogger.Instance),
            NullLogger.Instance);

        var offlineFeedAddContext = new OfflineFeedAddContext(
            packageFilePath,
            packageDirectory,
            NullLogger.Instance, // IConsole is an ILogger
            false,
            false,
            false,
            packageExtractionContext);

        await OfflineFeedUtility.AddPackageToSource(offlineFeedAddContext, token);
    }

    private static HashSet<NuGetPackage> GetNuGetPackagesSet(string nugetListFile)
    {
        var nupkgSet = new HashSet<NuGetPackage>();
        var packageRegex = new Regex(@"^(?<package>[\w.,_-]+)\s+(?<version>[.\w-]+$)");

        foreach (var line in File.ReadLines(nugetListFile))
        {
            var match = packageRegex.Match(line);
            if (match.Success)
            {
                var packageName = match.Groups["package"].Value;
                var packageVersion = match.Groups["version"].Value;

                if (!string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(packageVersion))
                {
                    nupkgSet.Add(new NuGetPackage(packageName, packageVersion));
                }
            }
        }

        return nupkgSet;
    }
}

internal record NuGetPackage(string Name, string Version);

internal class FileDownloader
{
    private const int MaxRetries = 5;
    private readonly HttpClient _client;

    public FileDownloader()
    {
        _client = new HttpClient();
        _client.Timeout = TimeSpan.FromMinutes(3);
    }

    public void SetAuthorizationToken(string authToken)
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);
    }

    public async Task DownloadFileAsync(string fileUrl, string destinationPath, CancellationToken token)
    {
        try
        {
            using var response = await _client.GetAsync(fileUrl, token);
            response.EnsureSuccessStatusCode();
            using (var stream = response.Content.ReadAsStream())
            using (var targetStream = File.OpenWrite(destinationPath))
            {
                var buf = new byte[4096];
                int n = stream.Read(buf, 0, buf.Length);
                while (n != 0)
                {
                    targetStream.Write(buf, 0, n);
                    n = stream.Read(buf, 0, buf.Length);
                }
            }
        }
        catch (Exception ex)
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            throw new Exception($"Failed to download file from {fileUrl}: {ex.Message}");
        }
    }
}