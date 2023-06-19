using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Signing;
using NuGet.Protocol.Core.Types;
using System.CommandLine;
using NuGet.Packaging.Core;
using NuGet.Packaging.PackageExtraction;

internal class NupkgRestorer
{
    public static async Task<int> Main(string[] args)
    {
        var offlineFeedOption = new Option<string>("--feed", "The offline feed directory");
        var packagesDirectoryOption = new Option<string>("--packages", "The package directory");

        offlineFeedOption.IsRequired = true;
        packagesDirectoryOption.IsRequired = true;

        RootCommand rootCommand = new RootCommand("Unpacks nupkgs from packages directory to offline feed folder");
        rootCommand.AddOption(offlineFeedOption);
        rootCommand.AddOption(packagesDirectoryOption);

        rootCommand.SetHandler(async (offlineFeedDirectory, packagesDirectory) => 
        { 
            await ExpandPackagesFromOfflineFeed(offlineFeedDirectory, packagesDirectory); 
        },
        offlineFeedOption,
        packagesDirectoryOption);
        
        return await rootCommand.InvokeAsync(args);
    }

    private static async Task ExpandPackagesFromOfflineFeed(string offlineFeedDirectory, string packagesDirectory)
    {
        Console.WriteLine($"Unpacking packages from {packagesDirectory} to offline feed {offlineFeedDirectory}");
        var packageFiles = Directory.GetFiles(packagesDirectory, "*" + PackagingCoreConstants.NupkgExtension);
        
        foreach (var packageFile in packageFiles)
        {
            var packageIdentity = new PackageArchiveReader(packageFile).GetIdentity();

            bool isValidPackage;
            if (OfflineFeedUtility.PackageExists(packageIdentity, offlineFeedDirectory, out isValidPackage))
            {
                if (isValidPackage)
                {
                    Console.WriteLine($"Package {packageIdentity} already exists and is valid.");
                }
                else
                {
                    Console.WriteLine($"Existing package {packageIdentity} is invalid.");
                }
            }
            
            await ExpandPackageAsync(packageFile, offlineFeedDirectory);
            Console.WriteLine($"Package {packageIdentity} expanded successfully.");
        }
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