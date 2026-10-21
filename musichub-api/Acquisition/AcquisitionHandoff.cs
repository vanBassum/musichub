using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Acquisition;

/// <summary>
/// Gives a downloaded file to the existing ingestion: it is stored and registered like any received file, nothing
/// more. Identification runs on it through the existing identification workflow; the provider's file name and
/// metadata never decide what the file is.
/// </summary>
public sealed class AcquisitionHandoff(FileIngestionService ingestion)
{
    /// <param name="expected">The length and SHA-256 the bytes must have; ingestion refuses the file when they differ.</param>
    public async Task<FileRecord> IngestAsync(
        AcquisitionDownloadOutcome outcome, CancellationToken ct, UploadExpectation? expected = null)
    {
        if (outcome.File is not { } file)
        {
            throw new InvalidOperationException("Only a completed download can be handed to ingestion.");
        }

        await using var content = file.OpenRead();
        return await ingestion.IngestAsync(content, file.FileName, null, expected, ct, source: SourceOf(outcome.Provider));
    }

    /// <summary>The <see cref="FileRecord.Source"/> of a file that a provider's download became.</summary>
    public static string SourceOf(string provider) => $"acquisition:{provider}";
}
