using NuGetInspectorApp.Models;

namespace NuGetInspectorApp.Services
{
    /// <summary>
    /// Provides methods for fetching package metadata from the NuGet API.
    /// </summary>
    public interface INuGetApiService
    {
        /// <summary>
        /// Fetches metadata for a specific package version from the NuGet API.
        /// </summary>
        /// <param name="id">The package ID.</param>
        /// <param name="version">The package version.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the package metadata.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> or <paramref name="version"/> is null or empty.</exception>
        /// <exception cref="HttpRequestException">Thrown when the HTTP request fails.</exception>
        Task<PackageMetadata> FetchPackageMetadataAsync(string id, string version, CancellationToken cancellationToken = default);
    }
}