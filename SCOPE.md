# Eyes On Paper — Scope Doc

**Jam theme:** Human Error  **Time box:** 72 hours  **Engine:** Unity 6 LTS (URP, WebGL build for itch.io)
**Status:** Scope LOCKED. Changes need the whole team to agree at a check-in.

> **Pitch:** You're sitting an exam. Your friend Jack is texting you the answers. You only see each one for three seconds. You cheated on every question. You still failed.

---

## 1. Design pillars

1. **The errors are human, not random.** Every wrong answer should trace back to something the player *did*: misremembered, swapped two answers, panicked mid-combo. No dice rolls deciding the outcome.
2. **Pressure causes the slips.** A calm player should get almost everything right. The invigilator is the cause; the memory failure is the effect.
3. **The results screen is the punchline.** The game is built to deliver "Jack said B for Q7. You wrote B on Q4."

---

## 2. Core loop (one pipeline)

| Step | What happens | Risk it creates |
|---|---|---|
| 1. Buzz | Jack sends a message for a **random** question. Phone vibrates (audible). | Noise near the invigilator raises suspicion |
| 2. Pull phone | Hold **Space**. Message is locked. | Phone is visible |
| 3. Stratagem combo | Arrow-key sequence. Length scales with that question's difficulty. **Hiding the phone resets the combo.** | Time spent exposed |
| 4. Reveal | "Q7: B" shows for ~3 s, then is gone for good. No re-checking. | Memory load |
| 5. Mark | Hide phone, find Q7 on the paper, mark B from memory. | Swapping / forgetting |

Combos = **detection risk**. Vanishing message = **memory risk**. The invigilator ties them together.

---

## 3. Systems

### 3.1 Player
- Seated first-person camera, **clamped** mouse-look (no movement).
- Controls: Mouse = look · Hold Space = phone · Arrow keys = combo · Click / 1–4 = mark bubble.

### 3.2 Phone & combos
- Combo length: **Easy 4 · Medium 6 · Hard 8** arrows.
- Wrong arrow → combo restarts (phone stays out).
- Release Space → phone hidden, combo reset, message stays locked.

### 3.3 Jack's messages
- Arrive every **15–25 s**, out of question order.
- Each message is for one question; reveal lasts **3 s**, then deleted.
- Unread messages queue up (player decides when to risk reading).
- Jack is **always correct**. All errors are the player's.

### 3.4 Answer sheet
- 10 questions, A–D bubbles. Player can change an answer any time before the end.

### 3.5 Invigilator (NavMeshAgent)

| Suspicion | Behaviour |
|---|---|
| Low 0–40% | Wanders to random points in the room |
| Medium 40–75% | Random points weighted toward player's desk; longer pauses nearby |
| High 75–99% | Circles close behind the player, looks at the desk |
| 100% | **Caught** → fail screen |

- **Suspicion up:** phone visible inside vision cone (fast); phone buzz while invigilator is near (spike); looking sideways too long (slow).
- **Suspicion down:** slow decay while behaving.
- **Fairness rule:** footsteps are always audible; the vision cone must be readable.

### 3.6 Game flow
Title → How to play (1 screen) → Exam (5:00 timer) → **Caught** screen *or* **Results reveal** → Retry.

### 3.7 Results reveal (never cut)
- Walk through the paper one question at a time.
- For each wrong answer, show *why*: "Jack said B for Q7. You wrote B on Q4." / "Jack's message for Q2 was never opened."
- Final line: "You cheated on X/10 questions. You scored Y/10."

---

## 4. Scope (MoSCoW)

### Must — the game doesn't exist without these
- [ ] Seated FP camera with clamped look
- [ ] Phone hold/hide, combo input, reset on hide
- [ ] Message queue: random question, 3 s reveal, then gone
- [ ] Answer sheet: 10 Qs, A–D
- [ ] Invigilator: NavMesh wander, vision cone, suspicion meter, 3 tiers, caught state
- [ ] 5-minute timer, end on time-out or caught
- [ ] Results reveal with per-question reasons
- [ ] Title screen + one-screen how-to-play
- [ ] WebGL build on itch.io

