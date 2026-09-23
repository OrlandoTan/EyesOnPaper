using System;
using System.Collections.Generic;
using UnityEngine;

public enum CheatSource { Self, Paper }

// Shared record of every answer the player has seen.
// The results screen compares this against what was written on the answer sheet.
public static class CheatLog
{
    public class Entry
    {
        public int question;        // 0-based
        public int answer;          // 0-3 (A-D), always the correct one
        public CheatSource source;
        public float time;
    }

    static readonly List<Entry> entries = new List<Entry>();

    public static IReadOnlyList<Entry> Entries => entries;

    public static event Action<Entry> OnRevealed;

    // "Is this question done?" Defaults to "has its answer been revealed".
    // AnswerSheet will replace this with "has something been written on the sheet",
    // so a question you saw but never wrote down comes back around.
    public static Func<int, bool> IsAnswered = IsRevealed;

    public static bool IsRevealed(int q) => entries.Exists(e => e.question == q);

    public static int NextUnanswered(int questionCount)
    {
        for (int q = 0; q < questionCount; q++)
            if (!IsAnswered(q)) return q;
        return -1;
    }

    public static void Reveal(int question, int answer, CheatSource source)
    {
        var e = new Entry { question = question, answer = answer, source = source, time = Time.time };
        entries.Add(e);
        OnRevealed?.Invoke(e);
        GameEvents.AnswerRevealed(question, answer);
    }

    // Called automatically when Play starts. Call again when restarting the exam.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        entries.Clear();
        OnRevealed = null;
        IsAnswered = IsRevealed;
    }
}
