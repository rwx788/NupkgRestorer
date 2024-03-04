using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.PackageExtraction;
using NuGet.Packaging.Signing;
using NuGet.Protocol.Core.Types;
using System.CommandLine;

namespace NupkgRestorer;

internal class NupkgRestorer
{
    const int SuccessExitCode = 0;
    const int FailureExitCode = 1;

    public static async Task Main(string[] args)
    {
        var offlineFeedOption = new Option<string>("--feed", "The offline feed directory");
        var packagesDirectoryOption = new Option<string>("--packages", "The packages list in <package name> <package version>");
        var verboseLogOption = new Option<bool>("--verbose", "Print verbose logs");

        var success = true;
        offlineFeedOption.IsRequired = true;
        packagesDirectoryOption.IsRequired = true;

        RootCommand rootCommand = new RootCommand("Unpacks nupkgs from packages directory to offline feed folder");
        rootCommand.AddOption(offlineFeedOption);
        rootCommand.AddOption(packagesDirectoryOption);
        rootCommand.AddOption(verboseLogOption);

        rootCommand.SetHandler(async (offlineFeedDirectory, packagesDirectory, verboseLog) => 
            { 
                success = await ExpandPackagesFromOfflineFeed(offlineFeedDirectory, packagesDirectory, verboseLog);
            },
            offlineFeedOption,
            packagesDirectoryOption,
            verboseLogOption);
        
        await rootCommand.InvokeAsync(args);

        Environment.Exit(success ? SuccessExitCode : FailureExitCode);
    }

    private static async Task<bool> ExpandPackagesFromOfflineFeed(string offlineFeedDirectory, string packagesDirectory, bool verboseLog)
    {
        Console.WriteLine($"Unpacking packages from {packagesDirectory} to offline feed {offlineFeedDirectory}");
        var packageFiles = Directory.GetFiles(packagesDirectory, "*" + PackagingCoreConstants.NupkgExtension);
        var parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = 4 };
        bool successFlag = true;
        object lockObject = new object();

        await Parallel.ForEachAsync(packageFiles, parallelOpts,
          async (packageFile, token) =>
          {
              try
              {
                  await ExpandPackageAsync(packageFile, offlineFeedDirectory, verboseLog, token);
              }
              catch (SignatureException exception)
              {
                  Console.Error.WriteLine(
                      $"Error during loading package {exception.PackageIdentity}: {exception.Code}");
                  foreach (var result in exception.Results)
                  {
                      Console.Error.WriteLine($"  Result: {result}");
                      foreach (var issue in result.Issues)
                      {
                          Console.Error.WriteLine($"    Issue: {issue.Level} {issue.Code} {issue.Message}");
                      }
                  }
                  lock (lockObject)
                  {
                      successFlag = false;
                  }
              }
              catch (Exception exception)
              {
                  Console.Error.WriteLine($"Unknown Error during loading package: {exception.Message}");
                  lock (lockObject)
                  {
                      successFlag = false;
                  }
              }
              finally
              {
                  // Removing source file once unpacked, or if file is corrupted to retry
                  File.Delete(packageFile);
              }
          });
        return successFlag;
    }

    private static async Task ExpandPackageAsync(string packageFilePath, string packageDirectory, bool verboseLog, CancellationToken token)
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
        if (verboseLog)
        {
            Console.WriteLine($"Package {packageFilePath} expanded successfully.");
        }
    }
}