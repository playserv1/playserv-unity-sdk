# Owner prediction and reconciliation (PSV-3124)

`Playserv.Prediction` supplies an opt-in input history/replay engine in
`Playserv.Runtime.dll`. It works with Editor, native players and WebGL without
threads, tasks, reflection or a particular network transport. Existing
`NetworkTransform` remote interpolation keeps its behavior.

The SDK owns sequencing, bounded history, authoritative reconciliation and visual
correction. Your game supplies its repeatable simulation and maps its server
protocol to the API. Adding the package alone does not change a game's controls
or make its server acknowledge inputs.

## Simulation and protocol agreement

- Execute one bounded simulation step per input command, including unchanged held
  input. Use the same step duration and movement rules on server and client.
  The server validates input, duration and actions; predicted positions are not
  authority. A transport acknowledgement is not a simulation acknowledgement.
- Every snapshot provides a strictly increasing snapshot tick, the owner's state,
  and cumulative `lastProcessedInput`: all commands up to this sequence have been
  applied to **that exact state**, each once. Never acknowledge a queued command
  before applying it. If the server coalesces inputs or free-runs held input, its
  adapter must map that timing to an equivalent replay baseline; a sequence number
  alone does not describe simulated duration.
- State contains everything needed to reproduce a step (velocity, cooldown and
  movement mode, for example). World interactions can still cause corrections.
  Do not assume deterministic Unity physics across platforms: supply a suitable
  simulator and tolerate authoritative corrections.
- A new connection, respawn or teleport needs an authoritative baseline and agreed
  sequence cursor. Call `Reset` and reset the visual smoother. Capture `Generation`
  when binding a connection/request and pass that captured value to its response
  handler. Broadcasts on a reused connection need a game incarnation ID or another
  fence for pre-reset frames. Reading current `Generation` when an old packet
  arrives defeats this protection.

## Minimal integration

```csharp
using Playserv.Prediction;
using UnityEngine;

var prediction = new PlayServPrediction<Vector3, Vector3>(
    (state, input, context) => state + input * (speed * context.DeltaTime),
    state => state, input => input); // Mutable graphs require deep-copy functions.
prediction.Reset(initialPosition, initialSnapshotTick, initialProcessedInput);
var connectionGeneration = prediction.Generation;

// One fixed simulation step, not variable render-frame time:
if (prediction.TryPredict(direction, fixedStepSeconds, out var command))
{
    // Send command.Sequence, command.Input and command.DeltaTime once.
    // Local effects use (command.Generation, command.Sequence) as their ID.
}

// Snapshot for the captured connection/incarnation:
var before = prediction.State;
var result = prediction.Reconcile(connectionGeneration, snapshotTick, lastProcessedInput, serverPosition);
// After Applied, correct the visual pose separately; keep rendering prediction.State.
```

`TryPredict` moves before any response. `Reconcile` restores the server state,
removes acknowledged commands and replays the rest with their original durations.
A newer tick with an unchanged watermark is valid. Duplicated/older ticks and old
local generations return `Stale`; regressing/future acknowledgements return
`InvalidAcknowledgement`. Rejection leaves state/history unchanged. Inspect these
results for protocol diagnostics.

Default history: 128 commands. Default maximum step: 0.1 seconds. Both are
constructor options. When full, `TryPredict` returns false without advancing or
losing commands. Pause command generation until acknowledgements free space, or
request a fresh baseline and reset after the server agrees. Do not discard inputs,
reuse a sequence for an unpredicted command, or accumulate a large catch-up time.
Sequence/generation cursors are nonnegative signed 64-bit values; overflow fails.

Use one owning thread (normally Unity's thread). Reentrant mutation is rejected.
Deep-copy functions must be pure: inputs are copied for storage, outgoing commands
and simulation; states on entry, simulation and reads. Identity copies suffice for
immutable values. Callback exceptions preserve the session but cannot undo external
side effects from an impure callback.

## Effects and authoritative outcomes

`PredictionSimulationContext.IsReplay` distinguishes replay from initial prediction;
`Generation` and `Sequence` stay unchanged during replay. Prefer network sends,
audio, particles and predicted-shot creation outside the simulator, after a
successful `TryPredict`. No SDK event resends them during reconciliation. Use the
command ID to match predicted effects to server confirmation/rejection; a movement
watermark does not confirm a hit. Damage, score and action acceptance remain
server-authoritative. This client engine does not implement server-side hit rewind.

## Visual correction

`PlayServPredictionSmoothing` smooths position/rotation only. After an applied
reconciliation, obtain the visual pose from the **pre-correction predicted pose
plus existing render offset** (`Apply(beforePose, ..., 0, ...)`), then call
`Correct(renderedPose, newPredictedPose)`. This handles several snapshots per frame.
Each render frame calls `Apply(predictedPose, ..., deltaTime, out position, out
rotation)`: new inputs move immediately while the offset decays. Never feed smoothed
coordinates back into simulation.

Defaults: 0.08-second half-life, snap beyond 3 world units or 90 degrees. Tune for
the game's scale. Explicit `Reset` clears the offset for teleport/respawn. Use
unscaled delta time to continue visual correction while paused. Invalid/non-finite
times or poses are rejected.

## Example and verification

Import **PlayServ SDK Examples**, add `OwnerPredictionDemo` to an empty scene,
then use arrow keys. The in-process server fixture defaults to 200 ms RTT, applies
commands once and sends authoritative corrections. The respawn button demonstrates
baseline reset. No credentials/live server are used. Enable the legacy Input
Manager or Both when using the new Input System.

`PlayServPredictionTests` cover immediate movement, delayed acknowledgements with
jitter/reordering, replay/effect isolation, copying, bounded history, stale/invalid
acks, resets and visual continuity. The source repository's
`unity/tools/prediction-smoke` exercises the API in a real WebGL/IL2CPP player.
Game acceptance additionally requires its simulation/protocol adapter and server
implementation of the agreement above.
