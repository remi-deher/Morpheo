using Microsoft.Extensions.DependencyInjection;
using Morpheo.Core.Blobs;
using Morpheo.Core.Configuration;
using Morpheo.Sdk.Blobs;

namespace Morpheo;

public static class BlobStoreExtensions
{
    /// <summary>
    /// Configures Morpheo to use the FileSystemBlobStore with the specified path.
    /// </summary>
    /// <param name="builder">The Morpheo builder.</param>
    /// <param name="path">The root path for storing blobs.</param>
    /// <returns>The Morpheo builder.</returns>
    public static IMorpheoBuilder UseFileSystemBlobs(this IMorpheoBuilder builder, string path)
    {
        builder.Services.Configure<FileSystemBlobStoreOptions>(options =>
        {
            options.RootPath = path;
        });

        builder.Services.AddSingleton<IMorpheoBlobStore, FileSystemBlobStore>();

        return builder;
    }
}
