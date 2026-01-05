using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morpheo.Core.Blobs;
using Morpheo.Core.Configuration;
using Morpheo.Sdk.Blobs;
using System;
using System.IO;

namespace Morpheo.Core.Extensions
{
    /// <summary>
    /// Extension methods for configuring blob storage.
    /// </summary>
    public static class MorpheoBlobExtensions
    {
        /// <summary>
        /// Adds a file system-based blob store to the DI container.
        /// </summary>
        /// <param name="builder">The Morpheo builder.</param>
        /// <param name="path">The root directory for storing blobs. Defaults to 'storage/blobs'.</param>
        /// <returns>The Morpheo builder.</returns>
        public static IMorpheoBuilder AddBlobStore(this IMorpheoBuilder builder, string? path = null)
        {
            var storagePath = path ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "storage", "blobs");

            builder.Services.TryAddSingleton<IMorpheoBlobStore>(sp => new FileSystemBlobStore(storagePath));

            return builder;
        }
    }
}
