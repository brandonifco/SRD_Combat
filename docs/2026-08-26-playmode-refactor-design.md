# Design — the PlayMode focus refactor (#327)

**Date:** 2026-08-26. **Author:** `architect`.

**Mandate:** Brandon, asked tonight to choose between this refactor — which has zero
player-visible payoff — and battlefield or audio work he could feel, chose this:

> *"i definitely DO want to do 327. i can't let this thing become a few monoliths of
> unmaintainable code."*

So **structural health is the goal, not a means to unblocking features.** This document
does not argue the refactor's case from what it unblocks. It argues it from the defect,
and it is deliberately shaped so the refactor cannot become a rewrite: nine slices, each
one leaving a client that builds, plays and ships.

This is the spec. Nothing here is implemented. The implementation slices are filed as
issues and listed in [§10](#10-slices-and-sequencing); each carries its own acceptance
criteria and its own proof obligation. `docs/2026-08-25-battlefield-overhaul-design.md`
and `docs/2026-08-26-battle-builder-design.md` are the model for this document's shape.

---

## 1. What exists today, measured

Measured at `7bd7c3a` (`main`), 2026-08-26. Every figure below is `wc -l`, `grep -c` or a
line-numbered read, not an estimate.

| | 2026-08-21 review | #327's comment (2026-08-26, pre-#470) | **`7bd7c3a`, tonight** |
| --- | --- | --- | --- |
| `client/PlayMode.cs` lines | 2,570 | 2,661 | **2,674** |
| instance fields | 39 | 48 | **48** (+ the `CreatedDrafts` init property) |
| `_UnhandledInput` | 233 lines | 207 | **207** (989–1195) |

For scale, the rest of the client: `FightScreen.cs` 2,432, `CreateMode.cs` 1,307,
`SpriteLibrary.cs` 781, `WatchMode.cs` 224, `LogHighlighter.cs` 221, `Main.cs` 23.

**Nine fields in five weeks, and the focus method shrank while the state it coordinates
grew.** That direction of travel is the whole diagnosis: extracting the method would look
like progress and fix nothing.

### 1.1 The modal priority order is written three times

Three hand-maintained copies of one truth, none of which references the others:

| copy | where | order it encodes |
| --- | --- | --- |
| keyboard | `_UnhandledInput`, 989–1195 | quit card · shop · outcome card · armed/menu · board |
| mouse | `HandleClick`, 1319–1453 | interlude(shop) · armed · spell rows · slot rows · attack rows · buttons · close-menu · overlay · board |
| pixels | `_Draw`, 1750–1913 | board · tokens · chrome · menus · notice · outcome card · hint · quit card |

Nothing makes them agree. They agree today because three people read the file carefully.

### 1.2 "Which menu is open" is answered five times

`ClearPending` (1308–1317), the inline clear inside `Invoke`'s `Attacks` case (661), the
inline clear at the tail of `HandleClick` (1439–1443), `OpenMenuLength` (812–816), and
`NothingLeftButEndTurn` (1222–1228). Five independent expressions over the same three
booleans, each of which must be updated when a fourth menu appears.

### 1.3 The six edit sites, named

Adding one modal to `PlayMode` today requires six edits, none of which the compiler,
a test, or the probe will demand:

1. a branch in the Esc cascade (`_UnhandledInput`, 1013–1042);
2. a term in the board-keyboard gate (`&& !_shopView`, 1054);
3. a branch in `HandleClick`, in the right position (1319);
4. a draw call in `_Draw`, at the right z (1750);
5. a clear in `ClearPending` or one of the two inline clears (1308, 661, 1439);
6. a term in `OpenMenuLength` / `NothingLeftButEndTurn` (812, 1222).

**That is the defect.** Not "the file is long" — *the compiler cannot help you.*

### 1.4 The ten fields that are really one thing

```
_spellMenuOpen  _attackMenuOpen  _slotMenuOpen          (143–145)
_pending  _pendingSpell  _pendingAttack  _pendingSlot   (146–149)
_outcomeCard (86)   _shopView (183)   _shopNotice (184)   _quitAsked (154)
```

Eleven fields encoding one fact — *what has the player's attention, and what did they
half-start on the way here* — as a set of independent booleans whose legal combinations
are written nowhere. `Pending` (the private enum at 46–64) is additionally a
character-for-character duplicate of `SRDCombat.Game`'s `TargetKind` plus a `Nothing`
member; `Pending.AttackTarget` ↔ `TargetKind.Attack`, and so on for all five.

### 1.5 `CreateMode : FightScreen`

`CreateMode` inherits a 2,432-line base class built for drawing a battlefield, and uses
from it: `TextFont` (29 sites), the palette colours, `ScreenWidth`/`ScreenHeight`,
`Trim` (3 sites), `Title`, `OnReady`, `LoadContent`, `ArgumentValue`. It uses **none** of
the board, camera, animation-act, token or sprite machinery — it draws no sprite at all
(`grep` for `Sprite|Texture|DrawCircle` in `CreateMode.cs`: zero hits). It nonetheless
inherits `FightScreen._Ready`, which calls `SpriteLibrary.Load()` — the whole sprite
corpus loaded to draw a party-creation menu.

---

## 2. The constraint that shapes the answer: nesting is already specified

Verified against `docs/2026-08-26-battle-builder-design.md` rather than taken on trust.

- §9 of that document gates **all three** of its UI slices (#482, #483, #484) on this
  issue, and states the reason: *"A builder needs nested focus (library → edit this
  scenario → edit one character). A builder that hand-rolls its own cascade leaves the
  project with two hand-rolled modal stacks instead of one."*
- #482's acceptance criterion 2 is stronger than a preference: *"Focus and nesting go
  through the structure #327 lands. A newly hand-rolled Esc cascade in this screen is a
  review failure, not a style note."*
- #483 additionally needs `CreateMode.Keep()`'s hard-coded
  `new PlayMode { CreatedDrafts = … }` handoff replaced by a completion callback, and the
  literal party size `4` parameterized (#483 cites `CreateMode.cs:472-474`; measured at
  `7bd7c3a` the handoff is `Keep()` at 461–478, the literal at 470, the caption at 530).

So nesting is a requirement with a filed consumer, and the `CreateMode` decoupling has a
second customer beyond "the name lies". **The structure below is a stack, not a flag.**

`PlayMode` itself already nests three deep today — Board → SpellMenu → SlotMenu →
Targeting — so this is not a future-proofing argument. It is a present one.

---

## 3. The structure

Four plain, Godot-free types in `client/Ui/`, plus one record hierarchy per screen.
Everything below is `internal`; `client/AssemblyInfo.cs` already carries
`[assembly: InternalsVisibleTo("SRDCombat.Viewer.Tests")]` (landed by #190 — see
[§6](#6-what-190-makes-reachable-measured-not-assumed)).

### 3.1 `PlayFocus` — the five questions a modal must answer

```csharp
/// One place the play screen's attention can be.
internal abstract record PlayFocus
{
    /// What Esc means while this is on top.
    internal abstract EscapeMeaning Escape { get; }

    /// Whether Up/Down/Enter belong to this layer's rows rather than to the board.
    internal abstract bool TakesRowKeys { get; }

    /// Whether an action hotkey is suppressed while this is on top.
    internal abstract bool SuppressesHotkeys { get; }

    /// Whether the board beneath answers no keyboard input at all while this is on top.
    internal abstract bool SuppressesBoard { get; }

    /// Whether this is a choice in progress that holds the turn open.
    internal abstract bool HoldsTurnOpen { get; }
}
```

Those five are not invented. **Each one is a hand-written expression that exists in
`PlayMode.cs` today**, and the mapping is exact:

| member | the expression it replaces | today at |
| --- | --- | --- |
| `Escape` | the Esc cascade's branch order | 1013–1042 |
| `TakesRowKeys` | `OpenMenuLength is > 0` | 812, 1107 |
| `SuppressesHotkeys` | `if (_pending == Pending.Nothing)` | 1141 |
| `SuppressesBoard` | `&& !_shopView` | 1054 |
| `HoldsTurnOpen` | `NothingLeftButEndTurn`'s four-flag conjunction | 1222–1228 |

Because they are **abstract members on an abstract record**, a new modal that fails to
answer one does not compile. That is the property the six edit sites lack. The sixth edit
site — the draw call — is answered by [§3.4](#34-drawing-comes-off-the-stack).

The concrete set, one per focus that exists today:

```csharp
internal sealed record Board                                       : PlayFocus;  // the stack's root
internal sealed record AttackMenu                                  : PlayFocus;
internal sealed record SpellMenu                                   : PlayFocus;
internal sealed record SlotMenu(SpellDefinition Spell)             : PlayFocus;
internal sealed record Targeting(TargetKind Kind,
                                 CombatAttack? Attack,
                                 SpellDefinition? Spell,
                                 int? Slot)                        : PlayFocus;
internal sealed record Shop(string? Notice)                        : PlayFocus;
internal sealed record Outcome                                     : PlayFocus;
internal sealed record QuitConfirm                                 : PlayFocus;
```

`Targeting` carries what `_pending`, `_pendingSpell`, `_pendingAttack` and `_pendingSlot`
carry today, so **popping the layer is what clears them** — the "did you remember to null
it" class disappears rather than being patched. `Pending` is deleted outright: its five
members are `TargetKind`'s five, and `Pending.Nothing` becomes "no `Targeting` on the
stack".

### 3.2 `FocusStack<T>` — the shared, tested collection

```csharp
internal sealed class FocusStack<TFocus> where TFocus : notnull
{
    internal FocusStack(TFocus root);
    internal TFocus Root { get; }
    internal TFocus Top { get; }
    internal int Depth { get; }              // 1 when only the root is present
    internal IReadOnlyList<TFocus> BottomUp { get; }   // draw order
    internal void Push(TFocus focus);
    internal TFocus Pop();                   // refuses to pop the root
    internal void PopToRoot();
    internal void ReplaceTop(TFocus focus);  // e.g. the shop's notice changing
    internal bool Holds<T>() where T : TFocus;
}
```

Generic over the screen's own focus type, with no constraint beyond `notnull`, **no
interface and no registry**. The builder (#482–#484) declares its own
`BuilderFocus` hierarchy and reuses this collection; it does not inherit `PlayFocus` and
`PlayMode` does not learn about the builder. That is the whole of the "designed for
nesting" claim, and it is one type, not a framework.

The root is never popped, so `Top` is total and there is no empty state to guard.

### 3.3 `PlayFocusRouter` — the decision, as a value

This is the part that makes the refactor worth doing rather than tidy.

```csharp
internal static Route Route(FocusStack<PlayFocus> focus, ClientInput input, RouteContext context);
```

- `ClientInput` is a **client-owned** value: `readonly record struct ClientInput(
  ClientInputKind Kind, ClientKey Key, char Character, float X, float Y)`. It is *not*
  `Godot.InputEvent`. See [§6](#6-what-190-makes-reachable-measured-not-assumed) — a test
  that constructs an `InputEventKey` does not throw, it **terminates the test host
  process** and takes every unrelated test in the assembly with it.
- `RouteContext` is a small struct of the facts the decision needs from the live screen:
  `Phase Phase`, `bool ActInProgress`, `bool HasCommandedCombatant`, `int MenuRowCount`,
  `bool AttacksOffered`, `bool HasCursor`.
- `Route` is a value naming what to do — `PopToRoot`, `CommitOutcome`, `LeaveTheGame`,
  `DismissQuitCard`, `MoveMenuIndex(delta)`, `TakeMenuRow`, `CycleTarget`, `ArmAttack`,
  `MoveCursor(dx, dy)`, `ActivateCursor`, `RunHotkey(char)`, `ClearHint`, `Ignore`, … —
  one member per branch that exists in `_UnhandledInput` today.

`PlayMode._UnhandledInput` becomes **translate → route → execute**. The node keeps the
Godot event translation and the effects; the *order* and the *decision* become a pure
function a test can drive with no display, no Godot binary, and no `PlayMode` instance.

This is the same division the project already keeps between the clients and the engine —
*"the clients hold no rules… they call the engine's public actions"* — applied one level
down: **the node holds no priority order.** It is also the structural answer #490 asks
for on a neighbouring path (*"the decision… can live in* `SRDCombat.Game` *…leaving the
client with nothing but 'call it, show what comes back'"*); here the decision is genuinely
presentation, so it stays in `client/` — but it stops living inside a `Node2D`.

### 3.4 Drawing comes off the stack

**Corrected 2026-08-28, after S5 shipped and review knocked the original claim out.** This
section used to say `_Draw` iterates `_focus.BottomUp` for the card-drawing layers and that
"z-order and input priority stop being two lists". The first half was built; the second half
was never true, and the loop that appeared to deliver it was inert.

What S5 actually lands: `_Draw` asks the stack **which card is eligible** — a switch on
`_focus.Top` for the three row menus, `Holds<Outcome>` for the outcome card — replacing four
hand-written conditions. That is the third copy of the modal order, and it is gone.

What it does **not** land, and why the loop was removed: **no two of these cards can be on
screen in the same frame**, so traversal order is unobservable. A row menu draws only while
it is `_focus.Top`, so at most one of three; the outcome card exists only once the fight is
complete, which is exactly when `CommandedCombatant()` returns null and every menu case is
dead. Review proved it by reversing the traversal — every probe capture stayed
byte-identical. A loop whose order provably cannot matter is a mechanism that looks like it
decides something and does not, which is the failure shape this project keeps catching, so
it was replaced by the switch rather than shipped with a comment excusing it.

Two cards genuinely can coexist — Esc during the closing animation leaves `QuitConfirm` open
and `_Process` pushes `Outcome` above it — and that pair's order stays hand-written, by name,
for the reason below. **When a second pair of stack-traversed cards can coexist, the
`BottomUp` loop earns its place; until then `FocusStack.BottomUp` is stack order and nothing
promises it is draw order.**

`Targeting` draws no card (it changes how the board draws, which the board already does by
asking the stack).

**One documented exception: the quit card is still drawn last, by name.** It sits above
even the pointer's hint, because a tooltip must never occlude the question that closes the
game — and today it does sit there (`_Draw` 1907–1912 draws the outcome card, then the hint, then the quit card). Making it a layer would put it
under the hint and change a pixel. The exception is one line with a reason, which is
cheaper than a sixth trait for one case.

**Addendum, S5's implementation (#504, two review rounds with qc/architect):
clearing and drawing are separate lifecycles, and the first two attempts at this section
missed it.** The obvious reading of "iterate `_focus.BottomUp` for the card-drawing
layers" is to visit only the layers actually on the stack and call each one's draw method.
That is unsafe here, for a reason specific to this screen: `DrawSpellMenu`,
`DrawAttackMenu` and `DrawSlotMenu` each own a row list (`_spellRows` etc.) that `HitTest`
reads unconditionally every frame, and a menu that was just popped is no longer in
`_focus.BottomUp` at all — a traversal keyed on presence would stop clearing its list at
exactly the moment clearing it matters, leaving a stale, invisible rectangle a click could
still land on. The fix is to decouple the two questions: a `ClearMenuRows()` step empties
every row list unconditionally, before the traversal runs at all, regardless of what the
stack holds; only then does the traversal decide whether one list gets repopulated. This
is a *stronger* form of the invariant than either the pre-#504 code or #504's first two
attempts gave it, not merely an equivalent restatement.

The second thing both earlier attempts got wrong: `DrawOutcomeCard` cannot share a
`commanded is { } character` guard with the row menus, because `CommandedCombatant()`
requires `_encounter is { IsComplete: false }` — `commanded` is provably null in every
single frame the outcome card exists. The traversal therefore lives *outside* that guard,
with `commanded` required only inside the row-menu cases themselves (`when … && commanded
is { } character`); `Outcome`'s case carries no such requirement and no
`ReferenceEquals(layer, _focus.Top)` guard either, because nothing is ever pushed above it
(every `Push` site was checked) — the traversal reaching that layer at all is the whole
answer. Getting both of these wrong looks identical to getting them right until the
specific frame that exercises them (a menu closed via Esc rather than superseded, a fight
ending with nobody commanded) — which is exactly why this is written down here rather than
left for the next reader to rediscover by breaking it.

### 3.5 What `Phase` does *not* become

`Phase` (`Fighting` / `Interlude` / `RunOver`) stays exactly as it is and is **not** folded
into the stack. A phase is which content the screen is showing; a focus is what has
attention on top of it. Folding them would make `RunOver` a "modal", would rewrite
`EnterInterlude`, `HandleFightEnd` and `CompleteAndReport`, and would turn a bounded
refactor into the run-loop rewrite this document exists to avoid. The stack's root is
`Board` in every phase; the phase decides what the root draws.

---

## 4. Behaviour preservation: the finding that changes the issue's framing

> **A real stack is not behaviour-preserving by default, and the issue's own wording
> ("a real stack or state object") hides that.**

Today, Esc from the slot menu calls `ClearPending()` (1308–1317), which clears *all three*
menu flags and all four pending fields at once. The player lands on the board. A stack
whose Esc pops one layer would land them back on the **spell menu** — a different game,
arrived at by taking "make it a stack" literally.

So the landing configuration is stated as a table, and it is the specification every
slice's tests assert:

| focus | `Escape` | `TakesRowKeys` | `SuppressesHotkeys` | `SuppressesBoard` | `HoldsTurnOpen` |
| --- | --- | --- | --- | --- | --- |
| `Board` | `AskToQuit` | no | no | no | no |
| `AttackMenu` | **`DropToBoard`** | yes | no | no | yes |
| `SpellMenu` | **`DropToBoard`** | yes | no | no | yes |
| `SlotMenu` | **`DropToBoard`** | yes | no | no | yes |
| `Targeting` | **`DropToBoard`** | no | **yes** | no | yes |
| `Shop` | `CloseSelf` | no | no | **yes** | no |
| `Outcome` | **`Commit`** | no | no | no | no |
| `QuitConfirm` | `LeaveTheGame` | no | no | no | yes |

Two rows deserve their reason written down, because both are places where "obvious" is
wrong:

- **`DropToBoard`, not `PopOne`.** `EscapeMeaning.DropToBoard` is `PopToRoot`. It
  reproduces `ClearPending` exactly. Whether Esc *should* step back one level instead is a
  real design question with a defensible answer either way — and it is **not this
  refactor's to answer.** Filed separately for `designer`; the refactor lands the
  behaviour that exists.
- **`Commit`, not a cancel.** Esc on the outcome card calls `CompleteAndReport` (1027) —
  it *advances* the run. Esc is not uniformly "back out" in this client, and a generic
  "Esc pops the stack" structure would silently turn an acknowledgement into a dismissal.

### 4.1 One preserved oddity and one subsequently resolved decision

Both were found while reading and deliberately left unchanged by the structural refactor.
The shop guard remains preserved history; Brandon subsequently resolved the quit-confirm
behaviour on #510:

1. **`!_shopView` gates the fighting-phase keyboard (1054), but the shop only opens during
   `Phase.Interlude`** — the term is unreachable defence. `Shop.SuppressesBoard = true`
   preserves it verbatim.
2. **The auto-end-turn is held by the quit confirm.** `QuitConfirm.HoldsTurnOpen = true`
   makes the established `NothingLeftButEndTurn` conjunction leave the current turn alone
   while the "LEAVE THE GAME?" decision is up. The card may dismiss or confirm leaving,
   but the fight cannot advance underneath it (#510).

---

## 5. What actually proves "no behaviour change"

The issue asks for "the probe still passes and its captures are unchanged". Two things had
to be checked before that criterion could be relied on. Both were checked tonight, on
`7bd7c3a`, and one of them fails.

### 5.1 Captures are byte-reproducible — measured, and they are

Two complete `--seed=1 --probe` runs to termination, same build, same display, compared
with `cmp`:

```
SAME play-1-turn-ready.png     SAME play-2-refused.png    SAME play-2b-hint.png
SAME play-2c-tab-armed.png     SAME play-3-moved.png      SAME play-4-attacked.png
SAME play-6-turn-ended.png     SAME play-7-spell-menu.png SAME run-0-interlude.png
SAME run-9-after-fight.png
```

**Ten of ten byte-identical.** Real-time pacing, an animation queue and a hover clock
measured in wall-clock seconds all sit between the input and the shutter, so this was not
safe to assume. **The criterion is usable**, and it is the strongest evidence available to
this refactor.

**It costs 15 minutes a run.** The two runs took 15m30s and 15m08s. Almost all of it is the
play-out loop at the end (`while (_phase == Phase.Fighting && safety < 5000)`, 2578–2591):
5,000 frames at the ~5 fps the client manages while `RefreshAfterAction` runs 504
pathfinds, full-board LOS and a fog upload after every action by anyone. So **a
before-and-after capture pair costs about half an hour of wall clock per slice** — nine
slices, twice each. S0 should decide whether to shorten it; it is not a reason to skip it.

**Operational notes for whoever executes S0, all measured tonight, none of them in any
document today:**

- The probe needs `--display-driver x11` and a reachable `DISPLAY`. **`:1`, as named in
  CLAUDE.md's Environment section at the time, was not reachable on this machine tonight;
  `:0` was.** (Resolved 2026-09-02: the live display moves between the two, so CLAUDE.md
  no longer names one — the `probe-diff` skill's `find-display.sh` detects it.)
- **Two of the probe's captures are never produced, and the probe does not say so.**
  `play-5-feature.png` is skipped when the commanded character has no feature button;
  `play-8-cast.png` is skipped when `_spellRows` is empty. Neither skip is reported —
  both were absent from every run tonight, and nothing distinguishes "the step passed"
  from "the step never happened". **A capture set that can silently shrink is a weak
  gate**, which is a second reason S0 exists.

### 5.2 The probe does not visit the states this refactor moves — measured, and it does not

`RunProbe` (2487–2595) visits: the opening interlude, a commanded turn, a deliberate
refusal, a hint, Tab-arming and Esc-cancel, a move, an attack, a feature, End Turn, the
spell menu, a cast, and the fight's end.

It **never** visits:

| state | why the probe misses it |
| --- | --- |
| `Shop` | reached only from an interlude at a Long Rest; the probe clicks Continue |
| `QuitConfirm` | the probe never presses Esc from a cold board |
| `SlotMenu` | needs a caster with more than one slot level; the probe takes row 0 |
| `AttackMenu` | needs a character with more than one attack; one attack arms directly |
| `Outcome` card | dismissed by the play-out loop's clicks before any capture |

**So the acceptance criterion is blind exactly where the refactor is riskiest.** Four of
the eight focuses in §4's table have no capture behind them at all. This is why the first
slice changes no production code.

### 5.3 The conformance table is the test, and it lands with the extraction

You cannot unit-test `_UnhandledInput` before extracting it: it is an instance method on a
`Node2D`, and a `Node2D` cannot be constructed in a test host (§6). The safety net before
extraction is therefore the probe; the safety net *after* extraction is §4's table,
asserted row by row against `PlayFocusRouter` — 8 focuses × 5 traits, plus one test per
`Route` branch. Those tests are read off the current code, which is the oracle, and they
land in the same PR as the extraction. That is #327's fourth acceptance criterion
satisfied concretely rather than by a single token test.

Add one reflection test that enumerates every `PlayFocus` subtype in the assembly and
asserts the router has a case for it — so a future engineer who adds a subtype and a lazy
`_ =>` arm gets a red test rather than a silent default.

---

## 6. What #190 makes reachable — measured, not assumed

Answers from the `#190` architect, measured on `test/190-client-test-project`, treated here
as firm:

- `tests/SRDCombat.Viewer.Tests` is a plain `Microsoft.NET.Sdk` xUnit project with a direct
  `ProjectReference` to `client/SRDCombat.Viewer.csproj`. It builds and runs with **no
  Godot binary and no display**. **Every slice below names that project. Do not create a
  second one.**
- `client/AssemblyInfo.cs` carries `[assembly: InternalsVisibleTo("SRDCombat.Viewer.Tests")]`.
  **`internal` is the access level every seam in this design uses; nothing becomes
  `public`.**
- A plain class or record in `client/` deriving from nothing Godot is fully constructible
  in a test. A `Node`/`Node2D`-derived type **cannot be instantiated**, though its managed
  statics can be called.
- `Color`, `Vector2`, `Vector2I`, `Rect2`, `Rect2I` and the Godot enums (`Key` included)
  are ordinary managed structs and are safe at a test boundary.
- **Anything deriving from `GodotObject`/`RefCounted` — including `InputEventKey` and
  `InputEventMouseButton` — kills the test host process when constructed.** Not an
  exception; an un-catchable abort that fails every other test in the assembly with a
  misleading message.

That last point is the reason `ClientInput` exists (§3.3) and is not negotiable.

**One gotcha inherited verbatim:** referencing the viewer pulls GodotSharp's source
generators in transitively; `ScriptPathAttributeGenerator` fails without `GodotProjectDir`
and `GodotPluginsInitializerGenerator` emits a `Main` that collides with the client's own
(CS0436, an error under `TreatWarningsAsErrors`). #190's csproj removes them with a
`BeforeTargets="CoreCompile"` target that drops `Godot.SourceGenerators` from
`@(Analyzer)`. Since no second project is created here, this is inherited rather than
rediscovered.

**What #190's first PR does *not* close:** the `--spawn`/`--level` refusal wiring in
`PlayMode.OnReady` (222–229) and `FightScreen.ResolveFight` remains pinned by nothing —
#490's knockout stands. **No slice below touches that code**, and S7's argument-helper move
is verbatim for exactly that reason.

---

## 7. Alternatives rejected

**A. Extract `_UnhandledInput` into an input router and stop.** The obvious move, and the
one the issue's title invites. Rejected on the measurement in §1: the method shrank 233 →
207 over five weeks while the state it coordinates grew 39 → 48. Routing without owning the
state leaves the other two copies of the priority order (§1.1) and all five "which menu is
open" expressions (§1.2) alive, and the next nine fields arrive anyway.

**B. Godot `Control` nodes with the engine's own focus system** (`Popup`, `AcceptDialog`,
`grab_focus`). The idiomatic Godot answer, and a reviewer will ask. Rejected on three
counts: the entire client is immediate-mode `_Draw` with hand-composed layout, so this is a
rewrite of `FightScreen`'s 2,432 lines too; `Control` needs the runtime, so it moves focus
logic *out* of test reach exactly when #190 has just brought a test project into
existence; and every probe capture changes, destroying the only proof mechanism available
(§5.1).

**C. Child scene nodes per modal, using Godot's reverse-tree input propagation.** Tempting,
because Godot's unhandled-input propagation order genuinely *is* a stack. Rejected: it
makes focus order an implicit property of scene-tree construction, which nothing headless
can inspect, and "which modal is open" becomes a tree query. The probe would still work;
no test would.

**D. A flat `Focus` enum field, no stack.** Rejected: `PlayMode` already nests three deep
(Board → SpellMenu → SlotMenu → Targeting), and the builder's three-deep nesting is filed
with acceptance criteria (§2). A flat field would have to be rebuilt by #482.

**E. An explicit transition table (each state names its legal successors).** Rejected as
speculative abstraction under CLAUDE.md's rule. The transitions here are pushes and pops;
enumerating legal pairs is a maintenance surface with no bug behind it.

**F. A full view/model split of `PlayMode`.** Rejected as the grand rewrite. It would
destabilise the client for weeks, which is precisely the outcome that would betray the
reason Brandon asked for this.

**G. A new Godot-free `client/SRDCombat.Viewer.Ui` csproj to guarantee testability.** This
was the design's fallback if a test project could not reference the viewer. #190 measured
that it can (§6), so the extra project buys nothing and costs a solution entry, a CI leg
and a second place client code lives. Rejected on evidence.

---

## 8. What this costs, stated plainly

- **Total client line count goes up slightly, not down.** S1 adds roughly 250 lines of new
  types in `client/Ui/` and removes rather less than that from `PlayMode`. The win is in
  §1.3's six edit sites and §1.1's three copies, not in a smaller repository. Anyone
  measuring this refactor by total lines will conclude it failed.
- **`PlayMode.cs` ends around 2,300–2,400 lines** after S1–S6, down from 2,674, with 48
  fields becoming **36** and one enum deleted. S8 then splits that file by concern into
  five partials of 300–700 lines each. S8 is **navigational, not structural** — labelled
  that way in its issue so nobody mistakes it for the win.
- **Nine PRs.** Each is small; the chain is long. S7 runs in parallel and S0 gates only
  S1–S5, so the critical path is shorter than the count suggests.

---

## 9. Coordination with work in flight

| in flight | file overlap | ruling |
| --- | --- | --- |
| **#190** (`test/190-client-test-project`) | `client/AssemblyInfo.cs`, `client/SpriteLibrary.cs`, `client/FightScreen.cs` (adds `internal static ScaleFor`) | **S1 starts after #190 merges.** Every slice names `tests/SRDCombat.Viewer.Tests`. No conflict: #190 touches `FightScreen`'s sprite scaling, S7 touches its chrome. |
| **#486** (`client/WatchMode.cs`, possibly `client/Main.cs`) | may hoist a refusal check into `Main._Ready`, where mode selection lives | **S7 starts after #486 merges** — S7 must move `HasArgument`/`ArgumentValue` off `FightScreen`, and `Main` is a caller. S0–S6 do not touch `Main` or `WatchMode`. |
| **#485 / PR #496** (CI workflow only) | none | no interaction. |
| **#490 / #491** (flag decision moves to `SRDCombat.Game`) | `PlayMode.OnReady` 222–229, `FightScreen.ResolveFight` | **No slice touches these.** S7 moves the argument *helpers* verbatim with forwarders left behind, so #490's eventual fix applies unchanged. |
| **#482–#484** (builder UI) | none yet | gated on this issue — see §11. |

Designing rather than editing meant none of these bound. That itself is the finding: the
overlap is real and would have bitten an implementation started tonight.

---

## 10. Slices and sequencing

One concern, one branch, one PR. **Every slice ends with a client that builds, plays and
ships** — there is no intermediate state where the client is half-migrated across a merge,
because each slice converts one field group completely.

| # | Slice | Issue | Blocked on | Pinned by |
| --- | --- | --- | --- | --- |
| S0 | Probe visits every modal; a two-run capture-diff script | #499 | — | itself (no production change) |
| S1 | `FocusStack<T>`, `PlayFocus`, `PlayFocusRouter`; the menu + targeting cluster becomes the stack | #500 | S0, #190 | S0's captures + §4's table as router tests |
| S2 | Shop and the outcome card become layers | #501 | S1 | S0's new shop/outcome captures |
| S3 | The quit confirm becomes a layer | #502 | S2 | S0's new quit-card capture |
| S4 | The click pipeline moves into the router | #503 | S3 | router tests + all captures |
| S5 | `_Draw` takes its order from the stack | #504 | S4 | all captures |
| S6 | One row list behind the three menus; `_menuIndex` moves onto the layer | #505 | S5 | S0's slot/attack-menu captures |
| S7 | `CreateMode` stops inheriting `FightScreen`; completion callback; party size | #506 | #486 | the `--create --probe` captures |
| S8 | `PlayMode.cs` splits into partials by concern | #508 | S6 | compiler + all captures |

The two judgement calls originally reserved in [§12](#12-judgement-calls) were
filed as **#509** (should Esc step back one level?) and **#510** (the auto-end-turn under
the quit confirm). Neither blocked a structural slice. Brandon settled both after the
structure landed: #509 makes Esc step back one layer, and #510 makes the quit confirmation
hold the current turn open so combat cannot advance beneath the modal decision.

**Critical path:** S0 → S1 → S2 → S3 → S4 → S5 → S6 → S8. **S7 is parallel** and may land
at any point after #486.

### S0 — Make the probe cover what the refactor moves

**No production code changes.** Extends `RunProbe` to visit the shop, the quit confirm, the
slot menu, the attack menu and the outcome card, and adds `scripts/probe-diff.sh` running
the probe twice into two directories and `cmp`-ing the results, so "captures unchanged" is
a command rather than a promise.

- The shop needs a Long Rest interlude with a purse; the slot and attack menus need a
  character with more than one slot level and more than one attack. **Choosing the seed
  (or seeds) that reach those states is the slice's real work** and belongs in the issue's
  acceptance criteria: the probe must *assert* it reached each state, not silently skip it
  the way `play-5-feature` and `play-8-cast` are skipped today.
- **Finds out why `run-9-after-fight.png` is never produced** (§5.1) and either fixes the
  play-out loop or states, in the README, what the probe does not reach. A capture the
  criterion names and the tool never writes is worse than no criterion.
- **Makes a skipped step loud.** `play-5-feature.png` and `play-8-cast.png` are skipped in
  silence today; the probe should report which steps it reached, so a capture set can be
  compared for completeness and not only for pixels.
- Documents the `--display-driver x11` requirement and the run time found in §5.1, and
  corrects CLAUDE.md's `DISPLAY=:1` claim if it is stale on Brandon's machine (ask; do not
  assume). *Done 2026-09-02, the other way round: the claim was retired rather than
  corrected, because the display moves; `find-display.sh` in the `probe-diff` skill
  replaces it.*

**Safe without client tests: yes.** It adds observations and changes no behaviour.

### S1 — The stack, the router, and the menu/targeting cluster

New: `client/Ui/FocusStack.cs`, `client/Ui/EscapeMeaning.cs`, `client/Ui/ClientInput.cs`,
`client/PlayFocus.cs`, `client/PlayFocusRouter.cs`.

Removed from `PlayMode`: `_spellMenuOpen`, `_attackMenuOpen`, `_slotMenuOpen`, `_pending`,
`_pendingSpell`, `_pendingAttack`, `_pendingSlot`, and the `Pending` enum. `ClearPending`
becomes `_focus.PopToRoot()`; `OpenMenuLength` and `NothingLeftButEndTurn` read the stack.
`_UnhandledInput`'s keyboard half becomes translate → route → execute. `HandleClick` reads
the stack but **keeps its hand-written order** (that is S4).

These seven fields move together because they are one fact. Splitting them further would
mean two representations of "which menu is open" coexisting inside one file — the exact
defect being removed.

**Safe without client tests: no, and this is the slice that most needs saying so.** It is
safe only behind S0's extended captures *plus* its own router tests. It must not start
before both S0 and #190 have merged.

### S2 — Shop and outcome card become layers

Removes `_shopView`, `_shopNotice`, `_outcomeCard`. The shop's notice rides the layer, so
Esc clearing it is the pop rather than a second assignment. `Shop.SuppressesBoard = true`
preserves the unreachable `!_shopView` term verbatim (§4.1).

**Safe without client tests: only behind S0's new shop and outcome captures.**

### S3 — The quit confirm becomes a layer

Removes `_quitAsked`. The preempt block (996–1011) becomes "the top of the stack is asked
first", which it already is. The quit card stays drawn last by name (§3.4).

Highest-consequence input path in the client — an accidental exit costs a fight — so it
gets its own PR rather than riding S2.

**Safe without client tests: only behind S0's quit-card capture**, and the PR body must
show that Esc-Esc still quits and Esc-any-other-key still stays.

### S4 — The click pipeline moves into the router

The node computes what the pixel hit — a `ClickHit(HitKind, int Row, int Button,
GridPosition? Square, bool OverOverlay)` — and the router decides. Rect hit-testing stays in
the node (it is layout, not decision); the *order* becomes one copy.

The order is preserved exactly, including its two non-obvious steps: the button row is
checked **after** menu rows and **before** the close-menu fallback, and a click on the grid
with a menu open **closes the menu and does not act on the square** (1439–1447).

**Safe without client tests: no — router tests for every step of the pipeline are the
slice's deliverable.**

### S5 — `_Draw` takes its order from the stack

`_Draw` iterates `_focus.BottomUp`. The third copy of the priority order dies. The quit
card's exception is documented in code, not just here.

**The pixel proof is the whole gate**: every capture byte-identical, all eight focuses
covered by S0.

### S6 — One row list

`_spellRows` / `_attackRows` / `_slotRows` become one `_menuRows` of
`(Rect2 Rect, Action Take)`; `_menuIndex` moves onto the menu layer, so a freshly pushed
menu starts at row 0 by construction and the three `_menuIndex = 0` assignments go.

**Row *routing* unifies; row *drawing* does not.** `DrawSpellMenu`, `DrawAttackMenu` and
`DrawSlotMenu` produce different pixels and stay three methods that fill one list. Merging
the drawing would change captures.

### S7 — `CreateMode` stops inheriting `FightScreen`

Runs in parallel with S1–S6; touches none of the same code.

- New `client/Ui/Palette.cs` (the colours), `client/Ui/Chrome.cs` (the font, `Trim`,
  the heading), `client/ClientArguments.cs` (`HasArgument`/`ArgumentValue` moved
  **verbatim**, with `FightScreen` keeping thin forwarders so no other call site changes),
  `client/ClientContent.cs` (`LoadContent`).
- `CreateMode : Node2D`, composing `Chrome`. It keeps `TextureFilter = Nearest`,
  `GetWindow().MinSize`, and the `SizeChanged` subscription — all three arrive from
  `FightScreen._Ready` today and all three matter.
- It stops calling `SpriteLibrary.Load()`, which it never used. **State this in the PR
  body as the one deliberate behaviour difference**: no pixel changes, party creation
  starts faster.
- `Keep()` gains a completion callback in place of constructing `PlayMode` itself, and the
  party size becomes a parameter defaulting to 4 (`CreateMode.cs:461-478`, the literal at 470 and the caption at 530). Both are #483's asks and belong here, not after.

**Safe without client tests: yes, behind the `--create --probe` captures**, which already
exist and already drive the creation screen through one class of each menu shape.

### S8 — `PlayMode.cs` splits into partials

`PlayMode.cs` (state and lifecycle), `PlayMode.Input.cs`, `PlayMode.Draw.cs`,
`PlayMode.Run.cs` (interlude, shop, fight end, save), `PlayMode.Probe.cs`. **Pure file
moves; a diff with zero logic changes.** Navigational, not structural — this is the slice
that answers "monolith" directly and the one that proves the least.

---

## 11. Phase placement — a recommendation for the steward

#327 is labelled `phase:F3-run-game` as F3's **entry gate**. Three findings bear on that,
and all three are the steward's call, not the architect's:

1. **The gate can be satisfied before the issue closes.** #482–#484 need a focus structure
   to build on and a reusable `CreateMode`. That is **S1–S4 and S7** — five of nine slices.
   S5, S6 and S8 are cleanup that no consumer waits on. Recommendation: the gate should
   name the **deliverable** ("the focus structure and the `CreateMode` decoupling have
   landed"), not the issue number, so the builder UI and F3's modals unblock at S4/S7
   rather than at S8.
2. **The gate's wording is already known to be wrong** (#492 — "any new *modal* surface"
   misses a new top-level screen). Fixing the wording and fixing the "issue number vs
   deliverable" problem are the same edit; recommend doing them together.
3. **Four `phase:FI-instrument` slices now gate on an `phase:F3-run-game` issue.** FI is
   running now; F3 has not started. Recommendation: **the slice issues below should carry
   no phase label of their own beyond `phase:F3-run-game` inherited from #327, and S0/S1
   should be scheduled now, alongside FI**, since the builder's UI slices are the nearest
   consumer and Brandon has asked for this work directly. If the steward prefers, S0 alone
   is defensible under `phase:F5-confidence` — it is pure test coverage.

---

## 12. Judgement calls

The first two questions below were deliberately left open during the behaviour-preserving
refactor, then settled by Brandon on their own issues. Their original reasoning remains
here, followed by the current rule.

1. **Esc steps back one level instead of dropping to the board (#509).** §4 landed the
   old flat-clear because the refactor was behaviour-preserving. Brandon subsequently
   settled the game behaviour: Esc closes one layer until the board is reached.
2. **Auto-end-turn is suppressed while the quit confirm is up (#510).** §4.1 preserved
   the old behaviour long enough to make it a separate decision. Brandon subsequently
   settled that the confirmation is modal: `QuitConfirm.HoldsTurnOpen` participates in
   the production `PlayTurnFlow.NothingLeftButEndTurn` decision, so the turn cannot
   advance until the player dismisses the card or confirms leaving.
3. **How far does `FightScreen` itself want splitting?** 2,432 lines with camera, sprites,
   animation acts, board drawing, content loading and fight resolution in one class. S7
   takes the chrome out because `CreateMode` needs it; the rest is out of scope here and
   should be re-read after S8, not designed now.
4. **Does the probe want a deterministic clock?** §5.1 measured captures byte-identical
   with the real clock, so no change is needed. If that ever stops holding, the answer is a
   frozen clock in probe mode, not a looser comparison.
