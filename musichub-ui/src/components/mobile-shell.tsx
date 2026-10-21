import { ChevronLeft, ChevronRight, SlidersHorizontal } from "lucide-react"
import type { ReactNode } from "react"

import { ArtistPage } from "@/components/artist-page"
import { BuildInfoSheet } from "@/components/build-info-sheet"
import { CompactPlayer } from "@/components/compact-player"
import { DownloadsView } from "@/components/downloads-view"
import { MobileNav } from "@/components/mobile-nav"
import { NowPlaying, QueueList } from "@/components/player-panel"
import { SearchPage } from "@/components/search-page"
import { SettingsScreen, type SettingsStates } from "@/components/settings-page"
import { Button } from "@/components/ui/button"
import {
  Sheet,
  SheetContent,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import type { ArtistRef } from "@/lib/artist"
import {
  activeNav,
  locationOf,
  settingsScreens,
  type AppLocation,
  type PlayerView,
} from "@/lib/navigation"
import type { DownloadQueueState } from "@/lib/use-download-queue"
import { cn } from "@/lib/utils"

interface MobileShellProps extends SettingsStates {
  location: AppLocation
  onNavigate: (location: AppLocation) => void
  // The catalog's track count, once known.
  total: number | undefined
  filters: ReactNode
  table: ReactNode
  downloadsState: DownloadQueueState
  artistRef: ArtistRef | null
  searchQuery: string
  onSearchQueryChange: (query: string) => void
  onSelectTrack: (trackId: number) => void
  onOpenArtist: (artist: ArtistRef) => void
}

const pageClassName = "flex min-h-0 flex-1 flex-col overflow-hidden"

// A page that isn't showing stays mounted (so uploads, jobs and search keep their
// state) but is hidden and inert, so keyboard focus can't land in it.
function Page({
  show,
  label,
  className,
  children,
}: {
  show: boolean
  label: string
  className?: string
  children: ReactNode
}) {
  return (
    <section
      aria-label={label}
      hidden={!show}
      inert={!show}
      className={cn(pageClassName, className)}
    >
      {children}
    </section>
  )
}

// The title row of a page, with a way back when the page is a level down.
function PageHeader({
  title,
  backLabel,
  onBack,
  children,
}: {
  title: string
  backLabel?: string
  onBack?: () => void
  children?: ReactNode
}) {
  return (
    <div className="flex items-center gap-2">
      {onBack && (
        <Button
          variant="ghost"
          size="icon-lg"
          aria-label={backLabel}
          onClick={onBack}
        >
          <ChevronLeft />
        </Button>
      )}
      <h1 className="text-xl font-semibold">{title}</h1>
      {children}
    </div>
  )
}

// Below `lg` the phone's navigation bar has Search, Library, Downloads, Player (Now
// Playing and Queue) and Settings (Files, Jobs, Problems). The location is the URL
// hash. Outside the Player page a compact player sits above the navigation.
export function MobileShell({
  location,
  onNavigate,
  total,
  filters,
  table,
  downloadsState,
  artistRef,
  searchQuery,
  onSearchQueryChange,
  onSelectTrack,
  onOpenArtist,
  ...settings
}: MobileShellProps) {
  const { tab, screen, playerView } = location

  return (
    <div className="flex h-dvh flex-col overflow-hidden">
      <Page show={tab === "search"} label="Search" className="gap-2 p-2">
        <SearchPage
          query={searchQuery}
          onQueryChange={onSearchQueryChange}
          onSelectTrack={onSelectTrack}
          onSelectArtist={onOpenArtist}
          className="flex-1"
        />
      </Page>
      <Page show={tab === "library"} label="Library" className="gap-3 p-3">
        <PageHeader title="Library">
          {total !== undefined && (
            <span className="text-muted-foreground">{total} tracks</span>
          )}
          <Sheet>
            <SheetTrigger
              render={
                <Button
                  variant="outline"
                  size="icon-lg"
                  aria-label="Filters"
                  className="ml-auto"
                />
              }
            >
              <SlidersHorizontal />
            </SheetTrigger>
            <SheetContent side="bottom" className="h-[85dvh]">
              <SheetTitle className="sr-only">Filters</SheetTitle>
              {filters}
            </SheetContent>
          </Sheet>
        </PageHeader>
        {table}
      </Page>
      <Page show={tab === "downloads"} label="Downloads" className="gap-3 p-3">
        <PageHeader title="Downloads" />
        <DownloadsView state={downloadsState} className="flex-1" />
      </Page>
      <Page show={tab === "player"} label="Player">
        <Tabs
          value={playerView}
          onValueChange={(value) =>
            onNavigate(locationOf("player", null, value as PlayerView))
          }
          className="p-2 pb-0"
        >
          <TabsList className="w-full">
            <TabsTrigger value="now-playing">Now Playing</TabsTrigger>
            <TabsTrigger value="queue">Queue</TabsTrigger>
          </TabsList>
        </Tabs>
        <div
          hidden={playerView !== "now-playing"}
          className="min-h-0 flex-1 overflow-y-auto"
        >
          <NowPlaying large />
        </div>
        <div hidden={playerView !== "queue"} className="min-h-0 flex-1 p-3">
          <div className="flex h-full min-h-0 flex-col rounded-xl border bg-card">
            <QueueList />
          </div>
        </div>
      </Page>
      <Page
        show={tab === "settings" && screen === null}
        label="Settings"
        className="gap-3 p-3"
      >
        <PageHeader title="Settings">
          <BuildInfoSheet className="ml-auto" />
        </PageHeader>
        <ul className="flex flex-col gap-1">
          {settingsScreens.map(({ id, label, icon: Icon }) => (
            <li key={id}>
              <Button
                variant="ghost"
                className="h-12 w-full justify-start gap-3 text-base"
                onClick={() => onNavigate(locationOf("settings", id))}
              >
                <Icon />
                <span className="flex-1 text-left">{label}</span>
                <ChevronRight className="text-muted-foreground" />
              </Button>
            </li>
          ))}
        </ul>
      </Page>
      {settingsScreens.map(({ id, label }) => (
        <Page
          key={id}
          show={tab === "settings" && screen === id}
          label={label}
          className="gap-3 p-3"
        >
          <PageHeader
            title={label}
            backLabel="Back to Settings"
            onBack={() => onNavigate(locationOf("settings"))}
          />
          <SettingsScreen id={id} className="flex-1" {...settings} />
        </Page>
      ))}
      <Page show={tab === "artist"} label="Artist" className="gap-3 p-3">
        <PageHeader
          title="Artist"
          backLabel="Back to Search"
          onBack={() => onNavigate(locationOf("search"))}
        />
        <ArtistPage artistRef={artistRef} className="flex-1" />
      </Page>
      {tab !== "player" && <CompactPlayer />}
      <MobileNav
        active={activeNav(tab)}
        onNavigate={(id) => onNavigate(locationOf(id))}
      />
    </div>
  )
}
