# Direct control trial

The user requested trying direct interfaces across existing functions while retaining working compatibility for unsupported operations. Control discovery still uses the same semantic snapshot and active launcher rules. Direct execution and compatibility are distinct outcomes in the operation journal.

| Function | First path | Compatibility / current boundary |
| --- | --- | --- |
| Play/pause, previous, next | QQ Music's Windows media session command | Semantic control activation only when no command was issued and the interface is unsupported |
| Named buttons, popup launchers, exposed popup items | Revalidate the MSAA object, then its default action | Existing semantic mouse dispatch when the provider explicitly lacks the action |
| Seek, including lyric timestamp jumps | Media-session position command when advertised | One existing timeline click on release; unsupported in the tested QQ Music session |
| Editable text | Existing MSAA value read/write | Submission still uses the existing verified editor and Enter; no verified direct submit action |
| Volume | Existing process-scoped audio-session API | This changes the Windows audio session, not QQ Music's internal slider object |
| Playback order | Existing explicit `/playmode` command | The tested media session does not advertise repeat/shuffle control |
| Hover, wheel, unsupported surfaces/list selection | Existing ownership-aware message routing | No validated equivalent direct operation in this provider |

## Execution rules

Only the exact `Tencent.QQMusic` session is selected, and only with a unique, still-running QQ Music process matching the source controller. The current system player is never used as a substitute. Capabilities are checked for each command. Optional WinRT projection is loaded through reflection, retaining the existing controller framework target without starting a scripting process.

`Unsupported` permits compatibility before an operation could have run. `Delivered`, `Rejected`, and `Uncertain` never repeat an action through another channel. Once a media command is issued, a false result, exception or timeout is uncertain rather than permission to send a click. Command waits are bounded and run on workers. Seek preserves one submission on release; a changed source, geometry or newer interaction cancels delayed compatibility.

The MSAA path matches role, name, description and live bounds, excludes hidden/disabled ancestors, rejects duplicate matches, and rechecks bounds/state immediately before invocation. Traversal includes the root object, is limited to 8,192 nodes, 32 levels and a 300 ms scan budget. It runs on a worker; the caller waits at most 350 ms. Source identity/geometry, click generation and deadline are rechecked before the action and before compatibility. The deadline prevents dispatch after a slow lookup, but cannot undo an already-issued external call; that case is uncertain and never followed by a click. List single-click selection is not replaced by a default action that might open or play the item.

## Observed provider limits

The tested QQ Music build advertises UIA Invoke on buttons, but a direct play/pause invocation returned successfully without changing playback. That path is deliberately not shipped. MSAA default actions for More, comments, playback order, volume, player style, effects and playlist returned unsupported. These retain their working paths; this trial does not claim that all QQ Music controls now offer direct commands.

A real media-session pause changed playback state, and a direct play command restored the original state without restarting QQ Music or changing tracks. Previous/next are advertised and covered by command-routing fixtures; this test did not deliberately change songs. The current session does not advertise direct seek, repeat or shuffle.

## Regression boundary

Owned offscreen controls verify a real MSAA invocation without mouse messages. Controller call-path tests cover direct success, unsupported compatibility, rejection, uncertain outcomes, source replacement, command mapping, non-player rejection, background seek and cancelled compatibility. The existing popup/ownership suites continue to exercise their compatibility fixtures. They do not establish live behavior for every QQ Music skin, menu or future provider version.

Operation records use identifiers and enum results: `media-api-*`, `control-api-*`, and `seek-api-*`. They contain no song titles, lyrics, user input or window text.
