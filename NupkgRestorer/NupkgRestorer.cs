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
    public static async Task<int> Main(string[] args)
    {
        var offlineFeedOption = new Option<string>("--feed", "The offline feed directory");
        var packagesDirectoryOption = new Option<string>("--packages", "The package directory");
        var verboseLogOption = new Option<bool>("--verbose", "Print verbose logs");

        offlineFeedOption.IsRequired = true;
        packagesDirectoryOption.IsRequired = true;

        RootCommand rootCommand = new RootCommand("Unpacks nupkgs from packages directory to offline feed folder");
        rootCommand.AddOption(offlineFeedOption);
        rootCommand.AddOption(packagesDirectoryOption);
        rootCommand.AddOption(verboseLogOption);

        rootCommand.SetHandler(async (offlineFeedDirectory, packagesDirectory, verboseLog) => 
            { 
                await ExpandPackagesFromOfflineFeed(offlineFeedDirectory, packagesDirectory, verboseLog); 
            },
            offlineFeedOption,
            packagesDirectoryOption,
            verboseLogOption);
        
        return await rootCommand.InvokeAsync(args);
    }

    private static async Task ExpandPackagesFromOfflineFeed(string offlineFeedDirectory, string packagesDirectory, bool verboseLog)
    {
        Console.WriteLine($"Unpacking packages from {packagesDirectory} to offline feed {offlineFeedDirectory}");
        var packageFiles = Directory.GetFiles(packagesDirectory, "*" + PackagingCoreConstants.NupkgExtension);
        var maxConcurrency = 4;
        var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var extractionTasks = new List<Task>();

        foreach (var packageFile in packageFiles)
        {
            await semaphore.WaitAsync();
            
            var extractionTask = Task.Factory.StartNew(() =>
            {
              try
              {
                ExpandPackageAsync(packageFile, offlineFeedDirectory).Wait();
                if (verboseLog)
                {
                    Console.WriteLine($"Package {packageFile} expanded successfully.");
                }
              }
              finally
              {
                semaphore.Release();
              }
            });

            extractionTasks.Add(extractionTask);
        }
        
        await Task.WhenAll(extractionTasks);
    }

    private static async Task ExpandPackageAsync(string packageFilePath, string packageDirectory)
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

        await OfflineFeedUtility.AddPackageToSource(offlineFeedAddContext, CancellationToken.None);
    }
}