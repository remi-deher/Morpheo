using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morpheo.Core.Blobs;
using Morpheo.Core.Configuration;
using Morpheo.Sdk.Blobs;
using System;
using System.IO;

namespace Morpheo
{
    /// <summary>
    /// Configures blob storage for large file handling (Sidecar Pattern).
    /// Prevents 1GB+ files from saturating RAM during sync.
    /// </summary>
    public static class MorpheoBlobExtensions
    {
        /// <summary>
        /// Registers filesystem-based blob storage with disk-backed metadata.
        /// </summary>
        /// <param name="builder">Morpheo DI builder.</param>
        /// <param name="path">Storage root directory (defaults to 'storage/blobs' relative to app base).</param>
        public static IMorpheoBuilder AddBlobStore(this IMorpheoBuilder builder, string? path = null)
        {
            var storagePath = path;
            if (string.IsNullOrEmpty(storagePath))
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                storagePath = Path.Combine(appData, "Morpheo", "Blobs");
            }

            builder.Services.Configure<FileSystemBlobStoreOptions>(options =>
            {
                options.RootPath = storagePath;
            });

            builder.Services.TryAddSingleton<IMorpheoBlobStore, FileSystemBlobStore>();

            return builder;
        }
    }
}
