import type { ReactNode } from "react"

import { ResultRow } from "@/components/result-row"
import { Badge } from "@/components/ui/badge"
import { Skeleton } from "@/components/ui/skeleton"
import { formatBytes } from "@/lib/format"
import {
  isMissingMusicSummary,
  type DownloadItem,
  type DownloadStatus,
  type JobExecution,
  type TrackAcquisitionOutcome,
  type TrackAcquisitionResult,
} from "@/lib/jobs-api"
import { useDownloads } from "@/lib/use-downloads"

export const downloadLabels: Record<DownloadStatus, string> = {
  Downloading: "Downloading",
  Failed: "Download failed",
  Cancelled: "Cancelled",
  Rejected: "Rejected",
  Imported: "Imported",
}

const downloadClassNames: Record<DownloadStatus, string> = {
  Downloading: "bg-primary text-primary-foreground",
  Failed: "bg-destructive text-white",
  Cancelled: "bg-muted text-muted-foreground",
  Rejected: "bg-warning text-warning-foreground",
  Imported: "bg-success text-success-foreground",
}

const trackLabels: Record<TrackAcquisitionOutcome, string> = {
  Enqueued: "Queued",
  AlreadyQueued: "Already queued",
  NothingFound: "Nothing found",
  ProviderUnavailable: "Provider unavailable",
  Skipped: "Skipped",
  Failed: "Error",
}

const trackClassNames: Record<TrackAcquisitionOutcome, string> = {
  Enqueued: "bg-primary text-primary-foreground",
  AlreadyQueued: "bg-muted text-muted-foreground",
  NothingFound: "bg-muted text-muted-foreground",
  ProviderUnavailable: "bg-destructive text-white",
  Skipped: "bg-muted text-muted-foreground",
  Failed: "bg-destructive text-white",
}

function Detail({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-col sm:flex-row sm:gap-2">
      <dt className="shrink-0 text-muted-foreground sm:w-32">{label}</dt>
      <dd className="min-w-0 break-all">{children}</dd>
    </div>
  )
}

function DownloadRow({ download }: { download: DownloadItem }) {
  return (
    <ResultRow
      title={download.trackTitle ?? `Track ${download.trackId ?? "(deleted)"}`}
      summary={download.reason || "In progress"}
      badge={
        <Badge className={downloadClassNames[download.status]}>
          {downloadLabels[download.status]}
        </Badge>
      }
    >
      <dl className="flex flex-col gap-1 text-xs">
        <Detail label="Source">
          {download.provider} · {download.source}
        </Detail>
        <Detail label="Path at source">{download.sourcePath}</Detail>
        <Detail label="Size">
          {formatBytes(download.sizeBytes)}
          {download.bitrateKbps !== null && ` · ${download.bitrateKbps} kbps`}
        </Detail>
        <Detail label="File SHA-256">{download.fileSha256 ?? "not available"}</Detail>
        <Detail label="Audio SHA-256">
          {download.audioHash ?? download.audioHashStatus?.toLowerCase() ?? "not available"}
        </Detail>
        {download.fileId !== null && <Detail label="Library file">{download.fileId}</Detail>}
        <Detail label="Started">{new Date(download.startedAt).toLocaleString()}</Detail>
        {download.finishedAt && (
          <Detail label="Finished">{new Date(download.finishedAt).toLocaleString()}</Detail>
        )}
      </dl>
    </ResultRow>
  )
}

function TrackRow({ result }: { result: TrackAcquisitionResult }) {
  return (
    <ResultRow
      title={result.title ? `${result.title} · ${result.artists}` : `Track ${result.trackId}`}
      summary={result.detail}
      badge={
        <Badge className={trackClassNames[result.outcome]}>{trackLabels[result.outcome]}</Badge>
      }
    >
      <p className="text-xs text-muted-foreground">Track {result.trackId}</p>
    </ResultRow>
  )
}

function Progress({ execution, downloads }: { execution: JobExecution; downloads: number }) {
  if (execution.state !== "Processing" || execution.total === null) return null
  return (
    <p className="text-sm text-muted-foreground">
      Going through {execution.total} {execution.total === 1 ? "track" : "tracks"}; {downloads}{" "}
      {downloads === 1 ? "candidate" : "candidates"} downloaded so far.
    </p>
  )
}

// What a Search for Missing Music execution did: each track's result once it is finished, and, as they are
// recorded, the candidates it downloaded with their hashes and how each ended.
export function MissingMusicResults({
  execution,
  version,
}: {
  execution: JobExecution
  version: string
}) {
  const { downloads, error } = useDownloads(execution.id, version)
  const tracks =
    execution.summary && isMissingMusicSummary(execution.summary) ? execution.summary.tracks : []

  return (
    <div className="flex flex-col gap-3">
      <Progress execution={execution} downloads={downloads?.length ?? 0} />
      {tracks.length > 0 && (
        <div>
          <h3 className="mb-2 text-sm font-semibold">Tracks</h3>
          <ul aria-label="Track results" className="flex flex-col gap-2">
            {tracks.map((result) => (
              <TrackRow key={result.trackId} result={result} />
            ))}
          </ul>
        </div>
      )}
      <div>
        <h3 className="mb-2 text-sm font-semibold">Downloads</h3>
        {error && (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}
        {downloads === null ? (
          !error && <Skeleton className="h-16 w-full" />
        ) : downloads.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No candidate was downloaded in this execution.
          </p>
        ) : (
          <ul aria-label="Downloads" className="flex flex-col gap-2">
            {downloads.map((download) => (
              <DownloadRow key={download.id} download={download} />
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}
