using AethericForge.Runtime.Abstractions.Interfaces.Archive.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Archive.Providers;
using AethericForge.Runtime.Abstractions.Interfaces.Archive.Serialization;
using AethericForge.Runtime.Abstractions.Interfaces.Archive.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Authorities;
using AethericForge.Runtime.Models.Archive.Serialization;
using AethericForge.Runtime.Models.Authorities;
using AethericForge.Runtime.Providers.Archive.InMemory;
using AethericForge.Runtime.Services.Archive;

namespace AdrCampus.Providers.Archive.Tests;

internal static class ArchiveTestSupport
{
    public const string Store = "adr-campus";

    public static InMemoryArchiveProvider NewProvider() => new(Store);

    public static IArchivist Archivist(IArchiveProvider provider) =>
        new AethericForge.Runtime.Services.Archive.Archivist(
            new ArchiveService([provider]),
            new Team<IArchiveClerk>(Array.Empty<IArchiveClerk>()),
            [new JsonArchiveSerializer()]);

    /// <summary>Wraps a provider and can fail the next write, for fault-injection tests.</summary>
    public sealed class FailNextPutArchiveProvider(IArchiveProvider inner) : IArchiveProvider
    {
        public bool FailNextPut { get; set; }
        public string Store => inner.Store;

        public Task<IArchiveReference> PutAsync(string key, Stream content, IArchiveMetadata? metadata = null, CancellationToken ct = default)
        {
            if (FailNextPut)
            {
                FailNextPut = false;
                throw new InvalidOperationException("Injected persistence failure.");
            }
            return inner.PutAsync(key, content, metadata, ct);
        }

        public Task<Stream> RetrieveAsync(IArchiveReference reference, CancellationToken ct = default) => inner.RetrieveAsync(reference, ct);
        public Task<IArchiveMetadata?> StatAsync(IArchiveReference reference, CancellationToken ct = default) => inner.StatAsync(reference, ct);
        public Task<bool> ExistsAsync(IArchiveReference reference, CancellationToken ct = default) => inner.ExistsAsync(reference, ct);
        public Task<bool> DeleteAsync(IArchiveReference reference, CancellationToken ct = default) => inner.DeleteAsync(reference, ct);
    }
}
