# Channel Ripper Branding Notes

## Can This Fork Have Its Own Name?

Yes. The lowest-risk branding changes are:

- app display name
- package display name
- publisher display name
- logos and tiles

These do not require changing Telegram functionality.

## Low-Risk Branding Files

Main package branding:

- `Telegram.Msix\Package.appxmanifest`

Main logo assets:

- `Telegram\Assets\Logos\*`

Display name resource:

- manifest currently uses `ms-resource:AppDisplayName` for visual elements

## Recommended Branding Strategy

If you want this to feel like your own fork without breaking anything major:

1. keep the Telegram protocol handlers
2. change the app display name
3. change the tile/store logos
4. optionally change the package identity later if you want side-by-side install with official Unigram

## Important Tradeoff

Changing the package identity is more invasive than changing the visible name.

- Pros:
  - cleaner separation from official Unigram
  - side-by-side install is possible
- Cons:
  - new app data location
  - new package family name
  - update/install docs must point to the new identity

## Recommendation

For now:

- change the visible display name and logos first
- leave package identity alone until the fork is otherwise stable

That gives you differentiation with minimal churn.
