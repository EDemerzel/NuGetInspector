using NuGetInspectorApp.Models;

namespace NuGetInspectorApp.Services;

/// <summary>
/// Provides methods for fetching package Metadata from the NuGet API.
/// </summary>
public interface INuGetApiService
{
    /// <summary>
    /// Fetches Metadata for a specific package version from the NuGet API.
    /// </summary>
    /// <param name="id">The package ID.</param>
    /// <param name="version">The package version.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the package Metadata.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> or <paramref name="version"/> is null or empty.</exception>
    /// <exception cref="HttpRequestException">Thrown when the HTTP request fails.</exception>
    Task<PackageMetaData> FetchPackageMetaDataAsync(string id, string version, CancellationToken cancellationToken = default);
}