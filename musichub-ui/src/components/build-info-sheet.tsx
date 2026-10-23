import { Info } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet"
import { buildInfo } from "@/lib/build-info"
import { cn } from "@/lib/utils"

const rows = [
  { label: "Commit", value: buildInfo.commit },
  { label: "Short commit", value: buildInfo.shortCommit },
  { label: "Built (UTC)", value: buildInfo.builtAt },
]

export function BuildInfoList({ className }: { className?: string }) {
  return (
    <dl className={cn("flex flex-col gap-3 text-sm", className)}>
      {rows.map(({ label, value }) => (
        <div key={label} className="flex flex-col gap-0.5">
          <dt className="text-muted-foreground">{label}</dt>
          <dd className="font-mono break-all select-all">{value}</dd>
        </div>
      ))}
    </dl>
  )
}

// Where the running build came from. Values are baked in at build time.
export function BuildInfoSheet({ className }: { className?: string }) {
  return (
    <Sheet>
      <SheetTrigger
        render={
          <Button
            variant="ghost"
            size="icon-lg"
            aria-label="Build info"
            className={className}
          />
        }
      >
        <Info />
      </SheetTrigger>
      <SheetContent side="bottom" className="sm:mx-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Build info</SheetTitle>
          <SheetDescription>
            The build this page is served from.
          </SheetDescription>
        </SheetHeader>
        <BuildInfoList className="p-4 pt-0" />
      </SheetContent>
    </Sheet>
  )
}
