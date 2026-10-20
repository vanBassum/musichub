using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Acquisition;

/// <summary>
/// Gives a downloaded file to the existing ingestion: it is stored and registered like any received file, nothing
/// more. Identification runs on it later, through the existing identification workflow; the provider's file name
/// and metadata never decide what the file is.
/// </summary>
public sealed class AcquisitionHandoff(FileIngestionService ingestion)
{
    public async Task<FileRecord> IngestAsync(AcquisitionDownloadOutcome outcome, CancellationToken ct)
    {
        if (outcome.File is not { } file)
        {
            throw new InvalidOperationException("Only a completed download can be handed to ingestion.");
        }

        await using var content = file.OpenRead();
        return await ingestion.IngestAsync(content, file.FileName, null, null, ct, source: $"acquisition:{outcome.Provider}");
    }
}
