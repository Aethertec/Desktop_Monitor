# Desktop-pin spike result (2026-10-02)

| Check | Owner mode | No-owner mode |
|---|---|---|
| A. Visible on desktop, top-right | Pass | not run |
| B. Behind app windows | Pass | not run |
| C. Visible after Win+D | Pass | not run |
| D. Click passes through to desktop icon | Pass | not run |
| Explorer restart: card re-attached | Pass | not run |
| Unlocked drag works | Pass | n/a |

Decision: UseDesktopOwner = true

Notes: Owner window is Progman (desktop host 0x10156 before restart); inspect-window showed only Progman below the card (z-position 18 of 20). Explorer was stopped and auto-restarted; the TaskbarCreated broadcast reached the card and the log recorded "DesktopPin: Explorer restarted, re-attaching", after which the owner was the new Progman and the card stayed visible. A-D and the drag test were confirmed by the user on screen. Fallback mode was not needed.
