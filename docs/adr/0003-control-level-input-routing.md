# Route desktop input through live semantic controls

The desktop mirror resembles QQ Music but is not its native input surface, and fixed screen positions break across skins, window recovery, duplicate accessibility trees, and QQ Music updates. Desktop actions therefore resolve live semantic controls and native popup ownership before dispatch; the timeline is the sole derived surface because QQ Music exposes time labels but no slider. Controller overlays cover volume, playback-order selection and verified editable source controls, where the parked native surface cannot reliably own visible desktop input. This adds accessibility-tree caching and invalidation work, but keeps interactions independent of resolution and prevents broad hit targets from stealing neighboring controls.


Whole-window routing uses the same snapshot for hit testing and delayed invocation. Named MSAA panes and semantic list/link/edit roles qualify; invisible ancestors stop traversal, while an offscreen state alone remains valid for a deliberately parked source. Source bounds clip projected input regions. A source move or a newer click cancels a delayed action.

The semantic text editor owns IME, clipboard and selection locally. It reads and writes only an enabled, writable, non-protected MSAA text node with matching live bounds and name. Enter commits and posts Enter to that source input child; Escape and deactivation cancel. Values and control labels remain out of operation logs. Unsupported value providers produce a diagnostic result instead of a global keyboard fallback.

General application dialogs require a newly visible window in the source process, a root-owner relationship to the source, and an exact title match to the triggering semantic control. Existing named accessories retain their specialized lifecycle. Ties and unproven windows remain unpresented.

The [direct-control trial](../direct-control-routing.md) adds native media commands and MSAA default actions before compatibility dispatch. Unsupported operations retain their existing behavior by user request; an attempted command with an uncertain outcome must not trigger a second channel. Existing popup ownership and semantic-target selection remain authoritative. UIA invocation is not enabled merely because a provider advertises it.
