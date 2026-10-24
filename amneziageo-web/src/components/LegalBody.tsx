import type { LegalBlock } from "@/legal"

export function LegalBody({ blocks }: { blocks: LegalBlock[] }) {
  return (
    <div className="flex flex-col gap-2 text-sm leading-6 text-ink">
      {blocks.map((block, index) => {
        const key = `${index}-${block.kind}`

        if (block.kind === "heading") {
          return (
            <h3 key={key} className="mt-3 text-[15px] font-semibold first:mt-0">
              {block.text}
            </h3>
          )
        }

        if (block.kind === "bullet") {
          return (
            <div key={key} className="flex gap-2 pl-1">
              <span aria-hidden>•</span>
              <span>{block.text}</span>
            </div>
          )
        }

        return <p key={key}>{block.text}</p>
      })}
    </div>
  )
}
