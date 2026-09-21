# Eyes On Paper — Build Plan

Companion to [SCOPE.md](SCOPE.md). This is the **order of work** for a two-person team.

| Person | Track | Works in |
|---|---|---|
| **Orlando** | Player: camera, phone, combos, messages, answer sheet, results | `Scenes/Test_Player.unity` |
| **Teammate** | Invigilator: room greybox, NavMesh, vision cone, suspicion, game manager | `Scenes/Main.unity` (**owner**) |

> The biggest risk is not the work, it's the **handshake** between the two halves. Agree the contract in Step 0 and neither of you waits on the other until the merge.

---

## Step 0 — Together (hour 0–1). Do this before splitting.

- [ ] Settle open decisions from SCOPE.md §9 (friend's name, title).
- [ ] Create folders:
  ```
  Assets/Project/Scripts/Core/
  Assets/Project/Scripts/Player/
  Assets/Project/Scripts/Invigilator/
  Assets/Project/Prefabs/
  Assets/Project/Scenes/
  ```
- [ ] Write and commit the contract (below). **Don't change it without telling each other.**
- [ ] Agree the PlayerRig contract:
  - Player root is tagged **`Player`**.
  - It has a child named **`Head`** (the camera). The vision cone raycasts to this.
  - That's all the invigilator ever needs from the player side.
- [ ] Scene ownership: **Teammate owns `Main.unity`** (NavMesh is baked on the room). Orlando works in `Test_Player.unity`.
- [ ] Commit + push. Then split.

### The contract

```csharp
// Assets/Project/Scripts/Core/GameEvents.cs
using System;

public static class GameEvents
{
    // Player → Invigilator
    public static event Action OnPhoneShown;
    public static event Action OnPhoneHidden;
    public static event Action OnBuzz;                      // phone vibrated (noise)
    public static event Action<int, int> OnAnswerRevealed;  // (questionIndex, choice 0-3)

    // Invigilator / GameManager → Player
    public static event Action OnCaught;
    public static event Action OnExamEnded;

    public static void PhoneShown()                 => OnPhoneShown?.Invoke();
    public static void PhoneHidden()                => OnPhoneHidden?.Invoke();
    public static void Buzz()                       => OnBuzz?.Invoke();
    public static void AnswerRevealed(int q, int c) => OnAnswerRevealed?.Invoke(q, c);
    public static void Caught()                     => OnCaught?.Invoke();
    public static void ExamEnded()                  => OnExamEnded?.Invoke();
}
```

Subscribe in `OnEnable`, unsubscribe in `OnDisable` — static events keep references across scene reloads otherwise.

---

## Parallel tracks (hour 1–16)

Each of you tests alone using **fake debug keys**. Neither waits on the other.

| # | **Orlando — Player** (`Test_Player.unity`) | **Teammate — Invigilator** (`Main.unity`) |
|---|---|---|
| 1 | **PlayerRig prefab:** chair, desk, clamped mouse-look camera | **Greybox room:** floor, walls, grid of cube desks. Bake NavMesh |
| 2 | **Phone hold/hide** on Space → fires `PhoneShown` / `PhoneHidden` | **Invigilator capsule** wandering to random NavMesh points |
| 3 | **ComboInput:** arrow sequence, length by difficulty, reset on hide | **Vision cone:** angle + distance + raycast to `Player/Head`. **Debug key P = fake phone out** |
| 4 | **QuestionData** ScriptableObject + 10 placeholder questions | **SuspicionMeter:** rise/decay, spike on `OnBuzz`, on-screen debug bar |
| 5 | **MessageQueue:** buzz on timer → locked until combo → 3 s reveal → gone. Fires `Buzz`, `AnswerRevealed` | **3 behaviour tiers:** wander → hover near player → circle behind |
| 6 | **AnswerSheet UI:** 10 × A–D, click to mark | **Caught:** at 100% fire `Caught()`, placeholder fail screen |
| 7 | **Debug key C = fake `Caught()`** to test player side reacting | **GameManager:** 5:00 timer, fires `ExamEnded()`, restart |

### Orlando — first 30 minutes after Step 0
1. Empty GameObject `PlayerRig`, tag `Player`, add chair + desk cubes.
2. Child `Head` with the Camera at seated eye height (~1.2 m).
3. `SeatedLook.cs`: mouse delta → yaw clamped ±70°, pitch clamped −40° to +30°, cursor locked.
4. Save scene, drag rig into `Prefabs/` → `PlayerRig.prefab`. Commit prefab + `.meta` + script. Push.

Getting the prefab in early lets the teammate drop a copy into `Main` and aim the vision cone at `Head` before the merge.

### Teammate — first 30 minutes after Step 0
1. In `Main.unity`: plane floor, 4 wall cubes, 4×5 grid of desk cubes.
2. Add **NavMeshSurface** (AI Navigation package) to the floor, mark desks as obstacles, bake.
3. Capsule `Invigilator` with **NavMeshAgent**; script picks a random point via `NavMesh.SamplePosition`, walks, pauses 1–3 s, repeats.
4. Save scene, commit, push.

---

## Merge point (hour ~16)

- [ ] Orlando pushes final `PlayerRig.prefab`.
- [ ] Teammate drags it into `Main.unity` at a desk, removes debug key P.
- [ ] Play the full loop together: buzz → combo → reveal → mark → invigilator reacts → caught / time out.
- [ ] Write down everything that feels wrong. That list is tomorrow's work.

If the merge takes more than an hour, the contract was broken somewhere — check `GameEvents` first.

---

## After the merge (hour 24+)

| Orlando | Teammate |
|---|---|
| **Results reveal** (player side holds the data: what Jack sent, what you wrote) | Mixamo invigilator model + walk / idle / look-around anims |
| Scribble-in-margin (Should) | Footsteps + heartbeat audio (fairness cue) |
| Title + how-to-play screens | Room art (Kenney / Quaternius), lighting bake |
| Buzz SFX, phone UI polish | Scripted "other student gets caught" moment |

**Shared, hour 64+:** itch page, GIF, screenshots, final WebGL build.

---

## Timeline recap

| Hours | Milestone |
|---|---|
| 0–1 | Step 0 done, contract committed |
| 1–3 | Empty WebGL build live on itch (whoever finishes first) |
| 3–16 | Parallel tracks |
| **16** | **Merge: ugly but complete loop** |
| 16–24 | Sleep |
| 24–40 | Musts complete, real art in |
| **40** | **Outside playtest (3+ people), cut list** |
| 40–56 | Sleep + Shoulds |
| **64** | **Feature freeze** |
| 64–70 | Bugs, final build, submit |
| 70–72 | Buffer |

## Rules (repeat of SCOPE.md §8)
- Save the scene before every commit; read `git status`.
- Commit `.meta` files with their assets.
- Only the scene owner saves `Main.unity`.
- Claude Code edits only `.cs` under `Assets/Project/Scripts/`.
