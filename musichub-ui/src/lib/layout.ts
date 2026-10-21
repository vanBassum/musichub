// The widths (in rem, as Tailwind's `md` and `lg`) where the layout changes: a phone
// below `tabletMinRem`, a tablet from there, and the docked-player desktop from
// `desktopMinRem`.
export const tabletMinRem = 48
export const desktopMinRem = 64

export type Layout = "phone" | "tablet" | "desktop"

export function layoutFor(widthRem: number): Layout {
  if (widthRem >= desktopMinRem) return "desktop"
  return widthRem >= tabletMinRem ? "tablet" : "phone"
}
