import { ChevronRight } from "lucide-react"
import type { ReactNode } from "react"

import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible"

// One recorded result in a list: a title, what it came to and a badge, with the details one click away.
export function ResultRow({
  title,
  summary,
  badge,
  children,
}: {
  title: ReactNode
  summary: ReactNode
  badge: ReactNode
  children: ReactNode
}) {
  return (
    <li>
      <Collapsible className="group rounded-lg border bg-muted/40">
        <CollapsibleTrigger className="flex w-full items-start gap-2 p-2 text-left text-sm">
          <ChevronRight
            aria-hidden="true"
            className="mt-0.5 size-4 shrink-0 transition-transform group-data-[open]:rotate-90"
          />
          <span className="min-w-0 flex-1">
            <span className="block break-words font-medium">{title}</span>
            <span className="block break-words text-muted-foreground">{summary}</span>
          </span>
          {badge}
        </CollapsibleTrigger>
        <CollapsibleContent className="border-t p-2">{children}</CollapsibleContent>
      </Collapsible>
    </li>
  )
}