### Should — cheap, strengthen the theme
- [ ] Audible buzz that spikes suspicion when the invigilator is near
- [ ] Scribble answer in margin (safe for memory, slow + visible)
- [ ] Scripted student gets caught in the first ~20 s (teaches the rules)
- [ ] Heartbeat audio scaling with suspicion

### Could — only if ahead at hour 48
- [ ] Exam-version twist (Jack has Version A, you have B)
- [ ] Near-duplicate combos so panic gives the wrong letter
- [ ] Difficulty modes (more messages, faster invigilator)

### Won't — do not build
Free movement · multiple exams/levels · copying from neighbours · LLM invigilator · voice acting · bubble-sheet offset · story/cutscenes

---

## 5. Starting numbers (tune after the hour-40 playtest)

| Parameter | Value |
|---|---|
| Questions | 10 |
| Exam timer | 5:00 |
| Pass mark | 50% |
| Message interval | 15–25 s |
| Reveal duration | 3 s |
| Combo length E/M/H | 4 / 6 / 8 |
| Suspicion gain (phone in cone) | +40%/s |
| Suspicion decay | −5%/s |

All exposed as `[SerializeField]` so they can be tuned in the Inspector without code changes.

---

## 6. Team & ownership

| Role | Owns | Folder |
|---|---|---|
| Dev A | Phone, combos, message queue, answer sheet | `Assets/Project/Scripts/Player/` |
| Dev B | Invigilator, suspicion, game flow, timer, results | `Assets/Project/Scripts/Invigilator/`, `Core/` |
| Art / Audio | Room, invigilator model + Mixamo anims, UI art, SFX, trailer GIF | `Assets/Project/Art/`, `Audio/` |

**Scene owner:** Dev B owns `Main.unity`. Everyone else works in prefabs and personal test scenes. Say "taking Main" / "releasing Main" in Discord.

### Scripts

| Script | Owner |
|---|---|
| `GameManager` | Dev B |
| `InvigilatorController` | Dev B |
| `SuspicionMeter` | Dev B |
| `ResultsScreen` | Dev B |
| `PhoneController` | Dev A |
| `ComboInput` | Dev A |
| `MessageQueue` | Dev A |
| `AnswerSheet` | Dev A |
| `QuestionData` (ScriptableObject) | Anyone |
| `GameEvents` (C# events: `OnPhoneShown`, `OnBuzz`, `OnMessageRevealed`, `OnCaught`) | Dev B |

---

## 7. 72-hour plan

| Hours | Goal | Milestone |
|---|---|---|
| 0–3 | Repo, project settings, **empty WebGL build on itch** | Web export works |
| 3–16 | Greybox: cube invigilator wanders, phone + combo, message reveal, bubbles | **H16: ugly but complete loop** |
| 16–24 | Sleep | — |
| 24–40 | Suspicion tiers, results reveal, real art swapped in | All Musts in |
| 40–48 | **Outside playtest (3+ people)**, cut what confuses them | Cut list decided |
| 48–56 | Sleep | — |
| 56–64 | Shoulds, audio, juice, tutorial | **H64: feature freeze** |
| 64–70 | Bugs only, final build, itch page, GIF, screenshots | Submitted |
| 70–72 | Buffer | Nobody touches code |

**Check-ins (10 min):** H0, H16, H24, H40, H56. Done / blocked / cut.
**If behind at H40:** cut Shoulds first. Never cut the results reveal.

---

## 8. Working rules

- **Save the scene before every commit** and read `git status`. If you changed something in the editor and no `.unity`/`.prefab` shows up, it isn't saved.
- **Always commit `.meta` files with their asset.**
- Merge to `main` 2–3× a day. `main` must always open and run.
- Claude Code edits **only** `.cs` files under `Assets/Project/Scripts/`. Never scenes, prefabs, `.meta`, or settings.
- Upload a build to itch every evening.

---

## 9. Open decisions

- [ ] Friend's name: **Jack** or **Jake**? (Lock before any UI text.)
- [ ] Final title: *Eyes On Paper* / *Eyes On Your Own Paper* / *Jack Said C* — check itch.io for duplicates.
- [ ] Assign names to Dev A / Dev B / Art.
